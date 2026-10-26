using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Core.Eq;
using RoomEq.Core.Headphones;

namespace RoomEq.App.ViewModels;

/// <summary>Une entrée du sélecteur de modèles de casques (liste ou recherche).</summary>
public sealed record HeadphoneOptionViewModel(string Id, string Label, string SubLabel);

/// <summary>
/// Sélecteur de corrections casques &amp; écouteurs (extrait de EqViewModel en
/// v3 pour respecter la taille cible des ViewModels) : marques, modèles,
/// recherche plein catalogue et application — spec I-APP-04. La bibliothèque
/// est en lecture seule (aucune écriture disque).
/// </summary>
public partial class HeadphonePickerViewModel : ObservableObject
{
    [ObservableProperty] private ObservableCollection<string> headphoneBrands = new();
    [ObservableProperty] private string? selectedHeadphoneBrand;
    [ObservableProperty] private ObservableCollection<HeadphoneOptionViewModel> headphoneModels = new();
    [ObservableProperty] private HeadphoneOptionViewModel? selectedHeadphoneModel;
    [ObservableProperty] private string headphoneSearch = "";
    [ObservableProperty] private string headphoneStatus = "";
    [ObservableProperty] private bool canApplyHeadphone;

    /// <summary>Texte descriptif affiché au-dessus du sélecteur (nombre de modèles).</summary>
    public string HeadphoneCatalogText { get; } =
        $"{HeadphonePresetLibrary.Count} modèles intégrés — la capture au micro n'étant pas " +
        "applicable aux casques, les corrections sont précalculées (données AutoEq, cible Harman).";

    /// <summary>Un preset usine vient d'être appliqué (le shell peut le lier à la sortie).</summary>
    public event Action<HeadphoneModel>? HeadphonePresetApplied;

    public void Init()
    {
        HeadphoneBrands = new ObservableCollection<string>(HeadphonePresetLibrary.Brands());
        SelectedHeadphoneBrand = HeadphoneBrands.FirstOrDefault(b =>
            string.Equals(b, "Sennheiser", StringComparison.OrdinalIgnoreCase))
            ?? HeadphoneBrands.FirstOrDefault();
    }

    partial void OnSelectedHeadphoneBrandChanged(string? value) => RefreshModels();

    partial void OnHeadphoneSearchChanged(string value) => RefreshModels();

    partial void OnSelectedHeadphoneModelChanged(HeadphoneOptionViewModel? value)
        => CanApplyHeadphone = value != null;

    /// <summary>
    /// Reconstruit la liste des modèles : résultats de recherche si l'utilisateur
    /// tape quelque chose (la recherche couvre TOUT le catalogue, la marque est
    /// ignorée), sinon les modèles de la marque sélectionnée.
    /// </summary>
    private void RefreshModels()
    {
        var previous = SelectedHeadphoneModel?.Id;
        List<HeadphoneOptionViewModel> list;

        if (!string.IsNullOrWhiteSpace(HeadphoneSearch))
        {
            list = HeadphonePresetLibrary.Search(HeadphoneSearch)
                .Select(ToOption)
                .ToList();
        }
        else
        {
            string? brand = SelectedHeadphoneBrand;
            list = (brand == null ? HeadphonePresetLibrary.LoadAll()
                                   : HeadphonePresetLibrary.ByBrand(brand))
                .Select(ToOption)
                .ToList();
        }

        HeadphoneModels = new ObservableCollection<HeadphoneOptionViewModel>(list);
        SelectedHeadphoneModel = HeadphoneModels.FirstOrDefault(m => m.Id == previous)
                                 ?? HeadphoneModels.FirstOrDefault();
    }

    private static HeadphoneOptionViewModel ToOption(HeadphoneModel m) =>
        new(m.Id, m.DisplayName, $"{m.TypeLabel} · {m.GammeLabel} · {m.Source}");

    /// <summary>Applique la correction du casque sélectionné (remonte le preset chargé).</summary>
    public EqPreset? ApplySelected()
    {
        var option = SelectedHeadphoneModel;
        if (option == null)
        {
            HeadphoneStatus = "Choisissez d'abord un modèle de casque.";
            return null;
        }
        var model = HeadphonePresetLibrary.Find(option.Id);
        if (model == null)
        {
            HeadphoneStatus = "Modèle introuvable dans la bibliothèque intégrée.";
            return null;
        }

        var preset = HeadphonePresetLibrary.ToEqPreset(model);
        HeadphonePresetApplied?.Invoke(model);
        HeadphoneStatus = $"Correction « {model.DisplayName} » appliquée : " +
            $"{preset.Bands.Count} bandes · préampli {preset.PreampDb:0.0} dB · source {model.Source}. " +
            "Elle est aussi mémorisée pour la sortie master actuelle — réglez le préampli si besoin.";
        return preset;
    }

    [RelayCommand]
    private void ApplyHeadphonePreset() => ApplySelected();
}
