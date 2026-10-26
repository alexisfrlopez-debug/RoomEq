using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;

namespace RoomEq.Audio.Playback;

/// <summary>
/// Moteur central de lecture (spec I-AUD-02) : gère la sortie active (WASAPI,
/// ASIO ou CoreAudio), la source (fichier ou signal de test), la chaîne de
/// traitement (rééchantillonnage → canaux → boucle → EQ → gain → tap).
/// Le moteur PREND POSSESSION de la source consommable (fichier) et la libère
/// au remplacement, à l'échec ou à l'abandon.
/// </summary>
public sealed class PlaybackEngine : IDisposable
{
    private readonly object _sync = new();
    private MMDeviceEnumerator? _enumerator;
    private IAudioOutput? _output;
    private ISampleProvider? _source;
    private Action? _restart;
    private string? _outputId;
    private bool _disposed;
    private double _volume = 1.0;
    private bool _loop;

    public EqChain Eq { get; } = new();
    public TapBuffer Tap { get; } = new();

    /// <summary>Volume maître 0..1,5 (lecture atomique depuis le thread audio).</summary>
    public double Volume
    {
        get => Volatile.Read(ref _volume);
        set => Volatile.Write(ref _volume, value);
    }

    public bool Loop
    {
        get => Volatile.Read(ref _loop);
        set => Volatile.Write(ref _loop, value);
    }

    public int CurrentSampleRate => _output?.SampleRate ?? 48000;
    public int CurrentChannels => _output?.Channels ?? 2;
    public string? CurrentOutputId => _outputId;
    public string? CurrentSourceName { get; private set; }
    public PlaybackState State => _output?.State ?? PlaybackState.Stopped;

    public event Action<PlaybackState>? StateChanged;
    public event Action<string>? ErrorOccurred;
    public event Action<string?>? SourceChanged;

    /// <summary>Énumérateur WASAPI créé à la demande (le sous-système audio peut manquer).</summary>
    private MMDeviceEnumerator Enumer()
    {
        if (_enumerator == null)
        {
            try
            {
                _enumerator = new MMDeviceEnumerator();
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "Système audio Windows indisponible : " + ex.Message, ex);
            }
        }
        return _enumerator;
    }

    /// <summary>Énumère les sorties : CoreAudio (macOS) ou WASAPI actif + pilotes ASIO (Windows).</summary>
    public IReadOnlyList<OutputDeviceInfo> EnumerateOutputs()
    {
        var list = new List<OutputDeviceInfo>();

        if (OperatingSystem.IsMacOS())
        {
            try
            {
                list.AddRange(Mac.CoreAudioCatalog.ToOutputInfos());
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("Énumération CoreAudio impossible : " + ex.Message);
            }
            return list;
        }

        try
        {
            foreach (var d in Enumer().EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                list.Add(new OutputDeviceInfo("wasapi|" + d.ID, "WASAPI · " + d.FriendlyName, "WASAPI"));
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke("Énumération WASAPI impossible : " + ex.Message);
        }

        try
        {
            foreach (var name in AsioOut.GetDriverNames())
                list.Add(new OutputDeviceInfo("asio|" + name, "ASIO · " + name, "ASIO"));
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke("Énumération ASIO impossible : " + ex.Message);
        }
        return list;
    }

    /// <summary>Sélectionne la sortie. Interrompt la lecture en cours (la source est conservée).</summary>
    public void SetOutput(string? id)
    {
        lock (_sync)
        {
            bool wasPlaying = State == PlaybackState.Playing;
            _output?.Stop();
            _output?.Dispose();
            _output = null;
            _outputId = id;

            if (id != null)
            {
                try
                {
                    _output = CreateOutput(id);
                    _output.PlaybackStopped += (_, _) => StateChanged?.Invoke(State);
                }
                catch (Exception ex)
                {
                    _output = null;
                    _outputId = null;
                    ErrorOccurred?.Invoke($"Impossible d'ouvrir la sortie : {ex.Message}");
                }
            }

            if (_output != null && _source != null)
            {
                try
                {
                    RebuildChainAndStart(wasPlaying);
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke("Redémarrage impossible : " + ex.Message);
                }
            }
            StateChanged?.Invoke(State);
        }
    }

    /// <summary>S'assure qu'une sortie existe (par défaut : sortie système WASAPI/CoreAudio).</summary>
    public void EnsureDefaultOutput()
    {
        lock (_sync) EnsureDefaultOutputLocked();
    }

    private IAudioOutput CreateOutput(string id)
    {
        if (id.StartsWith("coreaudio|", StringComparison.Ordinal))
        {
            string uid = id["coreaudio|".Length..];
            var dev = Mac.CoreAudioCatalog.Resolve(uid, Mac.CoreAudioApi.ScopeOutput)
                ?? throw new InvalidOperationException("Périphérique CoreAudio introuvable (débranché ?)");
            return new Mac.CoreAudioOutput(dev);
        }
        if (id.StartsWith("wasapi|", StringComparison.Ordinal))
        {
            string deviceId = id["wasapi|".Length..];
            var dev = Enumer().EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                                 .FirstOrDefault(d => d.ID == deviceId)
                     ?? throw new InvalidOperationException("Périphérique WASAPI introuvable (débranché ?).");
            return new WasapiOutput(dev);
        }
        if (id.StartsWith("asio|", StringComparison.Ordinal))
        {
            string driver = id["asio|".Length..];
            return new AsioOutput(driver);
        }
        throw new ArgumentException("Identifiant de sortie inconnu : " + id);
    }

    /// <summary>Lit une source quelconque. Le moteur prend possession de la source consommable.</summary>
    public void PlaySource(ISampleProvider source, string? sourceName, Action? restart)
    {
        lock (_sync)
        {
            if (_disposed)
            {
                DisposeSource(source);
                return;
            }
            if (_output == null) EnsureDefaultOutputLocked();
            if (_output == null)
            {
                ErrorOccurred?.Invoke("Aucune sortie audio disponible.");
                DisposeSource(source);
                return;
            }

            _output.Stop();
            DisposeSource(_source, except: source);
            _source = source;
            _restart = restart;
            CurrentSourceName = sourceName;
            try
            {
                RebuildChainAndStart(true);
            }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke("Démarrage de la lecture impossible : " + ex.Message);
            }
            SourceChanged?.Invoke(sourceName);
        }
    }

    /// <summary>Lit un fichier audio (wav, mp3, flac, aiff, wma… via Media Foundation).</summary>
    public void PlayFile(string path)
    {
        var reader = new AudioFileReader(path);
        void Restart() { try { reader.Position = 0; } catch { /* fichier corrompu : boucle s'arrête */ } }
        PlaySource(reader, Path.GetFileName(path), Restart);
    }

    /// <summary>Joue un signal de test (bruits, sinus ou balayage logarithmique).</summary>
    public void PlayTestSignal(TestSignal signal, double amplitude, double sineFrequency = 1000.0,
        LogSweepSpec? sweepSpec = null)
    {
        EnsureDefaultOutput();
        int rate = _output?.SampleRate ?? 48000;
        int ch = _output?.Channels ?? 2;
        var gen = new GeneratorSampleProvider(rate, ch)
        {
            Signal = signal,
            Amplitude = amplitude,
            SineFrequency = sineFrequency,
            SweepSpec = sweepSpec ?? LogSweepSpec.Default,
        };
        PlaySource(gen, DisplayNameOf(signal, sineFrequency, sweepSpec), null);
    }

    private static string DisplayNameOf(TestSignal signal, double sineFrequency, LogSweepSpec? sweepSpec)
        => signal switch
        {
            TestSignal.WhiteNoise => "Bruit blanc",
            TestSignal.PinkNoise => "Bruit rose",
            TestSignal.BrownNoise => "Bruit brun",
            TestSignal.VioletNoise => "Bruit violet",
            TestSignal.LogSweep => $"Balayage {(sweepSpec ?? LogSweepSpec.Default).F1:0} Hz → {(sweepSpec ?? LogSweepSpec.Default).F2:0} Hz",
            _ => $"Sinus {sineFrequency:0} Hz",
        };

    public void Stop()
    {
        lock (_sync) _output?.Stop();
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (State == PlaybackState.Playing) _output?.Pause();
        }
    }

    public void Resume()
    {
        lock (_sync)
        {
            if (State == PlaybackState.Paused) _output?.Play();
        }
    }

    /// <summary>Libère une source si elle est consommable — jamais d'exception.</summary>
    private static void DisposeSource(ISampleProvider? source, ISampleProvider? except = null)
    {
        if (source is IDisposable d && !ReferenceEquals(source, except))
        {
            try { d.Dispose(); } catch { /* une source corrompue ne doit pas casser la suivante */ }
        }
    }

    private void EnsureDefaultOutputLocked()
    {
        if (_output != null) return;
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                uint def = Mac.CoreAudioCatalog.GetDefaultOutputDeviceId();
                if (def == 0) throw new InvalidOperationException("Aucune sortie CoreAudio disponible.");
                string name = Mac.CoreAudioApi.ReadDeviceName(def);
                string uid = Mac.CoreAudioApi.ReadDeviceUid(def);
                _outputId = "coreaudio|" + (uid.Length > 0 ? uid : name);
                _output = new Mac.CoreAudioOutput(new Mac.CoreAudioDeviceInfo(def, uid, name, 2));
            }
            else
            {
                var def = Enumer().GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
                _outputId = "wasapi|" + def.ID;
                _output = new WasapiOutput(def);
            }
            _output.PlaybackStopped += (_, _) => StateChanged?.Invoke(State);
        }
        catch (Exception ex)
        {
            ErrorOccurred?.Invoke("Aucune sortie audio disponible : " + ex.Message);
        }
    }

    /// <summary>Construit : source → resample → canaux → boucle → EQ → gain → tap, puis initialise la sortie.</summary>
    private void RebuildChainAndStart(bool startPlaying)
    {
        var output = _output ?? throw new InvalidOperationException("Pas de sortie.");
        var source = _source ?? throw new InvalidOperationException("Pas de source.");

        ISampleProvider s = source;
        if (s.WaveFormat.SampleRate != output.SampleRate)
            s = new WdlResamplingSampleProvider(s, output.SampleRate);
        if (s.WaveFormat.Channels != output.Channels)
            s = new ChannelAdapterSampleProvider(s, output.Channels);

        s = new LoopingSampleProvider(s, _restart) { Loop = Loop };
        s = new EqSampleProvider(s, Eq);
        s = new GainSampleProvider(s, () => Volume * Math.Pow(10.0, Eq.PreampDb / 20.0));
        s = new TapSampleProvider(s, Tap);

        output.Init(s);
        if (startPlaying) output.Play();
    }

    public void Dispose()
    {
        lock (_sync)
        {
            _disposed = true;
            _output?.Dispose();
            _output = null;
            DisposeSource(_source);
            _source = null;
        }
    }
}
