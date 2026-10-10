using System.Numerics;
using RoomEq.Core.Calibration;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using Xunit;

namespace RoomEq.Core.Tests;

/// <summary>
/// Tests du balayage sinusoïdal progressif (générateur + analyseur par déconvolution
/// de Farina) et du plan de mesure associé. Le balayage remplace les sinus discrets :
/// la fréquence monte en continu de 20 Hz à 20 kHz, ce qui étalonne tout le spectre.
/// </summary>
public class SweepTests
{
    // ------------------------------------------------------------------
    // Générateur
    // ------------------------------------------------------------------

    [Fact]
    public void Generator_IsDeterministicAndBounded()
    {
        var spec = new LogSweepSpec(40.0, 16000.0, 2.0);
        var g1 = new LogSweepGenerator(spec, 48000);
        var g2 = new LogSweepGenerator(spec, 48000);

        var a = g1.GenerateFull(0.5);
        var b = new float[a.Length];
        g2.Fill(b, 0.5);

        Assert.Equal(a.Length, b.Length);
        for (int i = 0; i < a.Length; i++)
            Assert.Equal(a[i], b[i]); // même spécification → flux identique

        Assert.All(a, s => Assert.True(Math.Abs(s) <= 0.5f + 1e-6f, $"échantillon hors bornes : {s}"));
        Assert.All(a, s => Assert.False(float.IsNaN(s)));

        // silence au-delà de la durée totale
        var tail = new float[4800];
        g1.Fill(tail, 0.5);
        Assert.All(tail, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void Generator_FrequencyRisesMonotonically()
    {
        var gen = new LogSweepGenerator(LogSweepSpec.Default, 48000);
        double fPrev = 0;
        for (long n = 0; n < gen.TotalSamples; n += 4800)
        {
            double f = gen.FrequencyAt(n);
            Assert.True(f > fPrev, $"fréquence non croissante à n={n}");
            fPrev = f;
        }
        Assert.InRange(gen.FrequencyAt(0), 20.0, 20.5);
        Assert.InRange(gen.FrequencyAt(gen.TotalSamples - 1), 19900.0, 20001.0);
    }

    // ------------------------------------------------------------------
    // Analyseur (déconvolution)
    // ------------------------------------------------------------------

    /// <summary>Applique une réponse de transfert H(f) au balayage dans le domaine de
    /// Fourier, avec un retard D (la réponse synthétique devient causale) : silence
    /// avant l'arrivée du son, comme une vraie capture.</summary>
    private static float[] FilterSweep(float[] sweep, int sampleRate, Func<double, double> responseDb, double delaySeconds)
    {
        int n = Fft.NextPow2(sweep.Length * 2);
        var buf = new Complex[n];
        for (int i = 0; i < sweep.Length; i++) buf[i] = new Complex(sweep[i], 0.0);
        Fft.Forward(buf);

        double binHz = (double)sampleRate / n;
        for (int k = 0; k < n; k++)
        {
            double f = k * binHz;
            if (f > sampleRate / 2.0) f = sampleRate - f; // miroir (spectre réel)
            double gain = Math.Pow(10.0, responseDb(f) / 20.0);
            double angle = -2.0 * Math.PI * k * (delaySeconds * sampleRate) / n;
            buf[k] *= gain * new Complex(Math.Cos(angle), Math.Sin(angle));
        }
        Fft.Inverse(buf);

        int delaySamples = (int)(delaySeconds * sampleRate);
        // une vraie capture continue après l'arrêt de la lecture (post-roll) :
        // sans cela, la fin du balayage retardé (aigus) serait rognée
        var result = new float[sweep.Length + sampleRate];
        for (int i = delaySamples; i < result.Length; i++)
            result[i] = i < buf.Length ? (float)buf[i].Real : 0f;
        return result;
    }

    private static double RoomResponse(double f)
    {
        var lowPeak = new BiquadFilter(FilterType.Peaking, 48000, 100, 6.0, 2.0);
        var highDip = new BiquadFilter(FilterType.Peaking, 48000, 4000, -4.0, 3.0);
        return lowPeak.ResponseDb(f, 48000) + highDip.ResponseDb(f, 48000);
    }

    /// <summary>Synthétise une capture réaliste : le balayage passe dans DEUX BIQUADS
    /// réels (chaîne causale, minimum-phase, comme une vraie pièce), retardé de `delaySeconds`.</summary>
    private static float[] FilterSweepBiquads(float[] sweep, int sampleRate, double delaySeconds)
    {
        var lowPeak = new BiquadFilter(FilterType.Peaking, sampleRate, 100, 6.0, 2.0);
        var highDip = new BiquadFilter(FilterType.Peaking, sampleRate, 4000, -4.0, 3.0);
        int delaySamples = (int)(delaySeconds * sampleRate);
        var result = new float[sweep.Length + sampleRate]; // post-roll 1 s
        for (int i = 0; i < result.Length; i++)
        {
            double x = i >= delaySamples && i < delaySamples + sweep.Length ? sweep[i - delaySamples] : 0.0;
            result[i] = (float)highDip.ProcessSample(lowPeak.ProcessSample(x));
        }
        return result;
    }

    [Fact]
    public void Analyzer_FlatRoom_RecoversConstantLevel()
    {
        int fs = 48000;
        var spec = new LogSweepSpec(40.0, 16000.0, 3.0);
        var gen = new LogSweepGenerator(spec, fs);
        var sweep = gen.GenerateFull(1.0);

        // pièce « plate » : le balayage retardé de 0,2 s (propagation)
        var captured = FilterSweep(sweep, fs, _ => 0.0, 0.2);

        var analysis = SweepAnalyzer.Analyze(captured, fs, spec);

        Assert.True(analysis.BandFreqs.Length >= 30, $"trop peu de bandes : {analysis.BandFreqs.Length}");
        Assert.InRange(analysis.PeakDelaySeconds, 0.15, 0.25);

        for (int i = 0; i < analysis.BandFreqs.Length; i++)
        {
            double f = analysis.BandFreqs[i];
            if (f < 50 || f > 12000) continue; // marges aux extrémités du balayage
            Assert.True(Math.Abs(analysis.BandLevelsDb[i]) < 1.0,
                $"à {f:0.#} Hz : {analysis.BandLevelsDb[i]:0.00} dB (attendu ≈ 0)");
        }
    }

    [Fact]
    public void Analyzer_SyntheticRoom_RecoversAnomalies()
    {
        int fs = 48000;
        var spec = new LogSweepSpec(40.0, 16000.0, 3.0);
        var gen = new LogSweepGenerator(spec, fs);
        var sweep = gen.GenerateFull(1.0);

        var captured = FilterSweepBiquads(sweep, fs, 0.15);
        var analysis = SweepAnalyzer.Analyze(captured, fs, spec);

        // référence attendue : moyenne ÉNERGÉTIQUE de la réponse vraie sur chaque
        // bande 1/6 d'octave (une bande lisse une réponse qui varie dans la bande)
        var fineFreqs = new List<double>();
        for (double f = 20.0; f <= 20000.0 * 1.0001; f *= Math.Pow(2.0, 1.0 / 48.0))
            fineFreqs.Add(f);
        var fineDb = fineFreqs.Select(RoomResponse).ToArray();
        SpectrumTools.OctaveLevels(fineFreqs.ToArray(), fineDb,
            spec.F1 * 1.05, spec.EffectiveF2(fs) * 0.97, SweepAnalyzer.FractionOctave,
            out var expectedCenters, out var expectedLevels);
        Assert.Equal(expectedCenters.Length, analysis.BandFreqs.Length);

        int checkedBands = 0;
        for (int i = 0; i < analysis.BandFreqs.Length; i++)
        {
            double f = analysis.BandFreqs[i];
            if (f < 55 || f > 11000) continue;
            double error = Math.Abs(analysis.BandLevelsDb[i] - expectedLevels[i]);
            // les modes sont causaux et entièrement capturés par la fenêtre d'IR :
            // la fidélité attendue est meilleure que sur une synthèse à phase nulle
            Assert.True(error < 1.0,
                $"à {f:0.#} Hz : mesuré {analysis.BandLevelsDb[i]:0.00} dB, attendu {expectedLevels[i]:0.00} dB (écart {error:0.00})");
            checkedBands++;
        }
        Assert.True(checkedBands >= 30, $"trop peu de bandes vérifiées : {checkedBands}");

        // les deux anomalies sont bien retrouvées (pic ≈ +6 dB à 100 Hz, creux ≈ −4 dB à 4 kHz)
        double peak100 = analysis.BandLevelsDb.Where((_, i) => analysis.BandFreqs[i] is > 80 and < 130).Max();
        Assert.InRange(peak100, 4.5, 6.5);
        double dip4000 = analysis.BandLevelsDb.Where((_, i) => analysis.BandFreqs[i] is > 3400 and < 4800).Min();
        Assert.InRange(dip4000, -4.5, -3.0);
    }

    [Fact]
    public void Analyzer_RejectsSilentCapture()
    {
        int fs = 48000;
        var spec = new LogSweepSpec(40.0, 16000.0, 2.0);
        var silence = new float[fs * 2]; // zéros : aucun balayage détectable
        Assert.Throws<InvalidOperationException>(() =>
            SweepAnalyzer.Analyze(silence, fs, spec));
    }

    // ------------------------------------------------------------------
    // Catalogue : plan de mesure avec balayage
    // ------------------------------------------------------------------

    [Fact]
    public void Catalog_FrequencySweep_IsSingleSegmentAndFlat()
    {
        Assert.False(MeasurementSignalCatalog.IsBroadband(MeasurementSignalKind.FrequencySweep));
        Assert.True(MeasurementSignalCatalog.IsBroadband(MeasurementSignalKind.PinkNoise));

        // amplitude constante par ton → pente plate comme des sinus discrets
        Assert.Equal(0.0,
            MeasurementSignalCatalog.ExpectedBandSlopeDbPerOct(MeasurementSignalKind.FrequencySweep), 6);

        var kinds = new[] { MeasurementSignalKind.WhiteNoise, MeasurementSignalKind.FrequencySweep };
        Assert.Equal(2, MeasurementSignalCatalog.SegmentCount(kinds));

        string summary = MeasurementSignalCatalog.PlanSummary(kinds, 10.0, 12.0);
        Assert.Contains("2 signal(s)", summary);
        Assert.Contains("22 s", summary); // 10 + 12

        Assert.Equal("Balayage sinusoïdal",
            MeasurementSignalCatalog.DisplayName(MeasurementSignalKind.FrequencySweep));
    }

    // ------------------------------------------------------------------
    // Chaîne complète : balayage → croisement → correction sans doublon
    // ------------------------------------------------------------------

    [Fact]
    public void FullPipeline_SweepCurve_FeedsAggregateAndDesign()
    {
        int fs = 48000;
        var spec = new LogSweepSpec(40.0, 16000.0, 3.0);
        var sweep = new LogSweepGenerator(spec, fs).GenerateFull(1.0);
        var captured = FilterSweep(sweep, fs, RoomResponse, 0.15);
        var analysis = SweepAnalyzer.Analyze(captured, fs, spec);

        var curve = new SignalCurve
        {
            Name = MeasurementSignalCatalog.DisplayName(MeasurementSignalKind.FrequencySweep),
            Kind = MeasurementSignalKind.FrequencySweep,
            Freqs = analysis.BandFreqs,
            Db = analysis.BandLevelsDb
        };

        var opt = new CorrectionOptions
        {
            FreqMin = 40, FreqMax = 16000,
            AlignMinHz = 200, AlignMaxHz = 8000,
            Target = TargetType.Flat
        };
        var agg = MultiSignalAnalysis.Aggregate(new[] { curve }, new List<SinePoint>(), opt, null);
        Assert.True(agg.Freqs.Length >= 25, $"grille trop courte : {agg.Freqs.Length}");
        Assert.Single(agg.PerSignal);

        var correction = CorrectionDesigner.DesignFromAlignedCurve(
            agg.Freqs, agg.AlignedDb, opt, fs, "Test sweep", null);

        // la correction compense les anomalies (coupe à 100 Hz, comblement à 4 kHz)
        var freqs = correction.Preset.Bands.Select(b => b.Frequency).ToList();
        var gains = correction.Preset.Bands.Select(b => b.GainDb).ToList();
        Assert.Contains(freqs, f => f is > 70 and < 140);
        double at100 = SpectrumTools.InterpLog(freqs, gains, 100.0);
        Assert.True(at100 < -1.0, $"la bosse de 100 Hz n'est pas coupée : {at100:0.00} dB");
        double at4000 = SpectrumTools.InterpLog(freqs, gains, 4000.0);
        Assert.True(at4000 > 1.0, $"le creux de 4 kHz n'est pas comblé : {at4000:0.00} dB");

        // ZÉRO doublon de bandes : fréquences strictement croissantes et espacées
        AssertNoDuplicateBands(correction.Preset.Bands, opt);
    }

    /// <summary>Vérifie l'anti-doublon : centres strictement croissants et espacés
    /// d'au moins la zone d'exclusion (règle recalculée depuis l'API publique).</summary>
    internal static void AssertNoDuplicateBands(IEnumerable<EqBand> bands, CorrectionOptions opt)
    {
        var sorted = bands.OrderBy(b => b.Frequency).ToList();
        for (int i = 1; i < sorted.Count; i++)
        {
            Assert.True(sorted[i].Frequency > sorted[i - 1].Frequency,
                $"fréquences égales/non triées : {sorted[i - 1].Frequency:0.#} et {sorted[i].Frequency:0.#}");

            double distance = Math.Log2(sorted[i].Frequency / sorted[i - 1].Frequency);
            double halfPrev = ExclusionHalf(sorted[i - 1].Q);
            double halfCur = ExclusionHalf(sorted[i].Q);
            Assert.True(distance >= halfPrev + halfCur - 1e-9,
                $"bandes trop proches : {sorted[i - 1].Frequency:0.#} Hz (Q {sorted[i - 1].Q:0.0}) et " +
                $"{sorted[i].Frequency:0.#} Hz (Q {sorted[i].Q:0.0}) — écart {distance:0.000} oct < {halfPrev + halfCur:0.000} oct");
        }
    }

    /// <summary>Réplique la règle d'exclusion interne avec les options par défaut.</summary>
    private static double ExclusionHalf(double q)
        => Math.Max(1.0 / 12.0, 0.6 * AutoBandPlanner.BandwidthOctaves(q)) / 2.0;
}
