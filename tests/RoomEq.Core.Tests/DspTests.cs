using System.Numerics;
using RoomEq.Core.Dsp;
using Xunit;

namespace RoomEq.Core.Tests;

// ===========================================================================
// M-DSP-01 — FFT (spec I-DSP-01)
// ===========================================================================
public class FftTests
{
    [Fact]
    public void RoundTrip_RestoresInput()
    {
        foreach (int n in new[] { 8, 1024, 4096 })
        {
            var rng = new Random(42 + n);
            var data = new Complex[n];
            for (int i = 0; i < n; i++)
                data[i] = new Complex(rng.NextDouble() * 2 - 1, rng.NextDouble() * 2 - 1);

            var copy = (Complex[])data.Clone();
            Fft.Forward(data);
            Fft.Inverse(data);

            for (int i = 0; i < n; i++)
            {
                double scale = Math.Max(1.0, copy[i].Magnitude);
                Assert.True(Math.Abs(data[i].Real - copy[i].Real) < 1e-9 * scale &&
                            Math.Abs(data[i].Imaginary - copy[i].Imaginary) < 1e-9 * scale,
                            $"round-trip n={n}, i={i} : {copy[i]} -> {data[i]}");
            }
        }
    }

    [Fact]
    public void SineOnExactBin_ConcentratesEnergy()
    {
        int n = 1024, k = 100;
        double amp = 0.7;
        var data = new Complex[n];
        for (int i = 0; i < n; i++)
            data[i] = new Complex(amp * Math.Sin(2.0 * Math.PI * k * i / n), 0.0);

        Fft.Forward(data);
        Assert.Equal(amp * n / 2.0, data[k].Magnitude, 6);
        Assert.Equal(amp * n / 2.0, data[n - k].Magnitude, 6);
    }

    [Fact]
    public void DiracImpulse_HasFlatSpectrum()
    {
        int n = 512;
        var data = new Complex[n];
        data[0] = new Complex(1.0, 0.0);
        Fft.Forward(data);
        for (int k = 0; k < n; k++)
            Assert.Equal(1.0, data[k].Magnitude, 9);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(2, 2)]
    [InlineData(3, 4)]
    [InlineData(1023, 1024)]
    [InlineData(1048577, 2097152)]
    public void NextPow2_ReturnsSmallestPow2(int input, int expected)
        => Assert.Equal(expected, Fft.NextPow2(input));

    [Theory]
    [InlineData(0)]
    [InlineData(-4)]
    public void NextPow2_NonPositive_Throws(int n)
        => Assert.Throws<ArgumentException>(() => Fft.NextPow2(n));

    [Fact]
    public void Forward_NonPow2_Throws()
    {
        var data = new Complex[1000];
        Assert.Throws<ArgumentException>(() => Fft.Forward(data));
        Assert.Throws<ArgumentException>(() => Fft.Inverse(data));
    }

    [Fact]
    public void IsPowerOfTwo_CoversEdgeCases()
    {
        Assert.True(Fft.IsPowerOfTwo(1));
        Assert.True(Fft.IsPowerOfTwo(65536));
        Assert.False(Fft.IsPowerOfTwo(0));
        Assert.False(Fft.IsPowerOfTwo(1000));
        Assert.False(Fft.IsPowerOfTwo(-8));
    }

    [Fact]
    public void MagnitudeSpectrum_WindowedSine_GivesHalfAmplitude()
    {
        int n = 4096, fs = 48000;
        double amp = 0.5, freq = 12.0 * fs / n; // bin 12 exact
        var frame = new float[n];
        for (int i = 0; i < n; i++)
            frame[i] = (float)(amp * Math.Sin(2 * Math.PI * freq * i / fs));

        var win = Windowing.Create(WindowType.Rectangular, n);
        var mag = Fft.MagnitudeSpectrum(frame, win, Windowing.Sum(win));

        int peak = 0;
        for (int k = 1; k < mag.Length; k++)
            if (mag[k] > mag[peak])
                peak = k;
        Assert.Equal(12, peak);
        double db = 20 * Math.Log10(2 * mag[12]);
        Assert.Equal(20 * Math.Log10(amp), db, 1); // −6,02 dB
    }

    [Fact]
    public void MagnitudeSpectrum_PadsShortFrameWithZeros()
    {
        int n = 256;
        var win = Windowing.Create(WindowType.Rectangular, n);
        var mag = Fft.MagnitudeSpectrum(Array.Empty<float>(), win, Windowing.Sum(win));
        Assert.Equal(n / 2 + 1, mag.Length);
        Assert.All(mag, m => Assert.Equal(0.0, m, 12));
    }
}

// ===========================================================================
// M-DSP-02 — Fenêtrage (spec I-DSP-02)
// ===========================================================================
public class WindowingTests
{
    [Fact]
    public void Hann_BoundsAndSymmetry()
    {
        var w = Windowing.Create(WindowType.Hann, 8);
        Assert.Equal(0.0, w[0], 12);
        // Hann symétrique n=8 : le sommet exact (i=3,5) n'est pas échantillonné,
        // les échantillons voisins du centre valent 0,5·(1+cos(π/7)) ≈ 0,9505
        Assert.Equal(0.5 * (1 + Math.Cos(Math.PI / 7)), Math.Max(w[3], w[4]), 9);
        for (int i = 0; i < 8; i++)
        {
            Assert.InRange(w[i], 0.0, 1.0);
            Assert.Equal(w[7 - i], w[i], 12);
        }
    }

    [Fact]
    public void Hann_SumIsHalfLength()
    {
        var w = Windowing.Create(WindowType.Hann, 1024);
        Assert.Equal(512.0, Windowing.Sum(w), 1.0);
    }

    [Fact]
    public void BlackmanHarris_BoundedSymmetricCoherentGain()
    {
        int n = 1024;
        var w = Windowing.Create(WindowType.BlackmanHarris, n);
        for (int i = 0; i < n; i++)
        {
            Assert.InRange(w[i], 0.0, 1.0);
            Assert.Equal(w[n - 1 - i], w[i], 12);
        }
        // gain cohérent ≈ 0,358 (somme/n)
        Assert.InRange(Windowing.Sum(w) / n, 0.350, 0.366);
    }

    [Fact]
    public void Rectangular_IsAllOnes()
    {
        var w = Windowing.Create(WindowType.Rectangular, 77);
        Assert.All(w, v => Assert.Equal(1.0, v));
        Assert.Equal(77.0, Windowing.Sum(w));
    }

    [Fact]
    public void SingletonWindow_IsOne()
    {
        Assert.Equal(1.0, Windowing.Create(WindowType.Hann, 1)[0], 12);
        Assert.Equal(1.0, Windowing.Create(WindowType.BlackmanHarris, 1)[0], 12);
    }

    [Fact]
    public void Create_NonPositive_Throws()
        => Assert.Throws<ArgumentException>(() => Windowing.Create(WindowType.Hann, 0));
}

// ===========================================================================
// M-DSP-03 — Biquad (spec I-DSP-03)
// ===========================================================================
public class BiquadTests
{
    private const int Fs = 48000;

    [Theory]
    [InlineData(6.0)]
    [InlineData(-8.0)]
    [InlineData(0.0)]
    public void Peaking_GainAtCenter_IsExact(double gainDb)
    {
        var f = new BiquadFilter(FilterType.Peaking, Fs, 1000, gainDb, 1.4);
        Assert.Equal(gainDb, f.ResponseDb(1000), 5);
    }

    [Fact]
    public void Peaking_GainFallsBackTowardZero_AwayFromCenter()
    {
        var f = new BiquadFilter(FilterType.Peaking, Fs, 1000, 12, 2.0);
        double far = f.ResponseDb(100);
        Assert.True(Math.Abs(far) < 0.5, $"attendu ~0 dB, obtenu {far:0.00} dB");
    }

    [Fact]
    public void LowPass_IsMinus3dBAtCutoff()
    {
        var f = new BiquadFilter(FilterType.LowPass, Fs, 5000, 0, 0.707);
        Assert.Equal(-3.01, f.ResponseDb(5000), 1);
    }

    [Fact]
    public void HighPass_AttenuatesLowFrequencies()
    {
        var f = new BiquadFilter(FilterType.HighPass, Fs, 100, 0, 0.707);
        double db = f.ResponseDb(20);
        Assert.True(db < -26, $"atténuation insuffisante à 20 Hz : {db:0.00} dB");
    }

    [Fact]
    public void LowShelf_ReachesPlateau()
    {
        var f = new BiquadFilter(FilterType.LowShelf, Fs, 105, 6, 0.75);
        double low = f.ResponseDb(50);
        double high = f.ResponseDb(4000);
        Assert.InRange(low, 5.3, 6.05);
        Assert.True(Math.Abs(high) < 0.3, $"plateau haut non nul : {high:0.00} dB");
    }

    [Fact]
    public void Notch_DeepAtCenterTransparentElsewhere()
    {
        var f = new BiquadFilter(FilterType.Notch, Fs, 1000, 0, 8.0);
        Assert.True(f.ResponseDb(1000) < -20, $"creux insuffisant : {f.ResponseDb(1000):0.00} dB");
        Assert.True(Math.Abs(f.ResponseDb(2000)) < 1.0,
            $"perte à 2 kHz : {f.ResponseDb(2000):0.00} dB");
    }

    /// <summary>Mesure le gain du filtre sur un sinus permanent (régime établi).</summary>
    private static double MeasureSteadyGainDb(BiquadFilter f, double freq, double amp = 0.5, int warmupMs = 400)
    {
        int warm = Fs * warmupMs / 1000;
        int len = Fs / 2;
        var buf = new float[len];
        double phase = 0, inc = 2 * Math.PI * freq / Fs;
        for (int i = 0; i < warm + len; i++)
        {
            double x = Math.Sin(phase) * amp;
            phase += inc;
            if (phase > 2 * Math.PI) phase -= 2 * Math.PI;
            double y = f.Process(x);
            if (i >= warm) buf[i - warm] = (float)y;
        }
        double rms = Math.Sqrt(buf.Average(s => (double)s * s));
        return 20 * Math.Log10(rms / (amp / Math.Sqrt(2)) + 1e-12);
    }

    [Fact]
    public void TimeDomain_MatchesFrequencyResponse()
    {
        var f = new BiquadFilter(FilterType.Peaking, Fs, 1000, 6.0, 1.0);
        double measured = MeasureSteadyGainDb(f, 1000);
        Assert.Equal(f.ResponseDb(1000), measured, 2);

        f.Configure(FilterType.Peaking, Fs, 1000, 6.0, 1.0);
        double off = MeasureSteadyGainDb(f, 3000);
        Assert.Equal(f.ResponseDb(3000), off, 2);
    }

    [Fact]
    public void BlockProcess_IsEquivalentToSampleBySample()
    {
        var f1 = new BiquadFilter(FilterType.Peaking, Fs, 800, 4.0, 1.2);
        var f2 = new BiquadFilter(FilterType.Peaking, Fs, 800, 4.0, 1.2);
        var rng = new Random(7);
        var block = new float[512];
        for (int i = 0; i < block.Length; i++)
            block[i] = (float)(rng.NextDouble() * 2 - 1);
        var reference = (float[])block.Clone();

        f1.Process(block.AsSpan(0, 256));
        f1.Process(block.AsSpan(256, 256));
        for (int i = 0; i < reference.Length; i++)
            reference[i] = (float)f2.Process(reference[i]);

        for (int i = 0; i < block.Length; i++)
            Assert.Equal(reference[i], block[i], 4);
    }

    [Fact]
    public void Configure_ClampsOutOfRangeParameters()
    {
        var f = new BiquadFilter();
        f.Configure(FilterType.Peaking, Fs, 10_000_000, 6, 100); // f0 et Q hors bornes
        Assert.Equal(Fs * 0.499, f.Frequency, 6);
        Assert.Equal(50.0, f.Q, 6);

        f.Configure(FilterType.Peaking, Fs, 0.001, 0, 0.001);
        Assert.Equal(1.0, f.Frequency, 6);
        Assert.Equal(0.05, f.Q, 6);
    }

    [Fact]
    public void Configure_NonPositiveSampleRate_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(
            () => new BiquadFilter(FilterType.Peaking, 0, 1000, 0, 1));

    [Fact]
    public void ReconfigureOnTheFly_StaysBoundedAndFinite()
    {
        var f = new BiquadFilter(FilterType.Peaking, Fs, 1000, 6, 1);
        var rng = new Random(3);
        for (int step = 0; step < 200; step++)
        {
            f.Configure(FilterType.Peaking, Fs, 20 + rng.NextDouble() * 15000,
                        -12 + rng.NextDouble() * 24, 0.3 + rng.NextDouble() * 6);
            double y = f.Process(rng.NextDouble() * 2 - 1);
            Assert.True(double.IsFinite(y), $"sortie non finie à l'étape {step}");
            Assert.True(Math.Abs(y) < 100, $"sortie instable à l'étape {step} : {y}");
        }
    }

    [Fact]
    public void Reset_ClearsTemporalState()
    {
        var f1 = new BiquadFilter(FilterType.LowPass, Fs, 500, 0, 0.707);
        var f2 = new BiquadFilter(FilterType.LowPass, Fs, 500, 0, 0.707);

        // sature le premier filtre, puis remet à zéro
        for (int i = 0; i < 1000; i++)
            f1.Process(1.0);
        f1.Reset();

        // après reset : identique à un filtre neuf, échantillon par échantillon
        for (int i = 0; i < 100; i++)
        {
            double x = i == 0 ? 1.0 : 0.0; // impulsion
            Assert.Equal(f2.Process(x), f1.Process(x), 12);
        }
    }
}
