using System.Diagnostics;
using RoomEq.Audio.Capture;
using RoomEq.Audio.Playback;
using RoomEq.Core.Calibration;
using RoomEq.Core.Dsp;
using RoomEq.Core.Io;

namespace RoomEq.Audio.Measurement;

/// <summary>
/// Moteur de mesure acoustique : joue du bruit blanc sur la sortie sélectionnée,
/// capture le micro simultanément, puis conçoit la correction par analyse de fréquences.
/// </summary>
public sealed class RoomMeasurementEngine
{
    private readonly PlaybackEngine _playback;
    private readonly CaptureEngine _capture;

    public RoomMeasurementEngine(PlaybackEngine playback, CaptureEngine capture)
    {
        _playback = playback;
        _capture = capture;
    }

    /// <summary>Libère le périphérique de capture (fermeture de l'application).</summary>
    public void Dispose() => _capture.Dispose();

    /// <summary>Périphérique de capture à utiliser (« capture|… », ou null = micro par défaut).</summary>
    public string? CaptureDeviceId { get; set; }

    /// <summary>
    /// Canal du micro à capturer : <see cref="ChannelMixer.MixFirstTwo"/> (−1, défaut) =
    /// moyenne des deux premiers canaux ; 0 = canal 1 seul, 1 = canal 2 seul, etc.
    /// Utile pour les micros multi-canaux dont certains canaux sont muets ou parasites.
    /// </summary>
    public int CaptureChannelIndex { get; set; } = ChannelMixer.MixFirstTwo;

    /// <summary>Progression de la mesure (0..1).</summary>
    public event Action<double>? ProgressChanged;

    /// <summary>Chunk mono capturé en direct (pour l'analyseur temps réel pendant la mesure).</summary>
    public event Action<float[]>? LiveChunk;

    /// <summary>
    /// Lance une mesure complète et renvoie le résultat (courbes + preset de correction).
    /// </summary>
    /// <param name="durationSeconds">Durée d'intégration (10 à 45 s recommandé).</param>
    /// <param name="amplitude">Amplitude du bruit blanc 0..1 (0.15–0.3 recommandé).</param>
    /// <param name="options">Options de conception de la correction.</param>
    /// <param name="micProfile">Profil de correction micro (optionnel mais recommandé).</param>
    public async Task<CorrectionResult> MeasureAsync(
        double durationSeconds, double amplitude,
        CorrectionOptions options, MicProfile? micProfile,
        string presetName, CancellationToken ct)
    {
        bool previousBypass = _playback.Eq.Bypass;
        _playback.Eq.Bypass = true; // on mesure la pièce, pas l'égaliseur

        var chunks = new List<float[]>();
        var gate = new object();
        int captureRate = 48000;

        try
        {
            // la capture démarre AVANT la lecture : le pré-roulement (silence) est
            // capturé, le rognage d'attaque ne coupe donc jamais le signal utile
            StartCapture(chunks, gate, r => captureRate = r);
            await Task.Delay(700, ct); // laisser la capture démarrer (silence de pré-roulement)

            _playback.PlayTestSignal(TestSignal.WhiteNoise, amplitude);

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < durationSeconds)
            {
                ct.ThrowIfCancellationRequested();
                ProgressChanged?.Invoke(Math.Min(1.0, sw.Elapsed.TotalSeconds / durationSeconds));
                await Task.Delay(100, ct);
            }
            ProgressChanged?.Invoke(1.0);
        }
        finally
        {
            _playback.Stop();
            _capture.Stop();
            _playback.Eq.Bypass = previousBypass;
        }

        // laisser arriver les derniers fragments
        await Task.Delay(400, CancellationToken.None);

        var (all, rate) = AssembleSegment(chunks, gate, captureRate, trimStartSeconds: 0.5, trimEndSeconds: 0.25);

        double peakDb = SpectrumTools.PeakDb(all);
        DiagnosticsLog.Info(string.Format(
            "Mesure mono : {0:0.0} s à {1} Hz — crête {2:0.0} dBFS, RMS {3:0.0} dBFS",
            all.Length / (double)rate, rate, peakDb, SpectrumTools.RmsDb(all)));

        if (peakDb < SilencePeakDbfs)
            throw CreateSilenceError();

        if (all.Length < rate * 2)
            throw new InvalidOperationException(
                "Mesure trop courte. Vérifiez que le micro est le périphérique " +
                "d'entrée par défaut, qu'il n'est pas muet, et augmentez le niveau du bruit de test.");

        // Analyse spectrale (moyenne de Welch) puis conception de la correction
        var db = SpectrumTools.WelchDb(all, rate, 16384, out double[] binFreqs);
        return CorrectionDesigner.Design(binFreqs, db, options, _playback.CurrentSampleRate, presetName, micProfile);
    }

    /// <summary>Phase courante de la mesure multi-signaux (texte pour l'interface).</summary>
    public event Action<string>? PhaseChanged;

    /// <summary>Résultat d'une mesure multi-signaux complète.</summary>
    public sealed class MultiSignalResult
    {
        public CorrectionResult Correction { get; set; } = new();
        public List<SignalDeviation> PerSignal { get; set; } = new();
        public int SignalCount { get; set; }
        public int FilterCount { get; set; }
        public double ResidualRmsDb { get; set; }

        /// <summary>Signaux ignorés (analyse impossible) — affichés à l'utilisateur.</summary>
        public List<string> Warnings { get; } = new();
    }

    /// <summary>
    /// Mesure multi-signaux : chaque signal sélectionné (bruits + balayage sinusoïdal
    /// progressif) est joué et capturé tour à tour, puis toutes les mesures sont
    /// croisées (médiane robuste) et la correction est placée automatiquement par
    /// AutoBandPlanner — sans aucun doublon de bandes de fréquences.
    /// Si l'analyse d'un signal échoue, il est ignoré avec un avertissement visible ;
    /// la mesure ne peut échouer complètement qu'aucun signal exploitable n'a été capturé.
    /// </summary>
    /// <param name="kinds">Signaux à mesurer, dans l'ordre donné.</param>
    /// <param name="secondsPerSignal">Durée d'intégration de chaque signal de bruit (8 à 20 s).</param>
    /// <param name="sweepSeconds">Durée du balayage progressif (6 à 20 s).</param>
    public async Task<MultiSignalResult> MeasureMultiSignalAsync(
        IReadOnlyList<MeasurementSignalKind> kinds,
        double secondsPerSignal, double sweepSeconds, double amplitude,
        CorrectionOptions options, MicProfile? micProfile,
        string presetName, CancellationToken ct)
    {
        if (kinds.Count == 0)
            throw new ArgumentException("Sélectionnez au moins un signal de mesure.");

        DiagnosticsLog.Info(string.Format(
            "Calibration multi-signaux : {0} signal(s), {1:0} s par bruit, balayage {2:0} s, amplitude {3:0.00}, micro {4}, canal {5}",
            kinds.Count, secondsPerSignal, sweepSeconds, amplitude, CaptureDeviceId ?? "défaut",
            CaptureChannelIndex == ChannelMixer.MixFirstTwo ? "mixage" : (CaptureChannelIndex + 1).ToString()));

        bool previousBypass = _playback.Eq.Bypass;
        _playback.Eq.Bypass = true;

        var curves = new List<SignalCurve>();
        var warnings = new List<string>();
        Exception? lastError = null;

        try
        {
            int total = kinds.Count;
            int done = 0;

            var sweepSpec = MeasurementSignalCatalog.DefaultSweep(sweepSeconds);

            foreach (var kind in kinds)
            {
                ct.ThrowIfCancellationRequested();

                if (MeasurementSignalCatalog.IsBroadband(kind))
                {
                    string name = MeasurementSignalCatalog.DisplayName(kind);
                    PhaseChanged?.Invoke($"Mesure {done + 1}/{total} : {name}…");

                    try
                    {
                        var (samples, rate, stats) = await CaptureSegmentAsync(
                            kind switch
                            {
                                MeasurementSignalKind.WhiteNoise => TestSignal.WhiteNoise,
                                MeasurementSignalKind.PinkNoise => TestSignal.PinkNoise,
                                MeasurementSignalKind.BrownNoise => TestSignal.BrownNoise,
                                _ => TestSignal.VioletNoise
                            },
                            amplitude, null, secondsPerSignal, ct,
                            p => ProgressChanged?.Invoke((done + p) / total),
                            sweepSpec: null);

                        var db = SpectrumTools.WelchDb(samples, rate, 16384, out var binFreqs);
                        curves.Add(new SignalCurve
                        {
                            Name = name,
                            Kind = kind,
                            Freqs = binFreqs,
                            Db = db
                        });
                        done++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        string warn = $"{name} ignoré : {ex.Message}";
                        warnings.Add(warn);
                        DiagnosticsLog.Error($"Segment {name} perdu", ex);
                        PhaseChanged?.Invoke("⚠ " + warn);
                    }
                }
                else
                {
                    // balayage progressif : la fréquence monte en continu de F1 à F2,
                    // ce qui étalonne TOUT le spectre en une seule passe
                    PhaseChanged?.Invoke($"Mesure {done + 1}/{total} : balayage progressif " +
                                         $"{sweepSpec.F1:0} Hz → {sweepSpec.F2:0} Hz…");

                    try
                    {
                        var (samples, rate, stats) = await CaptureSegmentAsync(
                            TestSignal.LogSweep, amplitude, null, sweepSeconds, ct,
                            p => ProgressChanged?.Invoke((done + p) / total),
                            sweepSpec: sweepSpec, tailTrimSeconds: 0.05); // préserver la fin du balayage (aigus)

                        PhaseChanged?.Invoke("Analyse du balayage (déconvolution)…");
                        var analysis = await Task.Run(
                            () => SweepAnalyzer.Analyze(samples, rate, sweepSpec), ct);

                        DiagnosticsLog.Info(string.Format(
                            "Balayage analysé : pic à {0:0.000} s (magnitude {1:0.0}), fenêtre IR {2:0.00} s, {3} bandes",
                            analysis.PeakDelaySeconds, 20 * Math.Log10(analysis.PeakMagnitude + 1e-12),
                            analysis.IrWindowSeconds, analysis.BandFreqs.Length));

                        curves.Add(new SignalCurve
                        {
                            Name = MeasurementSignalCatalog.DisplayName(MeasurementSignalKind.FrequencySweep),
                            Kind = MeasurementSignalKind.FrequencySweep,
                            Freqs = analysis.BandFreqs,
                            Db = analysis.BandLevelsDb
                        });
                        done++;
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        lastError = ex;
                        string warn = $"Balayage progressif ignoré : {ex.Message}";
                        warnings.Add(warn);
                        DiagnosticsLog.Error("Analyse du balayage impossible", ex);
                        PhaseChanged?.Invoke("⚠ " + warn);
                    }
                }
            }

            ProgressChanged?.Invoke(1.0);
            PhaseChanged?.Invoke(curves.Count > 0
                ? $"Croisement des mesures ({curves.Count} exploitable(s))…"
                : "Aucun signal exploitable…");
        }
        finally
        {
            _playback.Eq.Bypass = previousBypass;
        }

        if (curves.Count == 0)
        {
            DiagnosticsLog.Error("Calibration multi-signaux : aucun signal exploitable", lastError);
            throw new InvalidOperationException(
                "Aucun signal exploitable n'a été capturé. " +
                (lastError?.Message ?? "Vérifiez le micro sélectionné et le niveau du signal de test."));
        }

        if (warnings.Count > 0)
            DiagnosticsLog.Warn($"Calibration : {warnings.Count} signal(aux) ignoré(s) — {string.Join(" | ", warnings)}");

        var aggregate = MultiSignalAnalysis.Aggregate(curves, new List<SinePoint>(), options, micProfile);

        var weight = MultiSignalAnalysis.ConfidenceWeight(aggregate.Freqs, aggregate.StdDb);
        var correction = CorrectionDesigner.DesignFromAlignedCurve(
            aggregate.Freqs, aggregate.AlignedDb, options,
            _playback.CurrentSampleRate, presetName, micProfile,
            plannerOptions: null, confidenceWeight: weight);

        DiagnosticsLog.Info(string.Format(
            "Calibration terminée : {0} signal(aux) croisé(s), {1} filtres, préampli {2:0.0} dB, avertissements {3}",
            curves.Count, correction.Preset.Bands.Count, correction.Preset.PreampDb, warnings.Count));

        var result = new MultiSignalResult
        {
            Correction = correction,
            PerSignal = aggregate.PerSignal,
            SignalCount = curves.Count,
            FilterCount = correction.Preset.Bands.Count,
            ResidualRmsDb = correction.ResidualRmsDb()
        };
        result.Warnings.AddRange(warnings);
        return result;
    }

    /// <summary>Niveau crête (dBFS) en dessous duquel un segment est considéré muet.</summary>
    public const double SilencePeakDbfs = -66.0;

    private static InvalidOperationException CreateSilenceError() => new(
        "Le micro sélectionné ne capte rien (niveau quasi nul). Vérifiez : " +
        "1) le micro choisi dans la liste (évitez « RoomEq Output » et les cartes virtuelles — " +
        "choisissez votre micro physique) ; 2) le niveau du micro dans les réglages son de Windows " +
        "(pas en sourdine) ; 3) le niveau du signal de test.");

    /// <summary>
    /// Démarre la capture : les chunks (y compris le pré-roulement silencieux)
    /// sont accumulés jusqu'à l'arrêt explicite. Le taux réel (repli de format inclus)
    /// est remonté via onRate. La réduction mono suit CaptureChannelIndex.</summary>
    private void StartCapture(List<float[]> chunks, object gate, Action<int> onRate)
    {
        int channelIndex = CaptureChannelIndex;
        _capture.Start(CaptureDeviceId, (samples, ch, rate) =>
        {
            onRate(rate);
            var mono = ChannelMixer.ToMono(samples, ch, channelIndex);
            lock (gate) chunks.Add(mono);
            LiveChunk?.Invoke(mono);
        });
    }

    /// <summary>
    /// Joue UN signal pendant `seconds`, capture le micro en parallèle et renvoie le
    /// fragment utile (0,5 s d'attaque écartés, `tailTrimSeconds` de fin écartés).
    /// La capture démarre AVANT la lecture (pré-roulement silencieux) : le rognage
    /// d'attaque ne coupe jamais le début du signal — essentiel pour le balayage,
    /// dont la référence d'analyse doit correspondre exactement au signal émis.
    /// Pour le balayage, une marge de fin garantit que la totalité du sweep (aigus
    /// compris) est capturée même si le démarrage de la lecture est retardé.
    /// </summary>
    private async Task<(float[] Samples, int Rate, string Stats)> CaptureSegmentAsync(
        TestSignal signal, double amplitude, double? sineFrequency,
        double seconds, CancellationToken ct, Action<double>? progress,
        LogSweepSpec? sweepSpec = null,
        double tailTrimSeconds = 0.25)
    {
        var chunks = new List<float[]>();
        var gate = new object();
        int captureRate = 48000;

        try
        {
            StartCapture(chunks, gate, r => captureRate = r);
            await Task.Delay(700, ct); // pré-roulement : silence capturé avant la lecture

            _playback.PlayTestSignal(signal, amplitude, sineFrequency ?? 1000.0, sweepSpec);

            // marge de fin pour le balayage : la lecture démarre avec un léger retard
            // (démarrage du pipeline audio) — on attend au-delà de la durée du sweep
            // pour garantir que la fin du balayage (aigus) est bien jouée et capturée
            double wait = sweepSpec != null ? seconds + 0.8 : seconds;

            var sw = Stopwatch.StartNew();
            while (sw.Elapsed.TotalSeconds < wait)
            {
                ct.ThrowIfCancellationRequested();
                progress?.Invoke(Math.Min(1.0, sw.Elapsed.TotalSeconds / seconds));
                await Task.Delay(100, ct);
            }
            progress?.Invoke(1.0);
        }
        finally
        {
            _playback.Stop();
            _capture.Stop();
        }

        // laisser arriver les derniers fragments
        await Task.Delay(400, CancellationToken.None);

        var (all, rate) = AssembleSegment(chunks, gate, captureRate,
            trimStartSeconds: 0.5, trimEndSeconds: tailTrimSeconds);

        double peakDb = SpectrumTools.PeakDb(all);
        double rmsDb = SpectrumTools.RmsDb(all);
        string stats = string.Format(
            "{0:0.0} s utiles à {1} Hz — crête {2:0.0} dBFS, RMS {3:0.0} dBFS",
            all.Length / (double)rate, rate, peakDb, rmsDb);
        DiagnosticsLog.Info($"Segment « {signal} » : {stats}");

        if (all.Length < rate / 2)
            throw new InvalidOperationException(
                "Segment de mesure trop court. Vérifiez le micro sélectionné et son niveau dans Windows.");

        if (peakDb < SilencePeakDbfs)
            throw CreateSilenceError();

        return (all, rate, stats);
    }

    /// <summary>Concatène les chunks en rognant le début et la fin (thread-safe).</summary>
    private static (float[] Samples, int Rate) AssembleSegment(
        List<float[]> chunks, object gate, int rate,
        double trimStartSeconds, double trimEndSeconds)
    {
        int trimStart = (int)(rate * trimStartSeconds); // transitoire d'attaque (tombe dans le pré-roulement silencieux)
        int trimEnd = (int)(rate * trimEndSeconds);     // transition d'arrêt
        float[] all;
        lock (gate)
        {
            int total = chunks.Sum(c => c.Length);
            int start = Math.Min(trimStart, total);
            int keep = Math.Max(0, total - start - trimEnd);
            all = new float[keep];
            int pos = 0, skipped = 0;
            foreach (var c in chunks)
            {
                int chunkStart = 0;
                if (skipped < start)
                {
                    int need = start - skipped;
                    chunkStart = Math.Min(need, c.Length);
                    skipped += chunkStart;
                }
                int available = c.Length - chunkStart;
                if (available <= 0 || pos >= keep) continue;
                int n = Math.Min(available, keep - pos);
                Array.Copy(c, chunkStart, all, pos, n);
                pos += n;
            }
        }
        return (all, rate);
    }
}
