using RoomEq.Core.Io;
using Xunit;

namespace RoomEq.Core.Tests;

/// <summary>
/// Télémétrie de la version de test : nommage Drive des journaux (unique par
/// machine), sélection des fichiers du jour + de la veille, repli mémoire du
/// journal de diagnostic, et configuration inactive par défaut.
/// </summary>
public class TelemetryTests : IDisposable
{
    private readonly string _tmp;

    public TelemetryTests()
    {
        _tmp = Path.Combine(Path.GetTempPath(), "roomeq-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tmp);
    }

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { }
    }

    // ------------------------------------------------------------ nommage

    [Fact]
    public void DriveNameFor_PrefixesMachineAndKeepsDayFile()
    {
        string name = DriveLogUploader.DriveNameFor("PC-Studio", "roomeq-2026-10-10.log");
        Assert.Equal("PC-Studio-roomeq-2026-10-10.log", name);
    }

    [Theory]
    [InlineData("MON-PC", "MON-PC")]
    [InlineData("bureau d'Anna", "bureau-d-Anna")]
    [InlineData("labo/démo #2", "labo-démo--2")]
    public void Sanitize_ReplacesUnsafeCharacters(string input, string expected)
    {
        Assert.Equal(expected, DriveLogUploader.Sanitize(input));
    }

    // ------------------------------------------------------------ collecte

    [Fact]
    public void CollectLogFiles_TakesTodayAndYesterdayOnly_OnceEach()
    {
        string dir = Path.Combine(_tmp, "logs");
        Directory.CreateDirectory(dir);

        string today = DateTime.Now.ToString("yyyy-MM-dd");
        string yesterday = DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");
        string old = DateTime.Now.AddDays(-9).ToString("yyyy-MM-dd");

        File.WriteAllText(Path.Combine(dir, $"roomeq-{today}.log"), "aujourd'hui");
        File.WriteAllText(Path.Combine(dir, $"roomeq-{yesterday}.log"), "hier");
        File.WriteAllText(Path.Combine(dir, $"roomeq-{old}.log"), "trop ancien");
        File.WriteAllText(Path.Combine(dir, "autre.txt"), "pas un journal");

        var files = DriveLogUploader.CollectLogFiles("PC-1", new[] { dir }, includeMemoryFallback: false);

        Assert.Equal(2, files.Count);
        Assert.All(files, f => Assert.StartsWith("PC-1-roomeq-", f.DriveName));
        Assert.Equal(2, files.Select(f => f.DriveName).Distinct().Count());
        Assert.Contains(files, f => f.DriveName.EndsWith($"roomeq-{today}.log"));
        Assert.Contains(files, f => f.DriveName.EndsWith($"roomeq-{yesterday}.log"));
    }

    [Fact]
    public void CollectLogFiles_DuplicateAcrossDirs_IsSentOnce()
    {
        string d1 = Path.Combine(_tmp, "a"), d2 = Path.Combine(_tmp, "b");
        Directory.CreateDirectory(d1);
        Directory.CreateDirectory(d2);
        string name = $"roomeq-{DateTime.Now:yyyy-MM-dd}.log";
        File.WriteAllText(Path.Combine(d1, name), "1");
        File.WriteAllText(Path.Combine(d2, name), "2");

        var files = DriveLogUploader.CollectLogFiles("PC-2", new[] { d1, d2 }, includeMemoryFallback: false);

        Assert.Single(files);
    }

    [Fact]
    public void CollectLogFiles_MemoryFallback_IsIncluded()
    {
        // écrire dans le journal pour remplir le tampon mémoire
        DiagnosticsLog.Info("Test télémétrie : entrée mémoire distinctive 42.");

        var files = DriveLogUploader.CollectLogFiles("PC-3", Array.Empty<string>(), includeMemoryFallback: true);

        Assert.Contains(files, f => f.DriveName.Contains("roomeq-memoire-"));
        var memory = files.First(f => f.DriveName.Contains("memoire"));
        string content = File.ReadAllText(memory.LocalPath);
        Assert.Contains("entrée mémoire distinctive 42", content);
    }

    // ------------------------------------------------------------ journal

    [Fact]
    public void DiagnosticsLog_MemoryDump_KeepsMostRecentLines()
    {
        // dépasser la capacité (600) : les lignes les plus anciennes sont éjectées
        for (int i = 0; i < 700; i++)
            DiagnosticsLog.Info($"sonde mémoire {i}");

        string dump = DiagnosticsLog.MemoryDump();
        Assert.Contains("sonde mémoire 699", dump);
        Assert.DoesNotContain("sonde mémoire 0\n", dump + "\n");
        Assert.DoesNotContain("sonde mémoire 99\n", dump + "\n");
    }

    // ------------------------------------------------------------ configuration

    [Fact]
    public void TelemetryConfig_MissingFile_IsDisabled()
    {
        // dossier vide : pas de telemetrie.json → null (télémétrie désactivée)
        Assert.Null(TelemetryConfig.LoadIn(_tmp));
    }

    [Fact]
    public void TelemetryConfig_IncompleteCredentials_AreDisabled()
    {
        File.WriteAllText(Path.Combine(_tmp, "telemetrie.json"),
            """{"enabled": true, "client_id": "x", "folder_id": "f"}""");
        var cfg = TelemetryConfig.LoadIn(_tmp)!;
        Assert.False(cfg.Enabled);
    }

    [Fact]
    public void TelemetryConfig_Complete_IsEnabled()
    {
        File.WriteAllText(Path.Combine(_tmp, "telemetrie.json"),
            """{"enabled": true, "client_id": "x", "client_secret": "s", "refresh_token": "r", "folder_id": "f"}""");
        var cfg = TelemetryConfig.LoadIn(_tmp)!;
        Assert.True(cfg.Enabled);
        Assert.Equal("f", cfg.FolderId);
    }
}
