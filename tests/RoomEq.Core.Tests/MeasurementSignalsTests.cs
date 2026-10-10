using RoomEq.Core.Calibration;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using Xunit;

namespace RoomEq.Core.Tests;

/// <summary>
/// Tests des nouveaux signaux de mesure (brun, violet) et du croisement
/// multi-signaux (MultiSignalAnalysis).
/// </summary>
public class MeasurementSignalsTests
{
    private static double BandLevelAt(float[] samples, int fs, double freq)
    {
        var db = SpectrumTools.WelchDb(samples, fs, 8192, out var bins);
        SpectrumTools.OctaveLevels(bins, db, freq, freq, 1.0, out var centers, out var levels);
        return levels.Length > 0 ? levels[0] : double.NaN;
    }

    [Fact]
    public void BrownNoise_BandSlope_IsAroundMinusSixDbPerOctave()
    {
        int fs = 48000, n = fs * 8;
        var buf = new float[n];
        new NoiseGenerator(42).FillBrown(buf);

        // mesure loin du coin de l'intégrateur fuyant (≈ 150 Hz)
        double l300 = BandLevelAt(buf, fs, 300);
        double l2400 = BandLevelAt(buf, fs, 2400);
        double slopePerOct = (l2400 - l300) / 3.0;

        Assert.InRange(slopePerOct, -6.8, -4.8);
    }

    [Fact]
    public void BrownNoise_StaysBounded()
    {
        int n = 48000 * 5;
        var buf = new float[n];
        new NoiseGenerator(7).FillBrown(buf);
        Assert.All(buf, s => Assert.True(Math.Abs(s) <= 1.0f, $"échantillon hors bornes : {s}"));
        Assert.All(buf, s => Assert.False(float.IsNaN(s)));
    }

    [Fact]
    public void VioletNoise_BandSlope_IsPlusSixDbPerOctave()
    {
        int fs = 48000, n = fs * 8;
        var buf = new float[n];
        new NoiseGenerator(42).FillViolet(buf);

        double l100 = BandLevelAt(buf, fs, 100);
        double l800 = BandLevelAt(buf, fs, 800);
        double slopePerOct = (l800 - l100) / 3.0;

        Assert.InRange(slopePerOct, 5.2, 6.9);
    }

    [Fact]
    public void VioletNoise_StaysBounded()
    {
        int n = 48000 * 5;
        var buf = new float[n];
        new NoiseGenerator(7).FillViolet(buf);
        Assert.All(buf, s => Assert.True(Math.Abs(s) <= 1.0f, $"échantillon hors bornes : {s}"));
    }

    [Fact]
    public void ExpectedBandSlopes_MatchTheory()
    {
        Assert.Equal(0.0, MeasurementSignalCatalog.ExpectedBandSlopeDbPerOct(MeasurementSignalKind.WhiteNoise), 6);
        Assert.Equal(-3.0103, MeasurementSignalCatalog.ExpectedBandSlopeDbPerOct(MeasurementSignalKind.PinkNoise), 3);
        Assert.Equal(-6.0206, MeasurementSignalCatalog.ExpectedBandSlopeDbPerOct(MeasurementSignalKind.BrownNoise), 3);
        Assert.Equal(6.0206, MeasurementSignalCatalog.ExpectedBandSlopeDbPerOct(MeasurementSignalKind.VioletNoise), 3);
    }

    [Fact]
    public void SineLevelDb_FindsExactLevelOfPureSine()
    {
        int fs = 48000, n = fs; // 1 s
        var buf = new float[n];
        double amp = 0.25;
        for (int i = 0; i < n; i++) buf[i] = (float)(amp * Math.Sin(2 * Math.PI * 1000.0 * i / fs));

        var (freq, level) = MultiSignalAnalysis.SineLevelDb(buf, fs, 1000);
        Assert.InRange(freq, 950, 1050);
        Assert.InRange(level, 20 * Math.Log10(amp) - 2.0, 20 * Math.Log10(amp) + 2.0);
    }

    /// <summary>Construit une « pièce synthétique » : +5 dB à 80 Hz, -4 dB à 3,15 kHz.</summary>
    private static double RoomResponse(double f)
    {
        var lowPeak = new BiquadFilter(FilterType.Peaking, 48000, 80, 5.0, 1.2);
        var highDip = new BiquadFilter(FilterType.Peaking, 48000, 3150, -4.0, 2.0);
        return lowPeak.ResponseDb(f, 48000) + highDip.ResponseDb(f, 48000);
    }

    /// <summary>Pentes PSD par octave = pentes attendues des niveaux de bandes (moyenne par bin).</summary>
    private static double PsdSlope(MeasurementSignalKind kind)
        => MeasurementSignalCatalog.ExpectedBandSlopeDbPerOct(kind);

    private static SignalCurve MakeCurve(MeasurementSignalKind kind, int seed, double extraOffset, double jitterDb)
    {
        var rng = new Random(seed);
        var freqs = new List<double>();
        for (double f = 25.0; f <= 20000.0 * 1.0001; f *= Math.Pow(2.0, 1.0 / 24.0))
            freqs.Add(f);

        var db = new double[freqs.Count];
        for (int i = 0; i < freqs.Count; i++)
        {
            double f = freqs[i];
            db[i] = RoomResponse(f)
                    + PsdSlope(kind) * Math.Log2(f / 1000.0)
                    + extraOffset
                    + (rng.NextDouble() * 2 - 1) * jitterDb;
        }
        return new SignalCurve
        {
            Name = MeasurementSignalCatalog.DisplayName(kind),
            Kind = kind,
            Freqs = freqs.ToArray(),
            Db = db
        };
    }

    private static CorrectionOptions DefaultOptions() => new()
    {
        FreqMin = 40,
        FreqMax = 16000,
        AlignMinHz = 200,
        AlignMaxHz = 8000,
        Target = TargetType.Flat
    };

    [Fact]
    public void Aggregate_MedianOfFourSignals_RecoversRoomResponse()
    {
        var curves = new List<SignalCurve>
        {
            MakeCurve(MeasurementSignalKind.WhiteNoise, 1, 0.0, 0.3),
            MakeCurve(MeasurementSignalKind.PinkNoise, 2, 3.0, 0.3),
            MakeCurve(MeasurementSignalKind.BrownNoise, 3, -2.0, 0.3),
            MakeCurve(MeasurementSignalKind.VioletNoise, 4, 7.0, 0.3)
        };

        var agg = MultiSignalAnalysis.Aggregate(curves, new List<SinePoint>(), DefaultOptions(), null);

        Assert.True(agg.Freqs.Length >= 40, $"grille trop courte : {agg.Freqs.Length} bandes");

        // les déviations des signaux individuels sont produites pour l'affichage
        Assert.Equal(4, agg.PerSignal.Count);

        // la médiane retrace la réponse attendue (±1,2 dB) dans la plage d'alignement et au-delà
        for (int i = 0; i < agg.Freqs.Length; i++)
        {
            double f = agg.Freqs[i];
            if (f < 100 || f > 10000) continue;
            double expected = RoomResponse(f) - 0.0; // alignée : moyenne ≈ 0 sur 200-8000 (réponse quasi symétrique)
            Assert.True(Math.Abs(agg.AlignedDb[i] - expected) < 1.2,
                $"à {f:0.#} Hz : {agg.AlignedDb[i]:0.00} dB, attendu ≈ {expected:0.00} dB");
        }
    }

    [Fact]
    public void Aggregate_IsRobustToOneCorruptedSignal()
    {
        var clean = new List<SignalCurve>
        {
            MakeCurve(MeasurementSignalKind.WhiteNoise, 11, 0.0, 0.3),
            MakeCurve(MeasurementSignalKind.PinkNoise, 12, 3.0, 0.3),
            MakeCurve(MeasurementSignalKind.BrownNoise, 13, -2.0, 0.3),
            MakeCurve(MeasurementSignalKind.VioletNoise, 14, 7.0, 0.3)
        };
        var cleanAgg = MultiSignalAnalysis.Aggregate(clean, new List<SinePoint>(), DefaultOptions(), null);

        // la courbe blanche est corrompue : +12 dB entre 1 et 3 kHz (mesure polluée)
        var corrupted = MakeCurve(MeasurementSignalKind.WhiteNoise, 11, 0.0, 0.3);
        for (int i = 0; i < corrupted.Freqs.Length; i++)
            if (corrupted.Freqs[i] >= 1000 && corrupted.Freqs[i] <= 3000)
                corrupted.Db[i] += 12.0;

        var list = new List<SignalCurve>
        {
            corrupted,
            clean[1], clean[2], clean[3]
        };
        var robustAgg = MultiSignalAnalysis.Aggregate(list, new List<SinePoint>(), DefaultOptions(), null);

        // dans la zone corrompue, la médiane reste fidèle à la réponse réelle
        int comparisons = 0;
        for (int i = 0; i < robustAgg.Freqs.Length; i++)
        {
            double f = robustAgg.Freqs[i];
            if (f < 1200 || f > 2500) continue;
            int j = Array.IndexOf(cleanAgg.Freqs, f);
            if (j < 0) continue;
            Assert.True(Math.Abs(robustAgg.AlignedDb[i] - cleanAgg.AlignedDb[j]) < 1.5,
                $"à {f:0.#} Hz : influence du signal corrompu = {robustAgg.AlignedDb[i] - cleanAgg.AlignedDb[j]:0.00} dB");
            comparisons++;
        }
        Assert.True(comparisons >= 5, "plage corrompue non couverte par la grille");

        // la dispersion inter-signaux signale la zone de désaccord
        double stdCorrupt = 0, stdClean = 0;
        int nC = 0, nL = 0;
        for (int i = 0; i < robustAgg.Freqs.Length; i++)
        {
            double f = robustAgg.Freqs[i];
            if (f >= 1200 && f <= 2500) { stdCorrupt += robustAgg.StdDb[i]; nC++; }
            if (f >= 400 && f <= 800) { stdClean += cleanAgg.StdDb[i]; nL++; }
        }
        Assert.True(stdCorrupt / Math.Max(1, nC) > stdClean / Math.Max(1, nL),
            "la dispersion ne reflète pas le désaccord inter-signaux");
    }

    [Fact]
    public void Aggregate_IncludesSinePointsAsExtraEstimates()
    {
        var curves = new List<SignalCurve>
        {
            MakeCurve(MeasurementSignalKind.WhiteNoise, 21, 0.0, 0.3),
            MakeCurve(MeasurementSignalKind.PinkNoise, 22, 3.0, 0.3)
        };

        // points sinus dérivés de la réponse de référence + offset constant
        var sines = MeasurementSignalCatalog.DefaultSineSteps
            .Select(f => new SinePoint(f, RoomResponse(f) + 4.0))
            .ToList();

        var agg = MultiSignalAnalysis.Aggregate(curves, sines, DefaultOptions(), null);

        // la déviation sinus apparaît dans les courbes individuelles
        var sineDev = agg.PerSignal.FirstOrDefault(p => p.Kind == MeasurementSignalKind.FrequencySweep);
        Assert.NotNull(sineDev);
        Assert.Equal(sines.Count, sineDev!.Points.Count);

        // les sinus affinent les bandes : la réponse agrégée reste fidèle autour de 63 Hz
        int idx = -1;
        for (int i = 0; i < agg.Freqs.Length; i++)
            if (Math.Abs(agg.Freqs[i] - 63.0) < 4) { idx = i; break; }
        Assert.True(idx >= 0, "bande 63 Hz absente");
        Assert.True(Math.Abs(agg.AlignedDb[idx] - RoomResponse(agg.Freqs[idx])) < 1.8,
            $"à {agg.Freqs[idx]:0.#} Hz : {agg.AlignedDb[idx]:0.00} dB");
    }

    [Fact]
    public void Aggregate_RequiresData()
    {
        Assert.Throws<ArgumentException>(() =>
            MultiSignalAnalysis.Aggregate(new List<SignalCurve>(), new List<SinePoint>(), DefaultOptions(), null));
    }

    /// <summary>Test de bout en bout : croisement multi-signaux → correction automatique.</summary>
    [Fact]
    public void FullPipeline_MultiSignal_DesignsOptimalCorrection()
    {
        var curves = new List<SignalCurve>
        {
            MakeCurve(MeasurementSignalKind.WhiteNoise, 31, 0.0, 0.3),
            MakeCurve(MeasurementSignalKind.PinkNoise, 32, 3.0, 0.3),
            MakeCurve(MeasurementSignalKind.BrownNoise, 33, -2.0, 0.3),
            MakeCurve(MeasurementSignalKind.VioletNoise, 34, 7.0, 0.3)
        };

        var opt = DefaultOptions();
        var agg = MultiSignalAnalysis.Aggregate(curves, new List<SinePoint>(), opt, null);
        var weight = MultiSignalAnalysis.ConfidenceWeight(agg.Freqs, agg.StdDb);

        var result = CorrectionDesigner.DesignFromAlignedCurve(
            agg.Freqs, agg.AlignedDb, opt, 48000, "Test multi", null,
            plannerOptions: null, confidenceWeight: weight);

        // des filtres ont été placés, dans les limites de sécurité
        Assert.True(result.Preset.Bands.Count >= 2, $"seulement {result.Preset.Bands.Count} bandes");
        Assert.All(result.Preset.Bands, b =>
        {
            Assert.InRange(b.GainDb, -opt.MaxCutDb - 1e-9, opt.MaxBoostDb + 1e-9);
            Assert.InRange(b.Q, 0.7, 8.0);
        });
        Assert.True(result.Preset.PreampDb <= 0, "préampli de sécurité attendu");

        // la prédiction se rapproche fortement de la cible plate
        double rms = 0; int n = 0;
        foreach (var p in result.PredictedCurve)
        {
            if (p.Freq < 150 || p.Freq > 8000) continue;
            rms += p.Db * p.Db;
            n++;
        }
        rms = Math.Sqrt(rms / n);
        Assert.True(rms < 1.5, $"résidu RMS trop élevé : {rms:0.00} dB");

        // la correction compense bien les anomalies connues de la pièce synthétique
        double at80 = SpectrumTools.InterpLog(
            result.Preset.Bands.Select(b => b.Frequency).ToList(),
            result.Preset.Bands.Select(b => b.GainDb).ToList(), 80.0);
        Assert.True(at80 < -1.0, $"la bosse de 80 Hz n'est pas coupée : {at80:0.00} dB");

        double at3150 = SpectrumTools.InterpLog(
            result.Preset.Bands.Select(b => b.Frequency).ToList(),
            result.Preset.Bands.Select(b => b.GainDb).ToList(), 3150.0);
        Assert.True(at3150 > 1.0, $"le creux de 3,15 kHz n'est pas comblé : {at3150:0.00} dB");
    }
}
