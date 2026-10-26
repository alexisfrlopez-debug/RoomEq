using RoomEq.App.ViewModels;
using RoomEq.Audio.Playback;
using RoomEq.Core.Calibration;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using RoomEq.Core.Io;
using Xunit;

namespace RoomEq.App.Tests;

/// <summary>
/// Égaliseur principal (spec I-APP-04) : preset courant poussé dans la chaîne,
/// courbe recalculée, presets sauvegardés (magasin en répertoire de test),
/// import de calibration, préampli, interactions des poignées.
/// </summary>
public class EqViewModelTests : IDisposable
{
    private readonly string _dir;
    private readonly PresetStore _store;
    private readonly PlaybackEngine _engine = new();

    public EqViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "roomeq-eq-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _store = new PresetStore(_dir);
    }

    public void Dispose()
    {
        _engine.Dispose();
        try { Directory.Delete(_dir, true); } catch { /* temp */ }
    }

    private EqViewModel NewVm()
    {
        var vm = new EqViewModel(_store);
        vm.Init(_engine);
        return vm;
    }

    [Fact]
    public void Init_LoadsFlatPreset_AndPushesItToTheChain()
    {
        var vm = NewVm();

        Assert.Equal("Plat", vm.PresetName);
        Assert.Equal(10, vm.Bands.Count); // EqPreset.Flat() : 10 bandes
        Assert.NotEmpty(vm.CurrentCurve());
        Assert.Equal(360, vm.CurrentCurve().Count);
        Assert.Contains("10 bandes", vm.CurveInfo);
        Assert.Equal(vm.Bands.Count, _engine.Eq.Preset.Bands.Count);
    }

    [Fact]
    public void BandEdition_PushesToEngineAndRecomputesCurve()
    {
        var vm = NewVm();
        List<CurvePoint>? last = null;
        vm.CurveChanged += c => last = c;

        vm.Bands[0].GainDb = 6;
        Assert.Equal(6, _engine.Eq.Preset.Bands[0].GainDb, 3);
        Assert.NotNull(last);

        // poignée : fréquence + gain arrondis au dixième
        vm.OnBandDragged(1, 1234.56, -2.34);
        Assert.Equal(1234.6, vm.Bands[1].Frequency, 3);
        Assert.Equal(-2.3, vm.Bands[1].GainDb, 3);

        // molette : Q borné 0,1–20
        vm.OnBandQChanged(1, 100.0);
        Assert.Equal(20.0, vm.Bands[1].Q, 3);
        vm.OnBandQChanged(1, 0.0001);
        Assert.Equal(0.1, vm.Bands[1].Q, 3);

        // index hors bornes : sans effet, sans lever
        vm.OnBandDragged(-1, 100, 1);
        vm.OnBandQChanged(999, 2);
    }

    [Fact]
    public void AddRemoveFlatten_RoundTrip()
    {
        var vm = NewVm();

        vm.AddBandCommand.Execute(null);
        Assert.Equal(11, vm.Bands.Count);
        Assert.Equal(11, _engine.Eq.Preset.Bands.Count);
        Assert.Equal(vm.Bands.Count - 1, vm.SelectedIndex);

        var band = vm.Bands[3];
        vm.RemoveBandCommand.Execute(band);
        Assert.Equal(10, vm.Bands.Count);

        vm.Bands[0].GainDb = 5;
        vm.FlattenCommand.Execute(null);
        Assert.All(_engine.Eq.Preset.Bands, b => Assert.Equal(0, b.GainDb, 6));
        Assert.Equal(0, vm.PreampDb, 6); // Flatten remet aussi le préampli
    }

    [Fact]
    public void Presets_SaveLoadDelete_RoundTrip()
    {
        var vm = NewVm();

        vm.Bands[0].GainDb = 4;
        vm.PresetName = "Mon preset";
        vm.SavePresetCommand.Execute(null);
        Assert.Equal("Mon preset", vm.SelectedSavedPreset);
        Assert.Contains("Mon preset", _store.List());

        // un nouveau VM charge le preset sauvegardé
        var vm2 = NewVm();
        vm2.SelectedSavedPreset = "Mon preset";
        vm2.LoadSelectedPresetCommand.Execute(null);
        Assert.Equal("Mon preset", vm2.PresetName);
        Assert.Equal(4, vm2.Bands[0].GainDb, 3);

        vm2.DeleteSelectedPresetCommand.Execute(null);
        Assert.DoesNotContain("Mon preset", _store.List());
        Assert.Contains("supprimé", vm2.PresetStatus);
    }

    [Fact]
    public void Presets_Rename_RefusesDuplicatesAndUpdatesCurrentName()
    {
        var vm = NewVm();
        vm.PresetName = "Alpha";
        vm.SavePresetCommand.Execute(null);
        vm.PresetName = "Beta";
        vm.SavePresetCommand.Execute(null);

        vm.SelectedSavedPreset = "Alpha";
        vm.NewPresetName = "Beta";
        vm.RenamePresetCommand.Execute(null);
        Assert.Contains("existe déjà", vm.PresetStatus);
        Assert.Contains("Alpha", _store.List());

        vm.NewPresetName = "Gamma";
        vm.RenamePresetCommand.Execute(null);
        Assert.DoesNotContain("Alpha", _store.List());
        Assert.Contains("Gamma", _store.List());
        Assert.Equal("Gamma", vm.SelectedSavedPreset);

        // le preset courant renommé voit son nom affiché suivre
        vm.PresetName = "Gamma";
        vm.NewPresetName = "Delta";
        vm.RenamePresetCommand.Execute(null);
        Assert.Equal("Delta", vm.PresetName);
    }

    [Fact]
    public void Presamps_ApplyImmediatelyToTheChain()
    {
        var vm = NewVm();
        vm.PreampDb = -4.5;
        Assert.Equal(-4.5, _engine.Eq.Preset.PreampDb, 3);
    }

    [Fact]
    public void SavePreset_EmptyName_FallsBackToSansNom()
    {
        var vm = NewVm();
        vm.PresetName = "   ";
        vm.SavePresetCommand.Execute(null);
        Assert.Contains("Sans nom", _store.List());
    }

    [Fact]
    public void CalibrationImport_WithoutCurve_ExplainsItself()
    {
        var vm = NewVm();
        vm.SetCalibrationSource(null);
        Assert.False(vm.HasCalibrationCurve);

        vm.ImportCalibrationCurveCommand.Execute(null);
        Assert.Contains("Aucune courbe", vm.ImportStatus);
        Assert.Equal(10, vm.Bands.Count); // rien n'a changé
    }

    [Fact]
    public void CalibrationImport_WithCurve_RegeneratesBands()
    {
        var vm = NewVm();

        // courbe de mesure factice : bosse de +6 dB vers 100 Hz, plate ailleurs
        var measured = new List<CurvePoint>();
        for (int i = 0; i <= 200; i++)
        {
            double f = 20 * Math.Pow(1000.0, i / 200.0); // 20 Hz → 20 kHz log
            double db = 6.0 * Math.Exp(-Math.Pow(Math.Log10(f / 100.0), 2) / 0.08);
            measured.Add(new CurvePoint(f, db));
        }
        var result = new CorrectionResult
        {
            MeasuredCurve = measured,
            TargetCurvePts = measured.Select(p => new CurvePoint(p.Freq, 0)).ToList(),
            PredictedCurve = new List<CurvePoint>(),
            Preset = new EqPreset("Import"),
        };

        vm.SetCalibrationSource(result);
        Assert.True(vm.HasCalibrationCurve);

        vm.ImportCalibrationCurveCommand.Execute(null);
        Assert.Contains("Courbe importée", vm.ImportStatus);
        Assert.Equal(vm.SelectedPrecision.Name, CorrectionPrecisionCatalog.All[1].Name); // Standard par défaut
    }

    [Fact]
    public void LinkPreset_WithoutNamedPreset_ExplainsItself()
    {
        var vm = NewVm();
        string? requested = null;
        vm.LinkPresetRequested += n => requested = n;

        vm.LinkPresetToOutputCommand.Execute(null); // « Personnalisé »
        Assert.Null(requested);
        Assert.Contains("Appliquez d'abord un preset nommé", vm.LinkPresetStatus);

        vm.PresetName = "Mon preset nommé";
        vm.LinkPresetToOutputCommand.Execute(null);
        Assert.Equal("Mon preset nommé", requested);
    }
}
