using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Capture;
using RoomEq.Audio.Measurement;
using RoomEq.Audio.Playback;
using RoomEq.Core.Calibration;
using RoomEq.Core.Eq;
using RoomEq.Core.Io;

namespace RoomEq.App.ViewModels;

/// <summary>
/// Choix du menu déroulant des signaux de test : soit un signal généré à la
/// volée par le moteur (bruit, sinus, balayage — voir TestSignalCatalog), soit
/// un fichier WAV téléchargé depuis le catalogue officiel du projet.
/// </summary>
public sealed record TestSignalChoice(
    string Id,
    string DisplayName,
    string Category,
    GeneratorSignalItem? Generator,
    string? FilePath);

/// <summary>
/// ViewModel principal : sorties audio, transport, volume, bypass, et sous-ViewModels.
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

    private readonly Avalonia.Threading.DispatcherTimer _telemetryTimer;

    [ObservableProperty] private ObservableCollection<OutputDeviceInfo> outputs = new();
    [ObservableProperty] private OutputDeviceInfo? selectedOutput;
    [ObservableProperty] private double volume = 1.0;
    [ObservableProperty] private bool bypass;
    [ObservableProperty] private string statusText = "Prêt.";
    [ObservableProperty] private string sourceName = "";
    [ObservableProperty] private bool isPlaying;

    /// <summary>Menu déroulant des signaux de test (intégrés + fichiers téléchargés).</summary>
    public ObservableCollection<TestSignalChoice> SignalChoices { get; } = new();
    [ObservableProperty] private TestSignalChoice? selectedSignal;

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
        Mixer = new MixerViewModel(Engine);

        // initialisations croisées
        Eq.Init(Engine);
        Calibration.CorrectionToEq += preset => Dispatcher.UIThread.Post(() => Eq.LoadPreset(preset));
        Calibration.CorrectionSavePreset += preset => Dispatcher.UIThread.Post(() =>
        {
            new PresetStore().Save(preset);
            Eq.LoadPreset(preset); // rafraîchit la liste des presets
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
        LoadSignalChoices();

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

    partial void OnVolumeChanged(double value)
    {
        // 1) lecteur interne (fichiers, signaux de test) : gain après l'EQ ;
        // 2) son ROUTÉ (Windows → carte virtuelle → mixeur → carte physique) :
        //    ce chemin ne passe PAS par le lecteur interne — le curseur pilote
        //    donc aussi le volume Windows de la carte physique routée (même
        //    cible que Ctrl+Alt+↑ / ↓). Sans lui, le curseur paraîtrait « sans
        //    effet » sur la sortie sélectionnée dans l'usage principal.
        Engine.Volume = value;
        Mixer.ApplyTopBarVolume(value);
        StatusText = value <= 0.001
            ? "Volume coupé (lecteur interne + carte physique routée)."
            : $"Volume : {value:0%} — lecteur interne + volume Windows de la carte physique routée.";
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

    /// <summary>Reconstruit le menu déroulant : signaux intégrés + bibliothèque téléchargée.</summary>
    public void RefreshSignalChoices() => LoadSignalChoices();

    private void LoadSignalChoices()
    {
        TestSignalChoice? previous = SelectedSignal;
        SignalChoices.Clear();
        foreach (var g in TestSignalCatalog.BuiltIn)
            SignalChoices.Add(new TestSignalChoice(g.Id, g.DisplayName, g.Category, g, null));

        var store = new SignalStore();
        foreach (var f in store.List())
            SignalChoices.Add(new TestSignalChoice(
                "fichier|" + f.FileName, f.DisplayName, "Téléchargés", null, store.PathFor(f.FileName)));

        // conserve la sélection précédente si elle existe encore, sinon le signal
        // par défaut (bruit rose : réglage de niveau d'écoute)
        SelectedSignal = (previous != null ? SignalChoices.FirstOrDefault(c => c.Id == previous.Id) : null)
                         ?? SignalChoices.FirstOrDefault(c => c.Id == TestSignalCatalog.DefaultItemId)
                         ?? SignalChoices.FirstOrDefault();
    }

    [RelayCommand]
    private void PlaySelectedSignal()
    {
        var choice = SelectedSignal;
        if (choice == null) return;

        if (choice.Generator is { } g)
        {
            Engine.PlayTestSignal(g.Signal, g.Amplitude, g.SineFrequency, g.SweepSpec);
            RefreshTransportState();
            StatusText = g.DisplayName + " — ajustez le volume si besoin.";
            return;
        }

        if (choice.FilePath != null)
        {
            try
            {
                // les fichiers du catalogue sont courts : lecture en boucle pour
                // un usage à l'oreille continu, comme les signaux générés
                var reader = new NAudio.Wave.AudioFileReader(choice.FilePath);
                void Restart() { try { reader.Position = 0; } catch { /* fichier retiré ? */ } }
                Engine.PlaySource(new LoopingSampleProvider(reader, Restart) { Loop = true },
                    choice.DisplayName, Restart);
                RefreshTransportState();
                StatusText = choice.DisplayName + " (fichier téléchargé, lecture en boucle).";
            }
            catch (Exception ex)
            {
                StatusText = "Lecture impossible : " + ex.Message;
                DiagnosticsLog.Error("Lecture d'un signal téléchargé : " + ex.Message);
            }
        }
    }

    [RelayCommand]
    private void OpenSignalCatalog() => SignalCatalogRequested?.Invoke();
}
