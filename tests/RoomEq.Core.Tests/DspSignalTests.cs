using RoomEq.Core.Dsp;
using Xunit;

namespace RoomEq.Core.Tests;

// ===========================================================================
// M-DSP-04 — Générateurs de bruit (spec I-DSP-04)
// ===========================================================================
public class NoiseTests
{
    private const int Fs = 48000;

    private static float[] Generate(int seconds, int seed, Action<NoiseGenerator, float[]> fill)
    {
        var buf = new float[seconds * Fs];
        fill(new NoiseGenerator(seed), buf);
        return buf;
    }

    private static double Rms(float[] b)
        => Math.Sqrt(b.Average(s => (double)s * s));

    [Fact]
    public void White_RmsAndBounds()
    {
        var buf = Generate(2, 42, (g, b) => g.FillWhite(b));
        Assert.InRange(Rms(buf), 0.577 * 0.95, 0.577 * 1.05);
        Assert.All(buf, s => Assert.InRange(s, -1.0f, 1.0f));
    }

    [Fact]
    public void Pink_SlopeMinus3DbPerOctave()
    {
        var gen = new NoiseGenerator(1234);
        int n = Fs * 8;
        var buf = new float[n];
        gen.FillPink(buf);
        Assert.True(Rms(buf) < 0.25, $"RMS rose trop élevé : {Rms(buf):0.00}");

        var db = SpectrumTools.WelchDb(buf, Fs, 8192, out var binFreqs);
        // pente médiane entre les bandes d'octave 100 Hz -> 10 kHz
        double s1 = SpectrumTools.MeanDb(binFreqs, db, 90, 110);
        double s2 = SpectrumTools.MeanDb(binFreqs, db, 900, 1100);
        double s3 = SpectrumTools.MeanDb(binFreqs, db, 9000, 11000);
        double slopeLow = (s2 - s1) / Math.Log2(1000.0 / 100.0);
        double slopeHigh = (s3 - s2) / Math.Log2(10000.0 / 1000.0);
        Assert.InRange(slopeLow, -4.5, -1.5);
        Assert.InRange(slopeHigh, -4.5, -1.5);
    }

    [Fact]
    public void Brown_EnergyConcentratedInLowFrequencies()
    {
        var gen = new NoiseGenerator(7);
        int n = Fs * 4;
        var buf = new float[n];
        gen.FillBrown(buf);
        Assert.InRange(Rms(buf), 0.12, 0.3);

        var db = SpectrumTools.WelchDb(buf, Fs, 8192, out var binFreqs);
        double low = SpectrumTools.MeanDb(binFreqs, db, 20, 500);
        double high = SpectrumTools.MeanDb(binFreqs, db, 2000, 10000);
        Assert.True(low - high > 12,
            $"le brun doit concentrer l'énergie en bas : {low:0.0} dB vs {high:0.0} dB");
    }

    [Fact]
    public void Violet_EnergyConcentratedInHighFrequencies()
    {
        var gen = new NoiseGenerator(9);
        int n = Fs * 4;
        var buf = new float[n];
        gen.FillViolet(buf);
        Assert.InRange(Rms(buf), 0.2, 0.4);

        var db = SpectrumTools.WelchDb(buf, Fs, 8192, out var binFreqs);
        double low = SpectrumTools.MeanDb(binFreqs, db, 20, 500);
        double high = SpectrumTools.MeanDb(binFreqs, db, 2000, 10000);
        Assert.True(high - low > 12,
            $"le violet doit concentrer l'énergie en haut : {low:0.0} dB vs {high:0.0} dB");
    }

    [Fact]
    public void Sine_PhaseContinuesAcrossBlocks()
    {
        var gen = new NoiseGenerator();
        var b1 = new float[Fs / 2];
        var b2 = new float[Fs / 2];
        gen.FillSine(b1, 1000, Fs, 0.8);
        gen.FillSine(b2, 1000, Fs, 0.8);
        var full = b1.Concat(b2).ToArray();

        // comparaison au sinus théorique global
        for (int i = 0; i < full.Length; i += 97)
        {
            double expected = Math.Sin(2 * Math.PI * 1000 * i / Fs) * 0.8;
            Assert.Equal(expected, full[i], 3);
        }

        // fréquence mesurée par passages à zéro montants
        int crossings = 0;
        for (int i = 1; i < full.Length; i++)
            if (full[i - 1] <= 0 && full[i] > 0)
                crossings++;
        double freq = (double)crossings * Fs / full.Length;
        Assert.InRange(freq, 990, 1010);
    }

    [Fact]
    public void SeededGenerators_AreReproducible()
    {
        var g1 = new NoiseGenerator(42);
        var g2 = new NoiseGenerator(42);
        var b1 = new float[4096];
        var b2 = new float[4096];
        g1.FillPink(b1);
        g2.FillPink(b2);
        Assert.Equal(b1, b2);

        g1.FillWhite(b1);
        g2.FillWhite(b2);
        Assert.Equal(b1, b2);
    }

    [Fact]
    public void Sine_NonPositiveSampleRate_Throws()
    {
        var gen = new NoiseGenerator();
        Assert.Throws<ArgumentOutOfRangeException>(() => gen.FillSine(new float[8], 1000, 0, 1));
    }
}

// ===========================================================================
// M-DSP-05 — Balayage logarithmique (spec I-DSP-05)
// ===========================================================================
public class SweepTests
{
    private const int Fs = 48000;

    [Fact]
    public void StreamedFill_MatchesGenerateFull()
    {
        var spec = new LogSweepSpec(20, 20000, 3.0);
        var streamed = new LogSweepGenerator(spec, Fs);
        var full = new LogSweepGenerator(spec, Fs).GenerateFull(0.9);

        var buf = new float[7000];
        int produced = 0;
        while (produced < full.Length)
        {
            streamed.Fill(buf, 0.9);
            int take = Math.Min(buf.Length, full.Length - produced);
            for (int i = 0; i < take; i++)
                Assert.Equal(full[produced + i], buf[i]);
            produced += buf.Length;
        }
    }

    [Fact]
    public void InstantaneousFrequency_FollowsExponentialLaw()
    {
        var spec = new LogSweepSpec(20, 20000, 12.0);
        var gen = new LogSweepGenerator(spec, Fs);
        Assert.Equal(20.0, gen.FrequencyAt(0), 1);
        long n = gen.TotalSamples - 1;
        Assert.InRange(gen.FrequencyAt(n), 20000 * 0.99, 20000 * 1.01);
    }

    [Fact]
    public void EqualDurationPerOctave()
    {
        var spec = new LogSweepSpec(20, 20000, 12.0);
        var gen = new LogSweepGenerator(spec, Fs);

        long TimeOf(double f) // premier échantillon où la fréquence dépasse f
        {
            long lo = 0, hi = gen.TotalSamples - 1;
            while (lo < hi)
            {
                long mid = (lo + hi) / 2;
                if (gen.FrequencyAt(mid) < f) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        long t100 = TimeOf(100), t200 = TimeOf(200);
        long t1000 = TimeOf(1000), t2000 = TimeOf(2000);
        long d1 = t200 - t100, d2 = t2000 - t1000;
        Assert.True(Math.Abs(d1 - d2) <= Math.Max(d1, d2) / 100,
            $"durées d'octave différentes : {d1} vs {d2}");
    }

    [Fact]
    public void AttackFade_IsSmooth()
    {
        var spec = new LogSweepSpec(20, 20000, 2.0);
        var gen = new LogSweepGenerator(spec, Fs);
        var s = gen.GenerateFull(1.0);
        int fade = Math.Max(16, Fs / 100);
        Assert.True(Math.Abs(s[0]) < 0.05, "le premier échantillon doit être quasi nul");
        Assert.True(Math.Abs(s[fade / 4]) < 0.25, "le fondu d'attaque doit être progressif");
        Assert.True(Math.Abs(s[^1]) < 0.05, "la fin doit être fondue");
    }

    [Fact]
    public void SilenceBeyondTotalSamples()
    {
        var spec = new LogSweepSpec(20, 20000, 1.0);
        var gen = new LogSweepGenerator(spec, Fs);
        var full = gen.GenerateFull(1.0);
        var tail = new float[5000];
        gen.Fill(tail, 1.0);
        Assert.All(tail, s => Assert.Equal(0f, s));
    }

    [Fact]
    public void EffectiveF2_IsClampedByNyquist()
    {
        var spec = new LogSweepSpec(20, 30000, 5.0);
        var gen = new LogSweepGenerator(spec, 32000);
        long n = gen.TotalSamples - 1;
        Assert.InRange(gen.FrequencyAt(n), 32000 * 0.44, 32000 * 0.46);
    }

    [Theory]
    [InlineData(1000, 1000)]   // F1 == F2
    [InlineData(2000, 1000)]   // F1 > F2
    [InlineData(0, 1000)]      // F1 nul
    public void InvalidSpec_Throws(double f1, double f2)
        => Assert.Throws<ArgumentException>(() => new LogSweepGenerator(new LogSweepSpec(f1, f2, 5.0), Fs));

    [Fact]
    public void TooShortDuration_Throws()
        => Assert.Throws<ArgumentException>(() => new LogSweepGenerator(new LogSweepSpec(20, 20000, 0.3), Fs));

    [Fact]
    public void OverallRms_IsSineLike()
    {
        var s = new LogSweepGenerator(new LogSweepSpec(20, 20000, 3.0), Fs).GenerateFull(1.0);
        double rms = Math.Sqrt(s.Average(x => (double)x * x));
        Assert.InRange(rms, 0.4, 0.8);
    }
}

// ===========================================================================
// M-DSP-06 — Analyse spectrale (spec I-DSP-06)
// ===========================================================================
public class SpectrumTests
{
    private const int Fs = 48000;

    [Fact]
    public void WhiteNoise_SpectrumIsFlat()
    {
        int n = Fs * 4;
        var noise = new NoiseGenerator(1234);
        var buf = new float[n];
        noise.FillWhite(buf);

        var db = SpectrumTools.WelchDb(buf, Fs, 8192, out var binFreqs);
        var smooth = SpectrumTools.FractionalOctaveSmooth(binFreqs, db, 1.0 / 3.0);

        double mean = SpectrumTools.MeanDb(binFreqs, smooth, 200, 10000);
        double maxDev = 0;
        for (int i = 0; i < binFreqs.Length; i++)
        {
            if (binFreqs[i] < 200 || binFreqs[i] > 10000)
                continue;
            maxDev = Math.Max(maxDev, Math.Abs(smooth[i] - mean));
        }
        Assert.True(maxDev < 2.0, $"bruit blanc non plat : écart max {maxDev:0.00} dB");
    }

    [Fact]
    public void Sine_IsFoundByWelchAtRightFrequency()
    {
        int n = Fs * 2;
        var buf = new float[n];
        double freq = 1000, amp = 0.25;
        for (int i = 0; i < n; i++)
            buf[i] = (float)(amp * Math.Sin(2 * Math.PI * freq * i / Fs));

        var db = SpectrumTools.WelchDb(buf, Fs, 8192, out var binFreqs);
        int peak = 0;
        for (int k = 1; k < db.Length; k++)
            if (db[k] > db[peak])
                peak = k;
        Assert.InRange(binFreqs[peak], 950, 1050);
        double theo = 20 * Math.Log10(amp);
        Assert.True(Math.Abs(db[peak] - theo) < 1.5,
            $"niveau mesuré {db[peak]:0.00} dB vs théorique {theo:0.00} dB");
    }

    [Fact]
    public void OctaveLevels_AggregatesBandsEnergetically()
    {
        int n = 8192;
        var freqs = new double[n / 2 + 1];
        var db = new double[n / 2 + 1];
        for (int k = 0; k < freqs.Length; k++)
        {
            freqs[k] = k * (double)Fs / n;
            db[k] = -10; // spectre plat à -10 dB
        }
        SpectrumTools.OctaveLevels(freqs, db, 100, 10000, 1.0 / 3.0, out var centers, out var levels);
        Assert.True(centers.Length >= 20);
        Assert.All(levels, l => Assert.Equal(-10, l, 1));
        // centres géométriques croissants
        for (int i = 1; i < centers.Length; i++)
            Assert.True(centers[i] > centers[i - 1]);
    }

    [Fact]
    public void FractionalOctaveSmooth_PreservesPlateau()
    {
        var freqs = Enumerable.Range(1, 200).Select(i => 20.0 * Math.Pow(1000.0 / 20.0, i / 199.0)).ToArray();
        var values = freqs.Select(f => f >= 100 && f <= 1000 ? 0.0 : -60.0).ToArray();

        var smoothed = SpectrumTools.FractionalOctaveSmooth(freqs, values, 1.0);
        // au cœur du plateau, le lissage 1/1 octave ne doit pas s'écarter de ±3 dB
        foreach (var (f, v) in freqs.Zip(smoothed))
            if (f >= 200 && f <= 500)
                Assert.InRange(v, -3.0, 3.0);

        // fraction ≤ 0 : copie intacte
        var identity = SpectrumTools.FractionalOctaveSmooth(freqs, values, 0.0);
        Assert.Equal(values, identity);
    }

    [Fact]
    public void InterpLog_InterpolatesInLogDomain()
    {
        var freqs = new double[] { 100, 200 };
        var values = new double[] { 0, 6 };
        Assert.Equal(3.0, SpectrumTools.InterpLog(freqs, values, 100 * Math.Sqrt(2)), 2);
        Assert.Equal(0.0, SpectrumTools.InterpLog(freqs, values, 50));   // extrapolation basse
        Assert.Equal(6.0, SpectrumTools.InterpLog(freqs, values, 1e6));  // extrapolation haute
        Assert.Equal(0.0, SpectrumTools.InterpLog(Array.Empty<double>(), Array.Empty<double>(), 100));
    }

    [Fact]
    public void PeakAndRms_AreDbfs()
    {
        Assert.Equal(-6.02, SpectrumTools.PeakDb(new float[] { 0.5f, -0.5f }), 2);
        Assert.True(SpectrumTools.PeakDb(new float[] { 1e-9f }) < -170);
        Assert.Equal(-240.0, SpectrumTools.RmsDb(Array.Empty<float>()));

        var sine = new float[Fs / 10];
        for (int i = 0; i < sine.Length; i++)
            sine[i] = (float)(0.5 * Math.Sin(2 * Math.PI * 1000 * i / Fs));
        Assert.Equal(20 * Math.Log10(0.5 / Math.Sqrt(2)), SpectrumTools.RmsDb(sine), 1);
    }

    [Fact]
    public void Welch_InvalidParameters_Throw()
    {
        var buf = new float[100];
        Assert.Throws<ArgumentException>(() => SpectrumTools.WelchDb(buf, Fs, 1000, out _));
        Assert.Throws<ArgumentException>(() => SpectrumTools.WelchDb(buf, Fs, 4096, out _)); // < fft/4
    }
}

// ===========================================================================
// M-DSP-07 — Mixage de canaux (spec I-DSP-07)
// ===========================================================================
public class ChannelMixerTests
{
    [Fact]
    public void Stereo_MixFirstTwo_Averages()
    {
        float[] interleaved = { 1, -1, 1, -1, 1, -1 };
        var mono = ChannelMixer.ToMono(interleaved, 2, ChannelMixer.MixFirstTwo);
        Assert.Equal(new float[] { 0, 0, 0 }, mono);
    }

    [Fact]
    public void Stereo_IsolatesRequestedChannel()
    {
        float[] interleaved = { 1, -1, 1, -1, 1, -1 };
        Assert.Equal(new float[] { 1, 1, 1 }, ChannelMixer.ToMono(interleaved, 2, 0));
        Assert.Equal(new float[] { -1, -1, -1 }, ChannelMixer.ToMono(interleaved, 2, 1));
    }

    [Fact]
    public void Quad_IsolatesThirdChannel()
    {
        float[] interleaved = { 1, 2, 3, 4, 5, 6, 7, 8 };
        var mono = ChannelMixer.ToMono(interleaved, 4, 2);
        Assert.Equal(new float[] { 3, 7 }, mono);
    }

    [Fact]
    public void OutOfRangeChannel_FallsBackToFirstTwoMix()
    {
        float[] interleaved = { 1, -1, 5, 5, 1, -1, 5, 5 }; // 4 canaux, 2 trames
        var mono = ChannelMixer.ToMono(interleaved, 4, 9);
        Assert.Equal(new float[] { 0, 0 }, mono);
    }

    [Fact]
    public void Mono_IsCopy()
    {
        float[] interleaved = { 0.25f, -0.75f, 0.5f };
        Assert.Equal(interleaved, ChannelMixer.ToMono(interleaved, 1, 0));
    }

    [Fact]
    public void ZeroChannels_TreatedAsMono()
    {
        float[] interleaved = { 0.25f, -0.75f };
        Assert.Equal(interleaved, ChannelMixer.ToMono(interleaved, 0, ChannelMixer.MixFirstTwo));
    }
}
