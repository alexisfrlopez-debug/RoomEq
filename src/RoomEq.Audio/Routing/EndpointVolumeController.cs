using NAudio.CoreAudioApi;

namespace RoomEq.Audio.Routing;

/// <summary>
/// Contrôle du VOLUME WINDOWS d'un périphérique de sortie physique (spec
/// I-AUD-03, API IAudioEndpointVolume). Nécessaire parce que le fader de la
/// carte VIRTUELLE (RoomEq Input) n'a aucun effet sur le niveau perçu : le
/// réglage utile est celui de la carte physique qui rend réellement le son —
/// la sortie master choisie dans l'onglet Mixage. Cible modifiable à chaud.
/// </summary>
public sealed class EndpointVolumeController : IDisposable
{
    private MMDevice? _device;
    private string? _rawDeviceId;
    private bool _disposed;

    /// <summary>
    /// Conversion du curseur « Volume » de la barre supérieure (0..1.5) en volume
    /// Windows du périphérique (0..1) : la plage utile 0–100 % règle la carte
    /// physique ; au-delà de 100 %, le volume Windows reste au maximum et seule
    /// l'amplification du lecteur interne augmente (150 % = +3,5 dB).
    /// </summary>
    public static double SliderToEndpoint(double sliderVolume) => Math.Clamp(sliderVolume, 0.0, 1.0);

    /// <summary>Le volume du périphérique cible est-il accessible ?</summary>
    public bool Available { get; private set; }

    /// <summary>
    /// Nombre de tentatives de re-acquisition (correctif v3.9.5) — instrument
    /// de test : prouve que l'auto-guérison est bien câblée au polling.
    /// </summary>
    public int ReacquireAttempts { get; private set; }

    /// <summary>Nom convivial du périphérique cible (pour l'affichage).</summary>
    public string? TargetName { get; private set; }

    /// <summary>
    /// Désigne le périphérique dont on pilote le volume. `outputId` est un
    /// identifiant d'<c>OutputDeviceInfo</c> (« wasapi|{id} » ou « asio|{driver} ») ;
    /// les pilotes ASIO ne passent pas par le mixeur Windows : non contrôlables.
    /// Renvoi vrai si le volume est accessible.
    /// </summary>
    public bool SetTarget(string? outputId)
    {
        string? raw = outputId != null && outputId.StartsWith("wasapi|", StringComparison.Ordinal)
            ? outputId["wasapi|".Length..]
            : null;

        if (string.IsNullOrEmpty(raw))
        {
            ReleaseDevice();
            Available = false;
            TargetName = null;
            return false;
        }

        if (_rawDeviceId == raw && Available) return true;

        ReleaseDevice();
        _rawDeviceId = raw;
        try
        {
            var dev = new MMDeviceEnumerator()
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .FirstOrDefault(d => d.ID == raw);
            if (dev == null)
            {
                Available = false;
                TargetName = null;
                return false;
            }
            _device = dev;
            TargetName = dev.FriendlyName;
            Available = true;
            _ = VolumeScalar; // sonde : certains périphériques refusent l'accès au volume
            return true;
        }
        catch
        {
            ReleaseDevice();
            Available = false;
            TargetName = null;
            return false;
        }
    }

    /// <summary>Volume maître du périphérique cible, 0..1 (−1 si indisponible).</summary>
    public double VolumeScalar
    {
        get
        {
            var dev = _device;
            if (!Available || dev == null) return -1;
            try { return Math.Clamp(dev.AudioEndpointVolume.MasterVolumeLevelScalar, 0.0, 1.0); }
            catch { Available = false; return -1; }
        }
    }

    public bool Muted
    {
        get
        {
            var dev = _device;
            if (!Available || dev == null) return false;
            try { return dev.AudioEndpointVolume.Mute; }
            catch { Available = false; return false; }
        }
    }

    /// <summary>
    /// Re-acquiert le MÊME périphérique (correctif v3.9.5 — « le volume ne
    /// répond plus » après une activation de l'EQ système). FAIT ÉTABLI : le
    /// redémarrage du service audio (`net stop/start audiosrv`, inévitable à
    /// chaque activation/désactivation APO) et les reconnexions Bluetooth
    /// invalident les objets COM WASAPI détenus par l'application — les
    /// lectures lèvent (→ plus jamais Available=false définitif : on retente)
    /// ou gèlent sur la dernière valeur, et les écritures se perdent vers un
    /// endpoint mort : « la barre de volume ne bouge plus / n'agit plus ».
    /// <see cref="SetTarget"/> n'est appelé qu'au CHANGEMENT de sortie : la
    /// cible restait morte pour toujours. Ici : ré-énumération + nouveau
    /// MMDevice + sonde — l'ancien est libéré, l'état Available est recalculé.
    /// Idempotent, sans exception ; false si le périphérique est absent.
    /// </summary>
    public bool Reacquire()
    {
        ReacquireAttempts++;
        string? raw = _rawDeviceId;
        if (_disposed || string.IsNullOrEmpty(raw))
            return false;
        try
        {
            var dev = new MMDeviceEnumerator()
                .EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                .FirstOrDefault(d => d.ID == raw);
            if (dev == null)
                return false; // pas encore de retour (BT en reconnexion, service en redémarrage)
            var old = _device;
            _device = dev;
            try
            {
                TargetName = dev.FriendlyName;
                _ = Math.Clamp(dev.AudioEndpointVolume.MasterVolumeLevelScalar, 0.0, 1.0); // sonde
                Available = true;
            }
            catch
            {
                Available = false; // endpoint présent mais muet (rare) : retenter plus tard
            }
            try { old?.Dispose(); } catch { /* libération tolérante */ }
            return Available;
        }
        catch
        {
            return false; // énumération indisponible (service en cours de redémarrage)
        }
    }
    /// <summary>Règle le volume (0..1). Renvoi vrai si appliqué.</summary>
    public bool SetVolumeScalar(double value)
    {
        var dev = _device;
        if (!Available || dev == null) return false;
        try
        {
            dev.AudioEndpointVolume.MasterVolumeLevelScalar = (float)Math.Clamp(value, 0.0, 1.0);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Bascule la sourdine. Renvoi le nouvel état (null si indisponible).</summary>
    public bool? ToggleMute()
    {
        var dev = _device;
        if (!Available || dev == null) return null;
        try
        {
            dev.AudioEndpointVolume.Mute = !dev.AudioEndpointVolume.Mute;
            return dev.AudioEndpointVolume.Mute;
        }
        catch
        {
            return null;
        }
    }

    private void ReleaseDevice()
    {
        try { _device?.Dispose(); } catch { /* libération tolérante */ }
        _device = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ReleaseDevice();
    }
}
