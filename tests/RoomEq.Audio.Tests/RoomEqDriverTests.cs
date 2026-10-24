using RoomEq.Audio.Routing;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Gestion du pilote de la carte virtuelle RoomEq (DriverSetupManager) :
/// lignes de commande pnputil, analyse du magasin de pilotes, détection du
/// paquet livré, et fournisseurs (carte RoomEq intégrée + boucle WASAPI,
/// sans aucune application tierce).
/// </summary>
public class RoomEqDriverTests : IDisposable
{
    private readonly string _tmp = Path.Combine(Path.GetTempPath(),
        "roomeq-driver-tests-" + Guid.NewGuid().ToString("N")[..8]);

    public RoomEqDriverTests() => Directory.CreateDirectory(_tmp);

    public void Dispose()
    {
        try { Directory.Delete(_tmp, recursive: true); } catch { /* temp */ }
    }

    // ------------------------------------------------------------ pnputil

    [Fact]
    public void BuildInstallArguments_QuotesInfPath()
    {
        string args = DriverSetupManager.BuildInstallArguments(@"C:\Program Files\RoomEq\driver\roomeqvad.inf");
        Assert.StartsWith("/add-driver ", args);
        Assert.Contains("\"C:\\Program Files\\RoomEq\\driver\\roomeqvad.inf\"", args);
        Assert.EndsWith(" /install", args);
    }

    [Fact]
    public void BuildUninstallArguments_UsesDriverStoreName()
    {
        Assert.Equal("/delete-driver oem42.inf /uninstall /force",
            DriverSetupManager.BuildUninstallArguments("oem42.inf"));
    }

    [Theory]
    [InlineData("en",
        "Published Name:     oem12.inf\r\nOriginal Name:      roomeqvad.inf\r\nProvider Name:      RoomEq\r\n",
        "oem12.inf")]
    [InlineData("en-other-order",
        "Published Name: oem99.inf\r\nOriginal Name: other.inf\r\nClass Name:    System\r\nPublished Name: oem7.inf\r\nOriginal Name: roomeqvad.inf\r\n",
        "oem7.inf")]
    [InlineData("fr",
        "Nom du fichier publié : oem33.inf\r\nNom INF d'origine : roomeqvad.inf\r\n",
        "oem33.inf")]
    [InlineData("fr-long",
        "Nom du fichier publié : oem5.inf\r\nNom du fichier INF d'origine : roomeqvad.inf\r\n",
        "oem5.inf")]
    [InlineData("absent",
        "Published Name: oem12.inf\r\nOriginal Name: autre.inf\r\n",
        null)]
    public void ParseDriverStoreName_FindsRoomEqEntry(string _, string output, string? expected)
    {
        Assert.Equal(expected, DriverSetupManager.ParseDriverStoreName(output, "roomeqvad.inf"));
    }

    [Fact]
    public void ParseDriverStoreName_EmptyInputs_Guarded()
    {
        Assert.Null(DriverSetupManager.ParseDriverStoreName("", "roomeqvad.inf"));
        Assert.Null(DriverSetupManager.ParseDriverStoreName("whatever", ""));
        Assert.Null(DriverSetupManager.ParseDriverStoreName(null!, "roomeqvad.inf"));
    }

    // ------------------------------------------------------------ paquet

    [Fact]
    public void FindInfFile_LocatesDriverSubfolder()
    {
        string driver = Path.Combine(_tmp, "driver");
        Directory.CreateDirectory(driver);
        File.WriteAllText(Path.Combine(driver, "roomeqvad.inf"), "[Version]");

        string? found = DriverSetupManager.FindInfFile(_tmp, new VirtualInstallerSpec());
        Assert.NotNull(found);
        Assert.EndsWith("roomeqvad.inf", found, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FindInfFile_LocatesBesideExe_WhenNoSubfolder()
    {
        File.WriteAllText(Path.Combine(_tmp, "roomeqvad.inf"), "[Version]");

        string? found = DriverSetupManager.FindInfFile(_tmp, new VirtualInstallerSpec());
        Assert.NotNull(found);
        Assert.EndsWith("roomeqvad.inf", found, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PackagePresent_FalseWhenNothingShipped()
    {
        Assert.False(DriverSetupManager.PackagePresent(_tmp, new VirtualInstallerSpec()));
        Assert.Null(DriverSetupManager.FindInfFile(_tmp, new VirtualInstallerSpec()));
    }

    // ------------------------------------------------------------ fournisseurs

    [Fact]
    public void Catalog_ContainsOnlyRoomEqAndLoopback()
    {
        Assert.Equal(2, VirtualAudioCatalog.All.Count);
        Assert.Equal("roomeq", VirtualAudioCatalog.All[0].Id);
        Assert.Equal("loopback", VirtualAudioCatalog.All[1].Id);
        Assert.Equal(VirtualAudioCatalog.All[0], VirtualAudioCatalog.Default);
    }

    [Fact]
    public void Catalog_ResolvesLegacyThirdPartyIds_ToRoomEqDefault()
    {
        // les anciennes configurations (vbcable, voicemeeter, vac) retombent
        // proprement sur la carte RoomEq
        Assert.Equal(VirtualAudioCatalog.Default, VirtualAudioCatalog.GetById("vbcable"));
        Assert.Equal(VirtualAudioCatalog.Default, VirtualAudioCatalog.GetById("voicemeeter"));
        Assert.Equal(VirtualAudioCatalog.Default, VirtualAudioCatalog.GetById(null));
        Assert.Equal(VirtualAudioCatalog.All[1], VirtualAudioCatalog.GetById("loopback"));
    }

    [Theory]
    [InlineData("RoomEq Input (RoomEq Virtual Audio)", true)]
    [InlineData("RoomEq Output (RoomEq Virtual Audio)", true)]
    [InlineData("Haut-parleurs (Realtek Audio)", false)]
    public void RoomEqProvider_MatchesOwnEndpointsOnly(string name, bool expected)
    {
        var p = (RoomEqVirtualProvider)VirtualAudioCatalog.All[0];
        Assert.Equal(expected, p.IsRenderEndpoint(name) || p.IsCaptureEndpoint(name));
    }

    [Fact]
    public void RoomEqProvider_RenderAndCaptureAreDistinct()
    {
        var p = (RoomEqVirtualProvider)VirtualAudioCatalog.All[0];

        if (OperatingSystem.IsMacOS())
        {
            // macOS : UN périphérique « RoomEq Virtual » remplit les deux rôles
            Assert.True(p.IsRenderEndpoint("RoomEq Virtual"));
            Assert.True(p.IsCaptureEndpoint("RoomEq Virtual"));
            return;
        }

        // Windows : paire « RoomEq Input / RoomEq Output », côtés strictement distincts
        Assert.True(p.IsRenderEndpoint("RoomEq Input (RoomEq Virtual Audio)"));
        Assert.False(p.IsRenderEndpoint("RoomEq Output (RoomEq Virtual Audio)"));
        Assert.True(p.IsCaptureEndpoint("RoomEq Output (RoomEq Virtual Audio)"));
        Assert.False(p.IsCaptureEndpoint("RoomEq Input (RoomEq Virtual Audio)"));
    }

    [Fact]
    public void LoopbackProvider_IsPreinstalledAndMatchesNothing()
    {
        var p = (LoopbackProvider)VirtualAudioCatalog.All[1];
        Assert.True(p.IsPreinstalled);
        Assert.False(p.IsRenderEndpoint("N'importe quoi"));
        Assert.False(p.IsCaptureEndpoint("RoomEq Input"));
        Assert.False(VirtualAudioCatalog.IsVirtualRender("Sorties quelconques"));
    }

    [Fact]
    public void Guides_DoNotMentionThirdPartyProducts()
    {
        foreach (var p in VirtualAudioCatalog.All)
        foreach (string line in p.GuideLines)
        {
            Assert.DoesNotContain("VB-CABLE", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("VoiceMeeter", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("vb-audio", line, StringComparison.OrdinalIgnoreCase);
        }
    }
}
