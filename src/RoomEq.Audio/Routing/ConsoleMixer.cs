using NAudio.CoreAudioApi;
using NAudio.Wave;
using RoomEq.Audio.Playback;
using RoomEq.Core.Eq;

namespace RoomEq.Audio.Routing;

/// <summary>
/// La table de mixage (spec I-AUD-03) : sommation des tranches vers un bus
/// master stéréo (EQ master synchronisé avec l'onglet Égaliseur + fader
/// master), rendu sur la sortie choisie (WASAPI partagé ou ASIO exclusif,
/// CoreAudio sur macOS). Les tranches à périphérique tournent chacune sur leur
/// callback de capture ; le bus est tiré par la sortie.
/// Protections intégrées (ordre SÉCURITÉ) : interdiction de capturer en
/// loopback le périphérique de sortie master (larsen) et d'envoyer le master
/// vers un câble virtuel (boucle).
/// </summary>
public sealed class ConsoleMixer : IDisposable
{
    /// <summary>Latence du bus master en WASAPI partagé (ms) — plus courte que la lecture classique.</summary>
    public const int MasterLatencyMs = 50;

    /// <summary>Latence indicative par défaut des autres sorties (ASIO…).</summary>
    public const int DefaultOtherLatencyMs = 12;

    /// <summary>Délai de disposition différée d'une tranche retirée pendant que le bus tourne.</summary>
    private static readonly TimeSpan GraveyardDelay = TimeSpan.FromSeconds(1.0);

    private readonly AudioDeviceCatalog _catalog = new();
    private readonly object _sync = new();
    private readonly List<ConsoleChannel> _channels = new();
    private ConsoleChannel[] _snapshot = Array.Empty<ConsoleChannel>();

    private IAudioOutput? _output;
    private string? _outputId;
    private int _masterRate = 48000;
    private double _masterFader = 1.0;
    private bool _masterMute;
    private bool _disposed;
    private MasterBus? _bus;

    // tranches retirées pendant que le bus tourne : disposition différée, après
    // que le thread audio a fini de lire l'ancien instantané (jamais en direct)
    private readonly List<ConsoleChannel> _graveyard = new();

    /// <summary>Crêtes du bus master (post fader), voie gauche / droite. Lire via Volatile.Read.</summary>
    public float MasterPeakL;
    public float MasterPeakR;

    /// <summary>Chaîne d'égalisation du bus master (synchronisée avec l'EQ principal).</summary>
    public EqChain MasterEq { get; } = new();

    public bool IsRunning { get; private set; }

    /// <summary>Latence indicative de la sortie (ms), pour l'affichage.</summary>
    public int OutputLatencyMs { get; private set; } = DefaultOtherLatencyMs;

    public event Action<string>? ErrorOccurred;
    public event Action? StateChanged;

    /// <summary>Instantané figé des tranches actives (lu par le thread audio sans verrou).</summary>
    internal ConsoleChannel[] Snapshot => _snapshot;

    // ---------------------------------------------------------------- sorties

    /// <summary>Destinations possibles du bus master : CoreAudio (macOS) ou sorties WASAPI + pilotes ASIO (Windows).</summary>
    public IReadOnlyList<OutputDeviceInfo> EnumerateOutputs()
    {
        var list = new List<OutputDeviceInfo>();
        if (OperatingSystem.IsMacOS())
        {
            try
            {
                list.AddRange(Mac.CoreAudioCatalog.ToOutputInfos());
            }
            catch
            {
                // CoreAudio indisponible : liste vide, l'UI le signalera
            }
            return list;
        }

        list.AddRange(_catalog.EnumerateRenderDevices());
        try
        {
            foreach (var name in AsioOut.GetDriverNames())
                list.Add(new OutputDeviceInfo("asio|" + name, "ASIO · " + name, "ASIO"));
        }
        catch
        {
            // pas de pilotes ASIO : sans importance
        }
        return list;
    }

    /// <summary>Change la destination du bus master (redémarre proprement si actif).</summary>
    public void SetOutput(string? id)
    {
        lock (_sync)
        {
            if (_outputId == id) return;
            _outputId = id;
            if (IsRunning) RestartLocked();
        }
    }

    // ---------------------------------------------------------------- tranches

    /// <summary>Ajoute une tranche ; la capture démarre immédiatement si le mixeur tourne.</summary>
    public ConsoleChannel AddChannel(TrackSource source)
    {
        var ch = new ConsoleChannel(source);
        lock (_sync)
        {
            _channels.Add(ch);
            PublishSnapshotLocked();
            if (IsRunning)
            {
                try
                {
                    ch.Prepare(_masterRate);
                    ch.StartCapture(_catalog);
                }
                catch (Exception ex)
                {
                    ErrorOccurred?.Invoke($"Piste « {source.Name} » : {ex.Message}");
                }
            }
        }
        return ch;
    }

    /// <summary>
    /// Retire une tranche. Le canal est disposé IMMÉDIATEMENT si le bus est arrêté ;
    /// sinon il est mis au « cimetière » et disposé plus tard (le thread audio peut
    /// encore lire l'ancien instantané qui le référence — le disposer maintenant
    /// provoquerait une lecture d'objet libéré).
    /// </summary>
    public void RemoveChannel(ConsoleChannel channel)
    {
        lock (_sync)
        {
            _channels.Remove(channel);
            PublishSnapshotLocked();
            if (IsRunning)
                DisposeChannelLater(channel);
            else
                channel.Dispose();
        }
    }

    private void FlushGraveyardLocked()
    {
        if (_graveyard.Count == 0) return;
        foreach (var ch in _graveyard)
        {
            try { ch.Dispose(); } catch { /* la libération ne doit jamais lever */ }
        }
        _graveyard.Clear();
    }

    private void DisposeChannelLater(ConsoleChannel channel)
    {
        // disposition sûre : le thread audio ne conserve l'instantané que le temps
        // d'un bloc lu — 1 s dépassera toujours la durée d'un appel Read
        _ = Task.Delay(GraveyardDelay).ContinueWith(_ =>
        {
            lock (_sync)
            {
                _graveyard.Remove(channel);
            }
            try { channel.Dispose(); } catch { /* libération tolérante */ }
        }, TaskScheduler.Default);
    }

    private void PublishSnapshotLocked()
        => _snapshot = _channels.ToArray(); // affectation atomique (référence)

    // ---------------------------------------------------------------- contrôles master

    /// <summary>Fader master 0..1,5.</summary>
    public double MasterFader
    {
        get => Volatile.Read(ref _masterFader);
        set => Volatile.Write(ref _masterFader, Math.Clamp(value, 0.0, 1.5));
    }

    public bool MasterMute
    {
        get => Volatile.Read(ref _masterMute);
        set => Volatile.Write(ref _masterMute, value);
    }

    // ---------------------------------------------------------------- démarrage / arrêt

    /// <summary>Démarre le routage : captures + bus master vers la sortie choisie.</summary>
    public void Start()
    {
        lock (_sync)
        {
            if (_disposed || IsRunning) return;

            ValidateNoFeedbackLocked();

            try
            {
                _output = CreateOutputLocked();
            }
            catch (Exception ex)
            {
                _output = null;
                throw new InvalidOperationException("Ouverture de la sortie master impossible : " + ex.Message, ex);
            }

            _masterRate = _output.SampleRate;
            OutputLatencyMs = _output is WasapiOutput w ? w.LatencyMs : DefaultOtherLatencyMs;

            MasterEq.Configure(_masterRate, 2);
            MasterEq.SetPreset(MasterEq.Preset); // reconstruction au taux réel de sortie

            var failed = new List<string>();
            foreach (var ch in _snapshot)
            {
                try
                {
                    ch.Prepare(_masterRate);
                    if (ch.Kind is TrackSourceKind.Capture or TrackSourceKind.Loopback)
                        ch.StartCapture(_catalog);
                }
                catch (Exception ex)
                {
                    failed.Add($"{ch.Title} : {ex.Message}");
                }
            }
            if (failed.Count > 0)
                ErrorOccurred?.Invoke("Pistes indisponibles : " + string.Join(" · ", failed));

            _bus = new MasterBus(this);
            ISampleProvider s = _bus;
            if (_output.Channels != 2)
                s = new StereoToOutputProvider(s, _output.Channels);

            try
            {
                _output.Init(s);
                _output.Play();
                IsRunning = true;
            }
            catch (Exception ex)
            {
                StopLocked();
                throw new InvalidOperationException("Démarrage du routage impossible : " + ex.Message, ex);
            }
            StateChanged?.Invoke();
        }
    }

    public void Stop()
    {
        lock (_sync) StopLocked();
    }

    private void StopLocked()
    {
        IsRunning = false;
        foreach (var ch in _snapshot)
        {
            try { ch.StopCapture(); } catch { /* arrêt tolérant */ }
        }
        try { _output?.Stop(); } catch { /* arrêt tolérant */ }
        try { _output?.Dispose(); } catch { /* libération tolérante */ }
        _output = null;
        _bus = null;
        MasterPeakL = 0;
        MasterPeakR = 0;
        // le bus est arrêté : plus aucune lecture en vol, disposition des tranches retirées
        FlushGraveyardLocked();
        StateChanged?.Invoke();
    }

    private void RestartLocked()
    {
        bool wasRunning = IsRunning;
        StopLocked();
        if (wasRunning)
        {
            try { Start(); }
            catch (Exception ex)
            {
                ErrorOccurred?.Invoke(ex.Message);
            }
        }
    }

    private IAudioOutput CreateOutputLocked()
    {
        string? id = _outputId;
        if (id == null)
        {
            if (OperatingSystem.IsMacOS())
            {
                // sortie par défaut macOS
                uint defaultId = Mac.CoreAudioCatalog.GetDefaultOutputDeviceId();
                if (defaultId == 0)
                    throw new InvalidOperationException("Aucune sortie CoreAudio disponible.");
                string name = Mac.CoreAudioApi.ReadDeviceName(defaultId);
                string uid = Mac.CoreAudioApi.ReadDeviceUid(defaultId);
                return new Mac.CoreAudioOutput(new Mac.CoreAudioDeviceInfo(defaultId, uid.Length > 0 ? uid : name, name, 2));
            }
            var def = new MMDeviceEnumerator().GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            _outputId = "wasapi|" + def.ID;
            return new WasapiOutput(def, MasterLatencyMs);
        }
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
            var mm = new MMDeviceEnumerator()
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .FirstOrDefault(d => d.ID == deviceId)
                ?? throw new InvalidOperationException("Périphérique de sortie introuvable (débranché ?).");
            return new WasapiOutput(mm, MasterLatencyMs);
        }
        if (id.StartsWith("asio|", StringComparison.Ordinal))
        {
            string driver = id["asio|".Length..];
            return new AsioOutput(driver);
        }
        throw new ArgumentException("Identifiant de sortie inconnu : " + id);
    }

    /// <summary>
    /// Interdit tout montage aboutissant à une boucle de rétroaction :
    /// une piste loopback ne doit pas capturer le périphérique de sortie master ;
    /// le master ne doit pas partir vers une entrée de câble virtuel, sinon le
    /// son re-rentre dans le câble et tourne en boucle (Windows et macOS).
    /// </summary>
    private void ValidateNoFeedbackLocked()
    {
        string? outWasapiId = _outputId?.StartsWith("wasapi|", StringComparison.Ordinal) == true
            ? _outputId["wasapi|".Length..]
            : null;
        string? outCoreAudioUid = _outputId?.StartsWith("coreaudio|", StringComparison.Ordinal) == true
            ? _outputId["coreaudio|".Length..]
            : null;

        // macOS : le master ne doit pas repartir dans le périphérique virtuel
        if (outCoreAudioUid != null
            && Mac.CoreAudioCatalog.FindRoomEqVirtual() is { } virt
            && string.Equals(virt.Uid, outCoreAudioUid, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "La sortie master ne peut pas être le périphérique virtuel RoomEq " +
                "lui-même (boucle audio). Choisissez vos enceintes ou votre casque.");
        }

        if (outWasapiId != null)
        {
            var friendly = _catalog.EnumerateRenderDevices()
                .FirstOrDefault(d => d.Id == outWasapiId)?.DisplayName ?? "";
            if (AudioDeviceCatalog.IsVirtualRender(friendly))
                throw new InvalidOperationException(
                    "La sortie master ne peut pas être la carte virtuelle elle-même " +
                    "(boucle audio). Choisissez vos enceintes ou votre casque.");
        }

        foreach (var ch in _snapshot)
        {
            if (ch.Kind == TrackSourceKind.Loopback && outWasapiId != null
                && string.Equals(ch.Source.DeviceId, outWasapiId, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"La piste « {ch.Title} » capture en boucle le périphérique utilisé " +
                    "comme sortie master : changez la sortie master ou supprimez cette piste.");
            }
        }
    }

    public void Dispose()
    {
        lock (_sync)
        {
            if (_disposed) return;
            _disposed = true;
            StopLocked();
            foreach (var ch in _channels) ch.Dispose();
            _channels.Clear();
            _snapshot = Array.Empty<ConsoleChannel>();
            FlushGraveyardLocked();
        }
    }

    // ---------------------------------------------------------------- bus master

    /// <summary>
    /// Le bus master (interne, testable) : lit chaque tranche (EQ de piste,
    /// fader, pan, Mute/Solo), somme, applique l'EQ master puis le fader
    /// master, mesure les crêtes. Format : float stéréo au taux de la sortie.
    /// </summary>
    internal sealed class MasterBus(ConsoleMixer m) : ISampleProvider
    {
        private float[] _accum = Array.Empty<float>();
        private WaveFormat? _format;

        public WaveFormat WaveFormat
            => _format ??= WaveFormat.CreateIeeeFloatWaveFormat(m._masterRate, 2);

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / 2;
            if (frames <= 0) return 0;
            int samples = frames * 2;
            if (_accum.Length < samples) _accum = new float[samples];

            Array.Clear(_accum, 0, samples);

            var snap = m._snapshot;
            bool anySolo = false;
            for (int i = 0; i < snap.Length; i++)
                if (snap[i].Solo) { anySolo = true; break; }

            for (int i = 0; i < snap.Length; i++)
            {
                var ch = snap[i];
                bool audible = !ch.Mute && (!anySolo || ch.Solo) && ch.SourceReady;
                ch.ReadInto(_accum, 0, frames, audible);
            }

            m.MasterEq.Process(_accum, 0, samples);

            double gain = m.MasterFader * Math.Pow(10.0, m.MasterEq.PreampDb / 20.0);
            if (m.MasterMute) gain = 0;

            float pkL = 0f, pkR = 0f;
            for (int f = 0; f < frames; f++)
            {
                int i = offset + f * 2;
                float l = (float)(_accum[f * 2] * gain);
                float r = (float)(_accum[f * 2 + 1] * gain);
                buffer[i] = l;
                buffer[i + 1] = r;
                float al = l < 0 ? -l : l;
                float ar = r < 0 ? -r : r;
                if (al > pkL) pkL = al;
                if (ar > pkR) pkR = ar;
            }
            Volatile.Write(ref m.MasterPeakL, pkL);
            Volatile.Write(ref m.MasterPeakR, pkR);

            return samples;
        }
    }

    /// <summary>Adaptation du bus stéréo vers une sortie multi-canaux (L→ch0, R→ch1, reste à zéro ; mono = moyenne).</summary>
    internal sealed class StereoToOutputProvider(ISampleProvider inner, int outChannels) : ISampleProvider
    {
        private readonly int _outChannels = Math.Max(1, outChannels);
        private float[] _tmp = Array.Empty<float>();

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(inner.WaveFormat.SampleRate, Math.Max(1, outChannels));

        public int Read(float[] buffer, int offset, int count)
        {
            int frames = count / _outChannels;
            int need = frames * 2;
            if (_tmp.Length < need) _tmp = new float[need];

            int read = inner.Read(_tmp, 0, need);
            int srcFrames = read / 2;
            if (read < need) Array.Clear(_tmp, read, need - read);

            for (int f = 0; f < srcFrames; f++)
            {
                int di = offset + f * _outChannels;
                float l = _tmp[f * 2], r = _tmp[f * 2 + 1];
                if (_outChannels == 1)
                {
                    buffer[di] = (l + r) * 0.5f;
                }
                else
                {
                    buffer[di] = l;
                    buffer[di + 1] = r;
                    for (int c = 2; c < _outChannels; c++) buffer[di + c] = 0f;
                }
            }
            return srcFrames * _outChannels;
        }
    }
}
