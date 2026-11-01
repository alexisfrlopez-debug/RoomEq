using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Playback;
using RoomEq.Audio.Routing;
using RoomEq.Audio.SystemApo;
using RoomEq.Core.Io;

namespace RoomEq.App.ViewModels;

/// <summary>
/// ViewModel de l'onglet Mixage — v3.9.0 (D26) : LA CONSOLE A ÉTÉ RETIRÉE.
/// Depuis l'EQ système par APO (D24), tout le son de Windows traverse
/// l'égaliseur directement dans le moteur audio — les tranches internes
/// (lecteur, générateur, pont DAW ASIO, périphérique virtuel macOS) et leur
/// bus master doublaient des fonctions déjà couvertes : lecteur et signaux
/// de test de la barre du haut, greffons VST/AU pour les DAW. L'onglet
/// devient le poste de pilotage de l'EQ système : sorties activées, verdict
/// des activations (jamais relégué dans une barre muette), volume de la
/// carte physique d'écoute, preset adapté à la sortie et nettoyage du
/// pilote hérité (v3.6/v3.7).
/// La sortie d'écoute suit la barre du haut (source de vérité unique) ;
/// la couche audio M-AUD-03 reste intacte (défense en profondeur).
/// Décomposition en fichiers partiels : Volume (carte physique), Driver
/// (pilote hérité + migration), Persist (mixer.json), SystemApo.
/// </summary>
public partial class MixerViewModel : ObservableObject, IOutputPresetMixerView
{
    private readonly AudioDeviceCatalog _catalog = new();
    private readonly MixerStore _store;
    private readonly DispatcherTimer _timer;
    private readonly EndpointVolumeController _endpointVolume = new();

    // synchro des deux curseurs de volume (barre du haut + Mixage) : une seule
    // source de vérité (le volume Windows de la carte), tous les affichages suivent
    private readonly Action<double>? _syncTopBarVolume; // recale le curseur de la barre du haut
    private bool _syncingRoutedVolume;                  // écriture PROGRAMMATIQUE de RoutedVolume en cours
    private bool _routedVolumeReady;                    // premier état réel reçu (le binding initial ne doit rien écrire)
    private double _lastEndpointSeen = -1;              // détection des changements faits hors de l'application

    private string? _storedOutputId;

    /// <summary>Sortie d'écoute (suit la barre du haut) : cible du volume
    /// physique, du preset adapté et de la mémorisation de session.</summary>
    [ObservableProperty] private OutputDeviceInfo? selectedOutput;

    // ----- preset automatique par sortie (politique pilotée par le shell) -----

    /// <summary>Options du sélecteur « Preset de la sortie » (remplies par le coordinateur).</summary>
    public ObservableCollection<OutputPresetOption> OutputPresetOptions { get; } = new();

    [ObservableProperty] private OutputPresetOption? selectedOutputPresetOption;

    /// <summary>Application automatique du preset adapté à la sortie d'écoute (calibration > usine reconnue).</summary>
    [ObservableProperty] private bool autoOutputPreset = true;

    /// <summary>Texte d'état du preset de la sortie (affiché sous le sélecteur).</summary>
    [ObservableProperty] private string outputPresetText = "";

    /// <summary>La sortie d'écoute a changé (le shell réévalue le preset).</summary>
    public event Action<OutputDeviceInfo?>? MasterOutputChanged;

    /// <summary>L'utilisateur a choisi une option du sélecteur « Preset de la sortie ».</summary>
    public event Action<OutputPresetOption>? OutputPresetOptionPicked;

    /// <summary>Drapeau anti-boucle pendant le remplissage programmatique du sélecteur.</summary>
    private bool _loadingPresetOptions;

    /// <summary>Protocole de carte virtuelle sondé pour la détection du pilote hérité
    /// (D25) : toujours la carte RoomEq — l'EQ système l'a remplacée.</summary>
    public IVirtualAudioProvider SelectedProvider => VirtualAudioCatalog.Default;

    [ObservableProperty] private VirtualCardStatus virtualCard = new();
    [ObservableProperty] private string statusText = "Onglet Mixage : activez l'EQ système sur vos sorties — tout le son de Windows traversera l'égaliseur.";

    /// <summary>Vrai sur macOS : pilote les adaptations d'interface (pas de pnputil).</summary>
    public bool IsMacOS => OperatingSystem.IsMacOS();

    public MixerViewModel(Action<double>? syncTopBarVolume = null,
        WindowsApoSystem? systemApo = null, MixerStore? store = null)
    {
        _syncTopBarVolume = syncTopBarVolume;
        _store = store ?? new MixerStore();

        // EQ système (APO) — partagé avec le moteur de mesure (bypass pendant
        // les balayages) : créé ici, câblé par le shell via SystemEqBypass
        SystemApo = systemApo ?? new WindowsApoSystem();
        SystemApo.Changed += () => Dispatcher.UIThread.Post(RefreshSystemApoCore);

        // suivi du volume de la carte d'écoute (réglages faits hors de
        // l'application : mixeur Windows, touches du casque)
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background,
            (_, _) => PollExternalEndpointVolume());

        LoadConfig();

        RefreshDevices();
        RefreshSystemApoCore();
        _timer.Start();
    }

    // ================================================================ sortie d'écoute

    /// <summary>
    /// La barre du haut a changé de sortie de lecture : elle devient la sortie
    /// d'écoute — volume physique, preset adapté et mémorisation suivent.
    /// </summary>
    public void FollowTopBarOutput(OutputDeviceInfo? device)
    {
        if (device == null || device.Id == SelectedOutput?.Id) return;
        SelectedOutput = device;
    }

    /// <summary>
    /// Sortie mémorisée par la session précédente (mixer.json), si elle existe
    /// encore — sert de sélection initiale à la barre du haut.
    /// </summary>
    public OutputDeviceInfo? PreferredInitialOutput(IEnumerable<OutputDeviceInfo> outputs)
    {
        if (_storedOutputId == null) return null;
        var preferred = outputs.FirstOrDefault(o => o.Id == _storedOutputId);
        _storedOutputId = null; // consommée : la barre du haut redevient la source de vérité
        return preferred;
    }

    partial void OnSelectedOutputChanged(OutputDeviceInfo? value)
    {
        if (value == null) return;
        bool ok = _endpointVolume.SetTarget(value.Id);
        DiagnosticsLog.Info("Volume physique ciblé : " + value.DisplayName + (ok ? "" : " (non accessible)"));
        RefreshRoutedVolumeDisplay();
        SyncTopBarFromEndpoint(); // la nouvelle carte peut avoir un volume différent : les deux curseurs suivent
        OnPropertyChanged(nameof(RoutedVolumeTarget));
        OnPropertyChanged(nameof(HasRoutedVolume));
        SaveConfig();
        MasterOutputChanged?.Invoke(value);   // réévaluation du preset de la sortie
    }

    partial void OnSelectedOutputPresetOptionChanged(OutputPresetOption? value)
    {
        if (!_loadingPresetOptions && value != null)
            OutputPresetOptionPicked?.Invoke(value);
    }

    /// <summary>Remplit le sélecteur « Preset de la sortie » sans déclencher l'événement utilisateur.</summary>
    public void SetOutputPresetOptions(IEnumerable<OutputPresetOption> options, OutputPresetOption? selected)
    {
        _loadingPresetOptions = true;
        try
        {
            OutputPresetOptions.Clear();
            foreach (var o in options) OutputPresetOptions.Add(o);
            SelectedOutputPresetOption = selected;
        }
        finally
        {
            _loadingPresetOptions = false;
        }
    }

    // ================================================================ périphériques

    /// <summary>Rafraîchissement en cours (bouton ⟳ animé/désactivé).</summary>
    [ObservableProperty] private bool isRefreshingDevices;

    /// <summary>
    /// Version ATTENDUE du rafraîchissement (bouton ⟳, flux d'installation) :
    /// capture en arrière-plan en UNE passe, application sur le thread UI.
    /// </summary>
    [RelayCommand]
    public Task RefreshDevicesAsync() => RefreshDevicesCoreAsync();

    /// <summary>
    /// Point d'entrée historique (démarrage, branchement à chaud, après la
    /// suppression du pilote hérité) : même rafraîchissement SANS l'attendre —
    /// l'interface ne bloque jamais sur une énumération audio.
    /// Hors Windows : application synchrone inchangée (macOS sonde CoreAudio
    /// localement, Linux/CI obtient des listes vides — aucun gel possible,
    /// et les tests restent déterministes).
    /// </summary>
    public void RefreshDevices()
    {
        if (OperatingSystem.IsWindows())
        {
            _ = RefreshDevicesCoreAsync();
            return;
        }
        ApplySnapshot(_catalog.CaptureSnapshot(SelectedProvider));
    }

    private async Task RefreshDevicesCoreAsync()
    {
        if (!await _refreshGate.WaitAsync(0).ConfigureAwait(false))
            return; // une recapture déjà en cours : les clics s'ignorent
        try
        {
            IsRefreshingDevices = true;
            var snap = await Task.Run(() => _catalog.CaptureSnapshot(SelectedProvider)).ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => ApplySnapshot(snap));
        }
        catch
        {
            // un rafraîchissement ne doit jamais faire échouer un clic
        }
        finally
        {
            IsRefreshingDevices = false;
            _refreshGate.Release();
        }
    }

    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    /// <summary>
    /// Applique une photographie de périphériques (thread UI) : état de la
    /// carte virtuelle (pilote hérité D25), migration des défauts et
    /// rafraîchissement des lignes EQ système.
    /// </summary>
    private void ApplySnapshot(AudioDeviceCatalog.DeviceSnapshot snap)
    {
        VirtualCard = snap.VirtualCard;
        OnPropertyChanged(nameof(ShowLegacyDriverCleanup));
        HandleLegacyVirtualDefault(snap);
        RefreshSystemApoCore(); // endpoints ajoutés/retirés à chaud
    }

    // ================================================================ fermeture

    /// <summary>Libère les ressources à la fermeture de l'application.</summary>
    public void Shutdown()
    {
        _endpointVolume.Dispose();
    }
}
