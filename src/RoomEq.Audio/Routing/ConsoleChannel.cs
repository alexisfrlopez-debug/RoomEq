using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using RoomEq.Audio.Playback;
using RoomEq.Core.Eq;

namespace RoomEq.Audio.Routing;

/// <summary>
/// Une tranche de la table de mixage : une source (périphérique d'entrée Windows,
/// sortie en loopback, pont ASIO d'un DAW, lecteur de fichiers ou générateur de
/// test) traversant sa
/// propre chaîne d'égalisation, un fader, un panoramique et les commutateurs
/// Mute/Solo, avec mesure de crêtes par voie. Le traitement se fait sur le domaine
/// stéréo du bus master (rééchantillonnage et adaptation de canaux inclus).
/// </summary>
public sealed class ConsoleChannel : IDisposable
{
    private readonly object _sync = new();

    private RingBuffer? _ring;
    private WasapiCapture? _capture;
    private ISampleProvider? _direct;       // lecteur de fichiers
    private GeneratorSampleProvider? _gen;  // générateur interne
    private ISampleProvider? _stereo;       // chaîne pull prête pour le bus master

    private float[] _scratch = Array.Empty<float>();
    private int _masterRate = 48000;
    private int _srcRate = 48000;
    private int _srcChannels = 2;

    private double _fader = 1.0;
    private double _pan;
    private bool _mute;
    private bool _solo;
    private bool _disposed;

    private TestSignal _genSignal = TestSignal.WhiteNoise;
    private double _genAmplitude = 0.25;
    private double _genFrequency = 1000.0;

    /// <summary>Crête de la dernière tranche lue, voie gauche (post-fader). Lire via Volatile.Read.</summary>
    public float PeakL;
    /// <summary>Crête de la dernière tranche lue, voie droite (post-fader). Lire via Volatile.Read.</summary>
    public float PeakR;

    /// <summary>Vrai quand le lecteur interne a atteint la fin du fichier.</summary>
    public bool PlayerFinished;

    /// <summary>Dernière erreur de capture (périphérique disparu…), pour l'UI.</summary>
    public string? CaptureStoppedWithErrors;

    /// <summary>Générateur : actif (sinon la tranche est muette côté source).</summary>
    public bool GeneratorEnabled;

    public ConsoleChannel(TrackSource source)
    {
        Source = source;
    }

    // ---------------------------------------------------------------- modèle

    public TrackSource Source { get; }

    public TrackSourceKind Kind => Source.Kind;

    public string Title => Source.Kind switch
    {
        TrackSourceKind.Player => string.IsNullOrEmpty(PlayerFileName) ? "Lecteur" : "Lecteur · " + PlayerFileName,
        TrackSourceKind.Generator => "Générateur",
        TrackSourceKind.AsioBridge => "DAW (ASIO)",
        TrackSourceKind.MacVirtualDevice => "macOS · RoomEq Virtual",
        _ => Source.Name
    };

    public string? PlayerFileName { get; private set; }

    // ---------------------------------------------------------------- contrôles

    /// <summary>Fader 0..1,5 (1,0 = 0 dB). Lecture atomique depuis le thread audio.</summary>
    public double Fader
    {
        get => Volatile.Read(ref _fader);
        set => Volatile.Write(ref _fader, Math.Clamp(value, 0.0, 1.5));
    }

    /// <summary>Panoramique -1 (gauche) .. +1 (droite), loi à puissance constante.</summary>
    public double Pan
    {
        get => Volatile.Read(ref _pan);
        set => Volatile.Write(ref _pan, Math.Clamp(value, -1.0, 1.0));
    }

    public bool Mute
    {
        get => Volatile.Read(ref _mute);
        set => Volatile.Write(ref _mute, value);
    }

    public bool Solo
    {
        get => Volatile.Read(ref _solo);
        set => Volatile.Write(ref _solo, value);
    }

    /// <summary>La tranche a-t-elle une source exploitable ?</summary>
    public bool SourceReady => Kind switch
    {
        TrackSourceKind.Generator => GeneratorEnabled,
        TrackSourceKind.Player => _direct != null && !PlayerFinished,
        TrackSourceKind.AsioBridge => Asio.AsioBridgeHost.Instance.IsHosting,
        TrackSourceKind.MacVirtualDevice => Mac.MacAudioBridge.DeviceBridge.IsHosting,
        _ => _capture != null
    };

    /// <summary>Chaîne d'égalisation propre à la piste (vide = transparente).</summary>
    public EqChain Eq { get; } = new();

    // ---------------------------------------------------------------- préparation

    /// <summary>
    /// Construit la chaîne de tirage : source → rééchantillonnage vers le taux master →
    /// stéréo → EQ de piste. Reconstructible à chaud (chargement de fichier, changement
    /// de taux de sortie) ; le contenu du tampon circulaire est conservé.
    /// </summary>
    public void Prepare(int masterRate)
    {
        lock (_sync)
        {
            _masterRate = masterRate;
            RebuildPullChainLocked();
        }
    }

    private void RebuildPullChainLocked()
    {
        ISampleProvider? s = null;

        if (_ring != null)
        {
            s = new RingBufferSampleProvider(_ring, _srcRate, _srcChannels);
        }
        else if (_direct != null)
        {
            s = _direct;
        }
        else if (Kind == TrackSourceKind.AsioBridge)
        {
            s = Asio.AsioBridgeHost.Instance.Provider;   // 48 kHz stéréo, resamplé vers le master
        }
        else if (Kind == TrackSourceKind.MacVirtualDevice)
        {
            s = Mac.MacAudioBridge.DeviceBridge.Provider; // 48 kHz stéréo, resamplé vers le master
        }
        else if (Kind == TrackSourceKind.Generator)
        {
            _gen = new GeneratorSampleProvider(_masterRate, 2);
            _gen.Signal = _genSignal;
            _gen.Amplitude = _genAmplitude;
            _gen.SineFrequency = _genFrequency;
            s = _gen;
        }

        if (s == null)
        {
            _stereo = null;
            return;
        }

        if (s.WaveFormat.SampleRate != _masterRate)
            s = new WdlResamplingSampleProvider(s, _masterRate);
        if (s.WaveFormat.Channels != 2)
            s = new ChannelAdapterSampleProvider(s, 2);

        // Configure + reconstruction forcée : garantit des filtres au taux master
        // même si le preset a été posé avant le démarrage.
        Eq.Configure(_masterRate, 2);
        Eq.SetPreset(Eq.Preset);

        _stereo = s;
    }

    // ---------------------------------------------------------------- sources à périphérique

    /// <summary>Démarre la capture WASAPI (ou loopback) vers le tampon circulaire.</summary>
    public void StartCapture(AudioDeviceCatalog catalog)
    {
        if (Kind is not (TrackSourceKind.Capture or TrackSourceKind.Loopback)) return;
        lock (_sync)
        {
            StopCaptureLocked();
            var dev = catalog.ResolveDevice(Source)
                ?? throw new InvalidOperationException("Périphérique introuvable : " + Source.Name);

            int rate, channels;
            using (var ac = dev.AudioClient)
            {
                var mix = ac.MixFormat;
                rate = mix.SampleRate;
                channels = Math.Clamp(mix.Channels, 1, 8);
            }

            _srcRate = rate;
            _srcChannels = channels;
            _ring = new RingBuffer(CapacityFor(rate, channels));

            WasapiCapture cap = Kind == TrackSourceKind.Loopback
                ? new WasapiLoopbackCapture(dev)
                : new WasapiCapture(dev);
            cap.WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
            cap.DataAvailable += OnCaptureData;
            cap.RecordingStopped += OnCaptureStopped;
            cap.StartRecording();
            _capture = cap;

            RebuildPullChainLocked();
        }
    }

    public void StopCapture()
    {
        lock (_sync) StopCaptureLocked();
    }

    private void StopCaptureLocked()
    {
        var cap = _capture;
        _capture = null;
        if (cap != null)
        {
            try
            {
                cap.DataAvailable -= OnCaptureData;
                cap.RecordingStopped -= OnCaptureStopped;
                cap.StopRecording();
                cap.Dispose();
            }
            catch
            {
                // ne jamais bloquer l'arrêt
            }
        }
    }

    private void OnCaptureStopped(object? sender, StoppedEventArgs e)
    {
        // la capture s'est arrêtée (périphérique débranché, erreur) : on le signale
        // via le dernier état ; le mixeur retente au prochain rafraîchissement.
        CaptureStoppedWithErrors = e.Exception?.Message;
    }

    private void OnCaptureData(object? sender, WaveInEventArgs e)
    {
        var ring = _ring;
        if (ring == null || e.BytesRecorded <= 0) return;
        try
        {
            var span = MemoryMarshal.Cast<byte, float>(e.Buffer.AsSpan(0, e.BytesRecorded));
            ring.Write(span);
        }
        catch
        {
            // ne jamais faire planter le thread de capture
        }
    }

    private static int CapacityFor(int rate, int channels)
    {
        // ~1,5 s de marge, borné pour la mémoire
        long cap = (long)rate * channels * 3 / 2;
        return (int)Math.Clamp(cap, 32768, 1_048_576);
    }

    // ---------------------------------------------------------------- sources internes

    /// <summary>Charge un fichier dans la tranche Lecteur.</summary>
    public void LoadFile(string path)
    {
        lock (_sync)
        {
            UnloadFileLocked();
            var reader = new AudioFileReader(path);
            _direct = reader;
            PlayerFileName = System.IO.Path.GetFileName(path);
            PlayerFinished = false;
            _srcRate = reader.WaveFormat.SampleRate;
            _srcChannels = reader.WaveFormat.Channels;
            RebuildPullChainLocked();
        }
    }

    /// <summary>Éjecte le fichier de la tranche Lecteur.</summary>
    public void UnloadFile()
    {
        lock (_sync) UnloadFileLocked();
    }

    private void UnloadFileLocked()
    {
        if (_direct is AudioFileReader r)
        {
            try { r.Dispose(); } catch { }
        }
        _direct = null;
        PlayerFileName = null;
        PlayerFinished = false;
        RebuildPullChainLocked();
    }

    /// <summary>Règle le signal du générateur interne (mémorisé même avant démarrage).</summary>
    public void SetGeneratorSignal(TestSignal signal, double amplitude, double sineFrequency = 1000.0)
    {
        _genSignal = signal;
        _genAmplitude = amplitude;
        _genFrequency = sineFrequency;
        var g = _gen;
        if (g == null) return;
        g.Signal = signal;
        g.Amplitude = amplitude;
        g.SineFrequency = sineFrequency;
    }

    // ---------------------------------------------------------------- traitement

    /// <summary>
    /// Lit la source, applique EQ de piste / fader / pan / Mute-Solo, accumule dans le
    /// bus stéréo master et met à jour les crêtes. Appelé sur le thread audio du mixeur.
    /// <paramref name="audible"/> = verdict Mute/Solo calculé par le mixeur.
    /// </summary>
    public void ReadInto(float[] accum, int offset, int frames, bool audible)
    {
        var s = _stereo;
        if (s == null || frames <= 0) return;

        int samples = frames * 2;
        if (_scratch.Length < samples) _scratch = new float[samples];

        int read = s.Read(_scratch, 0, samples);
        if (read < samples) Array.Clear(_scratch, read, samples - read);
        if (read > 0) Eq.Process(_scratch, 0, read);
        else if (Kind == TrackSourceKind.Player) PlayerFinished = true; // fin de fichier atteinte

        if (audible)
        {
            double g = Fader;
            (double gl, double gr) = PanGains(Pan);
            double a = g * gl, b = g * gr;

            float pkL = 0f, pkR = 0f;
            for (int f = 0; f < frames; f++)
            {
                int di = offset + f * 2;
                int si = f * 2;
                float l = (float)(_scratch[si] * a);
                float r = (float)(_scratch[si + 1] * b);
                accum[di] += l;
                accum[di + 1] += r;
                float al = l < 0 ? -l : l;
                float ar = r < 0 ? -r : r;
                if (al > pkL) pkL = al;
                if (ar > pkR) pkR = ar;
            }
            Volatile.Write(ref PeakL, pkL);
            Volatile.Write(ref PeakR, pkR);
        }
        else
        {
            Volatile.Write(ref PeakL, 0f);
            Volatile.Write(ref PeakR, 0f);
        }
    }

    /// <summary>Loi de panoramique à puissance constante (centre = -3 dB par voie).</summary>
    public static (double Left, double Right) PanGains(double pan)
    {
        double p = Math.Clamp(pan, -1.0, 1.0);
        double angle = (p + 1.0) * Math.PI / 4.0;
        return (Math.Cos(angle), Math.Sin(angle));
    }

    // ---------------------------------------------------------------- libération

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            StopCaptureLocked();
            UnloadFileLocked();
            _ring = null;
            _stereo = null;
            _gen = null;
        }
    }

    /// <summary>Source de lecture circulaire exposée comme ISampleProvider.</summary>
    private sealed class RingBufferSampleProvider : ISampleProvider
    {
        private readonly RingBuffer _ring;
        public WaveFormat WaveFormat { get; }

        public RingBufferSampleProvider(RingBuffer ring, int rate, int channels)
        {
            _ring = ring;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, channels);
        }

        public int Read(float[] buffer, int offset, int count)
            => _ring.Read(buffer.AsSpan(offset, count));
    }
}
