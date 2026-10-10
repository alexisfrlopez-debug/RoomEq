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
        Engine.Volume = value;
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

    [RelayCommand]
    private void TestWhiteNoise()
    {
        Engine.PlayTestSignal(TestSignal.WhiteNoise, 0.2);
        RefreshTransportState();
        StatusText = "Bruit blanc de test (amp. -14 dBFS) — ajustez le volume.";
    }

    [RelayCommand]
    private void TestPinkNoise()
    {
        Engine.PlayTestSignal(TestSignal.PinkNoise, 0.35);
        RefreshTransportState();
        StatusText = "Bruit rose de test.";
    }

    [RelayCommand]
    private void TestSine()
    {
        Engine.PlayTestSignal(TestSignal.Sine, 0.2, 1000);
        RefreshTransportState();
        StatusText = "Sinus 1 kHz de test.";
    }
}
