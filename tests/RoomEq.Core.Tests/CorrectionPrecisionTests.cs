using RoomEq.Core.Calibration;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using Xunit;

namespace RoomEq.Core.Tests;

/// <summary>
/// Tests de l'import de la courbe de calibration dans l'égaliseur : le niveau de
/// précision choisi détermine le nombre de bandes, leur espacement et leurs largeurs
/// (Q), tandis que les fréquences et les gains restent optimisés automatiquement.
/// </summary>
public class CorrectionPrecisionTests
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

    /// <summary>Pièce synthétique : 6 anomalies larges + une résonance étroite (Q 9).</summary>
    private static (double[] Freqs, double[] AlignedDb) SyntheticRoomCurve()
    {
        var freqs = LogGrid(25, 20000, 1.0 / 6.0);
        var room = new List<EqBand>
        {
            new(FilterType.Peaking, 80, 6.0, 1.2),
            new(FilterType.Peaking, 250, -5.0, 1.5),
            new(FilterType.Peaking, 900, 4.0, 3.0),
            new(FilterType.Peaking, 3150, -3.0, 2.0),
            new(FilterType.Peaking, 6300, 5.0, 5.0),
            new(FilterType.Peaking, 11000, -4.0, 2.5),
            new(FilterType.Peaking, 2200, 9.0, 9.0)
        };
        // courbe alignée = cible plate (0 dB) + réponse de la pièce
        return (freqs, ResponseOf(room, freqs));
    }

    private static CorrectionResult SourceFrom(double[] freqs, double[] alignedDb)
    {
        return new CorrectionResult
        {
            Options = new CorrectionOptions { FreqMin = 40, FreqMax = 16000, MaxBoostDb = 6, MaxCutDb = 12 },
            SampleRate = 48000,
            MeasuredCurve = freqs.Zip(alignedDb, (f, db) => new CurvePoint(f, db)).ToList()
        };
    }

    /// <summary>Vérifie l'anti-doublon avec les options de précision RÉELLEMENT
    /// utilisées (l'espacement minimal dépend du niveau choisi).</summary>
    private static void AssertNoDuplicates(IReadOnlyList<PlannedFilter> filters, AutoBandOptions opt)
    {
        for (int i = 0; i < filters.Count; i++)
        {
            for (int j = i + 1; j < filters.Count; j++)
            {
                Assert.True(filters[i].Frequency != filters[j].Frequency,
                    $"DOUBLON de fréquence : {filters[i].Frequency:0.#} Hz");

                double distance = Math.Log2(filters[j].Frequency / filters[i].Frequency);
                double halfI = ExclusionHalf(filters[i].Q, opt);
                double halfJ = ExclusionHalf(filters[j].Q, opt);
                Assert.True(distance >= halfI + halfJ - 1e-9,
                    $"bandes trop proches : {filters[i].Frequency:0.#} Hz / {filters[j].Frequency:0.#} Hz " +
                    $"({distance:0.000} oct < {halfI + halfJ:0.000} oct)");
            }
        }

        for (int i = 1; i < filters.Count; i++)
            Assert.True(filters[i].Frequency > filters[i - 1].Frequency, "filtres non triés");
    }

    private static double ExclusionHalf(double q, AutoBandOptions opt)
        => Math.Max(opt.MinSpacingOctaves, opt.SpacingFactor * AutoBandPlanner.BandwidthOctaves(q)) / 2.0;

    // ----- catalogue -----

    [Fact]
    public void Catalog_CoversAllPrecisions_WithUniqueTexts()
    {
        var kinds = CorrectionPrecisionCatalog.All.Select(o => o.Kind).ToHashSet();
        Assert.Equal(Enum.GetValues<CorrectionPrecision>().Length, CorrectionPrecisionCatalog.All.Count);
        Assert.True(kinds.SetEquals(Enum.GetValues<CorrectionPrecision>()),
            "le catalogue doit couvrir tous les niveaux de précision");

        Assert.All(CorrectionPrecisionCatalog.All, o =>
        {
            Assert.False(string.IsNullOrWhiteSpace(o.Name));
            Assert.False(string.IsNullOrWhiteSpace(o.Description));
        });
        Assert.Equal(CorrectionPrecisionCatalog.All.Count,
            CorrectionPrecisionCatalog.All.Select(o => o.Name).Distinct().Count());

        // accès direct par niveau
        foreach (var p in Enum.GetValues<CorrectionPrecision>())
            Assert.Equal(p, CorrectionPrecisionCatalog.Option(p).Kind);
    }

    [Fact]
    public void Settings_ScaleMonotonicallyFromSoftToUltra()
    {
        int previousMax = 0;
        double previousThreshold = double.MaxValue;
        double previousMaxQ = 0;
        double previousSpacing = double.MaxValue;

        foreach (var p in new[]
                 {
                     CorrectionPrecision.Soft, CorrectionPrecision.Standard,
                     CorrectionPrecision.Fine, CorrectionPrecision.Ultra
                 })
        {
            var o = CorrectionPrecisionSettings.ToOptions(p, new CorrectionOptions(), 48000);

            Assert.True(o.MaxFilters > previousMax, $"{p} : MaxFilters non croissant");
            Assert.True(o.ThresholdDb < previousThreshold, $"{p} : ThresholdDb non décroissant");
            Assert.True(o.MaxQ > previousMaxQ, $"{p} : MaxQ non croissant");
            Assert.True(o.MinSpacingOctaves <= previousSpacing, $"{p} : espacement non décroissant");
            Assert.True(o.MinQ > 0 && o.MinQ < o.MaxQ);

            previousMax = o.MaxFilters;
            previousThreshold = o.ThresholdDb;
            previousMaxQ = o.MaxQ;
            previousSpacing = o.MinSpacingOctaves;
        }
    }

    [Fact]
    public void Settings_KeepCorrectionOptionsBounds()
    {
        var opt = new CorrectionOptions { FreqMin = 35, FreqMax = 15000, MaxBoostDb = 5, MaxCutDb = 10 };

        foreach (var p in Enum.GetValues<CorrectionPrecision>())
        {
            var o = CorrectionPrecisionSettings.ToOptions(p, opt, 48000);
            Assert.Equal(35, o.FreqMin);
            Assert.Equal(15000, o.FreqMax);
            Assert.Equal(5, o.MaxBoostDb);
            Assert.Equal(10, o.MaxCutDb);
            Assert.Equal(48000, o.SampleRate);
        }
    }

    // ----- import de la courbe de calibration -----

    [Fact]
    public void Import_ProducesMoreBands_AndBetterFit_WithHigherPrecision()
    {
        var (freqs, aligned) = SyntheticRoomCurve();
        var source = SourceFrom(freqs, aligned);

        var soft = CorrectionDesigner.DesignFromCalibrationCurve(source, CorrectionPrecision.Soft, "Douce");
        var ultra = CorrectionDesigner.DesignFromCalibrationCurve(source, CorrectionPrecision.Ultra, "Très fine");

        Assert.True(soft.Preset.Bands.Count >= 1);
        Assert.True(soft.Preset.Bands.Count <= 8, $"douce : {soft.Preset.Bands.Count} bandes (max 8 attendu)");
        Assert.True(ultra.Preset.Bands.Count <= 30, $"très fine : {ultra.Preset.Bands.Count} bandes (max 30 attendu)");
        Assert.True(ultra.Preset.Bands.Count > soft.Preset.Bands.Count,
            $"très fine ({ultra.Preset.Bands.Count}) devrait placer plus de bandes que douce ({soft.Preset.Bands.Count})");

        Assert.True(ultra.ResidualRmsDb() < soft.ResidualRmsDb(),
            $"résidu très fine {ultra.ResidualRmsDb():0.00} dB devrait être inférieur à douce {soft.ResidualRmsDb():0.00} dB");

        // aucune bande dupliquée, quel que soit le niveau
        AssertNoDuplicates(soft.Preset.Bands.Select(b => new PlannedFilter
        {
            Frequency = b.Frequency, GainDb = b.GainDb, Q = b.Q
        }).ToList(), CorrectionPrecisionSettings.ToOptions(CorrectionPrecision.Soft, source.Options, 48000));

        AssertNoDuplicates(ultra.Preset.Bands.Select(b => new PlannedFilter
        {
            Frequency = b.Frequency, GainDb = b.GainDb, Q = b.Q
        }).ToList(), CorrectionPrecisionSettings.ToOptions(CorrectionPrecision.Ultra, source.Options, 48000));
    }

    [Fact]
    public void Import_UltraCorrectsNarrowResonance_BetterThanSoft()
    {
        var (freqs, aligned) = SyntheticRoomCurve();
        var source = SourceFrom(freqs, aligned);

        var soft = CorrectionDesigner.DesignFromCalibrationCurve(source, CorrectionPrecision.Soft, "Douce");
        var ultra = CorrectionDesigner.DesignFromCalibrationCurve(source, CorrectionPrecision.Ultra, "Très fine");

        // résidu restant = |courbe prédite - cible| ; cible plate = 0 dB
        static double ResidualNear(CorrectionResult r, double[] freqs, double f0, double span)
        {
            double max = 0;
            for (int i = 0; i < freqs.Length; i++)
            {
                if (freqs[i] < f0 / span || freqs[i] > f0 * span) continue;
                max = Math.Max(max, Math.Abs(r.PredictedCurve[i].Db));
            }
            return max;
        }

        double softRes = ResidualNear(soft, freqs, 2200, 1.35);
        double ultraRes = ResidualNear(ultra, freqs, 2200, 1.35);

        Assert.True(ultraRes < softRes,
            $"résonance étroite : résidu très fine {ultraRes:0.00} dB devrait être < douce {softRes:0.00} dB");
    }

    [Fact]
    public void Import_PreservesPresetName_AndSecurityPreamp()
    {
        var (freqs, aligned) = SyntheticRoomCurve();
        var source = SourceFrom(freqs, aligned);

        var result = CorrectionDesigner.DesignFromCalibrationCurve(
            source, CorrectionPrecision.Standard, "Import calibration 14:30");

        Assert.Equal("Import calibration 14:30", result.Preset.Name);
        Assert.Contains("précision Standard", result.Preset.Description);
        Assert.True(result.Preset.PreampDb <= 0, "le préampli de sécurité doit rester négatif ou nul");
        Assert.Equal(48000, result.SampleRate);
        Assert.Equal(source.Options.FreqMin, result.Options.FreqMin);
        Assert.Equal(source.Options.FreqMax, result.Options.FreqMax);
        Assert.Equal(freqs.Length, result.MeasuredCurve.Count);
    }

    [Fact]
    public void Import_EmptyOrShortCurve_Throws()
    {
        var source = SourceFrom(new[] { 100.0, 200.0, 400.0 }, new[] { 0.0, 1.0, 2.0 });
        Assert.ThrowsAny<ArgumentException>(() =>
            CorrectionDesigner.DesignFromCalibrationCurve(source, CorrectionPrecision.Standard, "X"));
    }
}
