using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Asio;
using RoomEq.Audio.Mac;
using RoomEq.Audio.Playback;
using RoomEq.Audio.Routing;
using RoomEq.Core.Io;

namespace RoomEq.App.ViewModels;

/// <summary>
/// ViewModel de la table de mixage (onglet Mixage) : pistes correspondant à
/// toutes les entrées / sorties de Windows, chacune avec son égaliseur propre,
/// fader, pan, mute, solo et VU ; bus master avec l'EQ principal synchronisé ;
/// routage du son Windows via la carte son virtuelle RoomEq ou en boucle
/// WASAPI native ; piste dédiée pour les DAW via le pont ASIO — spec I-APP-03.
/// Décomposition v3 en fichiers partiels par concern :
/// Routing (chaîne de routage), Volume (carte physique), Driver (installation),
/// Persist (mixer.json).
/// </summary>
public partial class MixerViewModel : ObservableObject, IOutputPresetMixerView
{
    private readonly PlaybackEngine _engine;
    private readonly AudioDeviceCatalog _catalog = new();
    private readonly MixerStore _store = new();
    private readonly DispatcherTimer _timer;
    private readonly EndpointVolumeController _endpointVolume = new();
    private readonly DriverSetupManager _driverSetup = new();
    private DispatcherTimer? _eqSyncDebounce;

    // synchro des deux curseurs de volume (barre du haut + Mixage) : une seule
    // source de vérité (le volume Windows de la carte), tous les affichages suivent
    private readonly Action<double>? _syncTopBarVolume; // recale le curseur de la barre du haut
    private bool _syncingRoutedVolume;                  // écriture PROGRAMMATIQUE de RoutedVolume en cours
    private bool _routedVolumeReady;                    // premier état réel reçu (le binding initial ne doit rien écrire)
    private double _lastEndpointSeen = -1;              // détection des changements faits hors de l'application

    /// <summary>Moteur audio de la console.</summary>
    public ConsoleMixer Mixer { get; } = new();

    public ObservableCollection<ChannelViewModel> Channels { get; } = new();

    /// <summary>Sources ajoutables : toutes les entrées + sorties (loopback) de Windows + internes.</summary>
    public ObservableCollection<TrackSource> AvailableSources { get; } = new();

    /// <summary>Destinations du bus master (sorties WASAPI + ASIO).</summary>
    public ObservableCollection<OutputDeviceInfo> Outputs { get; } = new();

    private string? _storedOutputId;
    private bool _initializing;

    [ObservableProperty] private OutputDeviceInfo? selectedOutput;

    // ----- preset automatique par sortie (politique pilotée par le shell) -----

    /// <summary>Options du sélecteur « Preset de la sortie » (remplies par le coordinateur).</summary>
    public ObservableCollection<OutputPresetOption> OutputPresetOptions { get; } = new();

    [ObservableProperty] private OutputPresetOption? selectedOutputPresetOption;

    /// <summary>Application automatique du preset adapté à la sortie master (calibration > usine reconnue).</summary>
    [ObservableProperty] private bool autoOutputPreset = true;

    /// <summary>Texte d'état du preset de la sortie (affiché sous le sélecteur).</summary>
    [ObservableProperty] private string outputPresetText = "";

    /// <summary>La sortie master a changé (le shell réévalue le preset).</summary>
    public event Action<OutputDeviceInfo?>? MasterOutputChanged;

    /// <summary>L'utilisateur a choisi une option du sélecteur « Preset de la sortie ».</summary>
    public event Action<OutputPresetOption>? OutputPresetOptionPicked;

    /// <summary>Drapeau anti-boucle pendant le remplissage programmatique du sélecteur.</summary>
    private bool _loadingPresetOptions;

    /// <summary>Protocoles de carte son virtuelle pris en charge (sélecteur).</summary>
    public ObservableCollection<IVirtualAudioProvider> VirtualProviders { get; } =
        new(VirtualAudioCatalog.All);

    [ObservableProperty] private IVirtualAudioProvider selectedProvider = VirtualAudioCatalog.Default;
    [ObservableProperty] private VirtualCardStatus virtualCard = new();
    [ObservableProperty] private string virtualStatusText = "Recherche de la carte son virtuelle…";
    [ObservableProperty] private string routingStatusText = "Routage à l'arrêt.";
    [ObservableProperty] private string asioStatusText = "Pont ASIO à l'arrêt.";
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private ChannelViewModel? selectedChannel;
    [ObservableProperty] private TrackEqViewModel trackEq = new();
    [ObservableProperty] private string statusText = "Onglet Mixage : ajoutez des pistes ou routez le son Windows vers l'égaliseur.";

    /// <summary>Vrai sur macOS : pilote les adaptations d'interface (pas de pnputil, pont périphérique).</summary>
    public bool IsMacOS => OperatingSystem.IsMacOS();

    public MixerViewModel(PlaybackEngine engine, Action<double>? syncTopBarVolume = null)
    {
        _engine = engine;
        _syncTopBarVolume = syncTopBarVolume;

        if (IsMacOS)
        {
            // la boucle WASAPI est propre à Windows : un seul fournisseur sur macOS
            VirtualProviders.Clear();
            VirtualProviders.Add(VirtualAudioCatalog.Default);
            SelectedProvider = VirtualAudioCatalog.Default;
        }

        Mixer.ErrorOccurred += msg => Dispatcher.UIThread.Post(() =>
        {
            StatusText = "Mixage : " + msg;
        });
        Mixer.StateChanged += () => Dispatcher.UIThread.Post(RefreshTransportState);

        // VU-mètres et suivi d'état
        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background,
            (_, _) => Tick());

        // Sélection de tranche → éditeur d'EQ de piste
        PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(SelectedChannel))
                TrackEq.Attach(SelectedChannel?.Channel);
        };

        // édition de l'EQ de piste → LED « EQ actif » de la tranche
        TrackEq.CurveChanged += _ => SelectedChannel?.UpdateEqLed();

        LoadConfigOrDefaults();
        SyncMasterEqFromMain();

        // la synchro EQ principal → bus master est REPOUSSÉE (anti-rebond) : pendant
        // un glissement de poignée ou du préampli, Changed est émis en continu et un
        // rebuild complet des filtres master à chaque pixel serait du gaspillage
        _eqSyncDebounce = new DispatcherTimer(TimeSpan.FromMilliseconds(80), DispatcherPriority.Background,
            (_, _) =>
            {
                _eqSyncDebounce?.Stop();
                SyncMasterEqFromMain();
            });
        _engine.Eq.Changed += () => Dispatcher.UIThread.Post(() =>
        {
            _eqSyncDebounce?.Stop();
            _eqSyncDebounce?.Start();
        });

        RefreshDevices();
        _timer.Start();

        // Pont du périphérique virtuel (macOS) ou ASIO (Windows) : la section
        // partagée doit exister AVANT que le greffon / le DAW ne s'y connecte —
        // démarrage systématique, aucune ressource n'est occupée tant que
        // personne ne s'y attache.
        try
        {
            if (IsMacOS)
                MacAudioBridge.DeviceBridge.Start();
            else
                AsioBridgeHost.Instance.Start();
        }
        catch (Exception ex)
        {
            AsioStatusText = "Pont indisponible : " + ex.Message;
        }
    }

    // ================================================================ EQ master

    /// <summary>Le bus master suit en permanence l'égaliseur de l'onglet Égaliseur.</summary>
    private void SyncMasterEqFromMain()
    {
        try
        {
            var preset = _engine.Eq.Preset;
            Mixer.MasterEq.SetPreset(preset);
            Mixer.MasterEq.Bypass = _engine.Eq.Bypass;
        }
        catch
        {
            // la chaîne principale peut être momentanément occupée : ignorer
        }
    }

    private void RefreshTransportState()
    {
        IsRunning = Mixer.IsRunning;
        RoutingStatusText = IsRunning
            ? "● Routage actif — le son Windows traverse l'égaliseur"
            : "○ Routage à l'arrêt";
    }

    // ================================================================ périphériques

    [RelayCommand]
    public void RefreshDevices()
    {
        if (IsMacOS)
        {
            VirtualCard = MacAudioBridge.Probe();
            OnPropertyChanged(nameof(DriverPackagePresent));
            VirtualStatusText = !VirtualCard.PartiallyInstalled
                ? "Greffon RoomEqHAL non détecté — réinstallez le paquet RoomEq (.pkg)."
                : VirtualCard.FullyRouted
                    ? "Périphérique virtuel actif : « RoomEq Virtual » est la sortie par défaut de macOS"
                    : "Périphérique virtuel installé : choisissez « RoomEq Virtual » comme sortie macOS (Réglages Système › Son)";

            RebuildOutputs();
            RebuildAvailableSources();
            return;
        }

        VirtualCard = _catalog.ProbeVirtualCard(SelectedProvider);
        OnPropertyChanged(nameof(DriverPackagePresent));
        VirtualStatusText = !VirtualCard.PartiallyInstalled
            ? SelectedProvider.IsPreinstalled
                ? "Boucle WASAPI native : aucune installation nécessaire"
                : SelectedProvider.DisplayName + " non installé"
            : !VirtualCard.Installed
                ? SelectedProvider.DisplayName + " : un seul côté détecté ("
                  + (VirtualCard.CableInputFound ? VirtualCard.CableInputName : VirtualCard.CableOutputName) + ")"
                : VirtualCard.FullyRouted
                    ? $"Carte virtuelle active : « {VirtualCard.CableInputName} » est la sortie par défaut de Windows"
                    : $"Carte virtuelle installée : « {VirtualCard.CableInputName} » — pas encore sortie par défaut";

        RebuildOutputs();
        RebuildAvailableSources();
    }

    private void RebuildOutputs()
    {
        var list = Mixer.EnumerateOutputs();
        Outputs.Clear();
        foreach (var o in list) Outputs.Add(o);

        if (SelectedOutput != null && list.Any(o => o.Id == SelectedOutput.Id)) return;
        // priorité : sortie de la session précédente, sinon une sortie non virtuelle
        var preferred = list.FirstOrDefault(o => o.Id == _storedOutputId)
                        ?? list.FirstOrDefault(o => !AudioDeviceCatalog.IsVirtualRender(o.DisplayName))
                        ?? list.FirstOrDefault();
        SelectedOutput = preferred;
        _storedOutputId = null;
    }

    private void RebuildAvailableSources()
    {
        AvailableSources.Clear();
        if (IsMacOS)
        {
            if (MacAudioBridge.PluginInstalled)
                AvailableSources.Add(TrackSource.MacVirtualSource());
        }
        else
        {
            foreach (var s in _catalog.EnumerateCaptureSources()) AvailableSources.Add(s);
            foreach (var s in _catalog.EnumerateLoopbackSources()) AvailableSources.Add(s);
            AvailableSources.Add(TrackSource.AsioBridgeSource());
        }
        AvailableSources.Add(TrackSource.InternalPlayer());
        AvailableSources.Add(TrackSource.InternalGenerator());
    }

    partial void OnSelectedOutputChanged(OutputDeviceInfo? value)
    {
        if (value == null) return;
        Mixer.SetOutput(value.Id);
        OutputInfoText = $"Sortie master : {value.DisplayName}";
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

    // ================================================================ tranches

    [RelayCommand]
    private void AddTrack(TrackSource? source)
    {
        if (source == null) return;
        try
        {
            var ch = Mixer.AddChannel(source);
            var vm = new ChannelViewModel(ch, () => SaveConfig());
            Channels.Add(vm);
            SaveConfig();
            StatusText = "Piste ajoutée : " + vm.Title;
        }
        catch (Exception ex)
        {
            StatusText = "Ajout impossible : " + ex.Message;
        }
    }

    [RelayCommand]
    private void RemoveTrack(ChannelViewModel? vm)
    {
        if (vm == null) return;
        if (SelectedChannel == vm) SelectedChannel = null;
        Mixer.RemoveChannel(vm.Channel);
        Channels.Remove(vm);
        SaveConfig();
        StatusText = "Piste retirée : " + vm.Title;
    }

    [RelayCommand]
    private void SelectTrack(ChannelViewModel? vm) => SelectedChannel = vm;

    /// <summary>Rafraîchit la LED « EQ actif » d'une tranche (appelé après édition).</summary>
    public void UpdateEqLedFor(ChannelViewModel vm) => vm.UpdateEqLed();

    // ================================================================ minuterie (VU)

    private void Tick()
    {
        // crêtes de tranches avec décroissance douce
        foreach (var vm in Channels)
            vm.UpdateMeter();
        UpdateMasterMeter();
        CheckCaptureHealth();
        PollExternalEndpointVolume();
        AsioStatusText = IsMacOS
            ? MacAudioBridge.DeviceBridge.StatusText
            : AsioBridgeHost.Instance.StatusText;
    }

    private void UpdateMasterMeter()
    {
        float l = Volatile.Read(ref Mixer.MasterPeakL);
        float r = Volatile.Read(ref Mixer.MasterPeakR);

        // rien ne change si le bus est muet et les crêtes déjà nulles :
        // éviter d'invalider l'UI 12 fois par seconde pour rien
        if (l == 0 && r == 0 && MasterDispL == 0 && MasterDispR == 0
            && MasterHoldL == 0 && MasterHoldR == 0) return;

        MasterDispL = Math.Max(l, MasterDispL * 0.82f);
        MasterDispR = Math.Max(r, MasterDispR * 0.82f);
        MasterHoldL = Math.Max(l, MasterHoldL * 0.985f);
        MasterHoldR = Math.Max(r, MasterHoldR * 0.985f);
        if (MasterHoldL < 0.004) MasterHoldL = 0;
        if (MasterHoldR < 0.004) MasterHoldR = 0;
        OnPropertyChanged(nameof(MasterDispL));
        OnPropertyChanged(nameof(MasterDispR));
        OnPropertyChanged(nameof(MasterHoldL));
        OnPropertyChanged(nameof(MasterHoldR));
    }

    public float MasterDispL { get; private set; }
    public float MasterDispR { get; private set; }
    public float MasterHoldL { get; private set; }
    public float MasterHoldR { get; private set; }

    private void CheckCaptureHealth()
    {
        foreach (var vm in Channels)
        {
            var err = vm.Channel.CaptureStoppedWithErrors;
            if (err != null)
            {
                vm.Channel.CaptureStoppedWithErrors = null;
                StatusText = $"Piste « {vm.Title} » : capture interrompue ({err}).";
            }
        }
    }

    // ================================================================ fermeture

    /// <summary>Libère les ressources à la fermeture de l'application.</summary>
    public void Shutdown()
    {
        try { AsioBridgeHost.Instance.Stop(); } catch { /* fermeture */ }
        try { MacAudioBridge.DeviceBridge.Stop(); } catch { /* fermeture */ }
        _endpointVolume.Dispose();
    }
}
