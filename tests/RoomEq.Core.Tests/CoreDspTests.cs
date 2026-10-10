using System.Numerics;
using RoomEq.Core.Calibration;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using Xunit;

namespace RoomEq.Core.Tests;

public class BiquadTests
{
    [Theory]
    [InlineData(6.0)]
    [InlineData(-8.0)]
    [InlineData(0.0)]
    public void Peaking_GainAtCenterFrequency_IsExact(double gainDb)
    {
        var f = new BiquadFilter(FilterType.Peaking, 48000, 1000, gainDb, 1.4);
        double measured = f.ResponseDb(1000, 48000);
        Assert.Equal(gainDb, measured, 5);
    }

    [Fact]
    public void Peaking_GainFallsBackTowardZero_AwayFromCenter()
    {
        var f = new BiquadFilter(FilterType.Peaking, 48000, 1000, 12, 2.0);
        double far = f.ResponseDb(100, 48000); // très loin sous la bande
        Assert.True(Math.Abs(far) < 0.5, $"attendu ~0 dB, obtenu {far:0.00} dB");
    }

    [Fact]
    public void LowPass_IsMinus3dBAtCutoff()
    {
        var f = new BiquadFilter(FilterType.LowPass, 48000, 5000, 0, 0.707);
        double db = f.ResponseDb(5000, 48000);
        Assert.Equal(-3.01, db, 1);
    }

    [Fact]
    public void HighPass_AttenuatesLowFrequencies()
    {
        var f = new BiquadFilter(FilterType.HighPass, 48000, 100, 0, 0.707);
        double db = f.ResponseDb(20, 48000);
        // 2e ordre Butterworth à f/5 : atténuation théorique -28 dB
        Assert.True(db < -26, $"atténuation insuffisante à 20 Hz : {db:0.00} dB");
    }

    [Fact]
    public void LowShelf_ReachesPlateau()
    {
        var f = new BiquadFilter(FilterType.LowShelf, 48000, 105, 6, 0.75);
        double low = f.ResponseDb(50, 48000);      // proche du plateau (transition vers 105 Hz)
        double high = f.ResponseDb(4000, 48000);   // au-dessus de la transition : ~0 dB
        Assert.InRange(low, 5.3, 6.05);
        Assert.True(Math.Abs(high) < 0.3, $"plateau haut non nul : {high:0.00} dB");
    }

    [Fact]
    public void Process_ImpulseThroughFlatPreset_StaysBounded()
    {
        var chain = new EqChain();
        chain.Configure(48000, 1);
        var preset = EqPreset.Flat(10);
        foreach (var b in preset.Bands) b.GainDb = 6.0;
        chain.SetPreset(preset);

        var buf = new float[48000];
        var rng = new Random(42);
        for (int i = 0; i < buf.Length; i++) buf[i] = (float)(rng.NextDouble() * 2 - 1);
        chain.Process(buf, 0, buf.Length);

        double max = buf.Max(Math.Abs);
        Assert.True(max < 60.0, $"instabilité : amplitude max {max:0.0}");
    }
}

public class FftTests
{
    [Fact]
    public void SineAtBinCenter_HasCorrectAmplitude()
    {
        int n = 8192, fs = 48000;
        double amp = 0.5, freq = 12.0 * fs / n; // bin 12 exact
        var frame = new float[n];
        for (int i = 0; i < n; i++) frame[i] = (float)(amp * Math.Sin(2 * Math.PI * freq * i / fs));

        var win = Windowing.Create(WindowType.Rectangular, n);
        var mag = Fft.MagnitudeSpectrum(frame, win, Windowing.Sum(win));

        int peak = 0;
        for (int k = 1; k < mag.Length; k++) if (mag[k] > mag[peak]) peak = k;
        Assert.Equal(12, peak);
        double db = 20 * Math.Log10(2 * mag[12]);
        Assert.Equal(20 * Math.Log10(amp), db, 1); // -6.02 dB
    }
}

public class SpectrumTests
{
    [Fact]
    public void WhiteNoise_SpectrumIsFlat()
    {
        int fs = 48000, n = fs * 4;
        var noise = new NoiseGenerator(1234);
        var buf = new float[n];
        noise.FillWhite(buf);

        var db = SpectrumTools.WelchDb(buf, fs, 8192, out var binFreqs);
        var smooth = SpectrumTools.FractionalOctaveSmooth(binFreqs, db, 1.0 / 3.0);

        double mean = SpectrumTools.MeanDb(binFreqs, smooth, 200, 10000);
        double maxDev = 0;
        for (int i = 0; i < binFreqs.Length; i++)
        {
            if (binFreqs[i] < 200 || binFreqs[i] > 10000) continue;
            maxDev = Math.Max(maxDev, Math.Abs(smooth[i] - mean));
        }
        Assert.True(maxDev < 2.0, $"bruit blanc non plat : écart max {maxDev:0.00} dB");
    }

    [Fact]
    public void Sine_IsFoundByWelchAtRightFrequency()
    {
        int fs = 48000, n = fs * 2;
        var buf = new float[n];
        double freq = 1000, amp = 0.25;
        for (int i = 0; i < n; i++) buf[i] = (float)(amp * Math.Sin(2 * Math.PI * freq * i / fs));

        var db = SpectrumTools.WelchDb(buf, fs, 8192, out var binFreqs);
        int peak = 0;
        for (int k = 1; k < db.Length; k++) if (db[k] > db[peak]) peak = k;
        Assert.InRange(binFreqs[peak], 950, 1050);
        // niveau mesuré proche du niveau théorique (scalloping Hann < 1,5 dB hors bin exact)
        double theo = 20 * Math.Log10(amp);
        Assert.True(Math.Abs(db[peak] - theo) < 1.5,
            $"niveau mesuré {db[peak]:0.00} dB vs théorique {theo:0.00} dB");
    }

    [Fact]
    public void OctaveLevels_AggregatesBands()
    {
        int fs = 48000, n = 8192;
        var freqs = new double[n / 2 + 1];
        var db = new double[n / 2 + 1];
        for (int k = 0; k < freqs.Length; k++)
        {
            freqs[k] = k * (double)fs / n;
            db[k] = -10; // spectre plat à -10 dB
        }
        SpectrumTools.OctaveLevels(freqs, db, 100, 10000, 1.0 / 3.0, out var centers, out var levels);
        Assert.True(centers.Length >= 20);
        Assert.All(levels, l => Assert.Equal(-10, l, 1));
    }
}

public class MicProfileTests
{
    [Fact]
    public void RewCalFile_IsParsedCorrectly()
    {
        string cal = "\"Calculated\"\n" +
                     "\"Frequency (Hz), Gain (dB)\"\n" +
                     "\"20,-0.45\"\n" +
                     "\"100,0.12\"\n" +
                     "\"1000,1.05\"\n" +
                     "\"5000,-2.30\"\n" +
                     "\"20000,-5.10\"\n";
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
        string csv = "frequence;gain\n" +
                     "50;0,2\n" +
                     "500;-0,8\n" +
                     "2000;1,4\n" +
                     "8000;-2,0\n" +
                     "15000;-4,5\n";
        var p = MicProfile.ParseText(csv, "test");
        Assert.True(p.IsValid);
        Assert.Equal(-0.8, p.GainAt(500), 3);
        Assert.Equal(-4.5, p.GainAt(15000), 3);
    }

    [Fact]
    public void GainAt_InterpolatesLogLinearly()
    {
        var p = MicProfile.ParseText("100,0\n10000,20\n", "t");
        double mid = p.GainAt(1000); // milieu log : la moitié de 20
        Assert.Equal(10.0, mid, 2);
    }
}

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

            // résonances gaussiennes dans le domaine log
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

        double residual = result.ResidualMaxDb();
        Assert.True(residual < 3.5, $"correction insuffisante : résidu max {residual:0.00} dB");
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

        // Préampli = -boost max (marge anti-écrêtage)
        double maxBoost = result.CorrectionDb.Max();
        Assert.Equal(-Math.Max(maxBoost, 0), result.Preset.PreampDb, 6);

        // Toutes les bandes générées sont dans la plage autorisée
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

        // Micro qui "voit" +4 dB de trop au-dessus de 2 kHz (pente log-linéaire)
        var mic = MicProfile.ParseText("20,0\n1000,0\n20000,4\n", "Micro test");

        var opt = new CorrectionOptions { Target = TargetType.Flat };
        var withMic = CorrectionDesigner.Design(freqs, db, opt, 48000, "AvecMicro", mic);
        var without = CorrectionDesigner.Design(freqs, db, opt, 48000, "SansMicro", null);

        // Avec profil : la correction dans les aigus doit être plus faible (le micro est déconvolué)
        static double Avg(double[] corr, double[] centers)
        {
            double s = 0; int n = 0;
            for (int i = 0; i < centers.Length; i++)
                if (centers[i] >= 8000) { s += corr[i]; n++; }
            return n > 0 ? s / n : 0;
        }
        double cWith = Avg(withMic.CorrectionDb, withMic.BandCenters);
        double cWithout = Avg(without.CorrectionDb, without.BandCenters);
        Assert.True(cWith > cWithout - 0.5,
            $"la correction micro n'a pas d'effet attendu : {cWith:0.00} vs {cWithout:0.00}");
    }
}

public class PresetTests
{
    [Fact]
    public void FlatPreset_HasCorrectBandLayout()
    {
        var p = EqPreset.Flat(10);
        Assert.Equal(10, p.Bands.Count);
        Assert.Equal(31.25, p.Bands[0].Frequency, 1);
        Assert.Equal(16000, p.Bands[^1].Frequency, 0);
        Assert.All(p.Bands, b => Assert.Equal(0, b.GainDb));
    }

    [Fact]
    public void EqChain_CompositeResponseMatchesBandGains()
    {
        var chain = new EqChain();
        chain.Configure(48000, 2);

        // Une seule bande : le gain au centre est exact
        var preset = new EqPreset("t");
        preset.Bands.Add(new EqBand(FilterType.Peaking, 1000, 6, 2));
        chain.SetPreset(preset);
        Assert.Equal(6, chain.ResponseDb(1000), 2);

        // Deux bandes : les queues de filtre se superposent légèrement
        preset.Bands.Add(new EqBand(FilterType.Peaking, 3000, -4, 2));
        chain.SetPreset(preset);
        double at1000 = chain.ResponseDb(1000);
        Assert.InRange(at1000, 5.5, 6.3);
        Assert.InRange(chain.ResponseDb(3000), -4.6, -3.4); // queue du filtre 1 kHz incluse
        Assert.True(Math.Abs(chain.ResponseDb(10000)) < 1.0,
            $"réponse loin des bandes devrait être ~0 : {chain.ResponseDb(10000):0.00}");
    }
}
