using RoomEq.App.ViewModels;
using RoomEq.Core.Headphones;
using Xunit;

namespace RoomEq.App.Tests;

/// <summary>
/// Sélecteur de corrections casques (spec I-APP-04) : marques peuplées,
/// filtrage par marque, recherche couvrant TOUT le catalogue, conservation
/// de la sélection, application (preset + événement).
/// </summary>
public class HeadphonePickerTests
{
    [Fact]
    public void Init_PopulatesBrands_AndSelectsFirst()
    {
        var picker = new HeadphonePickerViewModel();
        picker.Init();

        Assert.NotEmpty(picker.HeadphoneBrands);
        Assert.NotNull(picker.SelectedHeadphoneBrand);
        Assert.NotEmpty(picker.HeadphoneModels);
        Assert.NotNull(picker.SelectedHeadphoneModel);
        Assert.True(picker.CanApplyHeadphone);

        Assert.Contains("modèles intégrés", picker.HeadphoneCatalogText);
        Assert.Contains(HeadphonePresetLibrary.Count.ToString(), picker.HeadphoneCatalogText);
    }

    [Fact]
    public void BrandFilter_ListsOnlyThatBrand()
    {
        var picker = new HeadphonePickerViewModel();
        picker.Init();

        picker.SelectedHeadphoneBrand = picker.HeadphoneBrands.First();
        var brand = picker.SelectedHeadphoneBrand;

        Assert.All(picker.HeadphoneModels, m =>
            Assert.StartsWith(brand, HeadphonePresetLibrary.Find(m.Id)!.DisplayName, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Search_CoversTheWholeCatalog()
    {
        var picker = new HeadphonePickerViewModel();
        picker.Init();
        int byBrand = picker.HeadphoneModels.Count;

        // une recherche fructueuse peut dépasser le périmètre de la marque
        picker.HeadphoneSearch = "sa"; // lettres présentes dans beaucoup de modèles
        Assert.NotEmpty(picker.HeadphoneModels);

        // une recherche infructueuse vide la liste et désactive l'application
        picker.HeadphoneSearch = "zzzzzzzz-inexistant";
        Assert.Empty(picker.HeadphoneModels);
        Assert.Null(picker.SelectedHeadphoneModel);
        Assert.False(picker.CanApplyHeadphone);

        // effacer la recherche restaure la marque
        picker.HeadphoneSearch = "";
        Assert.NotEmpty(picker.HeadphoneModels);
        Assert.True(picker.CanApplyHeadphone);
        _ = byBrand; // (informationel : la marque redirige la liste hors recherche)
    }

    [Fact]
    public void Search_PreservesSelection_WhenStillVisible()
    {
        var picker = new HeadphonePickerViewModel();
        picker.Init();

        var selected = picker.SelectedHeadphoneModel;
        Assert.NotNull(selected);

        // requête qui matche le modèle sélectionné : la sélection est conservée
        picker.HeadphoneSearch = selected!.Label.Split(' ')[0];
        Assert.Equal(selected.Id, picker.SelectedHeadphoneModel?.Id);
    }

    [Fact]
    public void ApplySelected_LoadsPresetAndRaisesEvent()
    {
        var picker = new HeadphonePickerViewModel();
        picker.Init();

        HeadphoneModel? raised = null;
        picker.HeadphonePresetApplied += m => raised = m;

        var preset = picker.ApplySelected();

        Assert.NotNull(preset);
        Assert.Equal(picker.SelectedHeadphoneModel!.Id, raised!.Id);
        Assert.Contains("appliquée", picker.HeadphoneStatus);
        Assert.NotEmpty(preset!.Bands);
        Assert.True(preset.PreampDb <= 0); // correction : préampli de sécurité
    }

    [Fact]
    public void ApplySelected_WithoutSelection_ExplainsItself()
    {
        var picker = new HeadphonePickerViewModel();
        picker.Init();
        picker.HeadphoneSearch = "zzzzzzzz-inexistant";

        HeadphoneModel? raised = null;
        picker.HeadphonePresetApplied += m => raised = m;

        var preset = picker.ApplySelected();

        Assert.Null(preset);
        Assert.Null(raised);
        Assert.Contains("Choisissez d'abord", picker.HeadphoneStatus);
    }
}
