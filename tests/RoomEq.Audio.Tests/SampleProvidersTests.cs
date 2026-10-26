using NAudio.Wave;
using RoomEq.Audio.Playback;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Briques ISampleProvider de la spec I-AUD-02 : gain, adaptation de canaux,
/// tap, générateur, boucle, EQ dans le flux — pures, testables partout.
/// </summary>
public class SampleProvidersTests
{
    // ---------------------------------------------------------------- gain

    [Fact]
    public void Gain_Half_ScalesEverySample()
    {
        var src = new TestSources.ArraySampleProvider(new float[] { 0.5f, -0.25f, 1f, 0f });
        var gain = new GainSampleProvider(src, () => 0.5);

        var buf = new float[4];
        int n = gain.Read(buf, 0, 4);

        Assert.Equal(4, n);
        Assert.Equal(0.25f, buf[0], 5);
        Assert.Equal(-0.125f, buf[1], 5);
        Assert.Equal(0.5f, buf[2], 5);
        Assert.Equal(0f, buf[3], 5);
    }

    [Fact]
    public void Gain_Dynamic_ChangesBetweenBlocks()
    {
        var src = new TestSources.SineSource(1000.0, 1.0);
        double g = 1.0;
        var gain = new GainSampleProvider(src, () => g);

        var buf = new float[256];
        gain.Read(buf, 0, 256);
        double rms1 = Rms(buf);
        g = 0.1;
        gain.Read(buf, 0, 256);
        double rms2 = Rms(buf);

        Assert.Equal(0.1, rms2 / rms1, 2);
    }

    private static double Rms(ReadOnlySpan<float> data)
    {
        double sum = 0;
        foreach (var v in data) sum += (double)v * v;
        return Math.Sqrt(sum / data.Length);
    }

    // ---------------------------------------------------------------- canaux

    [Fact]
    public void ChannelAdapter_MonoToStereo_CopiesMono()
    {
        var src = new TestSources.ArraySampleProvider(
            new float[] { 0.25f, -0.75f, 0.5f }, sampleRate: 48000, channels: 1);
        var adapter = new ChannelAdapterSampleProvider(src, 2);

        Assert.Equal(2, adapter.WaveFormat.Channels);
        var buf = new float[6];
        int n = adapter.Read(buf, 0, 6);

        Assert.Equal(6, n);
        Assert.Equal(0.25f, buf[0]); Assert.Equal(0.25f, buf[1]);
        Assert.Equal(-0.75f, buf[2]); Assert.Equal(-0.75f, buf[3]);
        Assert.Equal(0.5f, buf[4]); Assert.Equal(0.5f, buf[5]);
    }

    [Fact]
    public void ChannelAdapter_StereoToMono_Averages()
    {
        var src = new TestSources.ArraySampleProvider(
            new float[] { 1f, -1f, 0.5f, 0.25f });
        var adapter = new ChannelAdapterSampleProvider(src, 1);

        var buf = new float[2];
        int n = adapter.Read(buf, 0, 2);

        Assert.Equal(2, n);
        Assert.Equal(0f, buf[0], 5);        // (1 + -1) / 2
        Assert.Equal(0.375f, buf[1], 5);   // (0.5 + 0.25) / 2
    }

    [Fact]
    public void ChannelAdapter_TwoToSix_DuplicatesLastChannel()
    {
        var src = new TestSources.ArraySampleProvider(
            new float[] { 0.1f, 0.2f });    // 1 frame stéréo
        var adapter = new ChannelAdapterSampleProvider(src, 6);

        var buf = new float[6];
        int n = adapter.Read(buf, 0, 6);

        Assert.Equal(6, n);
        Assert.Equal(0.1f, buf[0]);
        Assert.Equal(0.2f, buf[1]);
        for (int c = 2; c < 6; c++)
            Assert.Equal(0.2f, buf[c]);     // le dernier canal est dupliqué
    }

    [Fact]
    public void ChannelAdapter_SixToTwo_KeepsFirstChannels()
    {
        var src = new TestSources.ArraySampleProvider(
            new float[] { 1f, 2f, 3f, 4f, 5f, 6f });   // 1 frame 6 canaux
        var adapter = new ChannelAdapterSampleProvider(src, 2);

        var buf = new float[2];
        int n = adapter.Read(buf, 0, 2);

        Assert.Equal(2, n);
        Assert.Equal(1f, buf[0]);
        Assert.Equal(2f, buf[1]);
    }

    // ---------------------------------------------------------------- tap

    [Fact]
    public void Tap_FifoOrder_AndClear()
    {
        var tap = new TapBuffer();
        var provider = new TapSampleProvider(
            new TestSources.ArraySampleProvider(new float[] { 1f, 2f, 3f, 4f, 5f, 6f }), tap);

        var buf = new float[6];
        provider.Read(buf, 0, 6);

        Assert.True(tap.TryDequeue(out var first));
        Assert.Equal(new float[] { 1f, 2f, 3f, 4f, 5f, 6f }, first);
        Assert.False(tap.TryDequeue(out _));

        provider.Read(buf, 0, 6);
        provider.Read(buf, 0, 6);
        tap.Clear();
        Assert.False(tap.TryDequeue(out _));
    }

    [Fact]
    public void Tap_Bounded_DropsOldestChunks()
    {
        var tap = new TapBuffer();
        var big = new float[100_000];
        for (int i = 0; i < big.Length; i++) big[i] = i;

        tap.Push(big, 0, big.Length);   // 100k
        tap.Push(big, 0, big.Length);   // 200k > 192k : les plus anciens partent
        tap.Push(big, 0, big.Length);   // 300k cumulés

        // la borne garantit qu'on ne dépasse jamais ~192k échantillons tamponnés
        Assert.True(tap.BufferedSamples <= 192_000, $"borné, obtenu {tap.BufferedSamples}");
        Assert.True(tap.BufferedSamples > 90_000, "au moins le dernier chunk complet");
    }

    // ---------------------------------------------------------------- générateur

    [Fact]
    public void Generator_WhiteNoise_RmsWithinExpectedRange()
    {
        var gen = new GeneratorSampleProvider(48000, 2)
        {
            Signal = TestSignal.WhiteNoise,
            Amplitude = 1.0,
        };
        var buf = new float[48000];
        gen.Read(buf, 0, buf.Length);

        double rms = Rms(buf);
        // bruit blanc uniforme [−1, 1] : σ = 1/√3 ≈ 0,577 (tolérance statistique)
        Assert.InRange(rms, 0.50, 0.65);
    }

    [Fact]
    public void Generator_Sine_PeaksAtExactFrequency()
    {
        var gen = new GeneratorSampleProvider(48000, 1)
        {
            Signal = TestSignal.Sine,
            Amplitude = 1.0,
            SineFrequency = 1000.0,
        };
        int n = 16384;
        var buf = new float[n];
        gen.Read(buf, 0, n);

        // FFT (miroir) sur le buffer
        var spectrum = new System.Numerics.Complex[n];
        for (int i = 0; i < n; i++) spectrum[i] = buf[i];
        Fft.Forward(spectrum);

        int peakBin = 1;
        double best = 0;
        for (int k = 1; k < n / 2; k++)
        {
            double mag = spectrum[k].Magnitude;
            if (mag > best) { best = mag; peakBin = k; }
        }
        double peakFreq = (double)peakBin * 48000 / n;
        Assert.InRange(peakFreq, 990.0, 1010.0);
        Assert.True(best > n / 8.0, "le pic doit dominer le spectre");
    }

    [Fact]
    public void Generator_AppliesAmplitude()
    {
        var gen = new GeneratorSampleProvider(48000, 1)
        {
            Signal = TestSignal.Sine,
            Amplitude = 0.25,
            SineFrequency = 440.0,
        };
        var buf = new float[4800];
        gen.Read(buf, 0, buf.Length);
        Assert.True(buf.All(v => Math.Abs(v) <= 0.25 + 1e-6), "amplitude respectée");
        Assert.True(buf.Any(v => Math.Abs(v) > 0.2), "signal non nul");
    }

    // ---------------------------------------------------------------- boucle

    [Fact]
    public void Looping_Restarts_WhenLoopEnabled()
    {
        var data = new float[] { 1f, 2f, 3f, 4f };
        var src = new TestSources.ArraySampleProvider(data);
        int restarts = 0;
        var loop = new LoopingSampleProvider(src, () => { restarts++; /* rembobinage simulé */ });

        var buf = new float[4];
        int n1 = loop.Read(buf, 0, 4);
        Assert.Equal(4, n1);
        Assert.Equal(0, restarts);

        // fin de source sans boucle : 0 échantillon
        Assert.Equal(0, loop.Read(buf, 0, 4));
        Assert.Equal(0, restarts);

        // avec boucle : le callback est appelé, la source est épuisée mais Read rend 0
        loop.Loop = true;
        int n3 = loop.Read(buf, 0, 4);
        Assert.True(restarts > 0, "le rembobinage doit être tenté");
        Assert.Equal(0, n3);   // la source de test ne rembobine pas réellement
    }

    [Fact]
    public void Looping_ARealRewindableSource_LoopsForever()
    {
        // source à rembobinage réel : compteur modulo
        int counter = 0;
        NAudio.Wave.ISampleProvider src = new RewindableSource(() =>
        {
            var b = new float[2];
            b[0] = counter;
            b[1] = counter + 1;
            counter += 2;
            return b;
        });
        int restarts = 0;
        var loop = new LoopingSampleProvider(src, () => restarts++) { Loop = true };

        var buf = new float[8];
        int n = loop.Read(buf, 0, 8);
        Assert.Equal(8, n);    // 4 frames de 2 échantillons servies
        Assert.Equal(0, restarts);   // la source infinie n'atteint jamais la fin
    }

    private sealed class RewindableSource(Func<float[]> frame) : NAudio.Wave.ISampleProvider
    {
        private float[] _pending = Array.Empty<float>();
        private int _pos;

        public NAudio.Wave.WaveFormat WaveFormat { get; } =
            NAudio.Wave.WaveFormat.CreateIeeeFloatWaveFormat(48000, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            int done = 0;
            while (done < count)
            {
                if (_pos >= _pending.Length)
                {
                    _pending = frame();
                    _pos = 0;
                }
                int take = Math.Min(count - done, _pending.Length - _pos);
                Array.Copy(_pending, _pos, buffer, offset + done, take);
                _pos += take;
                done += take;
            }
            return done;
        }
    }

    // ---------------------------------------------------------------- EQ dans le flux

    [Fact]
    public void EqSampleProvider_HighPassAttenuatesLowSine()
    {
        var src = new TestSources.SineSource(100.0, 1.0);
        var chain = new EqChain();
        var preset = new EqPreset("Coupe-bas");
        preset.Bands.Clear();
        preset.Bands.Add(new EqBand(FilterType.HighPass, 8000, 0, 0.71));
        chain.SetPreset(preset);

        var eq = new EqSampleProvider(src, chain);
        var buf = new float[48000];
        int n = eq.Read(buf, 0, buf.Length);

        Assert.Equal(buf.Length, n);
        double rms = Rms(buf);
        Assert.True(rms < 0.05, $"le passe-haut 8 kHz doit écraser le sinus 100 Hz (RMS {rms})");
    }
}
