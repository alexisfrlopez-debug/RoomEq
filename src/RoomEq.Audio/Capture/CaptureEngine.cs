using NAudio.CoreAudioApi;
using NAudio.Wave;
using RoomEq.Audio.Playback;
using RoomEq.Core.Io;

namespace RoomEq.Audio.Capture;

/// <summary>
/// Capture micro via WASAPI (format float, adapté automatiquement au mixeur).
/// Les échantillons sont livrés entrelacés avec leur format réel via le callback.
/// Format demandé : float 48 kHz mono ; si le périphérique le refuse (ex. micro
/// verrouillé à 44,1 kHz), on retente avec son format de mixage natif — le
/// callback reçoit alors le taux réel et s'adapte (downmix mono inclus).
/// </summary>
public sealed class CaptureEngine : IDisposable
{
    private WasapiCapture? _capture;
    private MMDeviceEnumerator? _enumerator;

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

    /// <summary>Déclenché quand l'enregistrement s'arrête (erreur incluse).</summary>
    public event EventHandler<string>? Stopped;
    public string? LastError { get; private set; }

    /// <summary>Description du format réellement utilisé par la dernière capture.</summary>
    public string LastStartedFormat { get; private set; } = "";

    /// <summary>Liste des périphériques d'entrée actifs.</summary>
    public IReadOnlyList<OutputDeviceInfo> EnumerateDevices()
    {
        var list = new List<OutputDeviceInfo>();
        try
        {
            foreach (var d in Enumer().EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                list.Add(new OutputDeviceInfo("capture|" + d.ID, d.FriendlyName, "Capture"));
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
    /// indisponible. Sert à proposer la sélection mono (canal unique) pour les
    /// micros multi-canaux.
    /// </summary>
    public int? GetChannelCount(string? deviceId)
    {
        try
        {
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
    /// Démarre la capture. Callback : (échantillons entrelacés float, canaux, fréquence d'échantillonnage).
    /// Appelé sur le thread audio : rester rapide.
    /// </summary>
    public void Start(string? deviceId, Action<float[], int, int> onChunk)
    {
        var dev = ResolveDevice(deviceId);
        DiagnosticsLog.Info($"Capture : périphérique « {dev.FriendlyName} » ({deviceId ?? "défaut"})");

        _capture?.Dispose();

        // format demandé : float 48 kHz mono — sinon repli sur le format de mixage natif
        var requested = WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);
        WasapiCapture? capture = null;
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
                var fmt = format;
                onChunk(samples, fmt.Channels, fmt.SampleRate);
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

    private MMDevice ResolveDevice(string? deviceId)
    {
        if (string.IsNullOrEmpty(deviceId) || !deviceId.StartsWith("capture|", StringComparison.Ordinal))
            return Enumer().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console);

        string id = deviceId["capture|".Length..];
        return Enumer().EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                          .FirstOrDefault(d => d.ID == id)
               ?? throw new InvalidOperationException("Micro introuvable (débranché ?).");
    }

    public void Stop()
    {
        try
        {
            _capture?.StopRecording();
        }
        catch
        {
            // ignore
        }
    }

    public void Dispose()
    {
        Stop();
        try
        {
            _capture?.Dispose();
        }
        catch
        {
            // ignore
        }
        _capture = null;
    }
}
