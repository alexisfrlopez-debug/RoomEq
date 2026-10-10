using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.App.Controls;
using RoomEq.Core.Calibration;
using RoomEq.Core.Dsp;
using RoomEq.Core.Io;

namespace RoomEq.App.ViewModels;

/// <summary>
/// Gestion des profils de correction micro :
///  - « Mes profils » : import .cal REW / CSV, sélection, suppression, renommage ;
///  - bibliothèque intégrée de micros du marché (toutes technologies) avec recherche ;
///  - création de profils spécialisés et édition graphique de la courbe
///    (ajout / déplacement / suppression de points, lissage, enregistrement).
/// </summary>
public partial class MicViewModel : ObservableObject
{
    private readonly MicProfileStore _store = new();

    [ObservableProperty] private ObservableCollection<MicProfile> profiles = new();
    [ObservableProperty] private MicProfile? selectedProfile;
    [ObservableProperty] private string importInfo = "Aucun profil chargé. Sans profil, le micro est supposé linéaire.";

    // ----- bibliothèque intégrée -----
    [ObservableProperty] private ObservableCollection<MicProfile> libraryProfiles = new();
    [ObservableProperty] private MicProfile? selectedLibraryProfile;
    [ObservableProperty] private string librarySearch = "";

    // ----- édition de courbe -----
    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private string editStatus = "";
    [ObservableProperty] private int selectedPointIndex = -1;
    [ObservableProperty] private double selectedPointFreq;
    [ObservableProperty] private double selectedPointGain;
    [ObservableProperty] private string newProfileName = "";

    public bool HasSelection => SelectedProfile != null;
    public bool HasSelectedPoint => SelectedPointIndex >= 0;

    public event Action? SelectionChanged;
    public event Action? ProfileListChanged;
    public event Action? CurveEdited;

    public MicViewModel()
    {
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

        string name = SelectedLibraryProfile.Name;
        int suffix = 2;
        while (Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{SelectedLibraryProfile.Name} ({suffix++})";

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
        string name = string.IsNullOrWhiteSpace(NewProfileName) ? "Nouveau micro" : NewProfileName.Trim();
        int suffix = 2;
        while (Profiles.Any(p => string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase)))
            name = $"{(string.IsNullOrWhiteSpace(NewProfileName) ? "Nouveau micro" : NewProfileName.Trim())} ({suffix++})";

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

    // ----- édition de la courbe -----

    [RelayCommand]
    private void AddPoint()
    {
        if (SelectedProfile == null) return;
        AddPointAt(1000.0, 0.0);
    }

    /// <summary>Ajoute un point (retourne son index, -1 si impossible).</summary>
    public int AddPointAt(double freq, double gainDb)
    {
        if (SelectedProfile == null) return -1;
        var p = SelectedProfile;
        freq = Math.Clamp(freq, 20.0, 20000.0);
        gainDb = Math.Clamp(gainDb, -MicCurveEditor.YLimitDb, MicCurveEditor.YLimitDb);

        int idx = p.Frequencies.FindIndex(f => f > freq);
        if (idx < 0) idx = p.Frequencies.Count;
        p.Frequencies.Insert(idx, freq);
        p.GainsDb.Insert(idx, gainDb);

        SelectedPointIndex = idx;
        SyncSelectedPointFields();
        MarkDirty($"Point ajouté à {freq:0.#} Hz.");
        return idx;
    }

    [RelayCommand]
    private void RemovePoint()
    {
        if (SelectedProfile == null || SelectedPointIndex < 0) return;
        RemovePointAt(SelectedPointIndex);
    }

    public void RemovePointAt(int index)
    {
        if (SelectedProfile == null) return;
        if (SelectedProfile.Frequencies.Count <= 5)
        {
            EditStatus = "Un profil doit conserver au moins 5 points.";
            return;
        }
        if (index < 0 || index >= SelectedProfile.Frequencies.Count) return;

        SelectedProfile.Frequencies.RemoveAt(index);
        SelectedProfile.GainsDb.RemoveAt(index);
        SelectedPointIndex = -1;
        SyncSelectedPointFields();
        MarkDirty("Point supprimé.");
    }

    /// <summary>Déplace un point (appelé par l'éditeur graphique pendant le glissement).</summary>
    public void MovePoint(int index, double freq, double gainDb)
    {
        if (SelectedProfile == null) return;
        var p = SelectedProfile;
        if (index < 0 || index >= p.Frequencies.Count) return;

        freq = Math.Clamp(freq, 20.0, 20000.0);
        gainDb = Math.Clamp(gainDb, -MicCurveEditor.YLimitDb, MicCurveEditor.YLimitDb);

        double lo = index > 0 ? p.Frequencies[index - 1] * 1.0001 : 20.0;
        double hi = index < p.Frequencies.Count - 1 ? p.Frequencies[index + 1] * 0.9999 : 20000.0;
        if (hi < lo) hi = lo;
        p.Frequencies[index] = Math.Clamp(freq, lo, hi);
        p.GainsDb[index] = gainDb;

        SelectedPointIndex = index;
        SyncSelectedPointFields();
        IsDirty = true;
    }

    /// <summary>Édition numérique du point sélectionné (champs fréquence / gain).</summary>
    [RelayCommand]
    private void ApplySelectedPoint()
    {
        if (SelectedProfile == null || SelectedPointIndex < 0) return;
        MovePoint(SelectedPointIndex, SelectedPointFreq, SelectedPointGain);
        MarkDirty("Point modifié.");
    }

    [RelayCommand]
    private void SmoothCurve()
    {
        if (SelectedProfile is not { IsValid: true } p) return;
        int n = p.GainsDb.Count;
        if (n < 3) return;

        var gains = new double[n];
        for (int i = 0; i < n; i++)
        {
            double prev = p.GainsDb[Math.Max(0, i - 1)];
            double next = p.GainsDb[Math.Min(n - 1, i + 1)];
            gains[i] = 0.25 * prev + 0.5 * p.GainsDb[i] + 0.25 * next;
        }
        for (int i = 0; i < n; i++) p.GainsDb[i] = Math.Round(gains[i], 2);

        SyncSelectedPointFields();
        MarkDirty("Courbe lissée.");
    }

    /// <summary>Ramène tous les gains du profil sélectionné à 0 dB (profil plat).</summary>
    [RelayCommand]
    private void FlattenCurve()
    {
        if (SelectedProfile == null) return;
        for (int i = 0; i < SelectedProfile.GainsDb.Count; i++)
            SelectedProfile.GainsDb[i] = 0.0;
        SyncSelectedPointFields();
        MarkDirty("Profil ramené à plat (0 dB partout).");
    }

    [RelayCommand]
    private void SaveEdits()
    {
        if (SelectedProfile == null) return;
        if (!SelectedProfile.IsValid)
        {
            EditStatus = "Profil invalide (au moins 5 points requis).";
            return;
        }
        _store.Save(SelectedProfile);
        IsDirty = false;
        EditStatus = $"« {SelectedProfile.Name} » enregistré ({SelectedProfile.PointCount} points).";
        ImportInfo = EditStatus;
        CurveEdited?.Invoke();
        ProfileListChanged?.Invoke();
    }

    [RelayCommand]
    private void RevertEdits()
    {
        if (SelectedProfile == null) return;
        var fresh = _store.Load(SelectedProfile.Name);
        if (fresh == null)
        {
            EditStatus = "Impossible de restaurer : le profil n'a pas encore été enregistré.";
            return;
        }
        int idx = Profiles.IndexOf(SelectedProfile);
        if (idx >= 0) Profiles[idx] = fresh;
        SelectedProfile = fresh;
        IsDirty = false;
        SelectedPointIndex = -1;
        SyncSelectedPointFields();
        EditStatus = "Modifications annulées (dernière version enregistrée restaurée).";
        CurveEdited?.Invoke();
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

    partial void OnSelectedPointIndexChanged(int value) => SyncSelectedPointFields();

    public void SelectPoint(int index)
    {
        if (SelectedPointIndex == index) return;
        SelectedPointIndex = index; // SyncSelectedPointFields via OnSelectedPointIndexChanged
    }

    private void SyncSelectedPointFields()
    {
        if (SelectedProfile is { IsValid: true } p && SelectedPointIndex >= 0 && SelectedPointIndex < p.Frequencies.Count)
        {
            SelectedPointFreq = p.Frequencies[SelectedPointIndex];
            SelectedPointGain = p.GainsDb[SelectedPointIndex];
        }
        else
        {
            SelectedPointFreq = 0;
            SelectedPointGain = 0;
        }
        OnPropertyChanged(nameof(HasSelectedPoint));
        OnPropertyChanged(nameof(HasSelection));
    }

    private void MarkDirty(string message)
    {
        IsDirty = true;
        EditStatus = message;
        CurveEdited?.Invoke();
    }
}
