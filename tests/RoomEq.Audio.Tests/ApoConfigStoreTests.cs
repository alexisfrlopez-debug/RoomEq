using RoomEq.Audio.SystemApo;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Magasin de courbe active APO (spec I-AUD-09) : aller-retour preset,
/// révision croissante, bypass sans perte de courbe, écriture atomique
/// (aucun .tmp résiduel), répertoire isolé pour les tests.
/// </summary>
public class ApoConfigStoreTests : IDisposable
{
    private readonly string _dir =
        Path.Combine(Path.GetTempPath(), "roomeq-apo-store-" + Guid.NewGuid().ToString("N"));

    public ApoConfigStoreTests()
    {
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* temp */ }
    }

    private static EqPreset DemoPreset(double preamp = -2.5) => new("Correction pièce")
    {
        PreampDb = preamp,
        Bands =
        {
            new EqBand(FilterType.Peaking, 120.0, 5.5, 1.2),
            new EqBand(FilterType.LowShelf, 80.0, 3.0, 0.9, enabled: false),
            new EqBand(FilterType.Notch, 7860.0, 0.0, 8.0),
        },
    };

    [Fact]
    public void WriteCurve_ThenRead_RoundTripsExactly()
    {
        var store = new ApoConfigStore(_dir);
        var preset = DemoPreset();

        store.WriteCurve(preset, bypass: false);

        var read = store.Refresh();
        Assert.NotNull(read);
        Assert.Equal("Correction pièce", read!.Name);
        Assert.Equal(-2.5, read.PreampDb, 9);
        Assert.Equal(3, read.Bands.Count);
        Assert.Equal(FilterType.Peaking, read.Bands[0].Type);
        Assert.Equal(120.0, read.Bands[0].Frequency, 9);
        Assert.Equal(5.5, read.Bands[0].GainDb, 9);
        Assert.Equal(1.2, read.Bands[0].Q, 9);
        Assert.False(read.Bands[1].Enabled);      // bande désactivée conservée
        Assert.Equal(FilterType.Notch, read.Bands[2].Type);
        Assert.False(read.Bypass);
        Assert.Equal(1, read.Revision);
    }

    [Fact]
    public void WriteCurve_IncrementsRevision_EachTime()
    {
        var store = new ApoConfigStore(_dir);

        store.WriteCurve(DemoPreset(), false);
        store.WriteCurve(DemoPreset(), false);
        store.WriteCurve(DemoPreset(), true);

        Assert.Equal(3, store.Refresh()!.Revision);
        Assert.True(store.Refresh()!.Bypass);
    }

    [Fact]
    public void SetBypass_TogglesWithoutLosingCurve()
    {
        var store = new ApoConfigStore(_dir);
        store.WriteCurve(DemoPreset(), bypass: false);

        store.SetBypass(true);
        var read = store.Refresh();
        Assert.True(read!.Bypass);
        Assert.Equal(3, read.Bands.Count);         // la courbe est intacte
        Assert.Equal(2, read.Revision);

        store.SetBypass(false);
        Assert.False(store.Refresh()!.Bypass);

        // idempotent : un second appel sans changement n'écrit pas
        long rev = store.Refresh()!.Revision;
        store.SetBypass(false);
        Assert.Equal(rev, store.Refresh()!.Revision);
    }

    [Fact]
    public void WriteCurve_IsAtomic_NoTempLeftBehind()
    {
        var store = new ApoConfigStore(_dir);

        store.WriteCurve(DemoPreset(), false);
        store.WriteCurve(DemoPreset(), false);
        store.SetBypass(true);

        Assert.True(File.Exists(store.ActiveCurvePath));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp")); // jamais de résidu
        var files = Directory.GetFiles(_dir, "*");
        Assert.Single(files);                             // active.json seul
        Assert.Equal(store.ActiveCurvePath, files[0]);
    }

    [Fact]
    public void Read_MissingFile_ReturnsNull()
    {
        var store = new ApoConfigStore(_dir);
        Assert.Null(store.Refresh());
    }

    [Fact]
    public void Read_CorruptedFile_ReturnsNull_WithoutThrowing()
    {
        File.WriteAllText(Path.Combine(_dir, "active.json"), "{ pas du JSON");
        var store = new ApoConfigStore(_dir);
        Assert.Null(store.Refresh());
    }

    [Fact]
    public void ApoLogLastWrite_MissingFile_IsNull()
    {
        var store = new ApoConfigStore(_dir);
        Assert.Null(store.ApoLogLastWriteUtc);
    }

    // ================================================== journal de l'APO (v3.9.5)

    [Fact]
    public void ReadApoLogTail_RetourneLesDernieresLignes()
    {
        var lines = Enumerable.Range(1, 20)
            .Select(i => "ligne " + i + (i == 12 ? "\r" : "")); // \r\n toléré
        File.WriteAllText(Path.Combine(_dir, "apo.log"), string.Join("\n", lines));

        var tail = new ApoConfigStore(_dir).ReadApoLogTail(maxLines: 5);

        Assert.Equal(5, tail.Count);
        Assert.Equal("ligne 16", tail[0]);
        Assert.Equal("ligne 20", tail[^1]);
    }

    [Fact]
    public void ReadApoLogTail_FichierCourtOuAbsent_ListeVideOuComplete()
    {
        var store = new ApoConfigStore(_dir);

        // absent : vide, sans exception (distingué de « illisible » par la date)
        Assert.Empty(store.ReadApoLogTail());

        File.WriteAllText(Path.Combine(_dir, "apo.log"),
            "LockForProcess : 48000 Hz, 2 canaux\nnouvelle courbe publiee (rev 7, active)");
        var tail = store.ReadApoLogTail(8);
        Assert.Equal(2, tail.Count);
        Assert.Contains("nouvelle courbe publiee (rev 7, active)", tail);
    }
}
