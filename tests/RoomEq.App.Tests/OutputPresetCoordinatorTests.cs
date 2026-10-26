using RoomEq.App.ViewModels;
using RoomEq.Audio.Playback;
using RoomEq.Core.Eq;
using RoomEq.Core.Headphones;
using RoomEq.Core.Io;
using Xunit;

namespace RoomEq.App.Tests;

/// <summary>
/// Politique du preset adapté à la sortie master (spec I-APP-01) :
/// priorités (manuel > calibration > usine reconnue > rien), mémorisation
/// auto/none/user/factory, preset lié introuvable, liaison calibration.
/// Le coordinateur est testé via des vues factices : aucune dépendance
/// aux périphériques ni au dispatcher.
/// </summary>
public class OutputPresetCoordinatorTests : IDisposable
{
    private readonly string _dir;
    private readonly DevicePresetStore _devicePresets;
    private readonly PresetStore _presets;
    private readonly FakeEq _eq = new();
    private readonly FakeMixer _mixer = new();
    private readonly OutputPresetCoordinator _coordinator;

    private sealed class FakeEq : IOutputPresetEqView
    {
        public List<string> LoadedPresets { get; } = new();
        public string LinkTargetText { get; set; } = "";
        public string LinkPresetStatus { get; set; } = "";

        public void LoadPreset(EqPreset preset) => LoadedPresets.Add(preset.Name);
    }

    private sealed class FakeMixer : IOutputPresetMixerView
    {
        public OutputDeviceInfo? SelectedOutput { get; set; }
        public bool AutoOutputPreset { get; set; } = true;
        public List<(IReadOnlyList<OutputPresetOption>, OutputPresetOption?)> OptionsSet { get; } = new();
        public string OutputPresetText { get; set; } = "";
        public OutputDeviceInfo? LastDeviceForOptions { get; private set; }

        public void SetOutputPresetOptions(IEnumerable<OutputPresetOption> options, OutputPresetOption? selected)
            => OptionsSet.Add((options.ToList(), selected));
    }

    public OutputPresetCoordinatorTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "roomeq-app-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _devicePresets = new DevicePresetStore(_dir);
        _presets = new PresetStore(Path.Combine(_dir, "presets"));
        _coordinator = new OutputPresetCoordinator(_eq, _mixer, _devicePresets, _presets);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* temp */ }
    }

    private static OutputDeviceInfo Device(string id, string name) => new(id, name, "Render");

    private OutputPresetOption? LastSelected => _mixer.OptionsSet.Count > 0
        ? _mixer.OptionsSet[^1].Item2
        : null;

    private IReadOnlyList<OutputPresetOption> LastOptions => _mixer.OptionsSet.Count > 0
        ? _mixer.OptionsSet[^1].Item1
        : Array.Empty<OutputPresetOption>();

    // ================================================================ Evaluate

    [Fact]
    public void Evaluate_UnknownDeviceNoBinding_AppliesNothingButRebuildsOptions()
    {
        _mixer.SelectedOutput = Device("dev1", "Enceintes génériques");
        _coordinator.Evaluate(_mixer.SelectedOutput);

        Assert.Empty(_eq.LoadedPresets);
        Assert.Contains("aucun preset connu", _mixer.OutputPresetText);
        Assert.Equal(2, LastOptions.Count); // Auto + Aucun seulement
        Assert.Equal("auto", LastSelected!.Key);
        Assert.Contains("Enceintes génériques", _eq.LinkTargetText);
    }

    [Fact]
    public void Evaluate_RecognizedHeadphone_AppliesFactoryPreset()
    {
        // un nom de périphérique contenant un modèle connu de la bibliothèque
        var model = HeadphonePresetLibrary.LoadAll().First();
        _mixer.SelectedOutput = Device("dev1", "Casque (" + model.DisplayName + " Stereo)");
        _coordinator.Evaluate(_mixer.SelectedOutput);

        Assert.Single(_eq.LoadedPresets);
        Assert.Contains("reconnu", _mixer.OutputPresetText);
        // la suggestion usine apparaît dans le sélecteur
        Assert.Contains(LastOptions, o => o.Key == "factory:" + model.Id);
    }

    [Fact]
    public void Evaluate_ManualUserBinding_BeatsFactoryRecognition()
    {
        var preset = new EqPreset("Ma calibration") { PreampDb = -2 };
        preset.Bands.Add(new EqBand(RoomEq.Core.Dsp.FilterType.Peaking, 100, 2, 1.0));
        _presets.Save(preset);
        _devicePresets.Set(new DevicePresetBinding
        {
            DeviceId = "dev1",
            DeviceName = "Casque reconnu",
            PresetClass = "user",
            PresetRef = "Ma calibration",
            Kind = DevicePresetKind.Manual,
        });

        _mixer.SelectedOutput = Device("dev1", "Casque (JBL n'importe quoi)");
        _coordinator.Evaluate(_mixer.SelectedOutput);

        Assert.Single(_eq.LoadedPresets);
        Assert.Equal("Ma calibration", _eq.LoadedPresets[0]);
        Assert.Contains("preset manuel mémorisé", _mixer.OutputPresetText);
        Assert.Equal("user:Ma calibration", LastSelected!.Key);
    }

    [Fact]
    public void Evaluate_CalibrationBinding_LabelledAsCalibration()
    {
        _presets.Save(new EqPreset("Calibration 20260101"));
        _devicePresets.Set(new DevicePresetBinding
        {
            DeviceId = "dev1",
            PresetClass = "user",
            PresetRef = "Calibration 20260101",
            Kind = DevicePresetKind.Calibration,
        });

        _mixer.SelectedOutput = Device("dev1", "Enceintes");
        _coordinator.Evaluate(_mixer.SelectedOutput);
        Assert.Contains("preset de calibration mesuré", _mixer.OutputPresetText);
    }

    [Fact]
    public void Evaluate_NoneBinding_LoadsNothing()
    {
        _devicePresets.Set(new DevicePresetBinding
        {
            DeviceId = "dev1",
            Kind = DevicePresetKind.None,
        });
        _mixer.SelectedOutput = Device("dev1", "Enceintes");
        _coordinator.Evaluate(_mixer.SelectedOutput);

        Assert.Empty(_eq.LoadedPresets);
        Assert.Contains("Aucun preset", _mixer.OutputPresetText);
        Assert.Equal("none", LastSelected!.Key);
    }

    [Fact]
    public void Evaluate_AutoDisabled_ByMixerOption()
    {
        _mixer.AutoOutputPreset = false;
        _mixer.SelectedOutput = Device("dev1", "Enceintes");
        _coordinator.Evaluate(_mixer.SelectedOutput);

        Assert.Empty(_eq.LoadedPresets);
        Assert.Contains("désactivé", _mixer.OutputPresetText);
    }

    [Fact]
    public void Evaluate_MissingUserPreset_FallsBackToAuto()
    {
        _devicePresets.Set(new DevicePresetBinding
        {
            DeviceId = "dev1",
            PresetClass = "user",
            PresetRef = "Preset disparu",
            Kind = DevicePresetKind.Manual,
        });
        _mixer.SelectedOutput = Device("dev1", "Enceintes");
        _coordinator.Evaluate(_mixer.SelectedOutput);

        Assert.Empty(_eq.LoadedPresets);
        Assert.Contains("supprimé", _mixer.OutputPresetText);
        Assert.Null(_devicePresets.Get("dev1")); // liaison retirée
        Assert.Equal("auto", LastSelected!.Key);
    }

    // ================================================================ Pick

    [Fact]
    public void Pick_None_IsMemorized()
    {
        _mixer.SelectedOutput = Device("dev1", "Enceintes");
        _coordinator.Pick(new OutputPresetOption("none", "Aucun", "Mode"));

        Assert.Equal(DevicePresetKind.None, _devicePresets.Get("dev1")!.Kind);
        Assert.Equal("none", LastSelected!.Key);
    }

    [Fact]
    public void Pick_UserPreset_IsMemorizedAndApplied()
    {
        _presets.Save(new EqPreset("Mon preset"));
        _mixer.SelectedOutput = Device("dev1", "Enceintes");

        _coordinator.Pick(new OutputPresetOption("user:Mon preset", "Mon preset", "Vos presets"));

        var binding = _devicePresets.Get("dev1")!;
        Assert.Equal("user", binding.PresetClass);
        Assert.Equal("Mon preset", binding.PresetRef);
        Assert.Equal(DevicePresetKind.Manual, binding.Kind);
        Assert.Contains("Mon preset", _eq.LoadedPresets);
        Assert.Contains("mémorisé", _eq.LinkPresetStatus);
    }

    [Fact]
    public void Pick_Auto_RemovesTheBinding()
    {
        _devicePresets.Set(new DevicePresetBinding { DeviceId = "dev1", PresetClass = "user", PresetRef = "X" });
        _mixer.SelectedOutput = Device("dev1", "Enceintes");

        _coordinator.Pick(new OutputPresetOption("auto", "Auto (recommandé)", "Mode"));

        Assert.Null(_devicePresets.Get("dev1"));
    }

    // ================================================================ liaisons

    [Fact]
    public void LinkFactory_BindsCurrentMasterOutput()
    {
        var model = HeadphonePresetLibrary.LoadAll().First();
        _mixer.SelectedOutput = Device("dev1", "Enceintes");

        _coordinator.LinkFactory(model);

        var binding = _devicePresets.Get("dev1")!;
        Assert.Equal("factory", binding.PresetClass);
        Assert.Equal(model.Id, binding.PresetRef);
        Assert.Contains(model.DisplayName, _mixer.OutputPresetText);
    }

    [Fact]
    public void LinkNamed_UserPresetMustBeSaved()
    {
        _mixer.SelectedOutput = Device("dev1", "Enceintes");

        // pas encore sauvegardé : refus explicite
        _coordinator.LinkNamed("Preset fantôme");
        Assert.Contains("pas encore sauvegardé", _eq.LinkPresetStatus);
        Assert.Null(_devicePresets.Get("dev1"));

        // sauvegardé : liaison établie
        _presets.Save(new EqPreset("Preset réel"));
        _coordinator.LinkNamed("Preset réel");
        Assert.Equal("Preset réel", _devicePresets.Get("dev1")!.PresetRef);
    }

    [Fact]
    public void LinkNamed_FactoryDisplayName_IsLinkedAsFactory()
    {
        var model = HeadphonePresetLibrary.LoadAll().First();
        _mixer.SelectedOutput = Device("dev1", "Enceintes");

        _coordinator.LinkNamed(model.DisplayName);

        var binding = _devicePresets.Get("dev1")!;
        Assert.Equal("factory", binding.PresetClass);
        Assert.Equal(model.Id, binding.PresetRef);
    }

    [Fact]
    public void LinkNamed_WithoutMasterOutput_ExplainsItself()
    {
        _mixer.SelectedOutput = null;
        _coordinator.LinkNamed("N'importe");
        Assert.Contains("Aucune sortie master", _eq.LinkPresetStatus);
    }

    [Fact]
    public void BindCalibration_LinksTheMeasurementDevice()
    {
        var preset = new EqPreset("Calibration du jour");
        var measurementDevice = Device("dev-measure", "Casque de mesure");

        _coordinator.BindCalibration(preset, measurementDevice);

        var binding = _devicePresets.Get("dev-measure")!;
        Assert.Equal(DevicePresetKind.Calibration, binding.Kind);
        Assert.Equal("Calibration du jour", binding.PresetRef);
    }

    [Fact]
    public void BindCalibration_NullDevice_DoesNothing()
    {
        _coordinator.BindCalibration(new EqPreset("X"), null);
        Assert.Empty(_devicePresets.LoadAll());
    }
}
