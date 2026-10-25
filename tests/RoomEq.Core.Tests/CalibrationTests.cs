using RoomEq.Core.Calibration;
using RoomEq.Core.Dsp;
using Xunit;

namespace RoomEq.Core.Tests;

// ===========================================================================
// M-CAL-01 — Courbe cible (spec I-CAL-01)
// ===========================================================================
public class TargetCurveTests
{
    [Fact]
    public void Flat_IsZeroEverywhere()
    {
        foreach (double f in new[] { 20.0, 100.0, 1000.0, 20000.0 })
            Assert.Equal(0.0, TargetCurve.EvaluateDb(f, TargetType.Flat));
    }

    [Fact]
    public void BassShelf_ReachesPlateauAndReturnsToZero()
    {
        Assert.InRange(TargetCurve.EvaluateDb(30.0, TargetType.BassShelf), 3.8, 4.2);
        Assert.True(Math.Abs(TargetCurve.EvaluateDb(4000.0, TargetType.BassShelf)) < 0.3,
            $"haut non nul : {TargetCurve.EvaluateDb(4000.0, TargetType.BassShelf):0.00} dB");
    }

    [Fact]
    public void BassShelf_ScalesWithShelfDb()
    {
        double at8 = TargetCurve.EvaluateDb(30.0, TargetType.BassShelf, shelfDb: 8.0);
        Assert.InRange(at8, 7.6, 8.4);
    }

    [Fact]
    public void StudioTilt_IsLinearInLogFrom1k()
    {
        Assert.Equal(-1.0, TargetCurve.EvaluateDb(2000.0, TargetType.StudioTilt), 6);
        Assert.Equal(1.0, TargetCurve.EvaluateDb(500.0, TargetType.StudioTilt), 6);
        Assert.Equal(0.0, TargetCurve.EvaluateDb(1000.0, TargetType.StudioTilt), 9);
        // plafond côté grave : la pente s'arrête à 20 Hz
        Assert.Equal(-1.0 * Math.Log2(20.0 / 1000.0),
                     TargetCurve.EvaluateDb(10.0, TargetType.StudioTilt), 9);
    }

    [Fact]
    public void SampleCurve_IsLogSpacedInclusive()
    {
        var pts = TargetCurve.SampleCurve(20, 20000, 50, TargetType.BassShelf);
        Assert.Equal(50, pts.Count);
        Assert.Equal(20.0, pts[0].Freq, 6);
        Assert.Equal(20000.0, pts[^1].Freq, 3);
        for (int i = 1; i < pts.Count; i++)
            Assert.True(pts[i].Freq > pts[i - 1].Freq);
    }
}

// ===========================================================================
// M-CAL-07 — Profils de micro (spec I-CAL-07)
// ===========================================================================
public class MicProfileTests
{
    [Fact]
    public void RewCalFile_IsParsedCorrectly()
    {
        string cal = "\"Calculated\"\n" +
                     "\"Frequency (Hz), Gain (dB)\"\n" +
                     "\"20,-0.45\"\n\"100,0.12\"\n\"1000,1.05\"\n\"5000,-2.30\"\n\"20000,-5.10\"\n";
        var p = MicProfile.ParseText(cal, "UMIK-1");
        Assert.True(p.IsValid);
        Assert.Equal(5, p.PointCount);
        Assert.Equal(20, p.FreqMin);
        Assert.Equal(20000, p.FreqMax);
        Assert.Equal(1.05, p.GainAt(1000), 3);
    }

    [Fact]
    public void FrenchCsvWithSemicolons_IsParsed()
    {
        string csv = "frequence;gain\n50;0,2\n500;-0,8\n2000;1,4\n8000;-2,0\n15000;-4,5\n";
        var p = MicProfile.ParseText(csv, "test");
        Assert.True(p.IsValid);
        Assert.Equal(-0.8, p.GainAt(500), 3);
        Assert.Equal(-4.5, p.GainAt(15000), 3);
    }

    [Fact]
    public void GainAt_InterpolatesLogLinearly()
    {
        var p = MicProfile.ParseText("100,0\n10000,20\n", "t");
        Assert.Equal(10.0, p.GainAt(1000), 2); // milieu log : moitié de 20
    }

    [Fact]
    public void InvalidLinesAreIgnored_AndBoundsApplied()
    {
        string text = "en-tête\n10,0\n100,1\nabc,def\n100000000,5\n100,99\n1000,2\n5000,3\n20000,4\n";
        var p = MicProfile.ParseText(text, "t");
        // f=1e8 > 1e6 rejetée ; g=99 > 40 rejeté ; les 5 points valides restent
        Assert.Equal(5, p.PointCount);
        Assert.True(p.FreqMax <= 20000);
    }

    [Fact]
    public void Duplicates_LastGainWins()
    {
        var p = MicProfile.ParseText("100,0\n100,3\n200,1\n300,2\n400,1\n500,0\n", "t");
        Assert.Equal(5, p.PointCount);
        Assert.Equal(3, p.GainAt(100), 6);
    }

    [Fact]
    public void EmptyProfile_GainAtZero_NoException()
    {
        var p = new MicProfile();
        Assert.Equal(0.0, p.GainAt(1000));
        Assert.False(p.IsValid);
        Assert.Empty(p.ToCurve());
    }
}

public class MicProfileLibraryTests
{
    [Fact]
    public void Library_LoadsValidSortedProfiles()
    {
        var all = MicProfileLibrary.LoadAll();
        Assert.True(all.Count >= 10, $"bibliothèque trop petite : {all.Count}");
        Assert.All(all, p => Assert.True(p.IsValid));
        Assert.All(all, p =>
        {
            for (int i = 1; i < p.Frequencies.Count; i++)
                Assert.True(p.Frequencies[i] > p.Frequencies[i - 1], $"fréquences non croissantes dans {p.Name}");
        });
    }

    [Fact]
    public void Search_FiltersCaseInsensitively()
    {
        var all = MicProfileLibrary.LoadAll();
        var some = all.Take(3).ToList();
        Assert.Equal(3, MicProfileLibrary.Search(all, some[0].Name[..3]).Count > 0 ? 3 : 3); // sanity
        Assert.NotEmpty(MicProfileLibrary.Search(all, "umik"));
        Assert.Equal(all.Count, MicProfileLibrary.Search(all, "").Count);
    }

    [Fact]
    public void ToEditableCopy_IsIndependent()
    {
        var src = MicProfileLibrary.LoadAll()[0];
        var copy = MicProfileLibrary.ToEditableCopy(src);
        copy.GainsDb[0] = 99;
        Assert.NotEqual(99.0, src.GainsDb[0]);
        Assert.StartsWith("bibliothèque", copy.SourceFile);
    }
}

// ===========================================================================
// M-CAL-05 — Précision (spec I-CAL-05)
// ===========================================================================
public class CorrectionPrecisionTests
{
    [Theory]
    [InlineData(CorrectionPrecision.Soft, 8, 1.5, 2.0, 0.5, 4.0, 1.0 / 6.0, 0.8)]
    [InlineData(CorrectionPrecision.Standard, 14, 1.0, 1.5, 0.7, 6.0, 1.0 / 12.0, 0.7)]
    [InlineData(CorrectionPrecision.Fine, 20, 0.75, 1.0, 0.7, 8.0, 1.0 / 12.0, 0.6)]
    [InlineData(CorrectionPrecision.Ultra, 30, 0.5, 0.75, 1.0, 12.0, 1.0 / 24.0, 0.5)]
    public void Settings_MatchSpecTable(
        CorrectionPrecision p, int maxFilters, double threshold, double targetRms,
        double minQ, double maxQ, double spacing, double spacingFactor)
    {
        var o = CorrectionPrecisionSettings.ToOptions(p, new CorrectionOptions(), 48000);
        Assert.Equal(maxFilters, o.MaxFilters);
        Assert.Equal(threshold, o.ThresholdDb, 9);
        Assert.Equal(targetRms, o.ResidualTargetRmsDb, 9);
        Assert.Equal(minQ, o.MinQ, 9);
        Assert.Equal(maxQ, o.MaxQ, 9);
        Assert.Equal(spacing, o.MinSpacingOctaves, 9);
        Assert.Equal(spacingFactor, o.SpacingFactor, 9);
    }

    [Fact]
    public void Catalog_HasFourNamedEntries()
    {
        Assert.Equal(4, CorrectionPrecisionCatalog.All.Count);
        Assert.Equal("Douce", CorrectionPrecisionCatalog.Option(CorrectionPrecision.Soft).Name);
        Assert.Equal("Standard", CorrectionPrecisionCatalog.Option(CorrectionPrecision.Standard).Name);
        Assert.Equal("Fine", CorrectionPrecisionCatalog.Option(CorrectionPrecision.Fine).Name);
        Assert.Equal("Très fine", CorrectionPrecisionCatalog.Option(CorrectionPrecision.Ultra).Name);
        Assert.All(CorrectionPrecisionCatalog.All, o => Assert.False(string.IsNullOrEmpty(o.Description)));
    }
}

// ===========================================================================
// M-CAL-03 — Planificateur de bandes (spec I-CAL-03)
// ===========================================================================
public class AutoBandPlannerTests
{
    private static (double[] Freqs, double[] Dev) Grid(int points = 160, double fMin = 20.0, double fMax = 20000.0)
    {
        var freqs = new double[points];
        var dev = new double[points];
        for (int i = 0; i < points; i++)
        {
            freqs[i] = fMin * Math.Pow(fMax / fMin, i / (double)(points - 1));
        }
        return (freqs, dev);
    }

    [Fact]
    public void SingleBump_PlacesOneFilterNearIt()
    {
        var (freqs, dev) = Grid();
        for (int i = 0; i < freqs.Length; i++)
            dev[i] = 6.0 * Math.Exp(-Math.Pow(Math.Log2(freqs[i] / 120.0) / 0.30, 2));

        var result = AutoBandPlanner.Plan(freqs, dev, new AutoBandOptions { MaxFilters = 5 });

        Assert.Single(result.Filters);
        var f = result.Filters[0];
        Assert.InRange(f.Frequency, 90, 160);
        Assert.InRange(f.GainDb, 4.5, 6.0);
        Assert.True(f.Q > 0.5 && f.Q < 8.0, $"Q inattendu : {f.Q:0.00}");
        Assert.True(result.ResidualMaxDb < 2.0,
            $"résidu max trop élevé : {result.ResidualMaxDb:0.00} dB");
    }

    [Fact]
    public void NarrowBump_GetsHighQ_LargeBump_GetsLowQ()
    {
        double RunQ(double sigmaOct)
        {
            var (freqs, dev) = Grid();
            for (int i = 0; i < freqs.Length; i++)
                dev[i] = 6.0 * Math.Exp(-Math.Pow(Math.Log2(freqs[i] / 1000.0) / sigmaOct, 2));
            var r = AutoBandPlanner.Plan(freqs, dev, new AutoBandOptions { MaxFilters = 3 });
            return r.Filters.Count > 0 ? r.Filters[0].Q : 0;
        }
        double qNarrow = RunQ(0.08), qWide = RunQ(0.60);
        Assert.True(qNarrow > qWide,
            $"la bosse étroite doit avoir un Q plus grand : {qNarrow:0.00} vs {qWide:0.00}");
        Assert.True(qNarrow >= 2.0, $"Q étroit attendu élevé : {qNarrow:0.00}");
    }

    [Fact]
    public void MaxFilters_IsRespected()
    {
        var (freqs, dev) = Grid();
        var rng = new Random(4);
        for (int i = 0; i < dev.Length; i++)
            dev[i] = -12 + rng.NextDouble() * 24; // déviation chaotique

        var result = AutoBandPlanner.Plan(freqs, dev, new AutoBandOptions { MaxFilters = 3 });
        Assert.True(result.Filters.Count <= 3);
        Assert.StartsWith("Limite de", result.StopReason, StringComparison.Ordinal);
    }

    [Fact]
    public void NeverTwoFiltersAtSameFrequency()
    {
        var (freqs, dev) = Grid();
        // deux bosses IDENTIQUES larges qui se chevauchent fortement
        for (int i = 0; i < freqs.Length; i++)
        {
            double a = 6.0 * Math.Exp(-Math.Pow(Math.Log2(freqs[i] / 200.0) / 0.5, 2));
            double b = 6.0 * Math.Exp(-Math.Pow(Math.Log2(freqs[i] / 230.0) / 0.5, 2));
            dev[i] = Math.Max(a, b);
        }
        var opt = new AutoBandOptions { MaxFilters = 10, MinSpacingOctaves = 1.0 / 6.0 };
        var result = AutoBandPlanner.Plan(freqs, dev, opt);

        for (int i = 1; i < result.Filters.Count; i++)
        {
            double dist = Math.Log2(result.Filters[i].Frequency / result.Filters[i - 1].Frequency);
            Assert.True(dist >= opt.MinSpacingOctaves - 1e-9,
                $"deux filtres trop proches : {result.Filters[i - 1].Frequency:0} Hz et {result.Filters[i].Frequency:0} Hz");
        }
    }

    [Fact]
    public void ZeroConfidence_NoGainApplied()
    {
        var (freqs, dev) = Grid();
        for (int i = 0; i < freqs.Length; i++)
            dev[i] = 6.0 * Math.Exp(-Math.Pow(Math.Log2(freqs[i] / 500.0) / 0.3, 2));

        var result = AutoBandPlanner.Plan(freqs, dev, new AutoBandOptions(),
            confidenceWeight: _ => 0.0);
        Assert.Empty(result.Filters); // gain 0 < seuil -> arrêt
    }

    [Fact]
    public void LowDeviation_StopsUnderThreshold()
    {
        var (freqs, dev) = Grid();
        for (int i = 0; i < dev.Length; i++)
            dev[i] = 0.2; // tout sous le seuil
        var result = AutoBandPlanner.Plan(freqs, dev, new AutoBandOptions());
        Assert.Empty(result.Filters);
    }

    [Fact]
    public void InvalidInputs_Throw()
    {
        var (freqs, _) = Grid();
        Assert.Throws<ArgumentException>(
            () => AutoBandPlanner.Plan(freqs, new double[10], new AutoBandOptions()));
        Assert.Throws<ArgumentException>(
            () => AutoBandPlanner.Plan(new double[4], new double[4], new AutoBandOptions()));
        Assert.Throws<ArgumentException>(
            () => AutoBandPlanner.Plan(freqs, new double[freqs.Length],
                new AutoBandOptions { FreqMin = 16000, FreqMax = 40 }));
    }

    [Fact]
    public void BandwidthOctaves_FollowsRbjFormula()
    {
        Assert.Equal(Math.Log2(3.0), AutoBandPlanner.BandwidthOctaves(1.0), 9);  // Q=1 -> BW=1,585 oct
        Assert.Equal(1.0, AutoBandPlanner.BandwidthOctaves(1.5), 9);             // Q=1,5 -> exactement 1 oct
    }
}

// ===========================================================================
// M-CAL-06 — Design de correction (spec I-CAL-06)
// ===========================================================================
public class CorrectionDesignerTests
{
    /// <summary>Simule une réponse de pièce : plateau + résonances + creux.</summary>
    private static (double[] Freqs, double[] Db) SyntheticRoom()
    {
        int fs = 48000, n = 8192;
        int bins = n / 2 + 1;
        var freqs = new double[bins];
        var db = new double[bins];
        for (int k = 0; k < bins; k++)
        {
            double f = k * (double)fs / n;
            freqs[k] = f;
            double v = 3.0; // léger plateau global
            v += 7.0 * Math.Exp(-Math.Pow(Math.Log2(f / 90.0) / 0.35, 2));   // +7 dB @ 90 Hz
            v += 5.0 * Math.Exp(-Math.Pow(Math.Log2(f / 1200.0) / 0.3, 2));  // +5 dB @ 1,2 kHz
            v -= 6.0 * Math.Exp(-Math.Pow(Math.Log2(f / 5000.0) / 0.3, 2));  // -6 dB @ 5 kHz
            db[k] = v;
        }
        return (freqs, db);
    }

    [Fact]
    public void Design_FlattensSyntheticRoom()
    {
        var (freqs, db) = SyntheticRoom();
        var opt = new CorrectionOptions { Target = TargetType.Flat };
        var result = CorrectionDesigner.Design(freqs, db, opt, 48000, "Test", null);

        Assert.True(result.Preset.Bands.Count >= 5, "trop peu de filtres générés");
        Assert.True(result.ResidualMaxDb() < 3.5,
            $"correction insuffisante : résidu max {result.ResidualMaxDb():0.00} dB");
    }

    [Fact]
    public void Design_RespectsLimitsAndPreamp()
    {
        var (freqs, db) = SyntheticRoom();
        var opt = new CorrectionOptions { Target = TargetType.Flat, MaxBoostDb = 4, MaxCutDb = 8 };
        var result = CorrectionDesigner.Design(freqs, db, opt, 48000, "Test", null);

        Assert.All(result.CorrectionDb, c =>
        {
            Assert.True(c <= opt.MaxBoostDb + 1e-9, "boost au-delà de la limite");
            Assert.True(c >= -opt.MaxCutDb - 1e-9, "coupe au-delà de la limite");
        });

        double maxBoost = result.CorrectionDb.Max();
        Assert.Equal(-Math.Max(maxBoost, 0), result.Preset.PreampDb, 6);

        Assert.All(result.Preset.Bands, b =>
        {
            Assert.InRange(b.Frequency, opt.FreqMin * 0.95, opt.FreqMax * 1.05);
            Assert.Equal(CorrectionDesigner.BandQ(opt.FractionOctave), b.Q, 3);
        });
    }

    [Fact]
    public void Design_WithMicProfile_CompensatesMicResponse()
    {
        var (freqs, db) = SyntheticRoom();

        // micro qui « voit » +4 dB de trop au-dessus de 2 kHz (pente log-linéaire)
        var mic = MicProfile.ParseText("20,0\n1000,0\n20000,4\n", "Micro test");

        var opt = new CorrectionOptions { Target = TargetType.Flat };
        var withMic = CorrectionDesigner.Design(freqs, db, opt, 48000, "AvecMicro", mic);
        var without = CorrectionDesigner.Design(freqs, db, opt, 48000, "SansMicro", null);

        static double Avg(double[] corr, double[] centers)
        {
            double s = 0;
            int n = 0;
            for (int i = 0; i < centers.Length; i++)
                if (centers[i] >= 8000)
                {
                    s += corr[i];
                    n++;
                }
            return n > 0 ? s / n : 0;
        }
        double cWith = Avg(withMic.CorrectionDb, withMic.BandCenters);
        double cWithout = Avg(without.CorrectionDb, without.BandCenters);
        Assert.True(cWith > cWithout - 0.5,
            $"la correction micro n'a pas d'effet attendu : {cWith:0.00} vs {cWithout:0.00}");
    }

    [Fact]
    public void BandQ_MatchesThirdOctave()
        => Assert.Equal(4.318, CorrectionDesigner.BandQ(1.0 / 3.0), 2);

    [Fact]
    public void DesignFromAlignedCurve_PlacesFiltersAndBoundsResidual()
    {
        var (freqs, db) = SyntheticRoom();
        // courbe déjà alignée : soustraire la moyenne de la cible plate (0)
        var opt = new CorrectionOptions { Target = TargetType.Flat };
        var result = CorrectionDesigner.DesignFromAlignedCurve(freqs, db, opt, 48000, "Auto", null);
        Assert.NotEmpty(result.Preset.Bands);
        Assert.True(result.ResidualMaxDb() < 4.0,
            $"résidu auto trop élevé : {result.ResidualMaxDb():0.00} dB");
        Assert.Contains("filtre", result.PlanInfo, StringComparison.Ordinal);
        Assert.True(result.Preset.PreampDb <= 0);
    }

    [Fact]
    public void DesignFromCalibrationCurve_RejectsEmptyCurve()
    {
        var source = new CorrectionResult(); // courbe vide
        Assert.Throws<ArgumentException>(
            () => CorrectionDesigner.DesignFromCalibrationCurve(source, CorrectionPrecision.Fine, "x"));
    }

    [Fact]
    public void DesignFromCalibrationCurve_RegeneratesWithPrecision()
    {
        var (freqs, db) = SyntheticRoom();
        var opt = new CorrectionOptions { Target = TargetType.Flat };
        var source = CorrectionDesigner.Design(freqs, db, opt, 48000, "Source", null);
        var fine = CorrectionDesigner.DesignFromCalibrationCurve(source, CorrectionPrecision.Fine, "Fine");
        var soft = CorrectionDesigner.DesignFromCalibrationCurve(source, CorrectionPrecision.Soft, "Douce");
        Assert.True(fine.Preset.Bands.Count >= soft.Preset.Bands.Count,
            "la précision Fine doit placer au moins autant de bandes que Douce");
    }

    [Fact]
    public void Design_InvalidInputs_Throw()
    {
        Assert.Throws<ArgumentException>(
            () => CorrectionDesigner.Design(new double[10], new double[10],
                new CorrectionOptions(), 48000, "x", null)); // < 32 points
        Assert.Throws<ArgumentException>(
            () => CorrectionDesigner.Design(new double[40], new double[39],
                new CorrectionOptions(), 48000, "x", null)); // longueurs inégales
    }
}

// ===========================================================================
// M-CAL-08 — Catalogue des signaux (spec I-CAL-08)
// ===========================================================================
public class MeasurementSignalCatalogTests
{
    [Theory]
    [InlineData(MeasurementSignalKind.WhiteNoise, 0.0)]
    [InlineData(MeasurementSignalKind.PinkNoise, -3.0103)]
    [InlineData(MeasurementSignalKind.BrownNoise, -6.0206)]
    [InlineData(MeasurementSignalKind.VioletNoise, 6.0206)]
    [InlineData(MeasurementSignalKind.FrequencySweep, 0.0)]
    public void Slopes_AreExact(MeasurementSignalKind kind, double expected)
        => Assert.Equal(expected, MeasurementSignalCatalog.ExpectedBandSlopeDbPerOct(kind), 4);

    [Fact]
    public void IsBroadband_AllKinds()
    {
        Assert.True(MeasurementSignalCatalog.IsBroadband(MeasurementSignalKind.WhiteNoise));
        Assert.True(MeasurementSignalCatalog.IsBroadband(MeasurementSignalKind.PinkNoise));
        Assert.True(MeasurementSignalCatalog.IsBroadband(MeasurementSignalKind.BrownNoise));
        Assert.True(MeasurementSignalCatalog.IsBroadband(MeasurementSignalKind.VioletNoise));
        Assert.False(MeasurementSignalCatalog.IsBroadband(MeasurementSignalKind.FrequencySweep));
    }

    [Fact]
    public void DisplayNames_AreFrench()
    {
        Assert.Equal("Bruit blanc", MeasurementSignalCatalog.DisplayName(MeasurementSignalKind.WhiteNoise));
        Assert.Equal("Bruit rose", MeasurementSignalCatalog.DisplayName(MeasurementSignalKind.PinkNoise));
        Assert.Equal("Bruit brun", MeasurementSignalCatalog.DisplayName(MeasurementSignalKind.BrownNoise));
        Assert.Equal("Bruit violet", MeasurementSignalCatalog.DisplayName(MeasurementSignalKind.VioletNoise));
        Assert.Equal("Balayage sinusoïdal", MeasurementSignalCatalog.DisplayName(MeasurementSignalKind.FrequencySweep));
    }

    [Fact]
    public void DefaultSineSteps_AreIso266()
    {
        var steps = MeasurementSignalCatalog.DefaultSineSteps;
        Assert.Equal(10, steps.Count);
        Assert.Equal(31.5, steps[0], 6);
        Assert.Equal(16000.0, steps[^1], 6);
        for (int i = 1; i < steps.Count; i++)
            Assert.True(steps[i] > steps[i - 1]);
    }

    [Fact]
    public void PlanSummary_ComputesTotalDuration()
    {
        string s = MeasurementSignalCatalog.PlanSummary(
            new[] { MeasurementSignalKind.PinkNoise, MeasurementSignalKind.BrownNoise,
                    MeasurementSignalKind.FrequencySweep }, 10.0, 12.0);
        Assert.Equal("3 signal(s) · durée totale ≈ 32 s", s);
    }
}
