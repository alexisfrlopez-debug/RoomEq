using System.Collections.ObjectModel;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Capture;
using RoomEq.Audio.Measurement;
using RoomEq.Audio.Playback;
using RoomEq.Audio.Routing;
using RoomEq.Core.Eq;
using RoomEq.Core.Io;

namespace RoomEq.App.ViewModels;

/// <summary>
/// ViewModel principal : sorties audio, transport, volume, bypass et
/// sous-ViewModels — spec I-APP-01. La politique « preset adapté à la
/// sortie » est déléguée au <see cref="OutputPresetCoordinator"/> et le
/// menu des signaux de test au <see cref="Signals"/> (v3 : décomposition).
/// </summary>
public partial class MainViewModel : ObservableObject
{
    public PlaybackEngine Engine { get; }
    public CaptureEngine AnalyzerCapture { get; }
    public RoomMeasurementEngine Measurement { get; }

    public EqViewModel Eq { get; }
    public AnalyzerViewModel Analyzer { get; }
    public CalibrationViewModel Calibration { get; }
    public MicViewModel Mic { get; }
    public AsioViewModel Asio { get; }
    public MixerViewModel Mixer { get; }

    /// <summary>Menu des signaux de test de la barre supérieure.</summary>
    public TestSignalMenu Signals { get; }

    /// <summary>Politique du preset adapté à la sortie master.</summary>
    public OutputPresetCoordinator OutputPresets { get; }

    private readonly Avalonia.Threading.DispatcherTimer _telemetryTimer;
    private bool _syncingVolumeFromMixer; // recadrage du curseur depuis le Mixage : ne pas réécrire la carte

    /// <summary>Surveillance des branchements audio à chaud (inerte hors Windows).</summary>
    private readonly AudioDeviceWatcher _deviceWatcher = new();

    [ObservableProperty] private ObservableCollection<OutputDeviceInfo> outputs = new();
    [ObservableProperty] private OutputDeviceInfo? selectedOutput;
    [ObservableProperty] private double volume = 1.0;
    [ObservableProperty] private bool bypass;
    [ObservableProperty] private string statusText = "Prêt.";
    [ObservableProperty] private string sourceName = "";
    [ObservableProperty] private bool isPlaying;

    /// <summary>Demande l'ouverture du catalogue de signaux (fenêtre gérée par la vue).</summary>
    public event Action? SignalCatalogRequested;

    public MainViewModel()
    {
        Engine = new PlaybackEngine();
        AnalyzerCapture = new CaptureEngine();
        Measurement = new RoomMeasurementEngine(Engine, new CaptureEngine());

        Mic = new MicViewModel();
        Eq = new EqViewModel();
        Analyzer = new AnalyzerViewModel(Engine, AnalyzerCapture);
        Calibration = new CalibrationViewModel(Engine, Measurement, Mic);
        Asio = new AsioViewModel(() => Engine.Stop());
        Mixer = new MixerViewModel(Engine, SyncVolumeFromRouted);
        Signals = new TestSignalMenu(Engine, setStatus: s => StatusText = s);
        OutputPresets = new OutputPresetCoordinator(Eq, Mixer);

        // initialisations croisées
        Eq.Init(Engine);
        Calibration.CorrectionToEq += preset => Dispatcher.UIThread.Post(() => Eq.LoadPreset(preset));
        Calibration.CorrectionSavePreset += preset => Dispatcher.UIThread.Post(() =>
        {
            new PresetStore().Save(preset);
            Eq.LoadPreset(preset); // rafraîchit la liste des presets
            OutputPresets.BindCalibration(preset, SelectedOutput);
        });
        // la courbe mesurée devient importable dans l'onglet Égaliseur
        // (retirée automatiquement quand l'utilisateur réinitialise la calibration)
        Calibration.CorrectionReady += r => Dispatcher.UIThread.Post(() => Eq.SetCalibrationSource(r));
        Calibration.CurveReset += () => Dispatcher.UIThread.Post(() => Eq.SetCalibrationSource(null));

        // l'analyseur peut appliquer le profil micro sélectionné (lu dynamiquement à chaque analyse)
        Analyzer.MicGainAt = f => Mic.SelectedProfile is { IsValid: true, Enabled: true } p ? p.GainAt(f) : 0.0;

        // événements moteur (threads audio → UI)
        Engine.ErrorOccurred += msg => Dispatcher.UIThread.Post(() =>
        {
            StatusText = "Erreur : " + msg;
            DiagnosticsLog.Error("Moteur de lecture : " + msg);
        });
        Engine.StateChanged += _ => Dispatcher.UIThread.Post(RefreshTransportState);
        Engine.SourceChanged += name => Dispatcher.UIThread.Post(() =>
        {
            SourceName = name ?? "";
            StatusText = name != null ? "Lecture : " + name : "Lecture arrêtée.";
            RefreshTransportState();
        });

        RefreshOutputs();
        SelectedOutput = Outputs.FirstOrDefault();
        if (SelectedOutput != null) Engine.SetOutput(SelectedOutput.Id);
        Engine.EnsureDefaultOutput();
        Signals.Refresh();

        // politique « preset par périphérique » : la sortie master peut changer
        // (restauration de session, routage) et chaque preset appliqué
        // manuellement est mémorisé pour la sortie en cours
        Mixer.MasterOutputChanged += dev => Dispatcher.UIThread.Post(() => OutputPresets.Evaluate(dev));
        Mixer.OutputPresetOptionPicked += opt => Dispatcher.UIThread.Post(() => OutputPresets.Pick(opt));
        Eq.HeadphonePresetApplied += model => Dispatcher.UIThread.Post(() => OutputPresets.LinkFactory(model));
        Eq.LinkPresetRequested += name => Dispatcher.UIThread.Post(() => OutputPresets.LinkNamed(name));
        Dispatcher.UIThread.Post(() => OutputPresets.Evaluate(Mixer.SelectedOutput));

        // branchements à chaud : casque Bluetooth, carte USB… les listes suivent
        // toutes seules (les boutons ⟳ / « Rafraîchir » restent disponibles)
        _deviceWatcher.DevicesChanged += OnHotDevicesChanged;

        // télémétrie (version de test) : envoi des journaux toutes les 5 minutes
        _telemetryTimer = new Avalonia.Threading.DispatcherTimer(
            TimeSpan.FromMinutes(5), Avalonia.Threading.DispatcherPriority.Background,
            (_, _) => _ = Task.Run(DriveLogUploader.UploadNowAsync));
        if (DriveLogUploader.Config.Enabled)
            _telemetryTimer.Start();
    }

    /// <summary>
    /// Fermeture de l'application : arrêt du routage, libération des périphériques
    /// et DERNIER envoi des journaux vers le Drive (synchrone, 10 s max — c'est le
    /// seul moment où l'on peut garantir l'envoi de la session entière).
    /// </summary>
    public void OnAppClosing()
    {
        try
        {
            _telemetryTimer.Stop();
            _deviceWatcher.Dispose();
            Mixer.SaveConfig();
            Mixer.Mixer.Stop();
            Mixer.Shutdown();
            Engine.Stop();
            AnalyzerCapture.Dispose();
            Engine.Dispose();
        }
        catch
        {
            // la fermeture ne doit jamais échouer
        }

        if (DriveLogUploader.Config.Enabled)
        {
            try
            {
                DriveLogUploader.UploadNowAsync().Wait(TimeSpan.FromSeconds(10));
            }
            catch
            {
                // un envoi impossible ne doit pas retarder la fermeture
            }
        }
    }

    private void RefreshTransportState()
    {
        IsPlaying = Engine.State == NAudio.Wave.PlaybackState.Playing;
    }

    [RelayCommand]
    private void RefreshOutputs()
    {
        Outputs = new ObservableCollection<OutputDeviceInfo>(Engine.EnumerateOutputs());
        if (SelectedOutput != null && !Outputs.Contains(SelectedOutput))
        {
            SelectedOutput = Outputs.FirstOrDefault();
        }
    }

    partial void OnSelectedOutputChanged(OutputDeviceInfo? value)
    {
        if (value == null) return;
        Engine.SetOutput(value.Id);
        StatusText = $"Sortie : {value.DisplayName}";
    }

    /// <summary>
    /// Un périphérique audio vient d'être branché / retiré / ou devient le périphérique
    /// par défaut : rafraîchit toutes les listes (barre du haut, Mixage, Analyseur,
    /// Calibration) puis réévalue le preset de la sortie master — le nouveau casque
    /// branché est reconnu et sa correction appliquée sans redémarrer RoomEq.
    /// </summary>
    private void OnHotDevicesChanged()
    {
        Dispatcher.UIThread.Post(() =>
        {
            RefreshOutputs();
            Mixer.RefreshDevices();
            try { Analyzer.RefreshAudioDevices(); } catch { /* énumération indisponible */ }
            try { Calibration.RefreshAudioDevices(); } catch { /* énumération indisponible */ }
            OutputPresets.Evaluate(Mixer.SelectedOutput);
            StatusText = "Périphériques audio mis à jour (branchement à chaud détecté).";
        });
    }

    partial void OnVolumeChanged(double value)
    {
        // 1) lecteur interne (fichiers, signaux de test) : gain après l'EQ ;
        // 2) son ROUTÉ (Windows → carte virtuelle → mixeur → carte physique) :
        //    ce chemin ne passe PAS par le lecteur interne — le curseur pilote
        //    donc aussi le volume Windows de la carte physique routée (même
        //    cible que Ctrl+Alt+↑ / ↓). Sans lui, le curseur paraîtrait « sans
        //    effet » sur la sortie sélectionnée dans l'usage principal.
        Engine.Volume = value;

        // recadrage venant du Mixage / des raccourcis clavier / de Windows : la
        // carte est DÉJÀ à la bonne valeur (c'est elle qui a provoqué le recadrage)
        // — ne pas la réécrire, seule la position du curseur doit suivre.
        if (!_syncingVolumeFromMixer)
            Mixer.ApplyTopBarVolume(value);

        StatusText = value <= 0.001
            ? "Volume coupé (lecteur interne + carte physique routée)."
            : $"Volume : {Math.Min(value, 1.0):0%} — lecteur interne + carte physique routée" + (value > 1.0 ? " (carte au maximum, gain lecteur augmenté)" : "");
    }

    /// <summary>
    /// Recadre le curseur de la barre supérieure sur le volume RÉEL de la carte
    /// physique, après un changement venu d'ailleurs : curseur de l'onglet Mixage,
    /// raccourcis Ctrl+Alt+↑ / ↓, changement de sortie master, ou réglage fait
    /// hors de l'application (mixeur Windows). C'est ce qui garde les deux
    /// curseurs identiques quel que soit celui que l'on déplace, et dans quel
    /// ordre. Le drapeau évite la boucle : sans lui, OnVolumeChanged réécrirait
    /// la carte (déjà à la bonne valeur) et déclencherait un aller-retour.
    /// </summary>
    internal void SyncVolumeFromRouted(double endpointScalar)
    {
        if (!VolumeSyncPolicy.ShouldSyncTopBar(Volume, endpointScalar)) return;
        _syncingVolumeFromMixer = true;
        try { Volume = endpointScalar; }
        finally { _syncingVolumeFromMixer = false; }
    }

    partial void OnBypassChanged(bool value)
    {
        Engine.Eq.Bypass = value;
        StatusText = value ? "Égaliseur contourné (bypass)." : "Égaliseur actif.";
    }

    [RelayCommand]
    private void PlayPause()
    {
        switch (Engine.State)
        {
            case NAudio.Wave.PlaybackState.Playing:
                Engine.Pause();
                break;
            case NAudio.Wave.PlaybackState.Paused:
                Engine.Resume();
                break;
            default:
                StatusText = "Ouvrez un fichier ou lancez un signal de test pour écouter.";
                break;
        }
        RefreshTransportState();
    }

    [RelayCommand]
    private void Stop()
    {
        Engine.Stop();
        RefreshTransportState();
        StatusText = "Lecture arrêtée.";
    }

    /// <summary>Joue un fichier (appelé par la vue après le sélecteur de fichiers).</summary>
    public void PlayFile(string path)
    {
        Engine.PlayFile(path);
        RefreshTransportState();
    }

    // ================================================================ signaux de test

    /// <summary>Reconstruit le menu des signaux (après téléchargement).</summary>
    public void RefreshSignalChoices() => Signals.Refresh();

    [RelayCommand]
    private void OpenSignalCatalog() => SignalCatalogRequested?.Invoke();
}
