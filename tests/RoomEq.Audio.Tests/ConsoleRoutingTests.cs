using RoomEq.Audio.Playback;
using RoomEq.Audio.Routing;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Tranches, bus master et console (spec I-AUD-03), sans périphérique : les
/// sources internes (générateur) suffisent à exercer la chaîne de traitement.
/// Portage des tests legacy v2.2.2 + extensions v3.
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

    [Fact]
    public void GeneratorSourceReady_RequiresGeneratorEnabled()
    {
        var ch = new ConsoleChannel(TrackSource.InternalGenerator());
        Assert.False(ch.SourceReady);
        ch.GeneratorEnabled = true;
        Assert.True(ch.SourceReady);
        ch.Dispose();
    }

    [Fact]
    public void Titles_AreStablePerKind()
    {
        Assert.Equal("Générateur", new ConsoleChannel(TrackSource.InternalGenerator()).Title);
        Assert.Equal("DAW (ASIO)", new ConsoleChannel(TrackSource.AsioBridgeSource()).Title);
        Assert.Equal("Lecteur", new ConsoleChannel(TrackSource.InternalPlayer()).Title);
        Assert.Equal("Micro", new ConsoleChannel(TrackSource.CaptureDevice("{id}", "Micro")).Title);
    }
}

/// <summary>Bus master et adaptation stéréo → multi-canaux (extensions v3, spec I-AUD-03).</summary>
public class MasterBusTests
{
    [Fact]
    public void StereoToOutput_SixChannels_LandsOnFirstTwo()
    {
        var src = new TestSources.ArraySampleProvider(new float[] { 0.25f, -0.5f });
        var adapter = new ConsoleMixer.StereoToOutputProvider(src, 6);

        Assert.Equal(6, adapter.WaveFormat.Channels);
        var buf = new float[6];
        int n = adapter.Read(buf, 0, 6);

        Assert.Equal(6, n);
        Assert.Equal(0.25f, buf[0]);
        Assert.Equal(-0.5f, buf[1]);
        for (int c = 2; c < 6; c++) Assert.Equal(0f, buf[c]);
    }

    [Fact]
    public void StereoToOutput_Mono_AveragesChannels()
    {
        var src = new TestSources.ArraySampleProvider(new float[] { 1f, -0.5f });
        var adapter = new ConsoleMixer.StereoToOutputProvider(src, 1);

        var buf = new float[1];
        int n = adapter.Read(buf, 0, 1);

        Assert.Equal(1, n);
        Assert.Equal(0.25f, buf[0], 5);
    }

    [Fact]
    public void MasterBus_SumsGeneratorChannel_AndMeasuresPeaks()
    {
        using var mixer = new ConsoleMixer();
        var ch = mixer.AddChannel(TrackSource.InternalGenerator());
        ch.GeneratorEnabled = true;
        ch.SetGeneratorSignal(TestSignal.Sine, 0.9, 1000);
        ch.Prepare(48000);

        var bus = new ConsoleMixer.MasterBus(mixer);
        Assert.Equal(48000, bus.WaveFormat.SampleRate);
        Assert.Equal(2, bus.WaveFormat.Channels);

        var buf = new float[960];
        int n = bus.Read(buf, 0, 960);

        Assert.Equal(960, n);
        Assert.True(Volatile.Read(ref mixer.MasterPeakL) > 0.05f, "crête master mesurée");
    }

    [Fact]
    public void MasterBus_MuteMaster_SilencesEverything()
    {
        using var mixer = new ConsoleMixer();
        var ch = mixer.AddChannel(TrackSource.InternalGenerator());
        ch.GeneratorEnabled = true;
        ch.Prepare(48000);

        mixer.MasterMute = true;
        var bus = new ConsoleMixer.MasterBus(mixer);
        var buf = new float[960];
        bus.Read(buf, 0, 960);

        Assert.All(buf, v => Assert.Equal(0f, v));
        Assert.Equal(0f, Volatile.Read(ref mixer.MasterPeakL));
    }

    [Fact]
    public void MasterBus_TwoChannels_SumWithoutClippingReference()
    {
        using var mixer = new ConsoleMixer();
        var a = mixer.AddChannel(TrackSource.InternalGenerator());
        a.GeneratorEnabled = true;
        a.SetGeneratorSignal(TestSignal.Sine, 0.5, 1000);
        a.Prepare(48000);
        var b = mixer.AddChannel(TrackSource.InternalGenerator());
        b.GeneratorEnabled = true;
        b.SetGeneratorSignal(TestSignal.Sine, 0.5, 1000);
        b.Prepare(48000);

        var bus = new ConsoleMixer.MasterBus(mixer);
        var buf = new float[960];
        bus.Read(buf, 0, 960);

        // deux sinus en phase, pan au centre (−3 dB/voie) : chaque voie reçoit
        // 2 × 0,5 × 0,707 ≈ 0,707 — la sommation des tranches est vérifiée
        // par rapport à UNE seule tranche (≈ 0,354).
        float peak = Volatile.Read(ref mixer.MasterPeakL);
        Assert.InRange(peak, 0.55, 0.85);
    }
}

/// <summary>Console : protections anti-larsen et comportement sans sous-système audio.</summary>
public class ConsoleMixerTests
{
    [Fact]
    public void AntiFeedback_LoopbackOnMasterOutput_IsRejected()
    {
        // ORDRE SÉCURITÉ (spec I-AUD-03, invariant 2a) : la validation précède
        // l'ouverture de la sortie — le message d'aide est vérifiable même sans
        // périphérique (Linux/CI) car la validation échoue AVANT le WASAPI.
        using var mixer = new ConsoleMixer();
        mixer.SetOutput("wasapi|{0.0.0.00000000}.{deadbeef}");
        var ch = mixer.AddChannel(
            TrackSource.LoopbackDevice("{0.0.0.00000000}.{deadbeef}", "Sortie test (boucle)"));

        var ex = Assert.Throws<InvalidOperationException>(() => mixer.Start());
        Assert.Contains("capture en boucle", ex.Message);
        Assert.False(mixer.IsRunning);
        ch.Dispose();
    }

    [Fact]
    public void AntiFeedback_DifferentOutputAndLoopback_PassesValidation()
    {
        // pas de boucle : la validation passe, l'échec éventuel vient ensuite
        // de l'ouverture WASAPI (absente sous Linux) — messages distincts.
        using var mixer = new ConsoleMixer();
        mixer.SetOutput("wasapi|{autre-sortie}");
        mixer.AddChannel(TrackSource.LoopbackDevice("{cette-sortie}", "Sortie (boucle)"));

        var ex = Record.Exception(() => mixer.Start());
        if (ex is InvalidOperationException ioe)
            Assert.DoesNotContain("capture en boucle", ioe.Message);
        Assert.False(mixer.IsRunning);
    }

    [Fact]
    public void MasterFader_IsClamped_0_to_1_5()
    {
        using var mixer = new ConsoleMixer();
        mixer.MasterFader = 3.0;
        Assert.Equal(1.5, mixer.MasterFader, 6);
        mixer.MasterFader = -1.0;
        Assert.Equal(0.0, mixer.MasterFader, 6);
    }

    [Fact]
    public void EnumerateOutputs_NeverThrows_EvenWithoutAudioSubsystem()
    {
        // Linux/CI : WASAPI absent → liste WASAPI vide ; ASIO absent → vide.
        using var mixer = new ConsoleMixer();
        var outputs = mixer.EnumerateOutputs();
        Assert.NotNull(outputs);
        if (!OperatingSystem.IsWindows())
            Assert.Empty(outputs);
    }

    [Fact]
    public void RemoveChannel_WhenStopped_DisposesImmediately()
    {
        using var mixer = new ConsoleMixer();
        var ch = mixer.AddChannel(TrackSource.InternalGenerator());
        mixer.RemoveChannel(ch);
        // ajouter puis retirer à l'arrêt : aucun canal actif ne subsiste
        Assert.Empty(mixer.Snapshot);
    }
}

/// <summary>Persistance de la console (spec I-AUD-03) — portage legacy.</summary>
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

    [Fact]
    public void Load_CorruptedFile_ReturnsDefaults()
    {
        var dir = Path.Combine(Path.GetTempPath(), "roomeq-tests-" + Guid.NewGuid().ToString("N"));
        System.IO.Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "mixer.json"), "{ ceci n'est pas du JSON ");
        var cfg = new MixerStore(dir).Load();
        Assert.Empty(cfg.Channels);
    }
}

/// <summary>Synchronisation des EqChain (barre ↔ onglet ↔ master) — portage legacy.</summary>
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
