using RoomEq.Core.Eq;
using Xunit;

namespace RoomEq.Core.Tests;

/// <summary>
/// Magasin des liaisons preset ↔ périphérique de sortie : persistance JSON,
/// priorités (manuel > calibration > auto) et nettoyage.
/// </summary>
public class DevicePresetStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly DevicePresetStore _store;

    public DevicePresetStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "roomeq-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _store = new DevicePresetStore(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* temp */ }
    }

    [Fact]
    public void EmptyStore_ReturnsNoBinding()
    {
        Assert.Null(_store.Get("wasapi|abc"));
        Assert.Empty(_store.LoadAll());
    }

    [Fact]
    public void Set_AndGet_RoundTrips()
    {
        _store.Set(new DevicePresetBinding
        {
            DeviceId = "wasapi|abc",
            DeviceName = "Casque (JBL Tune Flex 2 Stereo)",
            PresetClass = "factory",
            PresetRef = "jbltuneflex2",
            Kind = DevicePresetKind.Manual,
        });

        var b = _store.Get("wasapi|abc");
        Assert.NotNull(b);
        Assert.Equal("factory", b!.PresetClass);
        Assert.Equal("jbltuneflex2", b.PresetRef);
        Assert.Equal(DevicePresetKind.Manual, b.Kind);
        Assert.True(b.IsUsable);

        // une seconde écriture REMPLACE (un périphérique = une liaison)
        _store.Set(new DevicePresetBinding
        {
            DeviceId = "WASAPI|ABC",   // insensible à la casse
            DeviceName = "Casque",
            PresetClass = "user",
            PresetRef = "Ma calibration",
            Kind = DevicePresetKind.Calibration,
        });
        Assert.Single(_store.LoadAll());
        Assert.Equal("Ma calibration", _store.Get("wasapi|abc")!.PresetRef);
    }

    [Fact]
    public void Set_RejectsDeviceIdLessBinding()
    {
        Assert.Throws<ArgumentException>(() => _store.Set(new DevicePresetBinding()));
    }

    [Fact]
    public void Remove_DeletesOnlyTheTarget()
    {
        _store.Set(Binding("dev1", "user", "A", DevicePresetKind.Manual));
        _store.Set(Binding("dev2", "user", "B", DevicePresetKind.Manual));

        Assert.True(_store.Remove("dev1"));
        Assert.Null(_store.Get("dev1"));
        Assert.NotNull(_store.Get("dev2"));

        Assert.False(_store.Remove("dev1"));   // déjà supprimé
        Assert.False(_store.Remove("inconnu"));
    }

    [Fact]
    public void CorruptedFile_FallsBackToEmpty()
    {
        File.WriteAllText(Path.Combine(_dir, "devicepresets.json"), "{ this is not json");
        Assert.Null(_store.Get("dev1"));
        _store.Set(Binding("dev1", "user", "A", DevicePresetKind.Manual));
        Assert.NotNull(_store.Get("dev1"));   // réécriture propre après l'incident
    }

    [Fact]
    public void NoneBinding_IsPersistedButNotUsable()
    {
        _store.Set(new DevicePresetBinding
        {
            DeviceId = "dev1",
            DeviceName = "Enceintes bureau",
            Kind = DevicePresetKind.None,
        });
        var b = _store.Get("dev1");
        Assert.NotNull(b);
        Assert.False(b!.IsUsable);            // « aucun preset » est un choix, pas un preset
    }

    private static DevicePresetBinding Binding(string dev, string cls, string reference, DevicePresetKind kind)
        => new()
        {
            DeviceId = dev,
            DeviceName = "Périphérique " + dev,
            PresetClass = cls,
            PresetRef = reference,
            Kind = kind,
        };
}
