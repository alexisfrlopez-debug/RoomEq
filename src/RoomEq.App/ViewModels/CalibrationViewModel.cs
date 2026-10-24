using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.App.Controls;
using RoomEq.Audio.Capture;
using RoomEq.Audio.Measurement;
using RoomEq.Audio.Playback;
using RoomEq.Core.Calibration;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using RoomEq.Core.Io;

namespace RoomEq.App.ViewModels;

/// <summary>Option de signal coché pour la mesure multi-signaux.</summary>
public partial class SignalOption : ObservableObject
{
    public MeasurementSignalKind Kind { get; }
    public string Name { get; }
    public string Description { get; }

    [ObservableProperty] private bool isSelected = true;

    public SignalOption(MeasurementSignalKind kind)
    {
        Kind = kind;
        Name = MeasurementSignalCatalog.DisplayName(kind);
        Description = MeasurementSignalCatalog.Description(kind);
    }
}

/// <summary>
/// Micro de mesure proposé dans la liste : les cartes virtuelles (RoomEq
/// Output…) sont signalées — elles ne captent pas la pièce et ne doivent
/// être choisies que dans des cas très particuliers.
/// </summary>
public sealed class MicDeviceOption
{
    public OutputDeviceInfo Device { get; }
    public bool IsVirtual { get; }
    public string DisplayName { get; }

    public MicDeviceOption(OutputDeviceInfo device)
    {
        Device = device;
        string n = device.DisplayName;
        // détection des cartes virtuelles : union des motifs des fournisseurs
        // pris en charge (carte RoomEq intégrée), pas de noms tiers codés en dur
        IsVirtual = ContainsAny(n, "roomeq output", "roomeq input", "virtual");
        DisplayName = IsVirtual ? n + "  — carte virtuelle (ne capte pas la pièce)" : n;
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        string lower = text.ToLowerInvariant();
        return needles.Any(lower.Contains);
    }
}

/// <summary>Option de canal du micro : mixage ou canal unique (mono).</summary>
public sealed class ChannelOption
{
    /// <summary>Index du canal (0 = canal 1) ou ChannelMixer.MixFirstTwo pour le mixage.</summary>
    public int Index { get; }
    public string Label { get; }

    public ChannelOption(int index, string label)
    {
        Index = index;
        Label = label;
    }
}

/// <summary>Option de courbe cible avec libellé français.</summary>
public sealed class TargetOption
{
    public TargetType Value { get; }
    public string Label { get; }

    public TargetOption(TargetType value)
    {
        Value = value;
        Label = value switch
        {
            TargetType.Flat => "Plate (neutre)",
            TargetType.BassShelf => "Plate + shelf de graves",
            TargetType.StudioTilt => "Studio (pente -1 dB/oct)",
            _ => value.ToString()
        };
    }
}

/// <summary>
/// Assistant de calibration acoustique : signaux de test sur la sortie, capture micro,
/// génération et application du preset de correction.
/// Mode multi-signaux : tous les bruits et le balayage sinusoïdal progressif sont
/// mesurés successivement, les résultats sont croisés (médiane robuste) et le nombre
/// de bandes, leurs fréquences et leurs largeurs sont choisis automatiquement —
/// sans aucun doublon de bandes de fréquences.
/// </summary>
public partial class CalibrationViewModel : ObservableObject
{
    private readonly PlaybackEngine _engine;
    private readonly RoomMeasurementEngine _measurement;
    private readonly MicViewModel _mic;
    private readonly RollingBuffer _liveBuffer;

    private CancellationTokenSource? _cts;
    private RoomMeasurementEngine.MultiSignalResult? _lastMulti;

    [ObservableProperty] private double duration = 12;
    [ObservableProperty] private double sweepSeconds = 12;
    [ObservableProperty] private bool multiSignal = true;
    [ObservableProperty] private double testLevel = 0.20;
    [ObservableProperty] private double maxBoost = 6;
    [ObservableProperty] private double maxCut = 12;
    [ObservableProperty] private bool useMicProfile = true;
    [ObservableProperty] private ObservableCollection<MicDeviceOption> micDevices = new();
    [ObservableProperty] private MicDeviceOption? selectedMicDevice;
    [ObservableProperty] private ObservableCollection<ChannelOption> captureChannels = new();
    [ObservableProperty] private ChannelOption? selectedCaptureChannel;
    [ObservableProperty] private bool isMeasuring;
    [ObservableProperty] private double progress;
    [ObservableProperty] private string phase = "Choisissez le micro, placez-le à la position d'écoute puis lancez la mesure.";
    [ObservableProperty] private string resultSummary = "";
    [ObservableProperty] private string errorStatus = "";
    [ObservableProperty] private bool hasResult;
    [ObservableProperty] private string telemetryText = "";

    /// <summary>La version courante envoie-t-elle les journaux vers le Drive de développement ?</summary>
    public bool TelemetryEnabled => DriveLogUploader.Config.Enabled;

    public string LogPathDisplay => AppFlavor.IsPublic
        ? "journalisation désactivée (version publique — aucun fichier écrit)"
        : DiagnosticsLog.LogPath ?? "journal indisponible";

    public ObservableCollection<SignalOption> SignalOptions { get; } = new(
        Enum.GetValues<MeasurementSignalKind>().Select(k => new SignalOption(k)));

    public string PlanSummary => MeasurementSignalCatalog.PlanSummary(
        SignalOptions.Where(s => s.IsSelected).Select(s => s.Kind), Duration, SweepSeconds);

    partial void OnDurationChanged(double value) => OnPropertyChanged(nameof(PlanSummary));
    partial void OnSweepSecondsChanged(double value) => OnPropertyChanged(nameof(PlanSummary));

    public ObservableCollection<TargetOption> TargetOptions { get; } = new(
        Enum.GetValues<TargetType>().Select(t => new TargetOption(t)));

    [ObservableProperty] private TargetOption selectedTarget;

    public bool HasMicProfile => _mic.SelectedProfile is { IsValid: true };

    public CalibrationViewModel(PlaybackEngine engine, RoomMeasurementEngine measurement, MicViewModel mic)
    {
        _engine = engine;
        _measurement = measurement;
        _mic = mic;
        _liveBuffer = new RollingBuffer(2);

        SelectedTarget = TargetOptions.First();

        RefreshMics();

        // branchement permanent des flux du moteur de mesure
        _measurement.ProgressChanged += p => Dispatcher.UIThread.Post(() => Progress = p);
        _measurement.PhaseChanged += text => Dispatcher.UIThread.Post(() => Phase = text);
        _measurement.LiveChunk += chunk =>
        {
            _liveBuffer.Add(chunk);
        };

        // timer d'affichage live pendant la mesure
        DispatcherTimer.Run(() =>
        {
            if (!IsMeasuring) return true;
            var data = _liveBuffer.Snapshot(_liveBuffer.Capacity);
            if (data.Length < 8192) return true;
            var db = SpectrumTools.WelchDb(data, _liveBuffer.SampleRate, 4096, out var freqs);
            SpectrumTools.OctaveLevels(freqs, db, 25, 20000, SpectrumTools.SixthOctave, out var centers, out var levels);
            if (centers.Length < 4) return true;

            // la courbe live est CENTRÉE sur sa moyenne : le niveau absolu dépend du
            // gain micro (souvent sous -40 dBFS, hors fenêtre du graphe) — seule la
            // forme de la réponse compte pour suivre la mesure en direct
            double mean = SpectrumTools.MeanDb(centers, levels, 200, 8000);
            var pts = centers.Zip(levels, (f, l) => new Core.Dsp.CurvePoint(f, l - mean)).ToList();
            SeriesChanged?.Invoke(new List<PlotSeries>
            {
                PlotSeries.Create("Micro (live, centré)", pts, Avalonia.Media.Color.Parse("#22D3EE"), fill: true)
            });
            return true;
        }, TimeSpan.FromMilliseconds(120));

        _mic.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MicViewModel.SelectedProfile))
                OnPropertyChanged(nameof(HasMicProfile));
        };

        // rafraîchir l'estimation de durée quand les signaux cochés changent
        foreach (var opt in SignalOptions)
            opt.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(SignalOption.IsSelected))
                    OnPropertyChanged(nameof(PlanSummary));
            };

        // télémétrie (version de test) : statut des envois de journaux
        TelemetryText = TelemetryEnabled
            ? "Envoi automatique vers le Drive de développement : activé."
            : AppFlavor.IsPublic
                ? "Version publique : aucun journal écrit, aucun envoi."
                : "Télémétrie désactivée (telemetrie.json absent).";
        DriveLogUploader.UploadFinished += msg => Dispatcher.UIThread.Post(() =>
            TelemetryText = "Télémétrie : " + msg);
    }

    public event Action<List<PlotSeries>>? SeriesChanged;
    public event Action<CorrectionResult>? CorrectionReady;

    /// <summary>Signalé quand l'utilisateur réinitialise la courbe (le graphe est vidé).</summary>
    public event Action? CurveReset;

    public void Attach(PlotControl plot)
    {
        plot.YMin = -40; plot.YMax = 40; plot.TickStepY = 6;
        SeriesChanged += s => plot.ShowSeries(s);
        CorrectionReady += r => plot.ShowSeries(BuildResultSeries(r));
    }

    [RelayCommand]
    private void RefreshMics()
    {
        using var cap = new CaptureEngine();
        List<MicDeviceOption> options = cap.EnumerateDevices().Select(d => new MicDeviceOption(d)).ToList();

        // conserver la sélection courante si possible, sinon préférer un micro PHYSIQUE :
        // les cartes virtuelles (RoomEq Output…) ne captent pas la pièce — les choisir
        // par défaut conduit à une capture muette et donc à l'échec de la mesure
        string? previousId = SelectedMicDevice?.Device.Id;
        MicDevices = new ObservableCollection<MicDeviceOption>(options);
        SelectedMicDevice =
            MicDevices.FirstOrDefault(m => m.Device.Id == previousId) ??
            MicDevices.FirstOrDefault(m => !m.IsVirtual) ??
            MicDevices.FirstOrDefault();

        _measurement.CaptureDeviceId = SelectedMicDevice?.Device.Id;
        OnPropertyChanged(nameof(LogPathDisplay));
        RefreshChannels();
    }

    partial void OnSelectedMicDeviceChanged(MicDeviceOption? value)
    {
        _measurement.CaptureDeviceId = value?.Device.Id;
        if (value != null)
            DiagnosticsLog.Info("Micro sélectionné : " + value.Device.DisplayName);
        RefreshChannels();
    }

    /// <summary>
    /// Reconstruit la liste des canaux du micro sélectionné : le premier canal des
    /// micros multi-canaux est proposé par défaut (plus fiable que le mixage quand
    /// un canal est muet ou parasite).
    /// </summary>
    private void RefreshChannels()
    {
        int? count = null;
        try
        {
            using var cap = new CaptureEngine();
            count = cap.GetChannelCount(SelectedMicDevice?.Device.Id);
        }
        catch
        {
            // périphérique indisponible : liste minimale
        }

        var options = new List<ChannelOption> { new(ChannelMixer.MixFirstTwo, "Mixage stéréo (L+R)") };
        if (count is { } n && n > 1)
        {
            for (int i = 0; i < n; i++)
                options.Add(new ChannelOption(i, $"Canal {i + 1} seul (mono)" + (n == 2 ? (i == 0 ? " — gauche" : " — droite") : "")));
        }

        int previous = SelectedCaptureChannel?.Index ?? ChannelMixer.MixFirstTwo; // mixage par défaut
        CaptureChannels = new ObservableCollection<ChannelOption>(options);
        SelectedCaptureChannel =
            CaptureChannels.FirstOrDefault(c => c.Index == previous) ?? CaptureChannels[0];
        OnPropertyChanged(nameof(HasChannelChoice));
    }

    /// <summary>Un choix de canal mono est-il utile pour ce micro ?</summary>
    public bool HasChannelChoice => CaptureChannels.Count > 2;

    partial void OnSelectedCaptureChannelChanged(ChannelOption? value)
    {
        _measurement.CaptureChannelIndex = value?.Index ?? ChannelMixer.MixFirstTwo;
        if (value != null)
            DiagnosticsLog.Info("Canal de capture : " + value.Label);
    }

    [RelayCommand]
    private async Task StartAsync()
    {
        if (IsMeasuring) return;
        IsMeasuring = true;
        Progress = 0;
        ResultSummary = "";
        ErrorStatus = "";
        _cts = new CancellationTokenSource();
        _liveBuffer.Clear();
        DiagnosticsLog.Info("Calibration démarrée (multi-signaux : " + MultiSignal + ")");

        try
        {
            Phase = "Mesure en cours — restez silencieux…";
            var options = new CorrectionOptions
            {
                Target = SelectedTarget.Value,
                MaxBoostDb = MaxBoost,
                MaxCutDb = MaxCut,
                FreqMin = 40,
                FreqMax = 16000
            };
            var mic = UseMicProfile && _mic.SelectedProfile is { IsValid: true } ? _mic.SelectedProfile : null;
            string presetName = $"Calibration {DateTime.Now:yyyyMMdd-HHmm}";

            if (MultiSignal)
            {
                var kinds = SignalOptions.Where(s => s.IsSelected).Select(s => s.Kind).ToList();
                if (kinds.Count == 0)
                {
                    Phase = "Sélectionnez au moins un signal de mesure.";
                    return;
                }

                var multi = await _measurement.MeasureMultiSignalAsync(
                    kinds, Duration, SweepSeconds, TestLevel, options, mic, presetName, _cts.Token);

                _lastMulti = multi;
                Result = multi.Correction;
                HasResult = true;
                ResultSummary =
                    $"{multi.SignalCount} signal(s) croisés · {multi.FilterCount} filtres optimisés · " +
                    $"préampli {multi.Correction.Preset.PreampDb:0.0} dB · " +
                    $"résidu RMS {multi.ResidualRmsDb:0.0} dB / max {multi.Correction.ResidualMaxDb():0.0} dB";
                if (multi.Warnings.Count > 0)
                    ResultSummary += "\n⚠ " + string.Join("\n⚠ ", multi.Warnings);
            }
            else
            {
                _lastMulti = null;
                var result = await _measurement.MeasureAsync(
                    Duration, TestLevel, options, mic, presetName, _cts.Token);

                Result = result;
                HasResult = true;
                ResultSummary =
                    $"{result.Preset.Bands.Count} filtres générés · préampli {result.Preset.PreampDb:0.0} dB · " +
                    $"résidu max {result.ResidualMaxDb():0.0} dB";
            }

            CorrectionReady?.Invoke(Result!);
            CorrectionToEq?.Invoke(Result!.Preset);

            Phase = "Terminé : la correction a été appliquée à l'égaliseur. Vérifiez le résultat à l'écoute.";
        }
        catch (OperationCanceledException)
        {
            Phase = "Mesure annulée.";
            DiagnosticsLog.Info("Calibration annulée par l'utilisateur.");
            // effacer la courbe « live » figée par l'annulation
            SeriesChanged?.Invoke(new List<PlotSeries>());
        }
        catch (Exception ex)
        {
            Phase = "Erreur : " + ex.Message;
            ErrorStatus = ex.Message;
            DiagnosticsLog.Error("Calibration en échec", ex);
            // restaurer le dernier résultat valide, sinon vider le graphe
            SeriesChanged?.Invoke(Result != null ? BuildResultSeries(Result) : new List<PlotSeries>());
        }
        finally
        {
            IsMeasuring = false;
            _cts.Dispose();
            _cts = null;
        }
    }

    public CorrectionResult? Result { get; private set; }

    /// <summary>
    /// Réinitialise la courbe affichée : graphe vidé, résultat effacé — l'onglet est
    /// prêt pour relancer un test. Une mesure en cours est annulée.
    /// </summary>
    [RelayCommand]
    private void ResetCurve()
    {
        if (IsMeasuring) _cts?.Cancel();

        Result = null;
        _lastMulti = null;
        HasResult = false;
        ResultSummary = "";
        ErrorStatus = "";
        _liveBuffer.Clear();
        SeriesChanged?.Invoke(new List<PlotSeries>());
        Phase = "Courbe réinitialisée. Vous pouvez relancer un test.";
        DiagnosticsLog.Info("Courbe de calibration réinitialisée.");
        CurveReset?.Invoke();
    }

    public event Action<EqPreset>? CorrectionToEq;

    [RelayCommand]
    private void Cancel() => _cts?.Cancel();

    [RelayCommand]
    private void SavePreset()
    {
        if (Result == null) return;
        CorrectionSavePreset?.Invoke(Result.Preset);
        Phase = $"Preset « {Result.Preset.Name} » enregistré.";
    }

    /// <summary>Séries du graphe pour un résultat (réutilisée par la vue).</summary>
    public List<PlotSeries> BuildSeries(CorrectionResult r) => BuildResultSeries(r);

    public event Action<EqPreset>? CorrectionSavePreset;

    /// <summary>Envoie maintenant les journaux de diagnostic vers le Drive (télémétrie).</summary>
    [RelayCommand]
    private async Task SendLogsNow()
    {
        Phase = "Envoi des journaux vers le Drive…";
        string result = await Task.Run(DriveLogUploader.UploadNowAsync);
        TelemetryText = "Télémétrie : " + result;
        Phase = "Envoi des journaux terminé.";
    }

    private List<PlotSeries> BuildResultSeries(CorrectionResult r)
    {
        var list = new List<PlotSeries>();

        // courbes individuelles (mode multi-signaux) — traits fins grisés
        if (_lastMulti != null)
        {
            int i = 0;
            string[] mutedColors = { "#4B5563", "#6B7280", "#7C8493", "#9CA3AF", "#64748B" };
            foreach (var sig in _lastMulti.PerSignal)
            {
                list.Add(PlotSeries.Create(
                    sig.Name + " (aligné)", sig.Points,
                    Avalonia.Media.Color.Parse(mutedColors[i % mutedColors.Length]),
                    strokeThickness: 1.1));
                i++;
            }
        }

        list.Add(PlotSeries.Create("Mesure (croisée)", r.MeasuredCurve, Avalonia.Media.Color.Parse("#22D3EE"), fill: true));
        list.Add(PlotSeries.Create("Cible", r.TargetCurvePts, Avalonia.Media.Color.Parse("#FF9F1C"), dashed: true));
        list.Add(PlotSeries.Create("Prédiction corrigée", r.PredictedCurve, Avalonia.Media.Color.Parse("#4ADE80")));
        return list;
    }
}
