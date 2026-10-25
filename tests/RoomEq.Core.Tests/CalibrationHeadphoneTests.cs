using RoomEq.Core.Calibration;
using RoomEq.Core.Dsp;
using RoomEq.Core.Headphones;
using Xunit;

namespace RoomEq.Core.Tests;

// ===========================================================================
// M-CAL-02 — Analyse de balayage (spec I-CAL-02)
// ===========================================================================
public class SweepAnalyzerTests
{
    private const int Fs = 48000;

    /// <summary>Simule une « pièce » : applique un filtre connu au balayage émis.</summary>
    private static float[] CaptureThroughRoom(LogSweepSpec spec, BiquadFilter room, int delaySamples)
    {
        var sweep = new LogSweepGenerator(spec, Fs).GenerateFull(0.8);
        var captured = new float[sweep.Length + delaySamples];
        for (int i = 0; i < sweep.Length; i++)
            captured[i + delaySamples] = (float)room.Process(sweep[i]);
        return captured;
    }

    [Fact]
    public void KnownRoom_IsDeconvolved()
    {
        var spec = new LogSweepSpec(20, 20000, 6.0);
        var room = new BiquadFilter(FilterType.Peaking, Fs, 1000, 6.0, 1.2);
        int delay = (int)(0.021 * Fs); // 21 ms de retard « pièce »
        float[] captured = CaptureThroughRoom(spec, room, delay);

        var analysis = SweepAnalyzer.Analyze(captured, Fs, spec);

        // bande maximale autour de 1 kHz : la moyenne énergétique 1/6 d'octave
        // dilue le pic Q=1,2 (~3,9 dB mesurés pour +6 dB théoriques)
        int maxIdx = -1;
        double maxDb = double.NegativeInfinity;
        for (int i = 0; i < analysis.BandFreqs.Length; i++)
            if (analysis.BandFreqs[i] is >= 700 and <= 1400 && analysis.BandLevelsDb[i] > maxDb)
            {
                maxDb = analysis.BandLevelsDb[i];
                maxIdx = i;
            }
        Assert.True(maxIdx >= 0, "bande 1 kHz absente");
        Assert.InRange(maxDb, 3.0, 8.5);

        // le retard détecté correspond au délai injecté (±2 ms)
        Assert.InRange(analysis.PeakDelaySeconds, 0.019, 0.023);

        // bandes loin de la résonance proches de 0 dB
        int low = Array.FindIndex(analysis.BandFreqs, f => f >= 100);
        Assert.True(Math.Abs(analysis.BandLevelsDb[low]) < 3.0,
            $"niveau à ~100 Hz inattendu : {analysis.BandLevelsDb[low]:0.0} dB");
    }

    [Fact]
    public void PureNoise_IsRejected()
    {
        var spec = new LogSweepSpec(20, 20000, 4.0);
        var rng = new Random(99);
        var noise = new float[Fs * 4];
        for (int i = 0; i < noise.Length; i++)
            noise[i] = (float)(rng.NextDouble() * 2 - 1) * 0.01f;

        var ex = Assert.Throws<InvalidOperationException>(
            () => SweepAnalyzer.Analyze(noise, Fs, spec));
        Assert.Contains("Balayage non détecté", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TooShortFragment_Throws()
        => Assert.Throws<ArgumentException>(
            () => SweepAnalyzer.Analyze(new float[8000], Fs, new LogSweepSpec(20, 20000, 2.0)));

    [Fact]
    public void LowSampleRate_Throws()
        => Assert.Throws<ArgumentException>(
            () => SweepAnalyzer.Analyze(new float[48000], 4000, new LogSweepSpec(20, 20000, 2.0)));
}

// ===========================================================================
// M-CAL-04 — Croisement multi-signaux (spec I-CAL-04)
// ===========================================================================
public class MultiSignalAnalysisTests
{
    private const int Fs = 48000;

    /// <summary>Spectre Welch d'un signal filtré par une « pièce » connue.</summary>
    private static SignalCurve CurveOf(float[] signal, MeasurementSignalKind kind, string name)
    {
        var db = SpectrumTools.WelchDb(signal, Fs, 8192, out var freqs);
        return new SignalCurve { Name = name, Kind = kind, Freqs = freqs, Db = db };
    }

    [Fact]
    public void SineLevelDb_FindsKnownSine()
    {
        int n = Fs / 2;
        var buf = new float[n];
        for (int i = 0; i < n; i++)
            buf[i] = (float)(0.3 * Math.Sin(2 * Math.PI * 2500 * i / Fs));
        var (f, level) = MultiSignalAnalysis.SineLevelDb(buf, Fs, 2500);
        Assert.InRange(f, 2500 * 0.99, 2500 * 1.01);
        // scalloping Hann inclus (2500 Hz hors bin exact : jusqu'à ~1,5 dB)
        Assert.InRange(level, 20 * Math.Log10(0.3) - 1.5, 20 * Math.Log10(0.3) + 1.5);
    }

    [Fact]
    public void SineLevelDb_TooShort_Throws()
        => Assert.Throws<ArgumentException>(() => MultiSignalAnalysis.SineLevelDb(new float[4096], Fs, 1000));

    [Fact]
    public void TwoNoises_AggregateToFlatRoom()
    {
        // pièce simulée : peaking +4 dB @ 200 Hz ; le blanc et le rose la traversent
        var room = new BiquadFilter(FilterType.Peaking, Fs, 200, 4.0, 1.0);
        var white = new float[Fs * 3];
        var pink = new float[Fs * 3];
        var g1 = new NoiseGenerator(11);
        var g2 = new NoiseGenerator(22);
        g1.FillWhite(white);
        g2.FillPink(pink);
        for (int i = 0; i < white.Length; i++)
            white[i] = (float)room.Process(white[i]);
        var room2 = new BiquadFilter(FilterType.Peaking, Fs, 200, 4.0, 1.0);
        for (int i = 0; i < pink.Length; i++)
            pink[i] = (float)room2.Process(pink[i]);

        var curves = new List<SignalCurve>
        {
            CurveOf(white, MeasurementSignalKind.WhiteNoise, "blanc"),
            CurveOf(pink, MeasurementSignalKind.PinkNoise, "rose"),
        };
        var agg = MultiSignalAnalysis.Aggregate(curves, Array.Empty<SinePoint>(),
            new CorrectionOptions(), null);

        Assert.True(agg.Freqs.Length >= 8);
        // l'agrégat doit montrer la bosse de +4 dB vers 200 Hz et être plat ailleurs
        double At(double f) => SpectrumTools.InterpLog(agg.Freqs, agg.AlignedDb, f);
        Assert.InRange(At(200) - At(2000), 2.5, 5.5);
        Assert.InRange(At(2000) - At(4000), -1.5, 1.5);
    }

    [Fact]
    public void AberrantSignal_DoesNotShiftMedian()
    {
        // trois spectres mesurés de bruit rose : deux concordants (plateau 0 dB),
        // un aberrant avec une bosse de +12 dB @ 300 Hz. L'alignement par courbe
        // neutralise les décalages constants (seule la forme compte) : pour
        // perturber, l'aberrance doit être une différence de FORME.
        var opt = new CorrectionOptions();
        double[] freqs = Enumerable.Range(0, 90).Select(i => 20.0 * Math.Pow(1000.0, i / 89.0)).ToArray();
        double slope = MeasurementSignalCatalog.ExpectedBandSlopeDbPerOct(MeasurementSignalKind.PinkNoise);
        SignalCurve Make(double bump) => new()
        {
            Name = $"c{bump:0}",
            Kind = MeasurementSignalKind.PinkNoise,
            Freqs = freqs,
            Db = freqs.Select(f => -6.0 + slope * Math.Log2(f / 1000.0)
                                   + bump * Math.Exp(-Math.Pow(Math.Log2(f / 300.0) / 0.7, 2))).ToArray()
        };
        var curves = new List<SignalCurve> { Make(0), Make(0), Make(12) };
        var agg = MultiSignalAnalysis.Aggregate(curves, Array.Empty<SinePoint>(), opt, null);

        // la médiane reste insensible au signal aberrant : ~0 dB partout
        Assert.All(agg.AlignedDb, v => Assert.InRange(v, -1.5, 1.5));

        // ... mais la dispersion signale la divergence autour de 300 Hz (l'alignement
        // de la courbe aberrante laisse une petite dispersion de base ailleurs)
        double stdAt300 = SpectrumTools.InterpLog(agg.Freqs, agg.StdDb, 300);
        double stdAt4k = SpectrumTools.InterpLog(agg.Freqs, agg.StdDb, 4000);
        Assert.True(stdAt300 > 5.0, $"dispersion attendue élevée à 300 Hz : {stdAt300:0.0}");
        Assert.True(stdAt4k < 2.5, $"dispersion attendue modérée à 4 kHz : {stdAt4k:0.0}");
        Assert.True(stdAt300 > stdAt4k * 2, $"la dispersion doit se concentrer à 300 Hz : {stdAt300:0.0} vs {stdAt4k:0.0}");
    }

    [Fact]
    public void ConfidenceWeight_ScalesWithDispersion()
    {
        var freqs = new double[] { 100, 1000, 10000 };
        var std = new double[] { 1.0, 3.75, 6.0 };
        var w = MultiSignalAnalysis.ConfidenceWeight(freqs, std);
        Assert.Equal(1.0, w(100), 6);     // accord parfait
        Assert.Equal(0.675, w(1000), 6);  // mi-parcours
        Assert.Equal(0.35, w(10000), 6);  // désaccord maximal
    }

    [Fact]
    public void NoMeasurements_Throw()
        => Assert.Throws<ArgumentException>(
            () => MultiSignalAnalysis.Aggregate(Array.Empty<SignalCurve>(), Array.Empty<SinePoint>(),
                new CorrectionOptions(), null));
}

// ===========================================================================
// M-HPH-01 — Bibliothèque de presets casque (spec I-HPH-01)
// ===========================================================================
public class HeadphonePresetLibraryTests
{
    [Fact]
    public void Library_IsLargeAndComplete()
    {
        var all = HeadphonePresetLibrary.LoadAll();
        Assert.True(all.Count >= 200, $"bibliothèque trop petite : {all.Count}");
        Assert.True(HeadphonePresetLibrary.Brands().Count >= 10);
        Assert.All(all, m =>
        {
            Assert.False(string.IsNullOrEmpty(m.Brand));
            Assert.NotEmpty(m.Filters);
            Assert.False(string.IsNullOrEmpty(m.Id));
        });
    }

    [Fact]
    public void ByBrand_FiltersAndSorts()
    {
        var brands = HeadphonePresetLibrary.Brands();
        string brand = brands.First(b => HeadphonePresetLibrary.ByBrand(b).Count >= 2);
        var models = HeadphonePresetLibrary.ByBrand(brand);
        Assert.All(models, m => Assert.Equal(brand, m.Brand, ignoreCase: true));
        for (int i = 1; i < models.Count; i++)
            Assert.True(string.Compare(models[i].Model, models[i - 1].Model,
                StringComparison.OrdinalIgnoreCase) >= 0);
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        var first = HeadphonePresetLibrary.LoadAll()[0];
        var found = HeadphonePresetLibrary.Find(first.Id.ToUpperInvariant());
        Assert.NotNull(found);
        Assert.Equal(first.Id, found.Id, ignoreCase: true);
        Assert.Null(HeadphonePresetLibrary.Find("id-inexistant-xyz"));
    }

    [Fact]
    public void Search_MatchesAllTokens()
    {
        var results = HeadphonePresetLibrary.Search("sony");
        Assert.NotEmpty(results);
        Assert.All(results, m => Assert.Contains("sony", (m.Brand + " " + m.Model).ToLowerInvariant()));
        Assert.Equal(HeadphonePresetLibrary.Count, HeadphonePresetLibrary.Search("").Count);
    }

    [Fact]
    public void MatchDeviceName_PrefersMostSpecificFullMatch()
    {
        // le modèle reconnu doit correspondre au nom du périphérique
        var m = HeadphonePresetLibrary.MatchDeviceName("Casque (Sony WH-1000XM4 Stereo)");
        Assert.NotNull(m);
        Assert.Equal("sony", m.Brand.ToLowerInvariant());
        Assert.Contains("1000xm4", m.Model.ToLowerInvariant());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("ab")]
    public void MatchDeviceName_ShortOrEmpty_ReturnsNull(string? name)
        => Assert.Null(HeadphonePresetLibrary.MatchDeviceName(name));

    [Fact]
    public void ToEqPreset_AppliesClampsAndPreampBounds()
    {
        var model = new HeadphoneModel
        {
            Id = "test", Brand = "Test", Model = "Clamps",
            PreampDb = -99.0,
        };
        model.Filters.Add(new HeadphoneFilter("peaking", 40000.0, 40.0, 99.0));
        model.Filters.Add(new HeadphoneFilter("low-shelf", 10.0, -40.0, 0.001));

        var preset = HeadphonePresetLibrary.ToEqPreset(model);
        Assert.Equal(-15.0, preset.PreampDb, 6);
        Assert.Equal(2, preset.Bands.Count);
        Assert.Equal(20000.0, preset.Bands[0].Frequency, 6); // clampé
        Assert.Equal(24.0, preset.Bands[0].GainDb, 6);
        Assert.Equal(20.0, preset.Bands[0].Q, 6);
        Assert.Equal(20.0, preset.Bands[1].Frequency, 6);
        Assert.Equal(-24.0, preset.Bands[1].GainDb, 6);
        Assert.Equal(0.1, preset.Bands[1].Q, 6);
        Assert.Contains("AutoEq", preset.Description, StringComparison.Ordinal);
    }

    [Fact]
    public void TypeLabels_AreFrench()
    {
        var m = new HeadphoneModel { Type = "over-ear", Gamme = "premium" };
        Assert.Equal("Circum-aural", m.TypeLabel);
        Assert.Equal("Haut de gamme", m.GammeLabel);
        var m2 = new HeadphoneModel { Type = "inconnu", Gamme = "xyz" };
        Assert.Equal("inconnu", m2.TypeLabel);
        Assert.Equal("xyz", m2.GammeLabel);
    }
}
