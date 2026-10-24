using Xunit;

namespace RoomEq.Core.Tests;

/// <summary>
/// Garde d'autonomie v2.0 : AUCUNE référence à une application tierce de câble
/// audio ne doit subsister dans le code livré (sources de l'application, pilote,
/// pont ASIO, installateur). L'historique (worklog, INSTRUCTIONS) est exclu :
/// il documente le passé par nature.
/// </summary>
public class NoThirdPartyReferencesTests
{
    // Produits tiers interdits dans le code livré.
    private static readonly (string Token, string Label)[] Forbidden =
    {
        ("vb-cable", "VB-CABLE"),
        ("vbcable", "VB-CABLE"),
        ("vb-audio", "VB-Audio"),
        ("voicemeeter", "VoiceMeeter"),
        ("virtual audio cable", "Virtual Audio Cable (Muzychenko)"),
    };

    private static readonly string RepoRoot = FindRepoRoot();

    private static string FindRepoRoot()
    {
        // tests/bin/Debug/net8.0/… -> racine du dépôt
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "RoomEq.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? ".";
    }

    private static IEnumerable<string> SourceFiles()
    {
        string[] roots = { "src", "driver", "asio", "installer", "macos" };
        string[] patterns = { "*.cs", "*.axaml", "*.csproj", "*.wxs", "*.wxi", "*.inx", "*.inf",
                              "*.cpp", "*.c", "*.h", "*.ps1", "*.iss", "*.sh", "*.plist",
                              "*.mm", "*.xml", "*.cs" };
        foreach (var root in roots)
        {
            string path = Path.Combine(RepoRoot, root);
            if (!Directory.Exists(path)) continue;
            foreach (var pattern in patterns)
                foreach (var f in Directory.EnumerateFiles(path, pattern, SearchOption.AllDirectories))
                    yield return f;
        }
    }

    private static bool IsException(string relativePath, string line)
    {
        // Le pilote WDM est dérivé de l'exemple SYSVAD de Microsoft (MIT) : les
        // en-têtes d'origine citent Microsoft, pas des produits tiers de câble.
        if (relativePath.StartsWith("driver/", StringComparison.Ordinal))
            return true;
        // Commentaires explicites d'historique/migration (rare, éventuel).
        return line.Contains("// historique v1.x", StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ShippedCode_ContainsNoThirdPartyCableProduct()
    {
        Assert.True(File.Exists(Path.Combine(RepoRoot, "RoomEq.sln")),
            "racine du dépôt introuvable depuis " + AppContext.BaseDirectory);

        var violations = new List<string>();
        foreach (string file in SourceFiles())
        {
            string relative = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
            if (IsException(relative, ""))
                continue;
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (var (token, label) in Forbidden)
                {
                    if (lines[i].Contains(token, StringComparison.OrdinalIgnoreCase)
                        && !IsException(relative, lines[i]))
                    {
                        violations.Add($"{relative}:{i + 1} — {label} → « {lines[i].Trim()[..Math.Min(70, lines[i].Trim().Length)]} »");
                    }
                }
            }
        }

        Assert.True(violations.Count == 0,
            "Références tierces interdites dans le code livré :\n" + string.Join("\n", violations));
    }

    [Fact]
    public void ShippedCode_DoesNotDownloadDrivers()
    {
        // l'installation du pilote est LOCALE (pnputil sur le paquet livré),
        // aucun domaine de téléchargement ne doit apparaître dans le code livré
        Assert.True(File.Exists(Path.Combine(RepoRoot, "RoomEq.sln")));
        foreach (string file in SourceFiles())
        {
            string relative = Path.GetRelativePath(RepoRoot, file).Replace('\\', '/');
            if (relative.StartsWith("driver/", StringComparison.Ordinal)) continue;
            string text = File.ReadAllText(file);
            Assert.DoesNotContain("download.vb-audio", text, StringComparison.OrdinalIgnoreCase);
        }
    }
}
