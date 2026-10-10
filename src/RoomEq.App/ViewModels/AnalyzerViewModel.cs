using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.App.Controls;
using RoomEq.Audio.Capture;
using RoomEq.Audio.Playback;
using RoomEq.Core.Dsp;

namespace RoomEq.App.ViewModels;

public enum AnalyzerSourceMode { Sortie, Micro }

/// <summary>
/// Analyseur de fréquences temps réel : source « Sortie » (signal post-EQ dérivé du
/// flux de lecture) ou « Micro » (capture continue). Résolution 1/6 d'octave, compensation
/// de profil micro optionnelle.
/// </summary>
public partial class AnalyzerViewModel : ObservableObject
{
    private readonly PlaybackEngine _engine;
    private readonly CaptureEngine _capture;
    private readonly RollingBuffer _outBuffer;
    private readonly RollingBuffer _micBuffer;
    private readonly DispatcherTimer _timer;

    [ObservableProperty] private AnalyzerSourceMode mode = AnalyzerSourceMode.Sortie;
    [ObservableProperty] private ObservableCollection<AnalyzerSourceMode> modes = new(
        Enum.GetValues<AnalyzerSourceMode>());
    [ObservableProperty] private ObservableCollection<OutputDeviceInfo> micDevices = new();
    [ObservableProperty] private OutputDeviceInfo? selectedMic;
    [ObservableProperty] private bool micRunning;
    [ObservableProperty] private bool applyMicCompensation;
    [ObservableProperty] private int fftSize = 8192;
    [ObservableProperty] private ObservableCollection<int> fftSizes = new() { 2048, 4096, 8192, 16384 };
    [ObservableProperty] private string info = "";
    [ObservableProperty] private double peakDb = -120;

    public event Action<List<PlotSeries>>? SeriesChanged;

    public AnalyzerViewModel(PlaybackEngine engine, CaptureEngine capture)
    {
        _engine = engine;
        _capture = capture;
        _outBuffer = new RollingBuffer(2);
        _micBuffer = new RollingBuffer(2);

        RefreshMics();

        // erreur de capture (périphérique débranché…) remontée à l'utilisateur
        _capture.Stopped += (_, msg) => Dispatcher.UIThread.Post(() =>
        {
            if (MicRunning && msg.Length > 0)
            {
                MicRunning = false;
                Info = "Capture micro interrompue : " + msg;
            }
        });

        _timer = new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Background, Update);
        _timer.Start();
    }

    public bool MicModeActive => Mode == AnalyzerSourceMode.Micro;

    /// <summary>Compensation de profil micro à appliquer (fonction gain(f) en dB), sinon null.</summary>
    public Func<double, double>? MicGainAt { get; set; }

    public void Attach(PlotControl plot)
    {
        plot.YMin = -100; plot.YMax = 0; plot.TickStepY = 10;
        SeriesChanged += s => plot.ShowSeries(s);
    }

    partial void OnModeChanged(AnalyzerSourceMode value)
    {
        OnPropertyChanged(nameof(MicModeActive));
        if (value != AnalyzerSourceMode.Micro && MicRunning) StopMic();
    }

    partial void OnSelectedMicChanged(OutputDeviceInfo? value)
    {
        if (MicRunning && value != null)
        {
            StopMic();
            StartMic();
        }
    }

    private void RefreshMics()
    {
        var list = _capture.EnumerateDevices();
        MicDevices = new ObservableCollection<OutputDeviceInfo>(list);
        if (SelectedMic == null) SelectedMic = MicDevices.FirstOrDefault();
    }

    [RelayCommand]
    private void ToggleMic()
    {
        if (MicRunning) StopMic();
        else StartMic();
    }

    private void StartMic()
    {
        try
        {
            _capture.Start(SelectedMic?.Id, (samples, ch, rate) =>
            {
                _micBuffer.SampleRate = rate;
                int frames = samples.Length / ch;
                if (frames <= 0) return;
                _micBuffer.Add(ChannelMixer.ToMono(samples, ch, ChannelMixer.MixFirstTwo));
            });
            MicRunning = true;
            Info = "Capture micro active.";
        }
        catch (Exception ex)
        {
            MicRunning = false;
            Info = "Capture impossible : " + ex.Message;
        }
    }

    private void StopMic()
    {
        _capture.Stop();
        MicRunning = false;
        Info = "Capture micro arrêtée.";
    }

    private void Update(object? sender, EventArgs e)
    {
        try
        {
            // source inactive : aucun calcul (le spectre affiché reste en l'état)
            bool active = Mode == AnalyzerSourceMode.Micro ? MicRunning : _engine.State == NAudio.Wave.PlaybackState.Playing;
            if (!active)
            {
                if (Info.Length > 0)
                {
                    Info = Mode == AnalyzerSourceMode.Micro
                        ? "Micro à l'arrêt — lancez la capture pour analyser."
                        : "Aucune lecture en cours.";
                    PeakDb = -120;
                }
                return;
            }

            List<PlotSeries> series;

            if (Mode == AnalyzerSourceMode.Sortie)
            {
                while (_engine.Tap.TryDequeue(out var chunk))
                {
                    _outBuffer.SampleRate = _engine.CurrentSampleRate;
                    _outBuffer.Add(chunk);
                }
                var data = _outBuffer.Snapshot(_outBuffer.Capacity);
                series = BuildSpectrum(data, _outBuffer.SampleRate, "Sortie (post-égalisation)", "#22D3EE");
            }
            else
            {
                var data = _micBuffer.Snapshot(_micBuffer.Capacity);
                series = BuildSpectrum(data, _micBuffer.SampleRate, "Micro", "#4ADE80");
            }

            SeriesChanged?.Invoke(series);
        }
        catch (ArgumentException)
        {
            // pas encore assez de données : on ignore ce tick
        }
        catch (Exception)
        {
            // ne jamais faire planter l'UI depuis le timer
        }
    }

    private List<PlotSeries> BuildSpectrum(float[] data, int sampleRate, string title, string color)
    {
        var list = new List<PlotSeries>();
        if (data.Length < FftSize / 2 || sampleRate <= 0)
        {
            Info = $"En attente de signal… ({data.Length} échantillons)";
            PeakDb = -120;
            return list;
        }

        var db = SpectrumTools.WelchDb(data, sampleRate, FftSize, out var binFreqs);

        // compensation de profil micro
        var micGain = MicGainAt;
        if (ApplyMicCompensation && micGain != null)
        {
            for (int i = 0; i < binFreqs.Length; i++)
                db[i] -= micGain(binFreqs[i]);
        }

        // affichage en bandes 1/6 d'octave (lisible sur axe log)
        SpectrumTools.OctaveLevels(binFreqs, db, 25, 20000, SpectrumTools.SixthOctave, out var centers, out var levels);
        var pts = centers.Zip(levels, (f, l) => new CurvePoint(f, l)).ToList();

        if (pts.Count > 0)
        {
            double peak = pts.Max(p => p.Db);
            int peakIdx = pts.FindIndex(p => Math.Abs(p.Db - peak) < 1e-9);
            PeakDb = peak;
            Info = $"Pic : {peak:0.0} dB à {centers[peakIdx]:0} Hz · {title}";
        }

        list.Add(PlotSeries.Create(title, pts, Avalonia.Media.Color.Parse(color), fill: true));
        return list;
    }
}
