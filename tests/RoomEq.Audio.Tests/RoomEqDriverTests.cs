using RoomEq.Audio.Routing;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Gestion du pilote de la carte virtuelle RoomEq (spec I-AUD-03) : lignes de
/// commande pnputil, analyse du magasin de pilotes, détection du paquet livré,
/// et fournisseurs (carte RoomEq intégrée + boucle WASAPI). Portage legacy.
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
    [InlineData("fr-win11-pilote",
        "Nom du pilote publié : oem41.inf\r\nNom du fichier INF d'origine : roomeqvad.inf\r\n",
        "oem41.inf")]
    [InlineData("fr-win11-pilote-origine",
        "Nom du pilote publié : oem9.inf\r\nNom du pilote INF d'origine : roomeqvad.inf\r\n",
        "oem9.inf")]
    [InlineData("fr-mojibake-cp850-lu-1252",
        // « é » CP850 (0x82) décodé en Windows-1252 = U+201A «‚» :
        // cas vécu (v2.2.0) où le parse échouait en silence.
        "Nom du fichier publi\u201a : oem8.inf\r\nNom du fichier INF d'origine : roomeqvad.inf\r\n",
        "oem8.inf")]
    [InlineData("fr-win11-court",
        // v2.2.2 : libellé RÉEL de Win11 FR (vécu) — « Nom d'origine » sans
        // « INF » ni « Name » : l'ancien suffixe exigé donnait un faux absent.
        "Nom publié : oem128.inf\r\nNom d'origine : roomeqvad.inf\r\n",
        "oem128.inf")]
    [InlineData("fr-win11-court-pilote",
        "Nom du pilote publié : oem3.inf\r\nNom d'origine : roomeqvad.inf\r\n",
        "oem3.inf")]
    [InlineData("fr-win11-court-absent",
        "Nom publié : oem1.inf\r\nNom d'origine : autre.inf\r\n",
        null)]
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
    public void FindInfFile_LocatesAnyDriverFolder()
    {
        string driver = Path.Combine(_tmp, "driver-2.2");
        Directory.CreateDirectory(driver);
        File.WriteAllText(Path.Combine(driver, "roomeqvad.inf"), "[Version]");

        string? found = DriverSetupManager.FindInfFile(_tmp, new VirtualInstallerSpec());
        Assert.NotNull(found);
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

    [Fact]
    public void VirtualCardStatus_DerivedFlags_AreConsistent()
    {
        var none = new VirtualCardStatus { CableInputFound = false, CableOutputFound = false };
        Assert.False(none.Installed);
        Assert.False(none.PartiallyInstalled);
        Assert.False(none.FullyRouted);

        var partial = new VirtualCardStatus { CableInputFound = true };
        Assert.False(partial.Installed);
        Assert.True(partial.PartiallyInstalled);

        var installed = new VirtualCardStatus { CableInputFound = true, CableOutputFound = true };
        Assert.True(installed.Installed);
        Assert.False(installed.FullyRouted);

        var routed = new VirtualCardStatus { CableInputFound = true, CableOutputFound = true, IsDefaultRender = true };
        Assert.True(routed.FullyRouted);
    }

    // ------------------------------------------------------------ setup embarqué

    [Fact]
    public void FindBundledSetupExe_LocatesVersionedAndPlainNames()
    {
        string a = Path.Combine(_tmp, "RoomEqDriverSetup-2.1.0.exe");
        string b = Path.Combine(_tmp, "RoomEqDriverSetup.exe");
        File.WriteAllText(a, "x");
        File.WriteAllText(b, "x");

        string? found = DriverSetupManager.FindBundledSetupExe(_tmp);
        Assert.NotNull(found);
        Assert.True(File.Exists(found));
        // n'importe lequel des deux : l'important est qu'un installateur
        // complet soit trouvé
        string name = Path.GetFileName(found!);
        Assert.StartsWith("RoomEqDriverSetup", name, StringComparison.OrdinalIgnoreCase);

        // un dossier sans installateur -> null (repli pnputil)
        string empty = Path.Combine(_tmp, "vide");
        Directory.CreateDirectory(empty);
        Assert.Null(DriverSetupManager.FindBundledSetupExe(empty));
        Assert.Null(DriverSetupManager.FindBundledSetupExe(Path.Combine(_tmp, "inexistant")));
    }

    [Fact]
    public void FindBundledSetupExe_IgnoresUnrelatedExes()
    {
        File.WriteAllText(Path.Combine(_tmp, "RoomEq.exe"), "x");
        File.WriteAllText(Path.Combine(_tmp, "UnTruc.exe"), "x");
        Assert.Null(DriverSetupManager.FindBundledSetupExe(_tmp));
    }

    [Fact]
    public void ServiceInstalled_TolerantOnUnix()
    {
        // hors Windows : la sonde ne doit jamais lever
        var r = DriverSetupManager.ServiceInstalled(new VirtualInstallerSpec());
        Assert.IsType<bool>(r);
    }

    // ------------------------------------------------------------ surveillance

    [Fact]
    public void AudioDeviceWatcher_IsInertOnNonWindowsOrGracefullyConstructed()
    {
        // macOS/Linux : le constructeur ne doit JAMAIS lever (surveillant
        // inerte) ; Windows sans service audio : idem (catch interne)
        using var watcher = new AudioDeviceWatcher();
        if (!OperatingSystem.IsWindows())
            Assert.False(watcher.IsWatching);
        // aucun crash à la destruction double
        watcher.Dispose();
    }

    // ------------------------------------------------------------ périphérique par défaut

    [Fact]
    public void DefaultDeviceSetter_RefusesEmptyOrNonWindows()
    {
        Assert.False(DefaultDeviceSetter.SetDefaultRender(""));
        Assert.False(DefaultDeviceSetter.SetDefaultRender(null!));
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(DefaultDeviceSetter.SetDefaultRender("{un-id}"));
            Assert.False(DefaultDeviceSetter.SetDefaultCapture("{un-id}"));
        }
    }

    [Fact]
    public void DefaultDeviceSetter_CandidateIids_AreTheThreeLegacyGuids()
    {
        Assert.Equal(3, DefaultDeviceSetter.CandidateIids.Length);
        Assert.Equal(new Guid("F8679F50-850A-41CF-9C72-430F290290C8"), DefaultDeviceSetter.CandidateIids[0]);
        Assert.Equal(new Guid("CA286FC3-91FD-42C3-8E9B-525B6C284480"), DefaultDeviceSetter.CandidateIids[1]);
        Assert.Equal(new Guid("F8679F50-850A-41CF-9C26-2208C9FD032B"), DefaultDeviceSetter.CandidateIids[2]);
    }
}
