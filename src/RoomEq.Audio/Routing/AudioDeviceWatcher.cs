using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;
using System.Threading;

namespace RoomEq.Audio.Routing;

/// <summary>
/// Surveille en temps réel les branchements / débranchements de périphériques
/// audio Windows (IMMNotificationClient) et les changements de périphérique
/// par défaut : brancher un casque Bluetooth ou une carte USB pendant que
/// RoomEq est ouvert met à jour toutes les listes de l'application.
///
/// Les événements bruts arrivent en rafale (un branchement Bluetooth en
/// produit cinq à dix) : un anti-rebond d'une seconde regroupe la rafale en
/// UN seul évément <see cref="DevicesChanged"/>. L'événement est levé sur un
/// thread du pool — les abonnés de l'interface doivent marshaler vers le
/// thread UI.
///
/// Non-Windows (macOS) : constructeur inoffensif, aucune notification — la
/// couche appelante interroge périodiquement.
/// </summary>
public sealed class AudioDeviceWatcher : IDisposable
{
    /// <summary>Durée d'anti-rebond : les notifications rafale → un seul événement.</summary>
    public static readonly TimeSpan DebounceDelay = TimeSpan.FromSeconds(1.0);

    private readonly object _gate = new();
    private readonly MMDeviceEnumerator? _enumerator;
    private readonly Client? _client;
    private readonly Timer? _debounce;
    private bool _disposed;

    /// <summary>Vrai si le système émet réellement des notifications (Windows).</summary>
    public bool IsWatching => _enumerator != null;

    /// <summary>
    /// Levé (anti-rebondu, thread du pool) quand un périphérique est ajouté,
    /// retiré, change d'état ou devient / cesse d'être la sortie par défaut.
    /// </summary>
    public event Action? DevicesChanged;

    public AudioDeviceWatcher()
    {
        if (!OperatingSystem.IsWindows())
            return;   // macOS : pas de WASAPI, reste inerte

        try
        {
            _enumerator = new MMDeviceEnumerator();
            _client = new Client(this);
            _enumerator.RegisterEndpointNotificationCallback(_client);
            _debounce = new Timer(_ => Raise(), null, Timeout.Infinite, Timeout.Infinite);
        }
        catch
        {
            // sous-système audio indisponible (session sans service audio) :
            // le surveillant reste inerte, l'application fonctionne au sondage
            _client = null;
            _enumerator?.Dispose();
            _enumerator = null;
            _debounce?.Dispose();
            _debounce = null;
        }
    }

    private void Signal()
    {
        lock (_gate)
        {
            if (_disposed || _debounce == null) return;
            _debounce.Change(DebounceDelay, Timeout.InfiniteTimeSpan);
        }
    }

    private void Raise()
    {
        DevicesChanged?.Invoke();
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _debounce?.Dispose();
        try
        {
            if (_client != null && _enumerator != null)
                _enumerator.UnregisterEndpointNotificationCallback(_client);
        }
        catch
        {
            // le service audio a pu disparaître avant nous : ignorer
        }
        _enumerator?.Dispose();
    }

    /// <summary>Implémentation COM des notifications WASAPI (traduit vers Signal).</summary>
    private sealed class Client(AudioDeviceWatcher owner) : IMMNotificationClient
    {
        public void OnDeviceStateChanged(string deviceId, DeviceState deviceState) => owner.Signal();
        public void OnDeviceAdded(string deviceId) => owner.Signal();
        public void OnDeviceRemoved(string deviceId) => owner.Signal();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => owner.Signal();
        public void OnPropertyValueChanged(string deviceId, PropertyKey propertyKey) => owner.Signal();
    }
}
