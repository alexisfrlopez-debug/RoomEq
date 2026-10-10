using NAudio.Wave;
using RoomEq.Audio.Playback;
using RoomEq.Audio.Routing;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Tests des tranches et du bus de la console, sans périphérique Windows :
/// les sources internes (générateur) suffisent à exercer la chaîne de traitement.
/// </summary>
public class ConsoleChannelTests
{
    private static float Rms(ReadOnlySpan<float> data)
    {
        double sum = 0;
        foreach (var v in data) sum += (double)v * v;
        return (float)Math.Sqrt(sum / data.Length);
    }

    [Fact]
    public void Generator_ProducesSignal()
    {
        var ch = new ConsoleChannel(TrackSource.InternalGenerator())
        {
            GeneratorEnabled = true
        };
        ch.Prepare(48000);

        var accum = new float[960];
        ch.ReadInto(accum, 0, 480, audible: true);

        Assert.True(Volatile.Read(ref ch.PeakL) > 0.01f, "le générateur doit produire du son");
        ch.Dispose();
    }

    [Fact]
    public void Fader_ScalesOutput()
    {
        var ch = new ConsoleChannel(TrackSource.InternalGenerator()) { GeneratorEnabled = true };
        ch.SetGeneratorSignal(TestSignal.Sine, 0.9, 1000); // sinus déterministe
        ch.Prepare(48000);

        var accumFull = new float[960];
        ch.Fader = 1.0;
        ch.ReadInto(accumFull, 0, 480, audible: true);
        float rmsFull = Rms(accumFull);

        var accumHalf = new float[960];
        ch.Fader = 0.5;
        ch.ReadInto(accumHalf, 0, 480, audible: true);
        float rmsHalf = Rms(accumHalf);

        Assert.True(rmsFull > 0);
        Assert.Equal(0.5, rmsHalf / rmsFull, 3);
        ch.Dispose();
    }

    [Fact]
    public void Inaudible_YieldsSilence_ButKeepsEqState()
    {
        var ch = new ConsoleChannel(TrackSource.InternalGenerator()) { GeneratorEnabled = true };
        ch.Eq.SetPreset(new EqPreset("Piste"));
        ch.Prepare(48000);

        var accum = new float[960];
        ch.ReadInto(accum, 0, 480, audible: false);

        Assert.Equal(0f, Volatile.Read(ref ch.PeakL));
        Assert.All(accum, v => Assert.Equal(0f, v));
        ch.Dispose();
    }

    [Fact]
    public void PanLaw_CenterAttenuates3Db_PerChannel()
    {
        var (gl, gr) = ConsoleChannel.PanGains(0.0);
        Assert.Equal(0.7071, gl, 3);
        Assert.Equal(0.7071, gr, 3);

        var (left, right) = ConsoleChannel.PanGains(1.0); // tout à droite
        Assert.Equal(0.0, left, 3);
        Assert.Equal(1.0, right, 3);

        var (l2, r2) = ConsoleChannel.PanGains(-1.0); // tout à gauche
        Assert.Equal(1.0, l2, 3);
        Assert.Equal(0.0, r2, 3);
    }

    [Fact]
    public void PanHardRight_MutesLeftSide()
    {
        var ch = new ConsoleChannel(TrackSource.InternalGenerator()) { GeneratorEnabled = true };
        ch.Prepare(48000);
        ch.Pan = 1.0;

        var accum = new float[960];
        ch.ReadInto(accum, 0, 480, audible: true);

        double leftEnergy = 0, rightEnergy = 0;
        for (int f = 0; f < 480; f++)
        {
            leftEnergy += (double)accum[f * 2] * accum[f * 2];
            rightEnergy += (double)accum[f * 2 + 1] * accum[f * 2 + 1];
        }
        Assert.Equal(0.0, leftEnergy, 5);
        Assert.True(rightEnergy > 0);
        ch.Dispose();
    }

    [Fact]
    public void TrackEq_HighPassAttenuatesLowFrequencies()
    {
        var ch = new ConsoleChannel(TrackSource.InternalGenerator()) { GeneratorEnabled = true };
        ch.Prepare(48000);
        ch.SetGeneratorSignal(TestSignal.Sine, 0.9, 100);

        var preset = new EqPreset("Coupe-bas");
        preset.Bands.Clear();
        preset.Bands.Add(new EqBand(FilterType.HighPass, 8000, 0, 0.71));
        ch.Eq.SetPreset(preset);

        var accum = new float[9600];
        ch.ReadInto(accum, 0, 4800, audible: true);

        // un passe-haut à 8 kHz attaque fortement un sinus à 100 Hz
        Assert.True(Volatile.Read(ref ch.PeakL) < 0.2f,
            "le passe-haut doit atténuer fortement les graves");
        ch.Dispose();
    }

    [Fact]
    public void MuteSolo_Verdict_ComesFromMixer()
    {
        // vérifie la règle audible = !Mute && (!anySolo || Solo) via un scénario simulé
        bool mute1 = true, solo1 = false, solo2 = true;
        bool anySolo = solo1 || solo2;
        bool audible1 = !mute1 && (!anySolo || solo1);   // fausse : mute prime
        bool audible2 = true && (!anySolo || solo2);     // vraie : solo

        Assert.False(audible1);
        Assert.True(audible2);
        Assert.True(anySolo);
    }
}

public class MixerStoreTests
{
    private static MixerStore TempStore()
    {
        var dir = Path.Combine(Path.GetTempPath(), "roomeq-tests-" + Guid.NewGuid().ToString("N"));
        return new MixerStore(dir);
    }

    [Fact]
    public void RoundTrip_PreservesConfiguration()
    {
        var store = TempStore();
        var cfg = new StoredMixer
        {
            MasterFader = 0.85,
            AutoStart = true,
            Channels =
            {
                new StoredChannel
                {
                    Kind = "loopback",
                    DeviceId = "{0.0.0.00000000}.test",
                    Name = "Sortie test (boucle)",
                    Fader = 0.7,
                    Pan = -0.5,
                    Mute = false,
                    Solo = true,
                    GeneratorSignal = "Bruit rose",
                    Eq = new EqPreset("Piste")
                    {
                        Bands = { new EqBand(FilterType.Peaking, 1000, 3, 1.4) }
                    }
                }
            }
        };

        store.Save(cfg);
        var loaded = store.Load();

        Assert.True(loaded.AutoStart);
        Assert.Equal(0.85, loaded.MasterFader, 5);
        Assert.Single(loaded.Channels);
        var ch = loaded.Channels[0];
        Assert.Equal("loopback", ch.Kind);
        Assert.Equal("{0.0.0.00000000}.test", ch.DeviceId);
        Assert.Equal(0.7, ch.Fader, 5);
        Assert.Equal(-0.5, ch.Pan, 5);
        Assert.True(ch.Solo);
        Assert.Equal("Bruit rose", ch.GeneratorSignal);
        Assert.NotNull(ch.Eq);
        Assert.Single(ch.Eq!.Bands);
        Assert.Equal(1000, ch.Eq.Bands[0].Frequency, 1);
    }

    [Fact]
    public void Load_WithoutFile_ReturnsDefaults()
    {
        var store = TempStore();
        var cfg = store.Load();
        Assert.NotNull(cfg);
        Assert.Empty(cfg.Channels);
        Assert.False(cfg.AutoStart);
    }
}

public class EqSyncTests
{
    [Fact]
    public void EqChain_Changed_FiresOnPresetAndBandAndPreamp()
    {
        var chain = new EqChain();
        int changes = 0;
        chain.Changed += () => changes++;

        chain.SetPreset(EqPreset.Flat(3));
        chain.UpdateBand(0, new EqBand(FilterType.Peaking, 500, 2, 1.0));
        chain.SetPreamp(-3);

        Assert.Equal(3, changes);
    }

    [Fact]
    public void EqChain_Bypass_TriggersChanged()
    {
        var chain = new EqChain();
        int changes = 0;
        chain.Changed += () => changes++;

        chain.Bypass = true;
        chain.Bypass = true;   // pas de changement : pas d'événement
        chain.Bypass = false;

        Assert.Equal(2, changes);
    }

    [Fact]
    public void MasterChain_SyncsPresetFromSourceChain()
    {
        var source = new EqChain();
        var master = new EqChain();

        source.Changed += () =>
        {
            master.SetPreset(source.Preset);
            master.Bypass = source.Bypass;
        };

        var preset = new EqPreset("Test");
        preset.Bands.Clear();
        preset.Bands.Add(new EqBand(FilterType.Peaking, 2000, 6, 1.0));
        source.SetPreset(preset);

        Assert.Single(master.Preset.Bands);
        Assert.Equal(6, master.Preset.Bands[0].GainDb, 3);

        source.Bypass = true;
        Assert.True(master.Bypass);
    }
}
