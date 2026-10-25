using System.Text.Json;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using Xunit;

namespace RoomEq.Core.Tests;

// ===========================================================================
// M-EQ-01 — Bandes (spec I-EQ-01) + M-EQ-03 — Presets (spec I-EQ-03)
// ===========================================================================
public class EqModelTests
{
    [Fact]
    public void Band_Defaults_ArePeaking1kHzUnity()
    {
        var b = new EqBand();
        Assert.Equal(FilterType.Peaking, b.Type);
        Assert.Equal(1000.0, b.Frequency);
        Assert.Equal(0.0, b.GainDb);
        Assert.Equal(1.1, b.Q);
        Assert.True(b.Enabled);
    }

    [Fact]
    public void Band_Clone_IsIndependent()
    {
        var b = new EqBand(FilterType.Notch, 250, -3, 6);
        var c = b.Clone();
        c.Frequency = 999;
        c.GainDb = 12;
        Assert.Equal(250, b.Frequency);
        Assert.Equal(-3, b.GainDb);
    }

    [Fact]
    public void Band_JsonRoundTrip()
    {
        var b = new EqBand(FilterType.HighShelf, 4000, 2.5, 0.7, enabled: false);
        var json = JsonSerializer.Serialize(b);
        var back = JsonSerializer.Deserialize<EqBand>(json);
        Assert.NotNull(back);
        Assert.Equal(b.Type, back.Type);
        Assert.Equal(b.Frequency, back.Frequency);
        Assert.Equal(b.GainDb, back.GainDb);
        Assert.Equal(b.Q, back.Q);
        Assert.Equal(b.Enabled, back.Enabled);
    }

    [Fact]
    public void Band_ToString_MentionsAllParameters()
    {
        var s = new EqBand(FilterType.Peaking, 1000, 3, 1.5).ToString();
        Assert.Contains("1000", s);
        Assert.Contains("+3", s);
        Assert.Matches(@"1[,\.]50", s); // indépendant de la culture courante
    }

    [Fact]
    public void FlatPreset_HasCorrectBandLayout()
    {
        var p = EqPreset.Flat(10);
        Assert.Equal(10, p.Bands.Count);
        Assert.Equal(31.25, p.Bands[0].Frequency, 1);
        Assert.Equal(16000, p.Bands[^1].Frequency, 0);
        Assert.All(p.Bands, b => Assert.Equal(0, b.GainDb));
        Assert.All(p.Bands, b => Assert.Equal(1.1, b.Q));
        // répartition géométrique : ratio constant entre bandes adjacentes
        for (int i = 1; i < p.Bands.Count; i++)
            Assert.Equal(p.Bands[1].Frequency / p.Bands[0].Frequency,
                         p.Bands[i].Frequency / p.Bands[i - 1].Frequency, 6);
    }

    [Fact]
    public void FlatPreset_SingleBand_DoesNotThrow()
    {
        var p = EqPreset.Flat(1);
        Assert.Single(p.Bands);
        Assert.InRange(p.Bands[0].Frequency, 31.25, 16000);
    }

    [Fact]
    public void FlatPreset_ZeroBand_Throws()
        => Assert.Throws<ArgumentOutOfRangeException>(() => EqPreset.Flat(0));

    [Fact]
    public void Preset_Clone_IsDeep()
    {
        var p = EqPreset.Flat(5);
        p.Name = "Original";
        p.PreampDb = -2.5;
        var c = p.Clone();
        c.Bands.Clear();
        c.PreampDb = 10;
        Assert.Equal(5, p.Bands.Count);
        Assert.Equal(-2.5, p.PreampDb);
    }

    [Fact]
    public void Preset_JsonRoundTrip()
    {
        var p = EqPreset.Flat(4);
        p.Name = "Test";
        p.Description = "desc";
        p.PreampDb = -1.5;
        p.Bands[2].GainDb = 4;
        var json = JsonSerializer.Serialize(p);
        var back = JsonSerializer.Deserialize<EqPreset>(json);
        Assert.NotNull(back);
        Assert.Equal(p.Name, back.Name);
        Assert.Equal(p.PreampDb, back.PreampDb);
        Assert.Equal(4, back.Bands.Count);
        Assert.Equal(4, back.Bands[2].GainDb);
    }
}

// ===========================================================================
// M-EQ-02 — Chaîne EQ temps réel (spec I-EQ-02)
// ===========================================================================
public class EqChainTests
{
    private const int Fs = 48000;

    [Fact]
    public void CompositeResponse_MatchesBandGains()
    {
        var chain = new EqChain();
        chain.Configure(Fs, 2);

        var preset = new EqPreset("t");
        preset.Bands.Add(new EqBand(FilterType.Peaking, 1000, 6, 2));
        chain.SetPreset(preset);
        Assert.Equal(6, chain.ResponseDb(1000), 2);

        preset.Bands.Add(new EqBand(FilterType.Peaking, 3000, -4, 2));
        chain.SetPreset(preset);
        Assert.InRange(chain.ResponseDb(1000), 5.5, 6.3);
        Assert.InRange(chain.ResponseDb(3000), -4.6, -3.4);
        Assert.True(Math.Abs(chain.ResponseDb(10000)) < 1.0,
            $"réponse loin des bandes devrait être ~0 : {chain.ResponseDb(10000):0.00}");
    }

    [Fact]
    public void Preamp_ShiftsWholeResponse()
    {
        var chain = new EqChain();
        chain.Configure(Fs, 2);
        var preset = new EqPreset("t");
        preset.Bands.Add(new EqBand(FilterType.Peaking, 1000, 6, 2));
        chain.SetPreset(preset);
        double before = chain.ResponseDb(1000);
        chain.SetPreamp(-3);
        Assert.Equal(before - 3, chain.ResponseDb(1000), 3);
        Assert.Equal(-3, chain.PreampDb, 6);
    }

    [Fact]
    public void Process_AppliesPreampAndBandsInTimeDomain()
    {
        var chain = new EqChain();
        chain.Configure(Fs, 1);
        var preset = new EqPreset("t");
        preset.Bands.Add(new EqBand(FilterType.Peaking, 1000, 6, 2));
        chain.SetPreset(preset);
        chain.SetPreamp(-3); // +6 dB de bande - 3 dB de préampli = +3 dB net @ 1 kHz

        var buf = new float[Fs];
        for (int i = 0; i < buf.Length; i++)
            buf[i] = (float)(0.25 * Math.Sin(2 * Math.PI * 1000 * i / Fs));
        chain.Process(buf, 0, buf.Length);

        // mesure du régime établi sur la deuxième moitié
        double rms = 0;
        int start = buf.Length / 2;
        for (int i = start; i < buf.Length; i++)
            rms += (double)buf[i] * buf[i];
        rms = Math.Sqrt(rms / (buf.Length - start));
        double gainDb = 20 * Math.Log10(rms / (0.25 / Math.Sqrt(2)));
        Assert.Equal(3.0, gainDb, 1);
    }

    [Fact]
    public void Bypass_ReturnsInputUntouched()
    {
        var chain = new EqChain();
        chain.Configure(Fs, 2);
        var preset = new EqPreset("t");
        preset.Bands.Add(new EqBand(FilterType.Peaking, 1000, 12, 2));
        chain.SetPreset(preset);
        chain.Bypass = true;

        var rng = new Random(5);
        var buf = new float[2048];
        for (int i = 0; i < buf.Length; i++)
            buf[i] = (float)(rng.NextDouble() * 2 - 1);
        var copy = (float[])buf.Clone();
        chain.Process(buf, 0, buf.Length);
        Assert.Equal(copy, buf);
    }

    [Fact]
    public void EmptyPreset_IsTransparentApartFromPreamp()
    {
        var chain = new EqChain();
        chain.Configure(Fs, 2);
        chain.SetPreset(new EqPreset("vide"));
        var buf = new float[1024];
        var rng = new Random(6);
        for (int i = 0; i < buf.Length; i++)
            buf[i] = (float)(rng.NextDouble() * 2 - 1);
        var copy = (float[])buf.Clone();
        chain.Process(buf, 0, buf.Length);
        Assert.Equal(copy, buf);
    }

    [Fact]
    public void Stereo_ChannelsAreFilteredIndependentlyButIdentically()
    {
        var chain = new EqChain();
        chain.Configure(Fs, 2);
        var preset = new EqPreset("t");
        preset.Bands.Add(new EqBand(FilterType.Peaking, 1000, 6, 2));
        chain.SetPreset(preset);

        int frames = 4096;
        var buf = new float[frames * 2];
        for (int i = 0; i < frames; i++)
        {
            float x = (float)(0.3 * Math.Sin(2 * Math.PI * 1000 * i / Fs));
            buf[i * 2] = x;
            buf[i * 2 + 1] = x;
        }
        chain.Process(buf, 0, buf.Length);
        for (int i = 0; i < frames; i++)
            Assert.Equal(buf[i * 2], buf[i * 2 + 1], 5);
    }

    [Fact]
    public void UpdateBand_DuringProcessStress_StaysSafe()
    {
        var chain = new EqChain();
        chain.Configure(Fs, 2);
        chain.SetPreset(EqPreset.Flat(10));

        var rng = new Random(11);
        var buf = new float[4800];
        for (int round = 0; round < 200; round++)
        {
            // mutation concurrente simulée entre deux process
            chain.UpdateBand(rng.Next(10), new EqBand(FilterType.Peaking,
                20 + rng.NextDouble() * 15000, -10 + rng.NextDouble() * 20, 0.5 + rng.NextDouble() * 4));
            for (int i = 0; i < buf.Length; i++)
                buf[i] = (float)(rng.NextDouble() * 2 - 1);
            chain.Process(buf, 0, buf.Length);
            Assert.All(buf, s => Assert.True(float.IsFinite(s)));
        }
    }

    [Fact]
    public void UpdateBand_InvalidIndex_IsSilentNoOp()
    {
        var chain = new EqChain();
        chain.Configure(Fs, 2);
        chain.SetPreset(EqPreset.Flat(3));
        chain.UpdateBand(-1, new EqBand());   // no-op
        chain.UpdateBand(99, new EqBand());   // no-op
        Assert.Equal(3, chain.Preset.Bands.Count);
    }

    [Fact]
    public void PresetGetter_ReturnsDefensiveCopy()
    {
        var chain = new EqChain();
        chain.Configure(Fs, 2);
        chain.SetPreset(EqPreset.Flat(5));
        var p1 = chain.Preset;
        p1.Bands.Clear();
        Assert.Equal(5, chain.Preset.Bands.Count);
    }

    [Fact]
    public void ResponseCurve_IsLogSpacedAndBounded()
    {
        var chain = new EqChain();
        chain.Configure(Fs, 2);
        chain.SetPreset(EqPreset.Flat(5));
        var curve = chain.ResponseCurve(20, 20000, 64);
        Assert.Equal(64, curve.Count);
        Assert.Equal(20.0, curve[0].Freq, 6);
        Assert.Equal(20000.0, curve[^1].Freq, 3);
        for (int i = 1; i < curve.Count; i++)
            Assert.True(curve[i].Freq > curve[i - 1].Freq);
    }

    [Fact]
    public void Changed_FiresOnEveryMutation()
    {
        var chain = new EqChain();
        chain.Configure(Fs, 2);
        int fired = 0;
        chain.Changed += () => fired++;

        chain.SetPreset(EqPreset.Flat(2));
        chain.UpdateBand(0, new EqBand());
        chain.SetPreamp(2);
        chain.Bypass = true;
        chain.Bypass = true; // sans changement : pas de déclenchement
        Assert.Equal(4, fired);
    }

    [Fact]
    public void ImpulseThroughLoudPreset_StaysBounded()
    {
        var chain = new EqChain();
        chain.Configure(Fs, 1);
        var preset = EqPreset.Flat(10);
        foreach (var b in preset.Bands)
            b.GainDb = 6.0;
        chain.SetPreset(preset);

        var buf = new float[48000];
        var rng = new Random(42);
        for (int i = 0; i < buf.Length; i++)
            buf[i] = (float)(rng.NextDouble() * 2 - 1);
        chain.Process(buf, 0, buf.Length);

        double max = buf.Max(Math.Abs);
        Assert.True(max < 60.0, $"instabilité : amplitude max {max:0.0}");
    }
}
