using System.Collections.ObjectModel;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.Core.Calibration;
using RoomEq.Core.Dsp;
using RoomEq.Core.Io;

namespace RoomEq.App.ViewModels;

/// <summary>
/// Gestion des profils de correction micro — partie profils :
/// « Mes profils » (import .cal REW / CSV, sélection, suppression, renommage),
/// bibliothèque intégrée de micros du marché avec recherche, création de
/// profils spécialisés — spec I-APP-05. L'édition de courbe est dans
/// MicViewModel.Curve.cs (v3 : décomposition par concern).
/// </summary>
public partial class MicViewModel : ObservableObject
{
    private readonly MicProfileStore _store;

    [ObservableProperty] private ObservableCollection<MicProfile> profiles = new();
    [ObservableProperty] private MicProfile? selectedProfile;
    [ObservableProperty] private string importInfo = "Aucun profil chargé. Sans profil, le micro est supposé linéaire.";

    // ----- bibliothèque intégrée -----
    [ObservableProperty] private ObservableCollection<MicProfile> libraryProfiles = new();
    [ObservableProperty] private MicProfile? selectedLibraryProfile;
    [ObservableProperty] private string librarySearch = "";

    // ----- édition de courbe (champs dans MicViewModel.Curve.cs) -----

    public bool HasSelection => SelectedProfile != null;
    public bool HasSelectedPoint => SelectedPointIndex >= 0;

    public event Action? SelectionChanged;
    public event Action? ProfileListChanged;
    public event Action? CurveEdited;

    /// <summary>Magasin injectable pour les tests (défaut : %APPDATA%).</summary>
    public MicViewModel(MicProfileStore? store = null)
    {
        _store = store ?? new MicProfileStore();
        LoadLibrary();
        LoadFromDisk();
    }

    private void LoadLibrary()
    {
        LibraryProfiles = new ObservableCollection<MicProfile>(MicProfileLibrary.LoadAll());
        SelectedLibraryProfile = LibraryProfiles.FirstOrDefault();
    }

    partial void OnLibrarySearchChanged(string value) => ApplyLibraryFilter();

    private void ApplyLibraryFilter()
    {
        var filtered = MicProfileLibrary.Search(MicProfileLibrary.LoadAll(), LibrarySearch);
        LibraryProfiles = new ObservableCollection<MicProfile>(filtered);
        SelectedLibraryProfile = LibraryProfiles.FirstOrDefault(p => p == SelectedLibraryProfile)
                                 ?? LibraryProfiles.FirstOrDefault();
    }

    private void LoadFromDisk()
    {
        var list = new ObservableCollection<MicProfile>();
        foreach (var name in _store.List())
        {
            try
            {
                var p = _store.Load(name);
                if (p is { IsValid: true }) list.Add(p);
            }
            catch
            {
                // profil corrompu : ignoré
            }
        }
        Profiles = list;
        SelectedProfile ??= Profiles.FirstOrDefault();
        ProfileListChanged?.Invoke();
    }

    /// <summary>Importe un fichier de calibration (appelé par la vue après le sélecteur de fichiers).</summary>
    public void ImportFromFile(string path)
    {
        try
        {
            string text = File.ReadAllText(path);
            var profile = MicProfile.ParseText(text, Path.GetFileNameWithoutExtension(path), path);
            if (!profile.IsValid)
            {
                ImportInfo = "Fichier non reconnu : aucune paire fréquence / gain valide trouvée.";
                return;
            }
            _store.Save(profile);
            Profiles.Add(profile);
            SelectedProfile = profile;
            ImportInfo = $"« {profile.Name} » : {profile.PointCount} points " +
                         $"({profile.FreqMin:0} Hz – {profile.FreqMax:0} Hz).";
            ProfileListChanged?.Invoke();
        }
        catch (Exception ex)
        {
            ImportInfo = "Import impossible : " + ex.Message;
        }
    }

    public List<CurvePoint> SelectedCurve()
    {
        if (SelectedProfile is not { IsValid: true } p) return new List<CurvePoint>();
        return p.ToCurve();
    }

    // ----- bibliothèque : import et création -----

    [RelayCommand]
    private void ImportLibraryProfile()
    {
        if (SelectedLibraryProfile == null) return;

        string name = UniqueName(SelectedLibraryProfile.Name, Profiles);
        var copy = MicProfileLibrary.ToEditableCopy(SelectedLibraryProfile, name);
        _store.Save(copy);
        Profiles.Add(copy);
        SelectedProfile = copy;
        IsDirty = false;
        EditStatus = $"« {name} » importé depuis la bibliothèque. Modifiez librement la courbe puis enregistrez.";
        ImportInfo = EditStatus;
        CurveEdited?.Invoke();
        ProfileListChanged?.Invoke();
    }

    [RelayCommand]
    private void CreateNewProfile()
    {
        string baseName = string.IsNullOrWhiteSpace(NewProfileName) ? "Nouveau micro" : NewProfileName.Trim();
        string name = UniqueName(baseName, Profiles);

        var p = new MicProfile
        {
            Name = name,
            Brand = "",
            Technology = "Profil spécialisé",
            Notes = "Créé manuellement dans RoomEq.",
            SourceFile = "création manuelle"
        };

        // 10 points plats géométriquement répartis 20 Hz → 20 kHz
        double ratio = Math.Pow(20000.0 / 20.0, 1.0 / 9.0);
        for (int i = 0; i < 10; i++)
        {
            double f = 20.0 * Math.Pow(ratio, i);
            p.Frequencies.Add(Math.Round(f, 1));
            p.GainsDb.Add(0.0);
        }

        _store.Save(p);
        Profiles.Add(p);
        SelectedProfile = p;
        NewProfileName = "";
        IsDirty = false;
        EditStatus = $"Profil spécialisé « {name} » créé (plat). Ajoutez ou déplacez des points puis enregistrez.";
        ImportInfo = EditStatus;
        CurveEdited?.Invoke();
        ProfileListChanged?.Invoke();
    }

    [RelayCommand]
    private void RenameProfile()
    {
        if (SelectedProfile == null) return;
        string newName = (NewProfileName ?? "").Trim();
        if (newName.Length == 0)
        {
            EditStatus = "Saisissez le nouveau nom dans le champ à côté.";
            return;
        }
        string old = SelectedProfile.Name;
        if (string.Equals(old, newName, StringComparison.OrdinalIgnoreCase))
        {
            EditStatus = "Le nom est identique.";
            return;
        }

        bool ok = _store.Rename(old, newName);
        if (!ok)
        {
            EditStatus = $"Renommage impossible : « {newName} » existe déjà.";
            return;
        }

        // rafraîchir la liste en place
        var fresh = _store.Load(newName);
        int idx = Profiles.IndexOf(SelectedProfile);
        if (fresh != null && idx >= 0) Profiles[idx] = fresh;
        SelectedProfile = fresh ?? SelectedProfile;
        NewProfileName = "";
        EditStatus = $"« {old} » renommé en « {newName} ».";
        ImportInfo = EditStatus;
        ProfileListChanged?.Invoke();
        CurveEdited?.Invoke();
    }

    [RelayCommand]
    private void DeleteSelected()
    {
        if (SelectedProfile == null) return;
        _store.Delete(SelectedProfile.Name);
        var name = SelectedProfile.Name;
        Profiles.Remove(SelectedProfile);
        SelectedProfile = Profiles.FirstOrDefault();
        ImportInfo = $"Profil « {name} » supprimé.";
        ProfileListChanged?.Invoke();
    }

    partial void OnSelectedProfileChanged(MicProfile? value)
    {
        IsDirty = false;
        SelectedPointIndex = -1;
        SyncSelectedPointFields();
        SelectionChanged?.Invoke();
    }

    // ----- helpers partagés avec l'édition de courbe -----

    /// <summary>Un nom unique dans la liste (suffixes « (2) », « (3) »…).</summary>
    private static string UniqueName(string baseName, IEnumerable<MicProfile> existing)
    {
        string name = baseName;
        int suffix = 2;
        while (existing.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{baseName} ({suffix++})";
        return name;
    }
}
