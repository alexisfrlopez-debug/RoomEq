using RoomEq.Core.Calibration;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using Xunit;

namespace RoomEq.Core.Tests;

/// <summary>
/// Tests du placement automatique des filtres : le nombre de bandes, leurs
/// fréquences et leurs largeurs (Q) doivent être choisis automatiquement pour
/// annuler la déviation mesurée avec un nombre minimal de filtres.
/// </summary>
public class AutoBandPlannerTests
{
    private static double[] LogGrid(double fMin, double fMax, double fractionOctave)
    {
        var list = new List<double>();
        for (double f = fMin; f <= fMax * 1.0001; f *= Math.Pow(2.0, fractionOctave))
            list.Add(f);
        return list.ToArray();
    }

    private static double[] ResponseOf(IEnumerable<EqBand> bands, double[] freqs, int fs = 48000)
    {
        var filters = bands.Select(b => new BiquadFilter(b.Type, fs, b.Frequency, b.GainDb, b.Q)).ToList();
        return freqs.Select(f => filters.Sum(fl => fl.ResponseDb(f, fs))).ToArray();
    }

    [Fact]
    public void Plan_RecoversTwoKnownAnomalies()
    {
        // pièce synthétique : +6 dB à 80 Hz (Q 1,2), -5 dB à 3,15 kHz (Q 2)
        var truth = new List<EqBand>
        {
            new(FilterType.Peaking, 80, 6.0, 1.2),
            new(FilterType.Peaking, 3150, -5.0, 2.0)
        };
        var freqs = LogGrid(20, 20000, 1.0 / 12.0);
        var deviation = ResponseOf(truth, freqs); // = cible - mesuré

        var result = AutoBandPlanner.Plan(freqs, deviation, new AutoBandOptions
        {
            FreqMin = 40,
            FreqMax = 16000,
            MaxBoostDb = 6,
            MaxCutDb = 12,
            SampleRate = 48000
        });

        Assert.True(result.Filters.Count >= 2, $"seulement {result.Filters.Count} filtres placés");

        // un filtre proche de 80 Hz avec le gain attendu
        var f80 = result.Filters.OrderBy(f => Math.Abs(f.Frequency - 80)).First();
        Assert.True(Math.Abs(f80.Frequency - 80) < 15, $"fréquence loin de 80 Hz : {f80.Frequency:0.#}");
        Assert.InRange(f80.GainDb, 4.5, 6.0);

        // un filtre proche de 3,15 kHz avec la coupe attendue
        var f3150 = result.Filters.OrderBy(f => Math.Abs(f.Frequency - 3150)).First();
        Assert.True(Math.Abs(f3150.Frequency - 3150) < 500, $"fréquence loin de 3,15 kHz : {f3150.Frequency:0.#}");
        Assert.InRange(f3150.GainDb, -5.0, -3.5);

        // le résidu final est très faible : la courbe est « optimale »
        Assert.True(result.ResidualRmsDb < 1.0, $"résidu RMS : {result.ResidualRmsDb:0.00} dB");

        // les Q restent dans les bornes
        Assert.All(result.Filters, f => Assert.InRange(f.Q, 0.7, 8.0));
    }

    [Fact]
    public void Plan_ClampsGainsToSafetyLimits()
    {
        var truth = new List<EqBand> { new(FilterType.Peaking, 1000, 20.0, 1.0) };
        var freqs = LogGrid(20, 20000, 1.0 / 12.0);
        var deviation = ResponseOf(truth, freqs);

        var result = AutoBandPlanner.Plan(freqs, deviation, new AutoBandOptions
        {
            FreqMin = 40,
            FreqMax = 16000,
            MaxBoostDb = 6,
            MaxCutDb = 12,
            MaxFilters = 6,
            SampleRate = 48000
        });

        Assert.True(result.Filters.Count > 0);
        Assert.All(result.Filters, f => Assert.True(f.GainDb <= 6.0 + 1e-9, $"boost non borné : {f.GainDb:0.0} dB"));
        Assert.All(result.Filters, f => Assert.True(f.GainDb >= -12.0 - 1e-9, $"coupe non bornée : {f.GainDb:0.0} dB"));

        // la limite de filtres borne bien la recherche
        Assert.True(result.Filters.Count <= 6);
    }

    [Fact]
    public void Plan_SmallDeviation_ProducesNoFilter()
    {
        var freqs = LogGrid(20, 20000, 1.0 / 12.0);
        var deviation = freqs.Select(_ => 0.3).ToArray(); // sous le seuil de 0,75 dB

        var result = AutoBandPlanner.Plan(freqs, deviation, new AutoBandOptions
        {
            FreqMin = 40,
            FreqMax = 16000,
            ThresholdDb = 0.75
        });

        Assert.Empty(result.Filters);
    }

    [Fact]
    public void Plan_ConfidenceWeight_ReducesCorrections()
    {
        var truth = new List<EqBand> { new(FilterType.Peaking, 1000, 6.0, 1.0) };
        var freqs = LogGrid(20, 20000, 1.0 / 12.0);
        var deviation = ResponseOf(truth, freqs);

        var without = AutoBandPlanner.Plan(freqs, deviation, new AutoBandOptions
        {
            FreqMin = 40, FreqMax = 16000, SampleRate = 48000
        });

        var withConfidence = AutoBandPlanner.Plan(freqs, deviation, new AutoBandOptions
        {
            FreqMin = 40, FreqMax = 16000, SampleRate = 48000
        }, confidenceWeight: _ => 0.5);

        var g0 = without.Filters.OrderBy(f => Math.Abs(f.Frequency - 1000)).First();
        var g1 = withConfidence.Filters.OrderBy(f => Math.Abs(f.Frequency - 1000)).First();

        Assert.InRange(g1.GainDb, 2.0, 4.0);
        Assert.True(g1.GainDb < g0.GainDb, "le poids de confiance ne réduit pas la correction");
    }

    [Fact]
    public void Plan_RejectsInvalidInputs()
    {
        var freqs = LogGrid(20, 20000, 1.0 / 6.0);
        Assert.Throws<ArgumentException>(() =>
            AutoBandPlanner.Plan(new double[10], new double[11], new AutoBandOptions()));
        Assert.Throws<ArgumentException>(() =>
            AutoBandPlanner.Plan(freqs, new double[freqs.Length],
                new AutoBandOptions { FreqMin = 1000, FreqMax = 100 }));
    }

    /// <summary>Vérifie l'anti-doublon sur une liste de filtres planifiés (règle
    /// d'exclusion recalculée depuis l'API publique, options par défaut).</summary>
    private static void AssertNoDuplicates(IReadOnlyList<PlannedFilter> filters)
    {
        for (int i = 0; i < filters.Count; i++)
        {
            for (int j = i + 1; j < filters.Count; j++)
            {
                Assert.True(filters[i].Frequency != filters[j].Frequency,
                    $"DOUBLON de fréquence : {filters[i].Frequency:0.#} Hz présent {Math.Min(2, filters.Count)} fois");

                double distance = Math.Log2(filters[j].Frequency / filters[i].Frequency);
                double halfI = ExclusionHalf(filters[i].Q);
                double halfJ = ExclusionHalf(filters[j].Q);
                Assert.True(distance >= halfI + halfJ - 1e-9,
                    $"bandes trop proches : {filters[i].Frequency:0.#} Hz et {filters[j].Frequency:0.#} Hz " +
                    $"(écart {distance:0.000} oct < {halfI + halfJ:0.000} oct)");
            }
        }

        // tri croissant attendu pour l'affichage dans l'égaliseur
        for (int i = 1; i < filters.Count; i++)
            Assert.True(filters[i].Frequency > filters[i - 1].Frequency, "filtres non triés");
    }

    private static double ExclusionHalf(double q)
        => Math.Max(1.0 / 12.0, 0.6 * AutoBandPlanner.BandwidthOctaves(q)) / 2.0;

    [Fact]
    public void Plan_ClampedBoost_ProducesNoDuplicateBands()
    {
        // anomalie de +12 dB alors que le boost est limité à 6 dB : l'ancien
        // algorithme empilait plusieurs filtres À LA MÊME FRÉQUENCE (doublons).
        var truth = new List<EqBand> { new(FilterType.Peaking, 250, 12.0, 1.0) };
        var freqs = LogGrid(20, 20000, 1.0 / 12.0);
        var deviation = ResponseOf(truth, freqs);

        var result = AutoBandPlanner.Plan(freqs, deviation, new AutoBandOptions
        {
            FreqMin = 40,
            FreqMax = 16000,
            MaxBoostDb = 6,
            MaxCutDb = 12,
            MaxFilters = 10,
            SampleRate = 48000
        });

        Assert.True(result.Filters.Count >= 2, $"seulement {result.Filters.Count} filtres");
        Assert.Equal(0, result.DuplicatesRemoved);
        AssertNoDuplicates(result.Filters);
        Assert.All(result.Filters, f => Assert.True(f.GainDb <= 6.0 + 1e-9));

        // la répartition autour de l'anomalie réduit bien le résidu
        Assert.True(result.ResidualMaxDb < 12.0, $"résidu max inchangé : {result.ResidualMaxDb:0.0} dB");
    }

    [Fact]
    public void Plan_TwoCloseNarrowPeaks_NeverOverlapsBands()
    {
        // deux anomalies étroites et proches : les zones d'exclusion interdisent
        // le recouvrement des bandes, quitte à ne corriger que la plus marquée
        var truth = new List<EqBand>
        {
            new(FilterType.Peaking, 2000, 6.0, 8.0),
            new(FilterType.Peaking, 2130, -6.0, 8.0)
        };
        var freqs = LogGrid(20, 20000, 1.0 / 24.0);
        var deviation = ResponseOf(truth, freqs);

        var result = AutoBandPlanner.Plan(freqs, deviation, new AutoBandOptions
        {
            FreqMin = 40,
            FreqMax = 16000,
            MaxFilters = 12,
            SampleRate = 48000
        });

        Assert.True(result.Filters.Count >= 1);
        AssertNoDuplicates(result.Filters);
        Assert.Equal(0, result.DuplicatesRemoved);
    }

    [Fact]
    public void BandwidthOctaves_MatchesTheory()
    {
        // Q élevé → bande étroite ; Q bas → bande large
        Assert.True(AutoBandPlanner.BandwidthOctaves(8.0) < AutoBandPlanner.BandwidthOctaves(1.0));
        Assert.InRange(AutoBandPlanner.BandwidthOctaves(8.0), 0.15, 0.22);   // log2(17/15) ≈ 0,181
        Assert.InRange(AutoBandPlanner.BandwidthOctaves(1.0), 1.5, 1.7);     // log2(3/1) ≈ 1,585
        Assert.InRange(AutoBandPlanner.BandwidthOctaves(0.7), 2.4, 2.8);     // log2(2,4/0,4) ≈ 2,585
    }

    [Fact]
    public void DesignFromAlignedCurve_BuildsCompletePreset()
    {
        // courbe alignée = - RoomResponse (la correction doit l'annuler)
        var freqs = LogGrid(25, 20000, 1.0 / 6.0);
        var lowPeak = new BiquadFilter(FilterType.Peaking, 48000, 80, 5.0, 1.2);
        var aligned = freqs.Select(f => -(lowPeak.ResponseDb(f, 48000))).ToArray();

        var opt = new CorrectionOptions { FreqMin = 40, FreqMax = 16000 };
        var result = CorrectionDesigner.DesignFromAlignedCurve(freqs, aligned, opt, 48000, "Preset test", null);

        Assert.True(result.Preset.Bands.Count >= 1);
        Assert.Equal("Preset test", result.Preset.Name);
        Assert.True(result.Preset.PreampDb <= 0);
        Assert.Equal(freqs.Length, result.PredictedCurve.Count);
        Assert.Equal(result.Preset.Bands.Count, result.BandCenters.Length);
        Assert.Equal(result.Preset.Bands.Count, result.CorrectionDb.Length);

        // la prédiction ramène la courbe vers 0
        double maxRes = result.PredictedCurve
            .Where(p => p.Freq >= 100 && p.Freq <= 8000)
            .Max(p => Math.Abs(p.Db));
        Assert.True(maxRes < 1.5, $"résidu max {maxRes:0.00} dB");
    }

    [Fact]
    public void DesignFromAlignedCurve_FlatCurve_EmptyPreset()
    {
        var freqs = LogGrid(25, 20000, 1.0 / 6.0);
        var aligned = new double[freqs.Length];

        var result = CorrectionDesigner.DesignFromAlignedCurve(
            freqs, aligned, new CorrectionOptions(), 48000, "Plat", null);

        Assert.Empty(result.Preset.Bands);
        Assert.Equal(0, result.Preset.PreampDb);
    }
}
