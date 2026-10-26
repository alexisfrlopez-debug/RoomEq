using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using RoomEq.App.Controls;
using RoomEq.Core.Calibration;

namespace RoomEq.App.ViewModels;

/// <summary>
/// Édition graphique et numérique de la courbe du profil sélectionné :
/// ajout / déplacement / suppression de points, lissage, aplatissement,
/// enregistrement et annulation — spec I-APP-05 (décomposition v3).
/// Rien n'est écrit sur disque hors de SaveEdits (IsDirty signale les
/// modifications en attente).
/// </summary>
public partial class MicViewModel
{
    [ObservableProperty] private bool isDirty;
    [ObservableProperty] private string editStatus = "";
    [ObservableProperty] private int selectedPointIndex = -1;
    [ObservableProperty] private double selectedPointFreq;
    [ObservableProperty] private double selectedPointGain;
    [ObservableProperty] private string newProfileName = "";

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

        // conserver l'ordre des fréquences (bornes = voisins)
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
