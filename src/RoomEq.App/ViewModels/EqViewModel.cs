using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Audio.Playback;
using RoomEq.Core.Calibration;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using RoomEq.Core.Headphones;
using RoomEq.Core.Io;

namespace RoomEq.App.ViewModels;

/// <summary>
/// ViewModel de l'égaliseur : preset courant (bandes + préampli), presets
/// sauvegardés, liaison temps réel avec la chaîne de traitement audio et
/// l'EqControl — spec I-APP-04. Le sélecteur casques est délégué au
/// sous-ViewModel <see cref="Headphone"/> (v3 : décomposition).
/// </summary>
public partial class EqViewModel : ObservableObject, IOutputPresetEqView
{
    private readonly PresetStore _store;
    private PlaybackEngine _engine = null!;

    [ObservableProperty] private string presetName = "Personnalisé";
    [ObservableProperty] private double preampDb;
    [ObservableProperty] private int selectedIndex = -1;
    [ObservableProperty] private ObservableCollection<BandViewModel> bands = new();
    [ObservableProperty] private ObservableCollection<string> savedPresets = new();
    [ObservableProperty] private string? selectedSavedPreset;
    [ObservableProperty] private string curveInfo = "";
    [ObservableProperty] private string newPresetName = "";
    [ObservableProperty] private string presetStatus = "";

    // ----- bibliothèque casques & écouteurs (corrections AutoEq intégrées) -----

    public HeadphonePickerViewModel Headphone { get; } = new();

    /// <summary>Un preset usine vient d'être appliqué (le shell peut le lier à la sortie).</summary>
    public event Action<HeadphoneModel>? HeadphonePresetApplied
    {
        add => Headphone.HeadphonePresetApplied += value;
        remove => Headphone.HeadphonePresetApplied -= value;
    }

    // ----- import de la courbe de calibration -----

    private CorrectionResult? _calibrationSource;

    // ----- liaison preset → sortie master (politique par périphérique) -----

    /// <summary>Nom de la sortie master routée, affiché par le bouton de liaison.</summary>
    [ObservableProperty] private string linkTargetText = "";
    /// <summary>Statut de la liaison preset → sortie (confirmation ou erreur explicite).</summary>
    [ObservableProperty] private string linkPresetStatus = "";

    /// <summary>Demande de liaison du preset courant à la sortie master (traitée par le shell).</summary>
    public event Action<string>? LinkPresetRequested;

    [RelayCommand]
    private void LinkPresetToOutput()
    {
        // refuser les états non nommés : le preset par défaut plat et l'état
        // « Personnalisé » (v3 : « Plat » échappait au garde-fou v2)
        if (string.IsNullOrWhiteSpace(PresetName)
            || PresetName == "Personnalisé"
            || PresetName == "Plat")
        {
            LinkPresetStatus = "Appliquez d'abord un preset nommé (casque de la bibliothèque ou preset sauvegardé) avant de le lier.";
            return;
        }
        LinkPresetRequested?.Invoke(PresetName);
    }

    [ObservableProperty] private ObservableCollection<CorrectionPrecisionOption> precisionOptions =
        new(CorrectionPrecisionCatalog.All);

    [ObservableProperty] private CorrectionPrecisionOption selectedPrecision = CorrectionPrecisionCatalog.All[1];
    [ObservableProperty] private bool hasCalibrationCurve;
    [ObservableProperty] private string importStatus = "";

    public event Action<List<CurvePoint>>? CurveChanged;

    private List<CurvePoint> _lastCurve = new();

    /// <summary>Dernière courbe composite calculée (pour l'initialisation des vues).</summary>
    public List<CurvePoint> CurrentCurve() => _lastCurve;

    /// <summary>Magasin injectable pour les tests (défaut : %APPDATA%).</summary>
    public EqViewModel(PresetStore? store = null)
    {
        _store = store ?? new PresetStore();
    }

    public void Init(PlaybackEngine engine)
    {
        _engine = engine;
        LoadPreset(EqPreset.Flat());
        RefreshSaved();
        Headphone.Init();
    }

    public void LoadPreset(EqPreset preset)
    {
        var newBands = new ObservableCollection<BandViewModel>(
            preset.Bands.Select(b => new BandViewModel(b, BandChanged)));

        PreampDb = preset.PreampDb;
        PresetName = preset.Name;
        Bands = newBands;
        SelectedIndex = Bands.Count > 0 ? 0 : -1;

        PushAllToEngine();
        RecomputeCurve();
        RefreshSaved();
    }

    private void PushAllToEngine() => _engine.Eq.SetPreset(BuildPreset());

    private EqPreset BuildPreset()
    {
        var p = new EqPreset(PresetName) { PreampDb = PreampDb };
        foreach (var b in Bands) p.Bands.Add(b.ToModel());
        return p;
    }

    private void BandChanged()
    {
        PushAllToEngine();
        RecomputeCurve();
    }

    private void RemoveAt(int index)
    {
        if (index < 0 || index >= Bands.Count) return;
        var current = BuildPreset();
        current.Bands.RemoveAt(index);
        LoadPreset(current);
    }

    private void RecomputeCurve()
    {
        var curve = _engine.Eq.ResponseCurve(20, 20000, 360);
        _lastCurve = curve;
        CurveChanged?.Invoke(curve);
        double peak = curve.Count > 0 ? curve.Max(c => Math.Abs(c.Db)) : 0;
        CurveInfo = $"{Bands.Count} bandes · amplitude max {peak:0.0} dB";
    }

    // ----- import de la courbe de calibration -----

    /// <summary>
    /// Fournit (ou retire) la dernière courbe de calibration mesurée, source de
    /// l'import dans l'égaliseur. Appelé par le shell quand la calibration
    /// produit un résultat, et quand l'utilisateur réinitialise la courbe.
    /// </summary>
    public void SetCalibrationSource(CorrectionResult? source)
    {
        _calibrationSource = source;
        HasCalibrationCurve = source is { MeasuredCurve.Count: >= 8 };
        if (!HasCalibrationCurve)
            ImportStatus = "";
    }

    /// <summary>
    /// Importe la courbe affichée dans l'onglet Calibration : les bandes actuelles
    /// sont remplacées par une correction optimisée dont le nombre, les fréquences
    /// et les largeurs (Q) dépendent de la précision choisie.
    /// </summary>
    [RelayCommand]
    private void ImportCalibrationCurve()
    {
        if (_calibrationSource == null)
        {
            ImportStatus = "Aucune courbe de calibration disponible — lancez d'abord une mesure dans l'onglet Calibration.";
            return;
        }

        try
        {
            string name = $"Import calibration {DateTime.Now:HH:mm}";
            var result = CorrectionDesigner.DesignFromCalibrationCurve(
                _calibrationSource, SelectedPrecision.Kind, name);

            LoadPreset(result.Preset);
            ImportStatus =
                $"Courbe importée — précision {SelectedPrecision.Name} : {result.Preset.Bands.Count} bandes · " +
                $"préampli {result.Preset.PreampDb:0.0} dB · résidu max {result.ResidualMaxDb():0.0} dB";
        }
        catch (Exception ex)
        {
            ImportStatus = "Import impossible : " + ex.Message;
        }
    }

    /// <summary>Applique la correction casque sélectionnée (délégué au sous-ViewModel).</summary>
    [RelayCommand]
    private void ApplyHeadphonePreset()
    {
        var preset = Headphone.ApplySelected();
        if (preset != null) LoadPreset(preset);
    }

    // ----- liaisons avec l'EqControl (poignées) -----

    public void OnBandDragged(int index, double freq, double gainDb)
    {
        if (index < 0 || index >= Bands.Count) return;
        var b = Bands[index];
        b.Frequency = Math.Round(freq, 1);
        b.GainDb = Math.Round(gainDb, 1);
        // les notifications de la bande déclenchent BandChanged → push audio + recalcul courbe
    }

    public void OnBandQChanged(int index, double factor)
    {
        if (index < 0 || index >= Bands.Count) return;
        var b = Bands[index];
        b.Q = Math.Clamp(Math.Round(b.Q * factor, 3), 0.1, 20.0);
    }

    // ----- commandes -----

    [RelayCommand]
    private void AddBand()
    {
        var current = BuildPreset();
        current.Bands.Add(new EqBand(FilterType.Peaking, 1000, 0, 1.0));
        LoadPreset(current);
        SelectedIndex = Bands.Count - 1;
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        if (SelectedIndex < 0 || SelectedIndex >= Bands.Count) return;
        RemoveAt(SelectedIndex);
    }

    [RelayCommand]
    private void RemoveBand(BandViewModel? band)
    {
        if (band == null) return;
        RemoveAt(Bands.IndexOf(band));
    }

    [RelayCommand]
    private void Flatten()
    {
        var current = BuildPreset();
        foreach (var b in current.Bands) b.GainDb = 0;
        current.PreampDb = 0;
        LoadPreset(current);
    }

    [RelayCommand]
    private void SavePreset()
    {
        var p = BuildPreset();
        if (string.IsNullOrWhiteSpace(p.Name)) p.Name = "Sans nom";
        _store.Save(p);
        RefreshSaved();
        SelectedSavedPreset = p.Name;
    }

    [RelayCommand]
    private void LoadSelectedPreset()
    {
        if (SelectedSavedPreset == null) return;
        var p = _store.Load(SelectedSavedPreset);
        if (p != null) LoadPreset(p);
    }

    [RelayCommand]
    private void DeleteSelectedPreset()
    {
        if (SelectedSavedPreset == null) return;
        _store.Delete(SelectedSavedPreset);
        RefreshSaved();
        PresetStatus = $"Preset « {SelectedSavedPreset} » supprimé.";
        SelectedSavedPreset = SavedPresets.FirstOrDefault();
    }

    [RelayCommand]
    private void RenamePreset()
    {
        if (SelectedSavedPreset == null)
        {
            PresetStatus = "Sélectionnez d'abord un preset dans la liste.";
            return;
        }

        string newName = (NewPresetName ?? "").Trim();
        if (newName.Length == 0)
        {
            PresetStatus = "Saisissez le nouveau nom dans le champ prévu.";
            return;
        }
        if (string.Equals(newName, SelectedSavedPreset, StringComparison.OrdinalIgnoreCase))
        {
            PresetStatus = "Le nom est identique au nom actuel.";
            return;
        }

        string oldName = SelectedSavedPreset;
        if (!_store.Rename(oldName, newName))
        {
            PresetStatus = $"Renommage impossible : « {newName} » existe déjà ou est invalide.";
            return;
        }

        // mettre à jour le nom affiché si le preset courant est celui renommé
        if (string.Equals(PresetName, oldName, StringComparison.OrdinalIgnoreCase))
            PresetName = newName;

        RefreshSaved();
        SelectedSavedPreset = newName;
        NewPresetName = "";
        PresetStatus = $"Preset « {oldName} » renommé en « {newName} ».";
    }

    private void RefreshSaved()
    {
        SavedPresets = new ObservableCollection<string>(_store.List());
    }

    partial void OnPreampDbChanged(double value)
    {
        if (_engine != null) _engine.Eq.SetPreamp(value);
    }
}
