using System.Text.Json;
using RoomEq.Core.Eq;
using RoomEq.Core.Io;
using Xunit;

namespace RoomEq.Core.Tests;

// ===========================================================================
// M-IO-01 — Saveur d'application (spec I-IO-01)
// ===========================================================================
public class AppFlavorTests
{
    [Theory]
    [InlineData("RoomEq-test.exe", AppFlavorKind.Test)]
    [InlineData("C:\\Program Files\\RoomEq\\RoomEq-test.exe", AppFlavorKind.Test)]
    [InlineData("roomeq-TEST.exe", AppFlavorKind.Test)]          // casse ignorée
    [InlineData("RoomEq.exe", AppFlavorKind.Public)]
    [InlineData("RoomEq.App.exe", AppFlavorKind.Public)]          // développement = public (silence par défaut)
    [InlineData("RoomEq-public.exe", AppFlavorKind.Public)]
    [InlineData("", AppFlavorKind.Public)]
    public void FromExecutableName_DecidesByFilename(string name, AppFlavorKind expected)
    {
        Environment.SetEnvironmentVariable("ROOMEQ_TEST", null);
        Assert.Equal(expected, AppFlavor.FromExecutableName(name));
    }

    [Fact]
    public void EnvironmentVariable_ForcesTest()
    {
        Environment.SetEnvironmentVariable("ROOMEQ_TEST", "1");
        try
        {
            Assert.Equal(AppFlavorKind.Test, AppFlavor.FromExecutableName("RoomEq.exe"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("ROOMEQ_TEST", null);
        }
    }

    [Fact]
    public void DisplayName_MatchesCurrent()
        => Assert.Equal(AppFlavor.Current == AppFlavorKind.Test ? "test" : "public", AppFlavor.DisplayName);

    [Fact]
    public void PublicExePath_NeverThrows()
        => Assert.EndsWith("RoomEq.exe", AppFlavor.PublicExePath, StringComparison.OrdinalIgnoreCase);
}

// ===========================================================================
// M-IO-02 — Journal de diagnostic (spec I-IO-02)
// ===========================================================================
public class DiagnosticsTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "roomeq-tests-log-" + Guid.NewGuid().ToString("N"));

    public DiagnosticsTests()
    {
        Directory.CreateDirectory(_dir);
        DiagnosticsLog.ResetForTests();      // le journal est statique : état neuf par test
        DiagnosticsLog.OverrideDir = _dir;
        DiagnosticsLog.Enabled = true;
    }

    public void Dispose()
    {
        DiagnosticsLog.OverrideDir = null;
        DiagnosticsLog.Enabled = true;
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private string TodayPath() => Path.Combine(_dir,
        "roomeq-" + DateTime.Now.ToString("yyyy-MM-dd") + ".log");

    [Fact]
    public void InfoWarnError_AreWrittenWithFormat()
    {
        DiagnosticsLog.Info("phase un");
        DiagnosticsLog.Warn("attention");
        DiagnosticsLog.Error("échec");

        string content = File.ReadAllText(TodayPath());
        Assert.Contains("[INFO] phase un", content);
        Assert.Contains("[WARN] attention", content);
        Assert.Contains("[ERREUR] échec", content);
        Assert.StartsWith("# RoomEq — journal", content.TrimStart());
        // horodatage HH:mm:ss.fff présent
        Assert.Matches(@"\d{2}:\d{2}:\d{2}\.\d{3}", content);
    }

    [Fact]
    public void ErrorWithNestedException_IncludesAllLevelsAndStack()
    {
        Exception inner = new("cause racine");
        Exception middle = new("cause intermédiaire", inner);
        DiagnosticsLog.Error("opération", middle);

        string content = File.ReadAllText(TodayPath());
        Assert.Contains("opération", content);
        Assert.Contains(middle.GetType().Name, content);
        Assert.Contains("cause intermédiaire", content);
        Assert.Contains("↳", content);
        Assert.Contains("cause racine", content);
    }

    [Fact]
    public void NewlinesInMessage_AreFlattened()
    {
        DiagnosticsLog.Info("ligne 1\nligne 2");
        string content = File.ReadAllText(TodayPath());
        Assert.DoesNotContain("ligne 1\nligne 2", content);
        Assert.Contains("ligne 1 ⏎ ligne 2", content);
    }

    [Fact]
    public void Disabled_WritesNothingAnywhere()
    {
        DiagnosticsLog.Enabled = false;
        DiagnosticsLog.Info("ne doit pas apparaître");
        DiagnosticsLog.Error("ni ceci", new Exception("pile invisible"));

        Assert.False(File.Exists(TodayPath()), "aucun fichier ne doit être créé");
        Assert.Equal("", DiagnosticsLog.MemoryDump());
    }

    [Fact]
    public void MemoryDump_ReflectsLastLines()
    {
        DiagnosticsLog.Info("message mémoire");
        Assert.Contains("message mémoire", DiagnosticsLog.MemoryDump());
    }

    [Fact]
    public void UnwritableDir_FallsBackToMemoryOnly()
    {
        DiagnosticsLog.OverrideDir = Path.Combine(_dir, "sous-dossier-fichier"); // inexistant
        // un chemin sous un FICHIER (pas un dossier) rend la création impossible
        File.WriteAllText(DiagnosticsLog.OverrideDir, "je suis un fichier");
        try
        {
            DiagnosticsLog.Info("en mémoire seulement");
            Assert.Contains("en mémoire seulement", DiagnosticsLog.MemoryDump());
            // aucune exception levée : le journal ne casse jamais l'application
        }
        finally
        {
            DiagnosticsLog.OverrideDir = _dir;
        }
    }
}

// ===========================================================================
// M-IO-03 — Télémétrie (spec I-IO-03)
// ===========================================================================
public class TelemetryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "roomeq-tests-tel-" + Guid.NewGuid().ToString("N"));

    public TelemetryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    private void WriteConfig(string json, string file = "telemetrie.json")
        => File.WriteAllText(Path.Combine(_dir, file), json);

    private const string CompleteJson =
        """
        {"enabled": true, "client_id": "cid", "client_secret": "sec",
         "refresh_token": "ref", "folder_id": "fld", "token_uri": "https://oauth/x"}
        """;

    [Fact]
    public void CompleteConfig_IsEnabled()
    {
        WriteConfig(CompleteJson);
        var cfg = TelemetryConfig.LoadIn(_dir);
        Assert.NotNull(cfg);
        Assert.True(cfg!.Enabled);
        Assert.Equal("cid", cfg.ClientId);
        Assert.Equal("fld", cfg.FolderId);
    }

    [Fact]
    public void MissingFile_ReturnsNull()
        => Assert.Null(TelemetryConfig.LoadIn(_dir));

    [Theory]
    [InlineData("""{"enabled": false, "client_id": "c", "client_secret": "s", "refresh_token": "r", "folder_id": "f"}""")]
    [InlineData("""{"enabled": true, "client_id": "", "client_secret": "s", "refresh_token": "r", "folder_id": "f"}""")]
    [InlineData("""{"enabled": true, "client_id": "c", "client_secret": "s", "refresh_token": "", "folder_id": "f"}""")]
    [InlineData("""{"enabled": true, "client_id": "c", "client_secret": "s", "refresh_token": "r"}""")]
    [InlineData("pas du tout du json")]
    public void IncompleteOrBrokenConfigs_AreDisabled(string json)
    {
        WriteConfig(json);
        var cfg = TelemetryConfig.LoadIn(_dir);
        if (cfg != null)
            Assert.False(cfg.Enabled);
    }

    [Fact]
    public void DriveNameFor_SanitizesMachineAndLowercasesFile()
    {
        string name = DriveLogUploader.DriveNameFor("PC d'Alexis!", "roomeq-2026-10-25.LOG");
        // machine : casse conservée, non alphanumériques -> '-' ; fichier : bas de casse
        Assert.EndsWith("-roomeq-2026-10-25.log", name, StringComparison.Ordinal);
        Assert.DoesNotContain("'", name, StringComparison.Ordinal);
        Assert.DoesNotContain(' ', name);
        Assert.StartsWith("PC-d", name, StringComparison.Ordinal);
    }

    [Fact]
    public void Sanitize_KeepsAlnumDashUnderscore()
    {
        Assert.Equal("machine", DriveLogUploader.Sanitize(""));
        Assert.Equal("ab-cd-9", DriveLogUploader.Sanitize("ab cd\t9"));
        Assert.Equal("a_b-c", DriveLogUploader.Sanitize("a b".Replace(' ', '_') + "-c"));
    }

    [Fact]
    public void CollectLogFiles_KeepsTodayAndYesterday_Deduplicates()
    {
        string today = DateTime.Now.ToString("yyyy-MM-dd");
        string yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
        string old = DateTime.Now.AddDays(-5).ToString("yyyy-MM-dd");

        string dir = Path.Combine(_dir, "logs");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, $"roomeq-{today}.log"), "a");
        File.WriteAllText(Path.Combine(dir, $"roomeq-{yesterday}.log"), "b");
        File.WriteAllText(Path.Combine(dir, $"roomeq-{old}.log"), "trop vieux");
        // doublon du jour dans un second répertoire -> dédupliqué par nom Drive
        string dir2 = Path.Combine(_dir, "logs2");
        Directory.CreateDirectory(dir2);
        File.WriteAllText(Path.Combine(dir2, $"roomeq-{today}.log"), "doublon");

        var files = DriveLogUploader.CollectLogFiles("pc", new[] { dir, dir2 }, includeMemoryFallback: false);

        Assert.Equal(2, files.Count);
        Assert.Contains(files, f => f.DriveName.EndsWith($"roomeq-{today}.log", StringComparison.Ordinal));
        Assert.Contains(files, f => f.DriveName.EndsWith($"roomeq-{yesterday}.log", StringComparison.Ordinal));
        Assert.DoesNotContain(files, f => f.DriveName.Contains(old, StringComparison.Ordinal));
    }

    [Fact]
    public void CollectLogFiles_MemoryFallbackWhenNonEmpty()
    {
        // le tampon mémoire n'est pas vide (tests précédents) : un fichier temporaire est ajouté
        var files = DriveLogUploader.CollectLogFiles("pc", Array.Empty<string>(), includeMemoryFallback: true);
        if (DiagnosticsLog.MemoryDump().Length > 0)
            Assert.Contains(files, f => f.DriveName.Contains("memoire", StringComparison.Ordinal));
    }

    [Fact]
    public void DayFromName_RecognizesDates()
    {
        Assert.Equal("2026-10-25", DriveLogUploader.DayFromName("roomeq-2026-10-25.log"));
        Assert.Equal("2026-10-25", DriveLogUploader.DayFromName("pc-roomeq-2026-10-25.log"));
        Assert.Equal("", DriveLogUploader.DayFromName("nimporte.log"));
    }
}

// ===========================================================================
// M-IO-04 — Catalogue de signaux (spec I-IO-04)
// ===========================================================================
public class SignalManifestTests
{
    private static string Valid(string overrides = "") =>
        $$"""
        {"signals": [
          {"id": "bruit-gris", "displayName": "Bruit gris", "description": "Égale sonie",
           "fileName": "bruit-gris-10s.wav", "bytes": 123456, "sha256": "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789"{{overrides}}}
        ]}
        """;

    [Fact]
    public void ValidManifest_Parses()
    {
        Assert.True(SignalManifest.TryParse(Valid(), out var signals, out var error));
        Assert.Equal("", error);
        var s = Assert.Single(signals);
        Assert.Equal("bruit-gris", s.Id);
        Assert.Equal("bruit-gris-10s.wav", s.FileName);
        Assert.Equal(123456, s.Bytes);
    }

    [Theory]
    [InlineData("pas du json", "JSON invalide")]
    [InlineData("""{"signals": {}}""", "tableau")]
    [InlineData("""{"signals": []}""", "aucun signal")]
    [InlineData("""{"signals": [{"id":"a b","displayName":"x","description":"","fileName":"f.wav","bytes":10,"sha256":"0000000000000000000000000000000000000000000000000000000000000000"}]}""", "identifiant incorrect")]
    [InlineData("""{"signals": [{"id":"ok","displayName":"","description":"","fileName":"f.wav","bytes":10,"sha256":"0000000000000000000000000000000000000000000000000000000000000000"}]}""", "nom d'affichage incorrect")]
    [InlineData("""{"signals": [{"id":"ok","displayName":"x","description":"","fileName":"../escape.wav","bytes":10,"sha256":"0000000000000000000000000000000000000000000000000000000000000000"}]}""", "nom de fichier refusé")]
    [InlineData("""{"signals": [{"id":"ok","displayName":"x","description":"","fileName":"f.txt","bytes":10,"sha256":"0000000000000000000000000000000000000000000000000000000000000000"}]}""", "nom de fichier refusé")]
    [InlineData("""{"signals": [{"id":"ok","displayName":"x","description":"","fileName":"f.wav","bytes":50000000,"sha256":"0000000000000000000000000000000000000000000000000000000000000000"}]}""", "taille hors bornes")]
    [InlineData("""{"signals": [{"id":"ok","displayName":"x","description":"","fileName":"f.wav","bytes":10,"sha256":"COURT"}]}""", "empreinte SHA-256 incorrecte")]
    public void InvalidManifests_AreRejectedWithClearError(string json, string expectedFragment)
    {
        Assert.False(SignalManifest.TryParse(json, out _, out string error));
        Assert.Contains(expectedFragment, error, StringComparison.Ordinal);
    }

    [Fact]
    public void DuplicateIdsAndFiles_AreRejected()
    {
        string dup = """
            {"signals": [
              {"id":"a","displayName":"x","description":"","fileName":"f.wav","bytes":10,"sha256":"0000000000000000000000000000000000000000000000000000000000000000"},
              {"id":"a","displayName":"y","description":"","fileName":"g.wav","bytes":10,"sha256":"0000000000000000000000000000000000000000000000000000000000000000"}
            ]}
            """;
        Assert.False(SignalManifest.TryParse(dup, out _, out var error));
        Assert.Contains("doublon", error, StringComparison.Ordinal);
    }

    [Fact]
    public void MoreThan50Signals_Rejected()
    {
        string json = JsonSerializer.Serialize(new
        {
            signals = Enumerable.Range(0, 51).Select(i => new
            {
                id = $"sig-{i}", displayName = $"S{i}", description = "",
                fileName = $"s{i}.wav", bytes = 10,
                sha256 = "0000000000000000000000000000000000000000000000000000000000000000"
            })
        });
        Assert.False(SignalManifest.TryParse(json, out _, out var error));
        Assert.Contains("50", error, StringComparison.Ordinal);
    }
}

public class SignalUrlsTests
{
    [Theory]
    [InlineData("https://raw.githubusercontent.com/alexisfrlopez-debug/RoomEq/main/signaux/manifest.json", true)]
    [InlineData("https://raw.githubusercontent.com/alexisfrlopez-debug/RoomEq/main/signaux/bruit-gris-10s.wav", true)]
    [InlineData("http://raw.githubusercontent.com/alexisfrlopez-debug/RoomEq/main/signaux/f.wav", false)]   // pas https
    [InlineData("https://evil.com/alexisfrlopez-debug/RoomEq/main/signaux/f.wav", false)]                  // mauvais domaine
    [InlineData("https://raw.githubusercontent.com/autre/RoomEq/main/signaux/f.wav", false)]               // autre dépôt
    [InlineData("https://raw.githubusercontent.com/alexisfrlopez-debug/RoomEq/main/autre/f.wav", false)]   // autre dossier
    [InlineData("https://raw.githubusercontent.com/alexisfrlopez-debug/RoomEq/main/signaux/../secret.wav", false)] // traversal
    [InlineData("https://raw.githubusercontent.com/alexisfrlopez-debug/RoomEq/main/signaux/f.wav?x=1", false)]     // query
    [InlineData("https://raw.githubusercontent.com/alexisfrlopez-debug/RoomEq/main/signaux/f.wav#frag", false)]    // fragment
    public void AllowedUrls_AreStrictlyFiltered(string url, bool expected)
        => Assert.Equal(expected, SignalUrls.IsAllowedFileUrl(url));
}

public class SignalStoreTests : IDisposable
{
    private readonly SignalStore _store = new(Path.Combine(Path.GetTempPath(),
        "roomeq-tests-store-" + Guid.NewGuid().ToString("N")));

    public void Dispose()
    {
        try { Directory.Delete(_store.DirectoryPath, true); } catch { /* best effort */ }
    }

    private static (byte[] Content, string Sha) MakeContent()
    {
        byte[] content = new byte[1000];
        new Random(42).NextBytes(content);
        string sha = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)).ToLowerInvariant();
        return (content, sha);
    }

    [Fact]
    public void SaveAndList_RoundTrip()
    {
        var (content, sha) = MakeContent();
        Assert.True(_store.Save("bruit-gris-10s.wav", content, sha, "Bruit gris"));

        var list = _store.List();
        var file = Assert.Single(list);
        Assert.Equal("bruit-gris-10s.wav", file.FileName);
        Assert.Equal("Bruit gris", file.DisplayName);
        Assert.Equal(1000, file.Bytes);
        Assert.True(_store.Contains("bruit-gris-10s.wav", 1000));
    }

    [Fact]
    public void WrongSha256_IsRejected()
    {
        var (content, _) = MakeContent();
        Assert.Throws<InvalidDataException>(
            () => _store.Save("f.wav", content, "0000000000000000000000000000000000000000000000000000000000000000", "x"));
        Assert.Empty(_store.List());
    }

    [Theory]
    [InlineData("../escape.wav")]
    [InlineData("sous/dossier.wav")]
    [InlineData(".cache.wav")]
    [InlineData("fichier.txt")]
    public void UnsafeFileNames_AreRejected(string fileName)
    {
        var (content, sha) = MakeContent();
        Assert.Throws<ArgumentException>(() => _store.Save(fileName, content, sha, "x"));
    }

    [Fact]
    public void EmptyContent_IsRejected()
    {
        Assert.Throws<ArgumentException>(
            () => _store.Save("f.wav", Array.Empty<byte>(), "0000000000000000000000000000000000000000000000000000000000000000", "x"));
    }

    [Fact]
    public void CorruptedIndex_FallsBackToFileName()
    {
        var (content, sha) = MakeContent();
        _store.Save("multi-tones.wav", content, sha, "Multi-tones");
        File.WriteAllText(Path.Combine(_store.DirectoryPath, "index.json"), "{cassé");
        var file = Assert.Single(_store.List());
        Assert.Equal("multi-tones", file.DisplayName); // repli : nom sans extension
    }
}

// ===========================================================================
// M-IO-05 — Magasins nommés (spec I-IO-05)
// ===========================================================================
public class StoresTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "roomeq-tests-stores-" + Guid.NewGuid().ToString("N"));

    public StoresTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    [Fact]
    public void PresetRoundTrip_ListSorted()
    {
        var store = new PresetStore(_dir);
        var preset = EqPreset.Flat(4);
        preset.Name = "Salon";
        preset.Bands[1].GainDb = 3.5;
        store.Save(preset);
        store.Save(new EqPreset("Amd"));
        store.Save(new EqPreset("zoo"));

        Assert.Equal(new[] { "Amd", "Salon", "zoo" }, store.List()); // tri casse ignorée

        var loaded = store.Load("Salon");
        Assert.NotNull(loaded);
        Assert.Equal(4, loaded!.Bands.Count);
        Assert.Equal(3.5, loaded.Bands[1].GainDb);
        Assert.Null(store.Load("Inconnu"));
    }

    [Fact]
    public void Rename_MovesIdentityAndContent()
    {
        var store = new PresetStore(_dir);
        var preset = EqPreset.Flat(3);
        preset.Name = "Ancien";
        store.Save(preset);

        Assert.True(store.Rename("Ancien", "Nouveau"));
        Assert.DoesNotContain("Ancien", store.List());
        Assert.Contains("Nouveau", store.List());
        var loaded = store.Load("Nouveau");
        Assert.NotNull(loaded);
        Assert.Equal("Nouveau", loaded!.Name);
        Assert.Equal(3, loaded.Bands.Count);
    }

    [Fact]
    public void Rename_RefusesConflictsAndMissing()
    {
        var store = new PresetStore(_dir);
        store.Save(new EqPreset("A"));
        store.Save(new EqPreset("B"));
        Assert.False(store.Rename("A", "B"));      // destination prise
        Assert.False(store.Rename("Manquant", "C")); // source absente
        Assert.False(store.Rename("A", "  "));     // nom vide
    }

    [Fact]
    public void SanitizeFileName_ReplacesInvalidChars()
    {
        string sanitized = AppPaths.SanitizeFileName("a<b>c:d\"e/f\\g|h?i*j");
        Assert.DoesNotContain('<', sanitized);
        Assert.DoesNotContain(':', sanitized);
        Assert.DoesNotContain('/', sanitized);
        Assert.DoesNotContain('?', sanitized);
    }

    [Fact]
    public void Delete_MissingIsSilent()
    {
        var store = new PresetStore(_dir);
        store.Delete("Jamais existé"); // pas d'exception
        store.Save(new EqPreset("X"));
        store.Delete("X");
        Assert.Empty(store.List());
    }
}
