using RoomEq.Core.Calibration;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using RoomEq.Core.Io;
using Xunit;

namespace RoomEq.Core.Tests;

/// <summary>Tests du renommage des presets et des profils micro (boutiques locales).</summary>
public class StoresRenameTests : IDisposable
{
    private readonly string _tmp;

    public StoresRenameTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "roomeq-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    private static EqPreset MakePreset(string name)
    {
        var p = new EqPreset(name) { PreampDb = -2.5, Description = "test" };
        p.Bands.Add(new EqBand(FilterType.Peaking, 100, 3, 1.4));
        p.Bands.Add(new EqBand(FilterType.Peaking, 5000, -4, 2.0));
        return p;
    }

    [Fact]
    public void PresetRename_ChangesFileAndKeepsContent()
    {
        var store = new PresetStore(Path.Combine(_tmp, "presets"));
        store.Save(MakePreset("Salle de concert"));

        Assert.True(store.Rename("Salle de concert", "Salon"));
        var list = store.List();
        Assert.Contains("Salon", list);
        Assert.DoesNotContain("Salle de concert", list);

        var loaded = store.Load("Salon");
        Assert.NotNull(loaded);
        Assert.Equal("Salon", loaded!.Name);
        Assert.Equal(-2.5, loaded.PreampDb);
        Assert.Equal(2, loaded.Bands.Count);
        Assert.Equal(3, loaded.Bands[0].GainDb);
    }

    [Fact]
    public void PresetRename_RefusesExistingTargetName()
    {
        var store = new PresetStore(Path.Combine(_tmp, "presets"));
        store.Save(MakePreset("A"));
        store.Save(MakePreset("B"));

        Assert.False(store.Rename("A", "B")); // nom déjà pris
        Assert.Contains("A", store.List());
        Assert.Contains("B", store.List());
    }

    [Fact]
    public void PresetRename_RefusesMissingOrEmptyNames()
    {
        var store = new PresetStore(Path.Combine(_tmp, "presets"));
        store.Save(MakePreset("A"));

        Assert.False(store.Rename("Inexistant", "X"));
        Assert.False(store.Rename("A", ""));
        Assert.False(store.Rename("A", "   "));
    }

    [Fact]
    public void PresetRename_SameNameCleansUpNameInFile()
    {
        var store = new PresetStore(Path.Combine(_tmp, "presets"));
        store.Save(MakePreset("Ancien"));
        // fichier construit avec un Name interne différent : le renommage « même nom »
        // resynchronise le contenu
        Assert.True(store.Rename("Ancien", "Ancien "));
        var loaded = store.Load("Ancien");
        Assert.NotNull(loaded);
        Assert.Equal("Ancien", loaded!.Name);
    }

    [Fact]
    public void MicProfileRename_WorksLikePresetRename()
    {
        var store = new MicProfileStore(Path.Combine(_tmp, "mics"));
        var profile = new MicProfile
        {
            Name = "UMIK personnel",
            Frequencies = new List<double> { 20, 100, 1000, 8000, 20000 },
            GainsDb = new List<double> { 0.2, 0.1, 0, -0.5, -1.2 }
        };
        store.Save(profile);

        Assert.True(store.Rename("UMIK personnel", "Mon UMIK"));
        var list = store.List();
        Assert.Contains("Mon UMIK", list);
        Assert.DoesNotContain("UMIK personnel", list);

        var loaded = store.Load("Mon UMIK");
        Assert.NotNull(loaded);
        Assert.Equal("Mon UMIK", loaded!.Name);
        Assert.Equal(5, loaded.PointCount);
        Assert.True(loaded.IsValid);
    }
}
