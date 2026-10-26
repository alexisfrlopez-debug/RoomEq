using RoomEq.Audio.Playback;
using RoomEq.Core.Eq;
using RoomEq.Core.Headphones;
using RoomEq.Core.Io;

namespace RoomEq.App.ViewModels;

/// <summary>
/// Une entrée du sélecteur « Preset de la sortie » (onglet Mixage, tranche
/// MASTER). Clés : "auto" (politique automatique), "none" (aucun preset,
/// même automatique), "user:&lt;nom&gt;" (preset sauvegardé),
/// "factory:&lt;id&gt;" (bibliothèque usine).
/// </summary>
public sealed record OutputPresetOption(string Key, string Label, string Group)
{
    public override string ToString() => Label;
}

/// <summary>Vues du coordinateur : ce dont la politique preset↔sortie a besoin
/// de la table de mixage — implémentée par MixerViewModel, imitable en tests.</summary>
public interface IOutputPresetMixerView
{
    OutputDeviceInfo? SelectedOutput { get; }
    bool AutoOutputPreset { get; }
    void SetOutputPresetOptions(IEnumerable<OutputPresetOption> options, OutputPresetOption? selected);
    string OutputPresetText { get; set; }
}

/// <summary>Vues du coordinateur côté égaliseur — implémentée par EqViewModel.</summary>
public interface IOutputPresetEqView
{
    void LoadPreset(EqPreset preset);
    string LinkTargetText { get; set; }
    string LinkPresetStatus { get; set; }
}

/// <summary>
/// Politique du preset adapté à la sortie master (extraite de MainViewModel
/// en v3) — spec I-APP-01 :
///   1. liaison manuelle de l'utilisateur (prioritaire) ;
///   2. preset de calibration mesuré sur ce périphérique ;
///   3. preset usine reconnu depuis le nom du périphérique (casque/écouteurs
///      de la bibliothèque intégrée) ;
///   4. rien — l'égaliseur reste tel quel.
/// Les méthodes s'exécutent sur le thread appelant : le shell poste sur l'UI
/// avant d'invoquer (les propriétés modifiées sont observables).
/// </summary>
public sealed class OutputPresetCoordinator
{
    private readonly IOutputPresetEqView _eq;
    private readonly IOutputPresetMixerView _mixer;
    private readonly DevicePresetStore _devicePresets;
    private readonly PresetStore _presets;

    /// <summary>Magasins injectables pour les tests (défaut : %APPDATA%).</summary>
    public OutputPresetCoordinator(IOutputPresetEqView eq, IOutputPresetMixerView mixer,
        DevicePresetStore? devicePresets = null, PresetStore? presets = null)
    {
        _eq = eq;
        _mixer = mixer;
        _devicePresets = devicePresets ?? new DevicePresetStore();
        _presets = presets ?? new PresetStore();
    }

    /// <summary>
    /// Un périphérique audio vient d'être branché / retiré / devient la sortie
    /// master : réévalue le preset de la sortie — le nouveau casque branché est
    /// reconnu et sa correction appliquée sans redémarrer RoomEq.
    /// </summary>
    public void Evaluate(OutputDeviceInfo? device)
    {
        if (device == null) return;
        _eq.LinkTargetText = $"Sortie master actuelle : {device.DisplayName}";
        var binding = _devicePresets.Get(device.Id);
        RebuildOptions(device, binding);

        if (!_mixer.AutoOutputPreset)
        {
            _mixer.OutputPresetText = "Preset auto désactivé (option du Mixage).";
            return;
        }

        if (binding is { IsUsable: true })
        {
            ApplyBinding(device, binding, binding.Kind == DevicePresetKind.Manual
                ? "preset manuel mémorisé pour cette sortie"
                : "preset de calibration mesuré sur cette sortie");
            return;
        }
        if (binding is { Kind: DevicePresetKind.None })
        {
            _mixer.OutputPresetText = "Aucun preset pour cette sortie (choix « Aucun »).";
            return;
        }

        var match = HeadphonePresetLibrary.MatchDeviceName(device.DisplayName);
        if (match != null)
        {
            _eq.LoadPreset(HeadphonePresetLibrary.ToEqPreset(match));
            _mixer.OutputPresetText = $"Auto : « {match.DisplayName} » reconnu (usine).";
            DiagnosticsLog.Info($"Preset auto : {device.DisplayName} -> {match.DisplayName} (usine)");
        }
        else
        {
            _mixer.OutputPresetText = "Auto : aucun preset connu pour cette sortie.";
        }
    }

    /// <summary>Applique une liaison (usine ou utilisateur) à l'égaliseur principal.</summary>
    private void ApplyBinding(OutputDeviceInfo device, DevicePresetBinding binding, string originLabel)
    {
        if (binding.PresetClass == "factory")
        {
            var model = HeadphonePresetLibrary.Find(binding.PresetRef);
            if (model == null)
            {
                _mixer.OutputPresetText = "Preset lié introuvable dans la bibliothèque — retour en auto.";
                _devicePresets.Remove(device.Id);
                RebuildOptions(device, null);
                return;
            }
            _eq.LoadPreset(HeadphonePresetLibrary.ToEqPreset(model));
        }
        else
        {
            var preset = _presets.Load(binding.PresetRef);
            if (preset == null)
            {
                _mixer.OutputPresetText = $"Preset « {binding.PresetRef} » supprimé — retour en auto.";
                _devicePresets.Remove(device.Id);
                RebuildOptions(device, null);
                return;
            }
            _eq.LoadPreset(preset);
        }
        _mixer.OutputPresetText = $"Appliqué : « {binding.PresetRef} » ({originLabel}).";
    }

    /// <summary>Reconstruit le sélecteur « Preset de la sortie » (Auto / Aucun / vôtres / suggestion usine).</summary>
    private void RebuildOptions(OutputDeviceInfo device, DevicePresetBinding? binding)
    {
        var options = new List<OutputPresetOption>
        {
            new("auto", "Auto (recommandé)", "Mode"),
            new("none", "Aucun", "Mode"),
        };

        // presets utilisateur (typiquement : calibrations sauvegardées)
        foreach (var name in _presets.List())
            options.Add(new OutputPresetOption("user:" + name, name, "Vos presets"));

        // suggestion usine : le modèle reconnu dans le nom du périphérique
        var match = HeadphonePresetLibrary.MatchDeviceName(device.DisplayName);
        if (match != null)
            options.Add(new OutputPresetOption("factory:" + match.Id,
                match.DisplayName + " (reconnu)", "Bibliothèque"));

        OutputPresetOption? selected = binding switch
        {
            { Kind: DevicePresetKind.None } => options.FirstOrDefault(o => o.Key == "none"),
            { IsUsable: true, PresetClass: "user" } =>
                options.FirstOrDefault(o => o.Key == "user:" + binding.PresetRef)
                ?? options.FirstOrDefault(o => o.Key == "auto"),
            { IsUsable: true, PresetClass: "factory" } =>
                options.FirstOrDefault(o => o.Key == "factory:" + binding.PresetRef)
                ?? options.FirstOrDefault(o => o.Key == "auto"),
            _ => options.FirstOrDefault(o => o.Key == "auto"),
        };
        _mixer.SetOutputPresetOptions(options, selected);
    }

    /// <summary>L'utilisateur a choisi une option du sélecteur « Preset de la sortie ».</summary>
    public void Pick(OutputPresetOption option)
    {
        var device = _mixer.SelectedOutput;
        if (device == null) return;

        switch (option.Key)
        {
            case "auto":
                _devicePresets.Remove(device.Id);
                Evaluate(device);
                _eq.LinkPresetStatus = $"Sortie « {device.DisplayName} » : mode Auto (calibration > usine reconnue).";
                break;

            case "none":
                _devicePresets.Set(new DevicePresetBinding
                {
                    DeviceId = device.Id,
                    DeviceName = device.DisplayName,
                    Kind = DevicePresetKind.None,
                });
                _mixer.OutputPresetText = "Aucun preset pour cette sortie (choix mémorisé).";
                _eq.LinkPresetStatus = $"Sortie « {device.DisplayName} » : aucun preset, même automatique.";
                RebuildOptions(device, _devicePresets.Get(device.Id));
                break;

            default:
                if (option.Key.StartsWith("user:", StringComparison.Ordinal))
                {
                    string name = option.Key["user:".Length..];
                    _devicePresets.Set(new DevicePresetBinding
                    {
                        DeviceId = device.Id,
                        DeviceName = device.DisplayName,
                        PresetClass = "user",
                        PresetRef = name,
                        Kind = DevicePresetKind.Manual,
                    });
                }
                else if (option.Key.StartsWith("factory:", StringComparison.Ordinal))
                {
                    string id = option.Key["factory:".Length..];
                    _devicePresets.Set(new DevicePresetBinding
                    {
                        DeviceId = device.Id,
                        DeviceName = device.DisplayName,
                        PresetClass = "factory",
                        PresetRef = id,
                        Kind = DevicePresetKind.Manual,
                    });
                }
                else break;

                ApplyBinding(device, _devicePresets.Get(device.Id)!, "choix manuel");
                RebuildOptions(device, _devicePresets.Get(device.Id));
                _eq.LinkPresetStatus = $"Sortie « {device.DisplayName} » : {option.Label} — mémorisé pour les prochaines sessions.";
                break;
        }
    }

    /// <summary>Un preset usine vient d'être appliqué dans l'onglet Égaliseur : lié à la sortie master.</summary>
    public void LinkFactory(HeadphoneModel model)
    {
        var device = _mixer.SelectedOutput;
        if (device == null) return;
        _devicePresets.Set(new DevicePresetBinding
        {
            DeviceId = device.Id,
            DeviceName = device.DisplayName,
            PresetClass = "factory",
            PresetRef = model.Id,
            Kind = DevicePresetKind.Manual,
        });
        RebuildOptions(device, _devicePresets.Get(device.Id));
        _mixer.OutputPresetText = $"Appliqué : « {model.DisplayName} » (choix manuel).";
        _eq.LinkPresetStatus = $"Correction {model.DisplayName} mémorisée pour la sortie « {device.DisplayName} ».";
    }

    /// <summary>Le bouton « Lier à la sortie » de l'onglet Égaliseur : preset nommé → sortie master.</summary>
    public void LinkNamed(string presetName)
    {
        var device = _mixer.SelectedOutput;
        if (device == null)
        {
            _eq.LinkPresetStatus = "Aucune sortie master sélectionnée (onglet Mixage).";
            return;
        }

        // preset usine ? (le nom affiché est « Marque Modèle »)
        var model = HeadphonePresetLibrary.LoadAll()
            .FirstOrDefault(m => string.Equals(m.DisplayName, presetName, StringComparison.OrdinalIgnoreCase));
        if (model != null)
        {
            LinkFactory(model);
            return;
        }

        // preset utilisateur : il doit être sauvegardé pour survivre aux sessions
        if (_presets.Load(presetName) == null)
        {
            _eq.LinkPresetStatus = $"Le preset « {presetName} » n'est pas encore sauvegardé : " +
                "cliquez « Enregistrer » ci-dessus, puis reliez-le.";
            return;
        }
        _devicePresets.Set(new DevicePresetBinding
        {
            DeviceId = device.Id,
            DeviceName = device.DisplayName,
            PresetClass = "user",
            PresetRef = presetName,
            Kind = DevicePresetKind.Manual,
        });
        RebuildOptions(device, _devicePresets.Get(device.Id));
        _mixer.OutputPresetText = $"Appliqué : « {presetName} » (choix manuel).";
        _eq.LinkPresetStatus = $"Preset « {presetName} » mémorisé pour la sortie « {device.DisplayName} ».";
    }

    /// <summary>
    /// Une calibration vient d'être sauvegardée : le preset est lié au périphérique
    /// de mesure — la reconnaissance automatique le réappliquera à chaque fois que
    /// ce périphérique redevient la sortie master.
    /// </summary>
    public void BindCalibration(EqPreset preset, OutputDeviceInfo? measurementDevice)
    {
        var device = measurementDevice;
        if (device == null) return;
        _devicePresets.Set(new DevicePresetBinding
        {
            DeviceId = device.Id,
            DeviceName = device.DisplayName,
            PresetClass = "user",
            PresetRef = preset.Name,
            Kind = DevicePresetKind.Calibration,
        });
        DiagnosticsLog.Info($"Calibration liee a {device.DisplayName} -> preset {preset.Name}");
        if (string.Equals(_mixer.SelectedOutput?.Id, device.Id, StringComparison.OrdinalIgnoreCase))
            RebuildOptions(device, _devicePresets.Get(device.Id));
    }
}
