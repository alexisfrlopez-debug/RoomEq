using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Asio;
using RoomEq.Audio.Mac;
using RoomEq.Audio.Playback;
using RoomEq.Audio.Routing;
using RoomEq.Audio.SystemApo;
using RoomEq.Core.Io;

namespace RoomEq.App.ViewModels;

/// <summary>
/// ViewModel de la table de mixage (onglet Mixage) — v3.8.0 (D25) : la
/// console n'expose plus que ses sources internes (lecteur, générateur, pont
/// DAW ASIO, périphérique virtuel macOS), chacune avec son égaliseur propre,
/// fader, pan, mute, solo et VU ; bus master avec l'EQ principal synchronisé.
/// Le son du SYSTÈME Windows est traité par l'EQ système APO (I-AUD-09),
/// sans carte virtuelle ni boucle — les tranches « entrée/sortie » ont été
/// retirées. Sur macOS (pas d'APO), le routage par périphérique virtuel
/// reste la voie principale — spec I-APP-03 v2.
/// Décomposition v3 en fichiers partiels par concern :
/// Routing (console + migration D25), Volume (carte physique), Driver
/// (suppression du pilote hérité), Persist (mixer.json), SystemApo.
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

    // rafraîchissement des périphériques : UNE recapture à la fois — les clics
    // en rafale (⟳, branchements à chaud) s'ignorent au lieu de s'empiler
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

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
    private string? _lastAppliedOutputId;

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

    /// <summary>
    /// Protocole de carte virtuelle sondé pour la détection du pilote hérité
    /// (D25) : toujours la carte RoomEq — le sélecteur de protocole a disparu
    /// avec le routage Windows (l'EQ système APO le remplace).
    /// </summary>
    public IVirtualAudioProvider SelectedProvider => VirtualAudioCatalog.Default;

    [ObservableProperty] private VirtualCardStatus virtualCard = new();
    [ObservableProperty] private string virtualStatusText = "";
    [ObservableProperty] private string routingStatusText = "Console à l'arrêt.";
    [ObservableProperty] private string asioStatusText = "Pont ASIO à l'arrêt.";
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private ChannelViewModel? selectedChannel;
    [ObservableProperty] private TrackEqViewModel trackEq = new();
    [ObservableProperty] private string statusText = "Onglet Mixage : activez l'EQ système sur vos sorties, ou démarrez la console pour le lecteur, le générateur et les DAW.";

    /// <summary>Vrai sur macOS : pilote les adaptations d'interface (pas de pnputil, pont périphérique).</summary>
    public bool IsMacOS => OperatingSystem.IsMacOS();

    public MixerViewModel(PlaybackEngine engine, Action<double>? syncTopBarVolume = null,
        WindowsApoSystem? systemApo = null)
    {
        _engine = engine;
        _syncTopBarVolume = syncTopBarVolume;

        // EQ système (APO) — partagé avec le moteur de mesure (bypass pendant
        // les balayages) : créé ici, câblé par le shell via SystemEqBypass
        SystemApo = systemApo ?? new WindowsApoSystem();
        SystemApo.Changed += () => Dispatcher.UIThread.Post(RefreshSystemApoCore);

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
        RefreshSystemApoCore();
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

    /// <summary>L'EQ système (APO) couvre-t-il la sortie master actuelle ?
    /// (cache recalculé au changement de sortie et à chaque état APO).</summary>
    public bool ApoOnMaster => _apoOnMaster;
    private bool _apoOnMaster;

    /// <summary>Texte de la tranche MASTER : qui applique l'EQ principal.</summary>
    public string MasterEqSourceText => _apoOnMaster
        ? "EQ appliqué par l'EQ système (moteur Windows)"
        : "EQ principal appliqué (bus master)";

    /// <summary>
    /// Recalcule le drapeau « l'APO couvre la sortie master » (thread UI).
    /// D25 : dans ce cas le bus master CONTOURNE son propre EQ — la courbe est
    /// déjà appliquée au niveau du périphérique par le moteur audio Windows,
    /// l'appliquer deux fois doublerait la correction (conflit réel des anciennes
    /// tranches « sortie » en boucle).
    /// </summary>
    private void RefreshApoOnMaster()
    {
        bool apo = false;
        if (!IsMacOS && SystemApo.IsReady
            && SelectedOutput?.Id is { } id && id.StartsWith("wasapi|", StringComparison.Ordinal))
        {
            string key = ApoConstants.EndpointKeyFromMmdeviceId(id["wasapi|".Length..]);
            apo = SystemApo.EnumerateEndpoints().Any(e =>
                e.Enabled && string.Equals(e.Key, key, StringComparison.OrdinalIgnoreCase));
        }
        if (apo != _apoOnMaster)
        {
            _apoOnMaster = apo;
            OnPropertyChanged(nameof(ApoOnMaster));
            OnPropertyChanged(nameof(MasterEqSourceText));
            SyncMasterEqFromMain();
        }
    }

    /// <summary>Le bus master suit en permanence l'égaliseur de l'onglet Égaliseur
    /// — et l'EQ système (APO) reçoit la même courbe quand il est actif.</summary>
    private void SyncMasterEqFromMain()
    {
        try
        {
            var preset = _engine.Eq.Preset;
            Mixer.MasterEq.SetPreset(preset);
            // D25 : APO actif sur la sortie master = la courbe y est déjà
            // appliquée par le moteur audio Windows → le bus master la contourne
            // (sinon double correction pour le lecteur, le générateur et le DAW).
            Mixer.MasterEq.Bypass = _engine.Eq.Bypass || _apoOnMaster;
            SystemApo.PushCurve(preset, _engine.Eq.Bypass);
        }
        catch
        {
            // la chaîne principale peut être momentanément occupée : ignorer
        }
    }

    private void RefreshTransportState()
    {
        IsRunning = Mixer.IsRunning;
        RoutingStatusText = IsMacOS
            ? IsRunning
                ? "● Routage actif — le son macOS traverse l'égaliseur"
                : "○ Routage à l'arrêt"
            : IsRunning
                ? "● Console active — lecteur, générateur et DAW traversent l'égaliseur"
                : "○ Console à l'arrêt";
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
    /// Point d'entrée historique (démarrage, arrêt de routage, changement de
    /// protocole) : lance le même rafraîchissement SANS l'attendre —
    /// l'interface ne bloque plus JAMAIS sur une énumération audio
    /// (correctif v3.7.0 : chaque clic gelait l'application le temps de six
    /// balayages COM + du chargement des pilotes ASIO).
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
        if (IsMacOS)
        {
            ApplyMacDevices();
            return;
        }
        var snap = _catalog.CaptureSnapshot(SelectedProvider);
        ApplySnapshot(snap, Mixer.EnumerateOutputs());
    }

    private async Task RefreshDevicesCoreAsync()
    {
        if (!await _refreshGate.WaitAsync(0).ConfigureAwait(false))
            return; // une recapture déjà en cours : les clics s'ignorent
        try
        {
            IsRefreshingDevices = true;
            var snap = await Task.Run(() => _catalog.CaptureSnapshot(SelectedProvider)).ConfigureAwait(false);
            var outputs = await Task.Run(() => ConsoleMixer.EnumerateOutputs(snap.RenderDevices))
                .ConfigureAwait(false);
            await Dispatcher.UIThread.InvokeAsync(() => ApplySnapshot(snap, outputs));
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

    /// <summary>Chemin macOS : sonde le greffon, puis listes CoreAudio.</summary>
    private void ApplyMacDevices()
    {
        VirtualCard = MacAudioBridge.Probe();
        OnPropertyChanged(nameof(ShowLegacyDriverCleanup));
        VirtualStatusText = !VirtualCard.PartiallyInstalled
            ? "Greffon RoomEqHAL non détecté — réinstallez le paquet RoomEq (.pkg)."
            : VirtualCard.FullyRouted
                ? "Périphérique virtuel actif : « RoomEq Virtual » est la sortie par défaut de macOS"
                : "Périphérique virtuel installé : choisissez « RoomEq Virtual » comme sortie macOS (Réglages Système › Son)";

        RebuildOutputs();
        RebuildAvailableSources();
    }

    /// <summary>Applique une photographie de périphériques (thread UI).</summary>
    private void ApplySnapshot(AudioDeviceCatalog.DeviceSnapshot snap,
        IReadOnlyList<OutputDeviceInfo> outputs)
    {
        VirtualCard = snap.VirtualCard;
        OnPropertyChanged(nameof(ShowLegacyDriverCleanup));

        RebuildOutputs(outputs);
        RebuildAvailableSources();

        HandleLegacyVirtualDefault(snap);
    }

    // ------------------------------------------------------------ pilote hérité (D25)

    /// <summary>Le bouton de suppression du pilote hérité est-il proposé ?</summary>
    public bool ShowLegacyDriverCleanup
        => !IsMacOS && OperatingSystem.IsWindows() && VirtualCard.PartiallyInstalled;

    /// <summary>Migration D25 déjà traitée (une seule tentative par session).</summary>
    private bool _legacyDefaultsHandled;

    /// <summary>
    /// Migration D25 (Windows, une fois) : une installation issue de v3.6/v3.7
    /// peut avoir laissé l'entrée de la carte virtuelle (« RoomEq Input »)
    /// comme sortie PAR DÉFAUT de Windows — plus aucune piste ne la capte
    /// désormais, ce serait un silence total. Si la sortie d'origine a été
    /// mémorisée par l'ancien routage (mixer.json), elle est restaurée ;
    /// sinon l'utilisateur est guidé (l'EQ remplace le routage, le pilote peut
    /// être supprimé via le bouton de maintenance).
    /// </summary>
    private void HandleLegacyVirtualDefault(AudioDeviceCatalog.DeviceSnapshot snap)
    {
        if (_legacyDefaultsHandled || IsMacOS || !OperatingSystem.IsWindows())
            return;
        if (snap.VirtualCard.CableInputId == null)
            return; // pas de carte détectée à ce passage (branchement à chaud possible)
        if (!snap.VirtualCard.IsDefaultRender)
        {
            _legacyDefaultsHandled = true; // la carte n'est pas la sortie par défaut : rien à faire
            return;
        }

        if (!string.IsNullOrEmpty(_savedRenderId))
        {
            try
            {
                if (DefaultDeviceSetter.SetDefaultRender(_savedRenderId))
                {
                    DiagnosticsLog.Info("D25 : sortie par défaut de Windows restaurée "
                        + "depuis la carte virtuelle héritée.");
                    StatusText = "Sortie par défaut de Windows restaurée sur votre périphérique "
                        + "d'origine — la carte virtuelle n'est plus utilisée (l'EQ système la remplace).";
                }
                if (!string.IsNullOrEmpty(_savedCaptureId))
                    DefaultDeviceSetter.SetDefaultCapture(_savedCaptureId);
            }
            catch
            {
                // la migration ne doit jamais interrompre le démarrage
            }
            _savedRenderId = null;
            _savedCaptureId = null;
            SaveConfig();
        }
        else
        {
            StatusText = "« " + snap.VirtualCard.CableInputName
                + " » est encore la sortie par défaut de Windows mais RoomEq ne l'utilise plus : "
                + "choisissez vos enceintes ou votre casque (sélecteur MASTER ou panneau son), "
                + "puis supprimez l'ancien pilote ci-dessous.";
        }
        _legacyDefaultsHandled = true;
    }

    private void RebuildOutputs(IReadOnlyList<OutputDeviceInfo>? preCaptured = null)
    {
        var list = preCaptured ?? Mixer.EnumerateOutputs();
        Outputs.Clear();
        foreach (var o in list) Outputs.Add(o);

        if (SelectedOutput != null && list.Any(o => o.Id == SelectedOutput.Id))
        {
            AnnotateOutputConflicts();
            return;
        }
        // priorité : sortie de la session précédente, sinon une sortie sans
        // conflit ni câble virtuel — jamais une sortie déjà routée en entrée
        var preferred = list.FirstOrDefault(o => o.Id == _storedOutputId && o.Selectable)
                        ?? list.FirstOrDefault(o => o.Selectable && !AudioDeviceCatalog.IsVirtualRender(o.DisplayName))
                        ?? list.FirstOrDefault(o => o.Selectable)
                        ?? list.FirstOrDefault();
        SelectedOutput = preferred;
        _storedOutputId = null;
        AnnotateOutputConflicts();
    }

    /// <summary>
    /// Met à jour le marquage anti-boucle des sorties (spec I-AUD-03 v2) : une
    /// sortie captée par une piste reçoit un motif de blocage (grisée dans le
    /// sélecteur, refusée au routage) ; la libération d'une piste la rend à
    /// nouveau choisissable. L'égalité de valeur des enregistrements conserve
    /// la sélection courante.
    /// </summary>
    private void AnnotateOutputConflicts()
    {
        for (int i = 0; i < Outputs.Count; i++)
        {
            var o = Outputs[i];
            string? reason = Mixer.OutputConflict(o.Id) is { } title
                ? "captée par la piste « " + title + " »"
                : null;
            if (o.BlockedReason != reason)
                Outputs[i] = o with { BlockedReason = reason };
        }
    }

    private void RebuildAvailableSources(IReadOnlyList<TrackSource>? captureSources = null,
        IReadOnlyList<TrackSource>? loopbackSources = null)
    {
        // D25 : uniquement les sources internes — les captures (« entrée ») et
        // boucles (« sortie ») de Windows ne sont plus proposées : l'EQ système
        // APO traite le son du système sans console, sans pilote, sans boucle.
        AvailableSources.Clear();
        if (IsMacOS)
        {
            if (MacAudioBridge.PluginInstalled)
                AvailableSources.Add(TrackSource.MacVirtualSource());
        }
        else
        {
            AvailableSources.Add(TrackSource.AsioBridgeSource());
        }
        AvailableSources.Add(TrackSource.InternalPlayer());
        AvailableSources.Add(TrackSource.InternalGenerator());
    }

    partial void OnSelectedOutputChanged(OutputDeviceInfo? value)
    {
        if (value == null) return;
        try
        {
            Mixer.SetOutput(value.Id);
        }
        catch (Exception ex)
        {
            // sortie refusée par la garde anti-boucle : message actionnable et
            // retour à la sortie précédente (jamais de routage en boucle)
            StatusText = "Sortie master refusée : " + ex.Message;
            SelectedOutput = Outputs.FirstOrDefault(o => o.Id == _lastAppliedOutputId);
            return;
        }
        _lastAppliedOutputId = value.Id;
        OutputInfoText = $"Sortie master : {value.DisplayName}";
        bool ok = _endpointVolume.SetTarget(value.Id);
        DiagnosticsLog.Info("Volume physique ciblé : " + value.DisplayName + (ok ? "" : " (non accessible)"));
        RefreshRoutedVolumeDisplay();
        SyncTopBarFromEndpoint(); // la nouvelle carte peut avoir un volume différent : les deux curseurs suivent
        OnPropertyChanged(nameof(RoutedVolumeTarget));
        OnPropertyChanged(nameof(HasRoutedVolume));
        RefreshApoOnMaster();     // D25 : l'EQ système peut couvrir (ou non) la nouvelle sortie
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
        if (MixerTrackPolicy.IsLegacyDeviceSource(source.Kind))
        {
            // défense en profondeur D25 : la vue ne propose plus ces sources,
            // la persistance les ignore — un appel direct est refusé avec un
            // message actionnable plutôt que de recréer les conflits d'avant.
            StatusText = "Pistes d'entrée/sortie retirées : le son du système est traité "
                + "par l'EQ système (sans pilote ni boucle).";
            return;
        }
        try
        {
            var ch = Mixer.AddChannel(source);
            var vm = new ChannelViewModel(ch, () => SaveConfig());
            Channels.Add(vm);
            AnnotateOutputConflicts();
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
        AnnotateOutputConflicts();
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
