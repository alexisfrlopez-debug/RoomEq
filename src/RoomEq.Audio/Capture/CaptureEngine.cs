using NAudio.CoreAudioApi;
using NAudio.Wave;
using RoomEq.Audio.Playback;
using RoomEq.Core.Io;

namespace RoomEq.Audio.Capture;

/// <summary>
/// Capture micro (spec I-AUD-01) : WASAPI (Windows) ou AUHAL (macOS). Format de
/// travail demandé float 48 kHz mono, repli sur le format de mixage natif du
/// périphérique ; le callback reçoit les échantillons entrelacés avec leur
/// format réel. Aucun traitement audio ici : la réduction mono et l'analyse
/// appartiennent au consommateur (I-AUD-06).
/// </summary>
public sealed class CaptureEngine : IDisposable
{
    private const string DevicePrefix = "capture|";

    private WasapiCapture? _capture;
    private Mac.CoreAudioCapture? _macCapture;
    private MMDeviceEnumerator? _enumerator;

    /// <summary>Déclenché quand l'enregistrement s'arrête (erreur incluse).</summary>
    public event EventHandler<string>? Stopped;

    public string? LastError { get; private set; }

    /// <summary>Description du format réellement utilisé par la dernière capture.</summary>
    public string LastStartedFormat { get; private set; } = "";

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

    /// <summary>Liste des périphériques d'entrée actifs (id « capture|&lt;id&gt; »). Vide si le sous-système manque.</summary>
    public IReadOnlyList<OutputDeviceInfo> EnumerateDevices()
    {
        var list = new List<OutputDeviceInfo>();

        if (OperatingSystem.IsMacOS())
        {
            try
            {
                foreach (var d in Mac.CoreAudioCatalog.EnumerateInputDevices())
                    list.Add(new OutputDeviceInfo(DevicePrefix + d.Uid, d.Name, "Capture"));
            }
            catch (Exception ex)
            {
                LastError = ex.Message;
                DiagnosticsLog.Error("Énumération des entrées CoreAudio impossible", ex);
            }
            return list;
        }

        try
        {
            foreach (var d in Enumer().EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                list.Add(new OutputDeviceInfo(DevicePrefix + d.ID, d.FriendlyName, "Capture"));
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            DiagnosticsLog.Error("Énumération des périphériques d'entrée impossible", ex);
        }
        return list;
    }

    /// <summary>
    /// Nombre de canaux du périphérique d'entrée (format de mixage) — null si
    /// indisponible. Sert à proposer la sélection mono des micros multi-canaux.
    /// </summary>
    public int? GetChannelCount(string? deviceId)
    {
        try
        {
            if (OperatingSystem.IsMacOS())
            {
                string uid = ParseDeviceId(deviceId);
                return Mac.CoreAudioCatalog.Resolve(uid, Mac.CoreAudioApi.ScopeInput)?.Channels;
            }
            using var dev = ResolveDevice(deviceId);
            using var ac = dev.AudioClient;
            return Math.Max(1, ac.MixFormat.Channels);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Démarre la capture. Callback : (échantillons entrelacés float, canaux,
    /// fréquence d'échantillonnage) — appelé sur le thread audio.
    /// </summary>
    public void Start(string? deviceId, Action<float[], int, int> onChunk)
    {
        if (OperatingSystem.IsMacOS())
        {
            StartMac(deviceId, onChunk);
            return;
        }

        var dev = ResolveDevice(deviceId);
        DiagnosticsLog.Info($"Capture : périphérique « {dev.FriendlyName} » ({deviceId ?? "défaut"})");

        _capture?.Dispose();

        // format demandé : float 48 kHz mono — sinon repli sur le format de mixage natif
        var requested = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        WasapiCapture? capture;
        try
        {
            capture = CreateAndStart(dev, requested, onChunk);
            LastStartedFormat = Describe(requested);
            DiagnosticsLog.Info($"Capture démarrée — format demandé accepté : {LastStartedFormat}");
        }
        catch (Exception ex)
        {
            DiagnosticsLog.Warn($"Format 48 kHz mono refusé ({ex.Message}) — repli sur le format de mixage du périphérique.");
            capture = null;
            try
            {
                using var ac = dev.AudioClient; // COM IDisposable : libéré immédiatement
                var mix = ac.MixFormat;
                var fallback = WaveFormat.CreateIeeeFloatWaveFormat(mix.SampleRate, mix.Channels);
                capture = CreateAndStart(dev, fallback, onChunk);
                LastStartedFormat = Describe(fallback);
                DiagnosticsLog.Info($"Capture démarrée — format de repli : {LastStartedFormat}");
            }
            catch (Exception ex2)
            {
                LastError = ex2.Message;
                DiagnosticsLog.Error("Démarrage de la capture impossible", ex2);
                capture?.Dispose();
                throw new InvalidOperationException(
                    $"Impossible de démarrer la capture du micro « {dev.FriendlyName} » : {ex2.Message}. " +
                    "Vérifiez qu'il n'est pas réservé en exclusif par une autre application et que Windows autorise son accès.", ex2);
            }
        }

        _capture = capture;
    }

    /// <summary>Capture macOS (AUHAL) : micro par défaut ou périphérique choisi.</summary>
    private void StartMac(string? deviceId, Action<float[], int, int> onChunk)
    {
        _macCapture?.Dispose();
        _macCapture = null;

        Mac.CoreAudioDeviceInfo? dev = null;
        string uid = ParseDeviceId(deviceId);
        if (uid.Length > 0)
            dev = Mac.CoreAudioCatalog.Resolve(uid, Mac.CoreAudioApi.ScopeInput);
        dev ??= Mac.CoreAudioCatalog.EnumerateInputDevices().FirstOrDefault();

        if (dev == null)
        {
            LastError = "Aucun périphérique d'entrée CoreAudio.";
            throw new InvalidOperationException(
                "Aucun micro disponible. Branchez un micro et vérifiez les permissions " +
                "dans Réglages Système › Confidentialité › Micro.");
        }

        DiagnosticsLog.Info($"Capture : périphérique « {dev.Name} » (CoreAudio)");
        try
        {
            var cap = new Mac.CoreAudioCapture(dev, onChunk, channels: 1);
            cap.Start();
            _macCapture = cap;
            LastStartedFormat = "48000 Hz · 1 canal (CoreAudio)";
            DiagnosticsLog.Info("Capture CoreAudio démarrée — 48 000 Hz mono");
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            DiagnosticsLog.Error("Démarrage de la capture CoreAudio impossible", ex);
            throw new InvalidOperationException(
                $"Impossible de démarrer la capture du micro « {dev.Name} » : {ex.Message}", ex);
        }
    }

    /// <summary>Crée la capture WASAPI, câble les événements puis démarre.</summary>
    private WasapiCapture CreateAndStart(MMDevice dev, WaveFormat format, Action<float[], int, int> onChunk)
    {
        var capture = new WasapiCapture(dev) { WaveFormat = format };
        capture.DataAvailable += (_, e) =>
        {
            try
            {
                int floats = e.BytesRecorded / 4;
                if (floats <= 0 || _capture == null) return;
                var samples = new float[floats];
                Buffer.BlockCopy(e.Buffer, 0, samples, 0, e.BytesRecorded);
                onChunk(samples, format.Channels, format.SampleRate);
            }
            catch
            {
                // ne jamais faire planter le thread audio
            }
        };
        capture.RecordingStopped += (_, e) =>
        {
            LastError = e.Exception?.Message;
            if (e.Exception != null)
                DiagnosticsLog.Error("Capture interrompue", e.Exception);
            Stopped?.Invoke(this, LastError ?? "");
        };
        capture.StartRecording();
        return capture;
    }

    private static string Describe(WaveFormat f)
        => $"{f.SampleRate} Hz · {f.Channels} canal(aux)";

    /// <summary>Extrait l'identifiant brut d'un « capture|… » (vide si autre chose).</summary>
    private static string ParseDeviceId(string? deviceId)
        => deviceId != null && deviceId.StartsWith(DevicePrefix, StringComparison.Ordinal)
            ? deviceId[DevicePrefix.Length..]
            : "";

    private MMDevice ResolveDevice(string? deviceId)
    {
        string id = ParseDeviceId(deviceId);
        if (id.Length == 0)
            return Enumer().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);

        return Enumer().EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                          .FirstOrDefault(d => d.ID == id)
               ?? throw new InvalidOperationException("Micro introuvable (débranché ?).");
    }

    public void Stop()
    {
        try { _capture?.StopRecording(); } catch { /* arrêt tolérant */ }
        try { _macCapture?.Stop(); } catch { /* arrêt tolérant */ }
    }

    public void Dispose()
    {
        Stop();
        try { _capture?.Dispose(); } catch { /* libération tolérante */ }
        try { _macCapture?.Dispose(); } catch { /* libération tolérante */ }
        _capture = null;
        _macCapture = null;
    }
}
