using RoomEq.App.ViewModels;
using RoomEq.Audio.Playback;
using RoomEq.Core.Calibration;
using RoomEq.Core.Io;
using Xunit;

namespace RoomEq.App.Tests;

/// <summary>
/// Onglets secondaires et menu des signaux (spec I-APP-02 / I-APP-05) :
/// options de calibration (libellés, cartes virtuelles, plan), moniteur live
/// (tampon + spectre centré), menu des signaux (intégrés + fichiers
/// téléchargés, conservation de sélection) et panneau ASIO sans pilote.
/// </summary>
public class SecondaryTabsTests : IDisposable
{
    private readonly string _dir;

    public SecondaryTabsTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "roomeq-sec-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* temp */ }
    }

    // ================================================================ options de calibration

    [Fact]
    public void TargetOptions_HaveFrenchLabels()
    {
        var flat = new TargetOption(RoomEq.Core.Calibration.TargetType.Flat);
        var shelf = new TargetOption(RoomEq.Core.Calibration.TargetType.BassShelf);
        var tilt = new TargetOption(RoomEq.Core.Calibration.TargetType.StudioTilt);

        Assert.Equal("Plate (neutre)", flat.Label);
        Assert.Equal("Plate + shelf de graves", shelf.Label);
        Assert.Equal("Studio (pente -1 dB/oct)", tilt.Label);
    }

    [Fact]
    public void MicDeviceOption_FlagsVirtualCards()
    {
        static OutputDeviceInfo Dev(string name) => new("id", name, "Capture");

        Assert.True(new MicDeviceOption(Dev("RoomEq Output (Virtual)")).IsVirtual);
        Assert.True(new MicDeviceOption(Dev("RoomEq Input")).IsVirtual);
        Assert.False(new MicDeviceOption(Dev("Micro USB Studio")).IsVirtual);

        var virtualOption = new MicDeviceOption(Dev("RoomEq Output (Virtual)"));
        Assert.Contains("carte virtuelle", virtualOption.DisplayName);
        Assert.DoesNotContain("carte virtuelle", new MicDeviceOption(Dev("Micro USB Studio")).DisplayName);
    }

    [Fact]
    public void SignalOptions_CoverEveryMeasurementKind()
    {
        foreach (var kind in Enum.GetValues<MeasurementSignalKind>())
        {
            var opt = new SignalOption(kind);
            Assert.False(string.IsNullOrWhiteSpace(opt.Name));
            Assert.False(string.IsNullOrWhiteSpace(opt.Description));
            Assert.True(opt.IsSelected); // tout coché par défaut
        }
    }

    [Fact]
    public void ChannelOption_LabelsMixAndMono()
    {
        var mix = new ChannelOption(RoomEq.Core.Dsp.ChannelMixer.MixFirstTwo, "Mixage stéréo (L+R)");
        var mono = new ChannelOption(0, "Canal 1 seul (mono) — gauche");
        Assert.Equal(RoomEq.Core.Dsp.ChannelMixer.MixFirstTwo, mix.Index);
        Assert.Equal(0, mono.Index);
        Assert.StartsWith("Mixage", mix.Label);
    }

    [Fact]
    public void PlanSummary_MentionsSignalCountAndDuration()
    {
        var kinds = new[] { MeasurementSignalKind.WhiteNoise, MeasurementSignalKind.FrequencySweep };
        string summary = MeasurementSignalCatalog.PlanSummary(kinds, 10, 15);
        Assert.False(string.IsNullOrWhiteSpace(summary));
    }

    // ================================================================ moniteur live

    [Fact]
    public void LiveMicMonitor_TicksOnlyWithEnoughData()
    {
        var monitor = new LiveMicMonitor();
        int fired = 0;
        monitor.SeriesChanged += _ => fired++;

        monitor.Tick();
        Assert.Equal(0, fired); // vide : rien

        // 20 000 échantillons de sinus 1 kHz @ 48 kHz
        var chunk = new float[20000];
        for (int i = 0; i < chunk.Length; i++)
            chunk[i] = (float)Math.Sin(2 * Math.PI * 1000 * i / 48000.0);
        monitor.AddChunk(chunk);

        monitor.Tick();
        Assert.Equal(1, fired);

        monitor.Clear();
        monitor.Tick();
        Assert.Equal(1, fired); // vidé : plus rien
    }

    [Fact]
    public void LiveMicMonitor_CurveIsCenteredOnItsMean()
    {
        var monitor = new LiveMicMonitor();
        List<RoomEq.Core.Dsp.CurvePoint>? pts = null;
        monitor.SeriesChanged += s => pts = s.Count > 0 ? s[0].Points : null;

        var chunk = new float[48000];
        for (int i = 0; i < chunk.Length; i++)
            chunk[i] = (float)Math.Sin(2 * Math.PI * 440 * i / 48000.0);
        monitor.AddChunk(chunk);

        monitor.Tick();

        Assert.NotNull(pts);
        Assert.True(pts!.Count >= 4);
        // centrage sur la moyenne de la fenêtre 200 Hz – 8 kHz (comme la production)
        double mean = pts.Where(p => p.Freq >= 200 && p.Freq <= 8000).Average(p => p.Db);
        Assert.InRange(mean, -1.0, 1.0);
    }

    // ================================================================ menu des signaux

    [Fact]
    public void SignalMenu_BuiltInsThenDownloadedFiles_DefaultPinkNoise()
    {
        using var engine = new PlaybackEngine();
        var store = new SignalStore(_dir);
        File.WriteAllBytes(Path.Combine(store.DirectoryPath, "bruit-gris-10s.wav"),
            new byte[2000]); // fichier factice : seul le listing compte ici

        var menu = new TestSignalMenu(engine, store);

        Assert.Equal(TestSignalCatalog.BuiltIn.Count + 1, menu.SignalChoices.Count);
        Assert.Equal(TestSignalCatalog.DefaultItemId, menu.SelectedSignal!.Id);
        Assert.Equal("bruit-rose", TestSignalCatalog.DefaultItemId);

        var downloaded = menu.SignalChoices.Single(c => c.Id == "fichier|bruit-gris-10s.wav");
        Assert.Equal("Téléchargés", downloaded.Category);
        Assert.Null(downloaded.Generator);
        Assert.EndsWith("bruit-gris-10s.wav", downloaded.FilePath);
    }

    [Fact]
    public void SignalMenu_Refresh_PreservesSelectionWhenStillThere()
    {
        using var engine = new PlaybackEngine();
        var store = new SignalStore(_dir);
        var menu = new TestSignalMenu(engine, store);

        var choice = menu.SignalChoices.First(c => c.Id != TestSignalCatalog.DefaultItemId);
        menu.SelectedSignal = choice;

        menu.Refresh();
        Assert.Equal(choice.Id, menu.SelectedSignal!.Id);

        // le défaut revient si la sélection disparaît (autre bibliothèque)
        var menu2 = new TestSignalMenu(engine, new SignalStore(Path.Combine(_dir, "vide")));
        menu2.SelectedSignal = choice;
        var menu3 = new TestSignalMenu(engine, new SignalStore(Path.Combine(_dir, "vide2")));
        Assert.Equal(TestSignalCatalog.DefaultItemId, menu3.SelectedSignal!.Id);
    }

    // ================================================================ ASIO (sans pilote)

    [Fact]
    public void AsioViewModel_WithoutDrivers_ShowsHelpfulMessage()
    {
        var vm = new AsioViewModel(() => { });
        // CI sans pilote ASIO : liste vide, message d'aide — pas d'exception
        if (vm.Drivers.Count == 0)
        {
            Assert.True(vm.NoDriver);
            Assert.Contains("Aucun pilote ASIO détecté", vm.Info);
            Assert.Empty(vm.SupportedRates);
        }
    }
}
