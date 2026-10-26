using RoomEq.App.ViewModels;
using RoomEq.Core.Calibration;
using RoomEq.Core.Io;
using Xunit;

namespace RoomEq.App.Tests;

/// <summary>
/// Profils micro (spec I-APP-05) : création, édition de courbe (points,
/// bornes voisins, minimum 5 points, lissage, aplatissement), annulation,
/// noms uniques, renommage, import de fichiers et de la bibliothèque —
/// via un magasin en répertoire de test.
/// </summary>
public class MicViewModelTests : IDisposable
{
    private readonly string _dir;
    private readonly MicProfileStore _store;
    private readonly MicViewModel _vm;

    public MicViewModelTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "roomeq-mic-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _store = new MicProfileStore(_dir);
        _vm = new MicViewModel(_store);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* temp */ }
    }

    private void SelectFreshProfile()
    {
        _vm.CreateNewProfileCommand.Execute(null);
        var p = _vm.SelectedProfile!;
        // bosse nette pour que le lissage soit observable
        for (int i = 0; i < p.GainsDb.Count; i++)
            p.GainsDb[i] = i == 4 ? 8.0 : 0.0;
    }

    // ================================================================ création

    [Fact]
    public void CreateNewProfile_TenFlatPoints_GeometricallySpaced()
    {
        _vm.NewProfileName = "Micro de test";
        _vm.CreateNewProfileCommand.Execute(null);

        var p = _vm.SelectedProfile!;
        Assert.Equal("Micro de test", p.Name);
        Assert.Equal(10, p.PointCount);
        Assert.True(p.IsValid);
        Assert.All(p.GainsDb, g => Assert.Equal(0, g, 6));
        Assert.Equal(20.0, p.Frequencies[0], 1);
        Assert.Equal(20000.0, p.Frequencies[^1], 0); // arrondi à 20000
        // répartition géométrique : rapport constant entre points (arrondi 0,1 Hz
        // sur les premiers points → tolérance relative de 1 %)
        double expectedRatio = Math.Pow(20000.0 / 20.0, 1.0 / 9.0);
        for (int i = 1; i < p.PointCount; i++)
        {
            double ratio = p.Frequencies[i] / p.Frequencies[i - 1];
            Assert.True(Math.Abs(ratio - expectedRatio) / expectedRatio < 0.01,
                $"rapport {i} : {ratio} vs {expectedRatio}");
        }
        Assert.False(_vm.IsDirty);
        Assert.Contains(_store.List(), n => n == "Micro de test");
    }

    [Fact]
    public void CreateNewProfile_TwiceSameName_GetsUniqueSuffix()
    {
        _vm.NewProfileName = "Doublon";
        _vm.CreateNewProfileCommand.Execute(null);
        _vm.NewProfileName = "Doublon";
        _vm.CreateNewProfileCommand.Execute(null);

        Assert.Equal(2, _vm.Profiles.Count);
        Assert.Equal("Doublon (2)", _vm.Profiles[^1].Name);
    }

    // ================================================================ édition de courbe

    [Fact]
    public void AddPointAt_InsertsSorted_AndSelectsIt()
    {
        SelectFreshProfile();
        int idx = _vm.AddPointAt(500, 3.0);

        var p = _vm.SelectedProfile!;
        int found = p.Frequencies.FindIndex(f => Math.Abs(f - 500) < 0.01);
        Assert.Equal(idx, found);
        Assert.Equal(idx, _vm.SelectedPointIndex);
        // trié : le voisin précédent est plus bas
        Assert.True(p.Frequencies[idx - 1] < 500);
        Assert.True(_vm.IsDirty);
        Assert.Equal(500, _vm.SelectedPointFreq, 3);
        Assert.Equal(3.0, _vm.SelectedPointGain, 3);
    }

    [Fact]
    public void RemovePointAt_EnforcesMinimumFivePoints()
    {
        SelectFreshProfile();
        var p = _vm.SelectedProfile!;

        // redescendre à 5 points
        while (p.PointCount > 5)
            _vm.RemovePointAt(p.PointCount - 1);
        Assert.Equal(5, p.PointCount);

        _vm.RemovePointAt(0);
        Assert.Equal(5, p.PointCount); // refus
        Assert.Contains("au moins 5 points", _vm.EditStatus);
    }

    [Fact]
    public void MovePoint_IsBoundedByNeighbours()
    {
        SelectFreshProfile();
        var p = _vm.SelectedProfile!;

        // déplacer le point 3 bien au-delà du point 4 : borné par lui-même*0.9999
        double f4 = p.Frequencies[4];
        _vm.MovePoint(3, 19000, 5.0);
        Assert.True(p.Frequencies[3] < f4);
        Assert.Equal(5.0, p.GainsDb[3], 3);

        // en deçà du point 2 : borné par lui*1.0001
        double f2 = p.Frequencies[2];
        _vm.MovePoint(3, 21, -5.0);
        Assert.True(p.Frequencies[3] > f2);
        Assert.Equal(-5.0, p.GainsDb[3], 3);

        // index invalide : sans effet, sans lever
        _vm.MovePoint(-1, 100, 1);
        _vm.MovePoint(999, 100, 1);
    }

    [Fact]
    public void SmoothCurve_AveragesNeighbours()
    {
        SelectFreshProfile();
        var p = _vm.SelectedProfile!;

        _vm.SmoothCurveCommand.Execute(null);
        // le sommet 8 dB est atténué (0.25*0 + 0.5*8 + 0.25*0 = 4 dB)
        Assert.Equal(4.0, p.GainsDb[4], 3);
        // les voisins remontent (0.25*8)
        Assert.Equal(2.0, p.GainsDb[3], 3);
        Assert.Equal(2.0, p.GainsDb[5], 3);
    }

    [Fact]
    public void FlattenCurve_ZeroesAllGains()
    {
        SelectFreshProfile();
        _vm.FlattenCurveCommand.Execute(null);
        Assert.All(_vm.SelectedProfile!.GainsDb, g => Assert.Equal(0, g, 6));
    }

    [Fact]
    public void SaveEdits_ThenRevertEdits_RoundTrip()
    {
        SelectFreshProfile();
        _vm.AddPointAt(500, 3.0);
        Assert.True(_vm.IsDirty);

        _vm.SaveEditsCommand.Execute(null);
        Assert.False(_vm.IsDirty);
        Assert.Contains("enregistré", _vm.EditStatus);
        Assert.Equal(11, _store.Load(_vm.SelectedProfile!.Name)!.PointCount);

        // modifications non enregistrées puis annulées : dernière version SAUVEGARDÉE restaurée
        _vm.AddPointAt(2000, 1.0);
        Assert.True(_vm.IsDirty);
        _vm.RevertEditsCommand.Execute(null);
        Assert.False(_vm.IsDirty);
        Assert.Equal(11, _vm.SelectedProfile!.PointCount);
    }

    [Fact]
    public void ApplySelectedPoint_UsesNumericFields()
    {
        SelectFreshProfile();
        _vm.SelectPoint(2);
        Assert.Equal(_vm.SelectedProfile!.Frequencies[2], _vm.SelectedPointFreq, 3);

        _vm.SelectedPointGain = -7.5;
        _vm.ApplySelectedPointCommand.Execute(null);
        Assert.Equal(-7.5, _vm.SelectedProfile!.GainsDb[2], 3);
        Assert.True(_vm.IsDirty);
    }

    [Fact]
    public void SelectPoint_SyncsFieldsWithoutSideEffects()
    {
        SelectFreshProfile();
        _vm.SelectPoint(0);
        Assert.True(_vm.HasSelectedPoint);
        Assert.True(_vm.HasSelection);

        _vm.SelectPoint(-1);
        Assert.False(_vm.HasSelectedPoint);
        Assert.Equal(0, _vm.SelectedPointFreq, 6);
    }

    // ================================================================ import / bibliothèque

    [Fact]
    public void ImportFromFile_ParsesCalFile()
    {
        string file = Path.Combine(_dir, "mon-micro.cal");
        File.WriteAllLines(file, new[]
        {
            "* fichier de calibration de test",
            "20 -1.5",
            "100 0",
            "1000 2.5",
            "5000 1",
            "20000 -3",
        });

        _vm.ImportFromFile(file);

        var p = _vm.SelectedProfile!;
        Assert.Equal("mon-micro", p.Name);
        Assert.Equal(5, p.PointCount);
        Assert.Equal(2.5, p.GainsDb[2], 3);
        Assert.Contains("5 points", _vm.ImportInfo);
        Assert.Contains(_store.List(), n => n == "mon-micro");
    }

    [Fact]
    public void ImportFromFile_Unrecognized_ExplainsItself()
    {
        string file = Path.Combine(_dir, "invalide.cal");
        File.WriteAllLines(file, new[] { "rien de lisible ici", "encore moins la" });
        int before = _vm.Profiles.Count;

        _vm.ImportFromFile(file);

        Assert.Equal(before, _vm.Profiles.Count);
        Assert.Contains("non reconnu", _vm.ImportInfo);
    }

    [Fact]
    public void ImportLibraryProfile_CopiesWithUniqueName()
    {
        var model = _vm.LibraryProfiles.First();
        int before = _vm.Profiles.Count;

        _vm.SelectedLibraryProfile = model;
        _vm.ImportLibraryProfileCommand.Execute(null);

        Assert.Equal(before + 1, _vm.Profiles.Count);
        var copy = _vm.SelectedProfile!;
        Assert.Equal(model.PointCount, copy.PointCount);
        Assert.Equal(model.Name, copy.Name);
        Assert.Contains("importé depuis la bibliothèque", _vm.EditStatus);
    }

    [Fact]
    public void LibrarySearch_Filters()
    {
        _vm.LibrarySearch = "zzzzzzzz-inexistant";
        Assert.Empty(_vm.LibraryProfiles);
        _vm.LibrarySearch = "";
        Assert.NotEmpty(_vm.LibraryProfiles);
    }

    // ================================================================ renommage / suppression

    [Fact]
    public void RenameProfile_RefusesDuplicate()
    {
        _vm.NewProfileName = "A";
        _vm.CreateNewProfileCommand.Execute(null);
        _vm.NewProfileName = "B";
        _vm.CreateNewProfileCommand.Execute(null);

        _vm.SelectedProfile = _vm.Profiles.First(p => p.Name == "A");
        _vm.NewProfileName = "B";
        _vm.RenameProfileCommand.Execute(null);
        Assert.Contains("existe déjà", _vm.EditStatus);
        Assert.Contains("A", _store.List());

        _vm.NewProfileName = "C";
        _vm.RenameProfileCommand.Execute(null);
        Assert.DoesNotContain("A", _store.List());
        Assert.Contains("C", _store.List());
    }

    [Fact]
    public void DeleteSelected_RemovesFromListAndStore()
    {
        _vm.NewProfileName = "Éphémère";
        _vm.CreateNewProfileCommand.Execute(null);
        Assert.Contains("Éphémère", _store.List());

        _vm.DeleteSelectedCommand.Execute(null);
        Assert.DoesNotContain("Éphémère", _store.List());
        Assert.DoesNotContain(_vm.Profiles, p => p.Name == "Éphémère");
        Assert.Contains("supprimé", _vm.ImportInfo);
    }
}
