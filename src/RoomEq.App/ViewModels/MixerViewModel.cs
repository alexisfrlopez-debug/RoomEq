using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Playback;
using RoomEq.Audio.Routing;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using RoomEq.Core.Io;

namespace RoomEq.App.ViewModels;

/// <summary>
/// ViewModel de la table de mixage (onglet Mixage) : pistes correspondant à toutes
/// les entrées / sorties de Windows, chacune avec son égaliseur propre, fader, pan,
/// mute, solo et VU ; bus master avec l'EQ principal synchronisé ; routage du son
/// Windows via une carte son virtuelle (VB-CABLE, VoiceMeeter, VAC — installation
/// automatique depuis les sites officiels) ou en boucle WASAPI native.
/// </summary>
public partial class MixerViewModel : ObservableObject
{
    private readonly PlaybackEngine _engine;
    private readonly AudioDeviceCatalog _catalog = new();
    private readonly MixerStore _store = new();
    private readonly DispatcherTimer _timer;
    private readonly EndpointVolumeController _endpointVolume = new();
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

    /// <summary>Protocoles de carte son virtuelle pris en charge (sélecteur).</summary>
    public ObservableCollection<IVirtualAudioProvider> VirtualProviders { get; } =
        new(VirtualAudioCatalog.All);

    [ObservableProperty] private IVirtualAudioProvider selectedProvider = VirtualAudioCatalog.Default;
    [ObservableProperty] private VirtualCardStatus virtualCard = new();
    [ObservableProperty] private string virtualStatusText = "Recherche de la carte son virtuelle…";
    [ObservableProperty] private bool isInstalling;
    [ObservableProperty] private string installPhaseText = "";
    [ObservableProperty] private double installPercent;
    [ObservableProperty] private bool installIndeterminate = true;
    [ObservableProperty] private string routingStatusText = "Routage à l'arrêt.";
    [ObservableProperty] private bool isRunning;
    [ObservableProperty] private bool autoStart;
    [ObservableProperty] private bool autoRouteOnInstall = true;
    [ObservableProperty] private bool switchDefaultCapture = true;
    [ObservableProperty] private double masterFader = 1.0;
    [ObservableProperty] private bool masterMute;
    [ObservableProperty] private string masterFaderText = "0,0 dB";
    [ObservableProperty] private string outputInfoText = "";
    [ObservableProperty] private ChannelViewModel? selectedChannel;
    [ObservableProperty] private TrackEqViewModel trackEq = new();
    [ObservableProperty] private string statusText = "Onglet Mixage : ajoutez des pistes ou routez le son Windows vers l'égaliseur.";

    // volume WINDOWS du périphérique physique qui rend le son (celui de la carte
    // virtuelle n'a aucun effet) — piloté aussi au clavier (Ctrl+Alt+↑ / ↓ / M)
    [ObservableProperty] private double routedVolume = -1;
    [ObservableProperty] private bool routedMute;
    [ObservableProperty] private string routedVolumeText = "—";

    /// <summary>Le volume de la sortie physique est-il accessible ?</summary>
    public bool HasRoutedVolume => _endpointVolume.Available;

    /// <summary>Nom du périphérique dont on règle le volume.</summary>
    public string RoutedVolumeTarget => _endpointVolume.TargetName ?? SelectedOutput?.DisplayName ?? "";

    /// <summary>Signalé à chaque changement de volume (affichage OSD plein écran).</summary>
    public event Action<string>? RoutedVolumeFeedback;

    // défauts Windows mémorisés avant bascule (restauration à l'arrêt du routage)
    private string? _savedRenderId;
    private string? _savedCaptureId;

    public MixerViewModel(PlaybackEngine engine, Action<double>? syncTopBarVolume = null)
    {
        _engine = engine;
        _syncTopBarVolume = syncTopBarVolume;

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

    // ================================================================ transport

    [RelayCommand]
    private void ToggleRouting()
    {
        if (IsRunning)
        {
            Mixer.Stop();
            TryRestoreDefaults();
            RefreshTransportState();
            RefreshDevices();
            StatusText = "Routage arrêté — périphériques par défaut de Windows restaurés.";
            return;
        }

        if (StartRoutingChain())
            StatusText = $"Routage actif : Windows → carte virtuelle → EQ → « {SelectedOutput?.DisplayName} » "
                       + $"({Mixer.OutputLatencyMs + 20} ms approx.). Les périphériques par défaut d'origine "
                       + "sont restaurés à l'arrêt.";
    }

    /// <summary>
    /// Démarre la chaîne complète : bascule des périphériques par défaut de Windows
    /// vers la carte virtuelle (sortie, et entrée si l'option est active), puis
    /// captures + bus master. Retourne false si le démarrage a échoué.
    /// </summary>
    private bool StartRoutingChain()
    {
        try
        {
            SyncMasterEqFromMain();
            TrySwitchDefaultsForRouting();
            Mixer.Start();
            RefreshTransportState();
            RefreshDevices();
            return true;
        }
        catch (Exception ex)
        {
            StatusText = "Routage impossible : " + ex.Message;
            RefreshTransportState();
            return false;
        }
    }

    /// <summary>
    /// Bascule Windows vers la carte virtuelle : la sortie par défaut passe sur
    /// l'entrée de la carte (« CABLE Input », « VoiceMeeter Input »…) et, si
    /// l'option est active, l'entrée par défaut passe sur la sortie de la carte
    /// (« micro virtuel »). Les défauts d'origine sont mémorisés une seule fois
    /// pour être restaurés à l'arrêt du routage.
    /// </summary>
    private void TrySwitchDefaultsForRouting()
    {
        try
        {
            var st = _catalog.ProbeVirtualCard(SelectedProvider);
            if (!st.Installed || st.CableInputId == null)
                return; // boucle WASAPI ou carte absente : rien à basculer

            if (string.IsNullOrEmpty(_savedRenderId))
            {
                _savedRenderId = st.DefaultRenderId;
                _savedCaptureId = _catalog.GetDefaultCaptureId();
                SaveConfig(); // mémorisé aussi sur disque (survit à un plantage)
            }

            if (!string.Equals(st.DefaultRenderId, st.CableInputId, StringComparison.OrdinalIgnoreCase))
            {
                if (DefaultDeviceSetter.SetDefaultRender(st.CableInputId))
                    StatusText = "Sortie par défaut de Windows → « " + st.CableInputName + " ».";
                else
                    StatusText = "Bascule automatique refusée par Windows : utilisez « Définir par défaut » ou le panneau son.";
            }

            if (SwitchDefaultCapture && st.CableOutputId != null)
            {
                string cur = _catalog.GetDefaultCaptureId() ?? "";
                if (!string.Equals(cur, st.CableOutputId, StringComparison.OrdinalIgnoreCase))
                    DefaultDeviceSetter.SetDefaultCapture(st.CableOutputId);
            }
        }
        catch
        {
            // la bascule des défauts ne doit jamais empêcher le démarrage du routage
        }
    }

    /// <summary>Restaure les périphériques par défaut mémorisés avant la bascule.</summary>
    private void TryRestoreDefaults()
    {
        try
        {
            if (!string.IsNullOrEmpty(_savedRenderId))
                DefaultDeviceSetter.SetDefaultRender(_savedRenderId);
            if (!string.IsNullOrEmpty(_savedCaptureId))
                DefaultDeviceSetter.SetDefaultCapture(_savedCaptureId);
        }
        catch
        {
            // la restauration ne doit jamais lever
        }
        finally
        {
            _savedRenderId = null;
            _savedCaptureId = null;
            SaveConfig();
        }
    }

    private void RefreshTransportState()
    {
        IsRunning = Mixer.IsRunning;
        RoutingStatusText = IsRunning
            ? "● Routage actif — le son Windows traverse l'égaliseur"
            : "○ Routage à l'arrêt";
    }

    /// <summary>Démarre automatiquement le routage si configuré (appelé au chargement de la fenêtre).</summary>
    public void AutoStartIfConfigured()
    {
        if (!AutoStart || IsRunning) return;
        var st = _catalog.ProbeVirtualCard(SelectedProvider);
        if (st.CableOutputFound || _catalog.EnumerateLoopbackSources().Count > 0)
        {
            Dispatcher.UIThread.Post(ToggleRouting);
        }
    }

    // ================================================================ périphériques

    [RelayCommand]
    private void RefreshDevices()
    {
        VirtualCard = _catalog.ProbeVirtualCard(SelectedProvider);
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
        foreach (var s in _catalog.EnumerateCaptureSources()) AvailableSources.Add(s);
        foreach (var s in _catalog.EnumerateLoopbackSources()) AvailableSources.Add(s);
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
    }

    // ================================================================ volume carte physique

    private void RefreshRoutedVolumeDisplay()
    {
        double v = _endpointVolume.VolumeScalar;
        _syncingRoutedVolume = true; // le binding TwoWay renverrait sinon la valeur dans OnRoutedVolumeChanged
        try
        {
            RoutedVolume = v;
            RoutedMute = _endpointVolume.Muted;
            RoutedVolumeText = v < 0
                ? "—"
                : (RoutedMute ? "Muet" : $"{Math.Round(v * 100)} %");
        }
        finally { _syncingRoutedVolume = false; }
        if (VolumeSyncPolicy.IsUsable(v)) _routedVolumeReady = true;
        _lastEndpointSeen = v;
    }

    /// <summary>
    /// Le curseur « Volume carte physique » du Mixage a bougé (binding TwoWay) :
    /// la valeur devient la consigne de la carte, PUIS la barre du haut est recalée
    /// — les deux curseurs restent identiques quel que soit celui que l'on déplace.
    /// </summary>
    partial void OnRoutedVolumeChanged(double value)
    {
        // écriture programmatique (RefreshRoutedVolumeDisplay) : rien à propager ;
        // premier peuplement du binding : RoutedVolume démarre à −1 et le Slider
        // le contraint à 0 dès l'affichage — ne PAS écrire 0 dans la carte !
        if (_syncingRoutedVolume || !_routedVolumeReady) return;
        if (!VolumeSyncPolicy.IsUsable(value)) return;
        if (!_endpointVolume.Available) return;

        double target = VolumeSyncPolicy.ClampToEndpointRange(value);
        if (VolumeSyncPolicy.ShouldWriteToEndpoint(_endpointVolume.VolumeScalar, target))
            _endpointVolume.SetVolumeScalar(target);
        RefreshRoutedVolumeDisplay();
        SyncTopBarFromEndpoint();
    }

    /// <summary>Recale le curseur de la barre supérieure sur le volume réel de la carte
    /// (la décision finale — déjà synchronisé ou non — appartient au ViewModel principal).</summary>
    private void SyncTopBarFromEndpoint()
    {
        double v = _endpointVolume.VolumeScalar;
        if (VolumeSyncPolicy.IsUsable(v)) _syncTopBarVolume?.Invoke(v);
    }

    private void NudgeRoutedVolume(int direction)
    {
        if (!_endpointVolume.Available)
        {
            StatusText = "Volume indisponible sur cette sortie (pilote ASIO ?).";
            return;
        }
        double v = _endpointVolume.VolumeScalar;
        if (v < 0) return;
        _endpointVolume.SetVolumeScalar(v + direction * 0.05);
        RefreshRoutedVolumeDisplay();
        SyncTopBarFromEndpoint(); // le curseur de la barre du haut suit les raccourcis clavier
        FeedbackRoutedVolume();
    }

    [RelayCommand] private void RoutedVolumeUp() => NudgeRoutedVolume(+1);
    [RelayCommand] private void RoutedVolumeDown() => NudgeRoutedVolume(-1);

    /// <summary>
    /// Applique le curseur « Volume » de la barre supérieure au périphérique
    /// physique routé : 0–100 % règle le volume Windows de la carte (le son routé
    /// ne passe PAS par le lecteur interne, d'où ce pont) ; au-delà de 100 % le
    /// volume Windows reste au maximum et seul le gain du lecteur interne monte.
    /// Sans effet si la sortie n'expose pas de volume Windows (ASIO).
    /// Le curseur du Mixage suit via RefreshRoutedVolumeDisplay (pas d'appel
    /// réciproque : la barre du haut est la SOURCE du changement ici).
    /// </summary>
    public void ApplyTopBarVolume(double value)
    {
        if (!_endpointVolume.Available) return;
        double target = EndpointVolumeController.SliderToEndpoint(value);
        if (VolumeSyncPolicy.ShouldWriteToEndpoint(_endpointVolume.VolumeScalar, target))
            _endpointVolume.SetVolumeScalar(VolumeSyncPolicy.ClampToEndpointRange(target));
        RefreshRoutedVolumeDisplay();
    }

    [RelayCommand]
    private void ToggleRoutedMute()
    {
        bool? state = _endpointVolume.ToggleMute();
        if (state == null)
        {
            StatusText = "Sourdine indisponible sur cette sortie.";
            return;
        }
        RefreshRoutedVolumeDisplay();
        FeedbackRoutedVolume();
    }

    private void FeedbackRoutedVolume()
    {
        string text = _endpointVolume.Available
            ? $"Volume — {RoutedVolumeTarget} : {RoutedVolumeText}"
            : "Volume non disponible pour cette sortie.";
        RoutedVolumeFeedback?.Invoke(text);
    }

    partial void OnMasterFaderChanged(double value)
    {
        Mixer.MasterFader = value;
        MasterFaderText = value <= 0.001 ? "−∞ dB" : (20 * Math.Log10(value)).ToString("+0.0;-0.0") + " dB";
    }

    partial void OnMasterMuteChanged(bool value) => Mixer.MasterMute = value;

    partial void OnAutoStartChanged(bool value) => SaveConfig();

    partial void OnAutoRouteOnInstallChanged(bool value) => SaveConfig();

    partial void OnSwitchDefaultCaptureChanged(bool value) => SaveConfig();

    partial void OnSelectedProviderChanged(IVirtualAudioProvider value)
    {
        OnPropertyChanged(nameof(CanAutoInstall));
        OnPropertyChanged(nameof(HasProductPage));
        OnPropertyChanged(nameof(ProviderIsPreinstalled));
        OnPropertyChanged(nameof(ProviderLicenseNote));
        if (!_initializing)
        {
            RefreshDevices();
            SaveConfig();
        }
    }

    // ================================================================ carte virtuelle (protocoles)

    /// <summary>Une installation automatique est-elle disponible pour ce protocole ?</summary>
    public bool CanAutoInstall => SelectedProvider.Installer.Supported;

    /// <summary>Un lien vers la page produit officielle est-il disponible ?</summary>
    public bool HasProductPage => !SelectedProvider.IsPreinstalled
                                  && SelectedProvider.Installer.ProductPageUrl.Length > 0;

    /// <summary>Ce protocole ne nécessite aucune installation ?</summary>
    public bool ProviderIsPreinstalled => SelectedProvider.IsPreinstalled;

    /// <summary>Note de licence affichée près du bouton d'installation.</summary>
    public string ProviderLicenseNote => SelectedProvider.Installer.LicenseNote;

    /// <summary>Une note de licence doit-elle être affichée ?</summary>
    public bool HasLicenseNote => ProviderLicenseNote.Length > 0;

    /// <summary>Définit le périphérique render du protocole choisi comme sortie par défaut de Windows (API PolicyConfig).</summary>
    [RelayCommand]
    private void SetVirtualDefault()
    {
        if (VirtualCard.CableInputId == null)
        {
            StatusText = SelectedProvider.DisplayName + " n'est pas détecté : installez-le d'abord « Installer automatiquement » ou via le guide.";
            return;
        }
        bool ok = DefaultDeviceSetter.SetDefaultRender(VirtualCard.CableInputId);
        StatusText = ok
            ? "« " + VirtualCard.CableInputName + " » est maintenant la sortie par défaut de Windows."
            : "Définition automatique refusée par Windows : passez par le panneau son (bouton ci-contre).";
        if (!ok) OpenSoundPanel();
        RefreshDevices();
    }

    /// <summary>
    /// Télécharge et installe automatiquement le pilote du protocole sélectionné
    /// (VB-CABLE, VoiceMeeter…) depuis le site officiel, avec suivi de progression,
    /// puis attend l'apparition des périphériques.
    /// </summary>
    [RelayCommand]
    private async Task InstallVirtualCardAsync(CancellationToken ct)
    {
        var provider = SelectedProvider;
        if (provider.IsPreinstalled)
        {
            StatusText = "Aucune installation nécessaire pour ce protocole (boucle WASAPI native).";
            return;
        }
        if (!provider.Installer.Supported)
        {
            StatusText = "Installation manuelle : ouverture de la page du produit…";
            OpenProductPage();
            return;
        }

        IsInstalling = true;
        InstallIndeterminate = true;
        InstallPhaseText = "Préparation du téléchargement…";
        try
        {
            var progress = new Progress<InstallProgress>(p =>
            {
                InstallPhaseText = p.Message;
                if (p.Percent is { } v)
                {
                    InstallIndeterminate = false;
                    InstallPercent = v;
                }
                else
                {
                    InstallIndeterminate = true;
                }
            });
            var installer = new VirtualAudioInstaller(progress);
            var probe = new Func<bool>(() => _catalog.ProbeVirtualCard(provider).Installed);
            var result = await installer.InstallAsync(provider, ct, probe);
            StatusText = result.Message;
            if (result.Success)
            {
                RefreshDevices();
                if (VirtualCard.Installed)
                {
                    EnsureCableTrack();

                    if (AutoRouteOnInstall)
                    {
                        // la sortie master de Windows bascule automatiquement vers la
                        // carte virtuelle, la piste câble alimente l'EQ, et le bus
                        // rend le son sur la sortie physique choisie
                        if (!IsRunning) StartRoutingChain();
                        else if (!string.IsNullOrEmpty(VirtualCard.CableInputId) && !VirtualCard.IsDefaultRender)
                            DefaultDeviceSetter.SetDefaultRender(VirtualCard.CableInputId);

                        RefreshDevices();
                        StatusText = result.Message + " Chaîne active : Windows → « "
                                   + (VirtualCard.CableInputName ?? "carte virtuelle") + " » → EQ → « "
                                   + (SelectedOutput?.DisplayName ?? "?") + " ».";
                    }
                    else if (!IsRunning)
                    {
                        StatusText += " Cliquez « Router Windows vers l'EQ » pour activer le traitement.";
                    }
                }
            }
        }
        finally
        {
            IsInstalling = false;
            InstallIndeterminate = true;
        }
    }

    /// <summary>Ouvre la page produit officielle du protocole sélectionné.</summary>
    [RelayCommand]
    private void OpenProductPage()
    {
        string url = SelectedProvider.Installer.ProductPageUrl;
        if (url.Length == 0) return;
        try
        {
            Process.Start(new ProcessStartInfo { FileName = url, UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = "Ouverture du navigateur impossible : " + ex.Message + " — " + url;
        }
    }

    [RelayCommand]
    private void OpenSoundPanel()
    {
        try
        {
            Process.Start(new ProcessStartInfo { FileName = "mmsys.cpl", UseShellExecute = true });
        }
        catch (Exception ex)
        {
            StatusText = "Ouverture du panneau son impossible : " + ex.Message;
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

    /// <summary>
    /// Garantit la présence de la piste captant la sortie de la carte virtuelle
    /// (CABLE Output, VoiceMeeter Output…) — c'est elle qui alimente l'égaliseur.
    /// </summary>
    private void EnsureCableTrack()
    {
        if (VirtualCard.CableOutputId == null) return;
        bool exists = Channels.Any(c => c.Channel.Source.Kind == TrackSourceKind.Capture
            && string.Equals(c.Channel.Source.DeviceId, VirtualCard.CableOutputId, StringComparison.OrdinalIgnoreCase));
        if (!exists)
        {
            AddTrack(TrackSource.CaptureDevice(VirtualCard.CableOutputId,
                (VirtualCard.CableOutputName ?? "Sortie virtuelle") + " — Windows"));
        }
    }

    [RelayCommand]
    private void SelectTrack(ChannelViewModel? vm) => SelectedChannel = vm;

    /// <summary>Rafraîchit la LED « EQ actif » d'une tranche (appelé après édition).</summary>
    public void UpdateEqLedFor(ChannelViewModel vm) => vm.UpdateEqLed();

    /// <summary>Ajoute les tranches par défaut d'une session : Windows, lecteur, micro, générateur.</summary>
    private void AddDefaultTracks()
    {
        // Son Windows : via le câble virtuel du protocole choisi, sinon loopback de la sortie par défaut
        if (VirtualCard.CableOutputFound && VirtualCard.CableOutputId != null)
        {
            AddTrack(TrackSource.CaptureDevice(VirtualCard.CableOutputId,
                (VirtualCard.CableOutputName ?? "Sortie virtuelle") + " — Windows"));
        }
        else if (VirtualCard.DefaultRenderId != null)
        {
            AddTrack(TrackSource.LoopbackDevice(VirtualCard.DefaultRenderId,
                (VirtualCard.DefaultRenderName ?? "Sortie Windows") + " — boucle"));
        }

        // Lecteur interne
        AddTrack(TrackSource.InternalPlayer());

        // Première entrée non virtuelle comme Micro
        var micro = _catalog.EnumerateCaptureSources()
            .FirstOrDefault(s => !AudioDeviceCatalog.IsVirtualCapture(s.Name));
        if (micro != null) AddTrack(micro);

        // Générateur
        AddTrack(TrackSource.InternalGenerator());
    }

    // ================================================================ minuterie (VU)

    private void Tick()
    {
        // crêtes de tranches avec décroissance douce
        foreach (var vm in Channels)
            vm.UpdateMeter();
        UpdateMasterMeter();
        CheckCaptureHealth();
        PollExternalEndpointVolume();
    }

    /// <summary>
    /// Le volume de la carte peut changer HORS de l'application (mixeur Windows,
    /// touches du casque, autre logiciel) : en le sondant à chaque battement, les
    /// deux curseurs restent fidèles au réglage réel, quel que soit l'ordre et
    /// l'origine des modifications.
    /// </summary>
    private void PollExternalEndpointVolume()
    {
        if (!_endpointVolume.Available)
        {
            _lastEndpointSeen = -1;
            return;
        }
        double v = _endpointVolume.VolumeScalar;
        if (!VolumeSyncPolicy.HasChangedSinceLastSeen(_lastEndpointSeen, v)) return;
        RefreshRoutedVolumeDisplay();
        SyncTopBarFromEndpoint();
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

    // ================================================================ persistance

    /// <summary>Libère les ressources à la fermeture de l'application.</summary>
    public void Shutdown()
    {
        _endpointVolume.Dispose();
    }

    public void SaveConfig()
    {
        try
        {
            var stored = new StoredMixer
            {
                MasterOutputId = SelectedOutput?.Id,
                MasterFader = MasterFader,
                MasterMute = MasterMute,
                AutoStart = AutoStart,
                VirtualProviderId = SelectedProvider.Id,
                AutoRouteOnInstall = AutoRouteOnInstall,
                SwitchDefaultCapture = SwitchDefaultCapture,
                SavedDefaultRenderId = _savedRenderId,
                SavedDefaultCaptureId = _savedCaptureId
            };
            foreach (var vm in Channels)
            {
                stored.Channels.Add(new StoredChannel
                {
                    Kind = vm.Channel.Kind switch
                    {
                        TrackSourceKind.Capture => "capture",
                        TrackSourceKind.Loopback => "loopback",
                        TrackSourceKind.Player => "player",
                        _ => "generator"
                    },
                    DeviceId = vm.Channel.Source.DeviceId,
                    Name = vm.Channel.Source.Name,
                    Fader = vm.Fader,
                    Pan = vm.Pan,
                    Mute = vm.Mute,
                    Solo = vm.Solo,
                    GeneratorEnabled = vm.GeneratorOn,
                    GeneratorSignal = vm.SelectedSignalText,
                    Eq = vm.Channel.Eq.Preset
                });
            }
            _store.Save(stored);
        }
        catch
        {
            // la persistance ne doit jamais perturber l'audio
        }
    }

    private void LoadConfigOrDefaults()
    {
        _initializing = true;
        try
        {
            var cfg = _store.Load();
            AutoStart = cfg.AutoStart;
            AutoRouteOnInstall = cfg.AutoRouteOnInstall;
            SwitchDefaultCapture = cfg.SwitchDefaultCapture;
            _savedRenderId = cfg.SavedDefaultRenderId;
            _savedCaptureId = cfg.SavedDefaultCaptureId;
            MasterFader = cfg.MasterFader <= 0 ? 1.0 : cfg.MasterFader;
            MasterMute = cfg.MasterMute;
            _storedOutputId = cfg.MasterOutputId;
            SelectedProvider = VirtualAudioCatalog.GetById(cfg.VirtualProviderId);

            VirtualCard = _catalog.ProbeVirtualCard(SelectedProvider);

            int restored = 0;
            foreach (var sc in cfg.Channels)
            {
                TrackSource src = sc.Kind switch
                {
                    "capture" => TrackSource.CaptureDevice(sc.DeviceId, sc.Name),
                    "loopback" => TrackSource.LoopbackDevice(sc.DeviceId, sc.Name),
                    "player" => TrackSource.InternalPlayer(),
                    _ => TrackSource.InternalGenerator()
                };

                // un périphérique disparu depuis la dernière session est ignoré
                if (src.Kind is TrackSourceKind.Capture or TrackSourceKind.Loopback
                    && _catalog.ResolveDevice(src) == null) continue;

                var ch = Mixer.AddChannel(src);
                var vm = new ChannelViewModel(ch, () => SaveConfig())
                {
                    Fader = Math.Clamp(sc.Fader, 0, 1.5),
                    Pan = Math.Clamp(sc.Pan, -1, 1),
                    Mute = sc.Mute,
                    Solo = sc.Solo,
                    GeneratorOn = sc.GeneratorEnabled,
                    SelectedSignalText = sc.GeneratorSignal ?? "Bruit blanc"
                };
                if (sc.Eq != null) ch.Eq.SetPreset(sc.Eq);
                Channels.Add(vm);
                restored++;
            }

            if (restored == 0) AddDefaultTracks();
        }
        finally
        {
            _initializing = false;
        }
    }
}
