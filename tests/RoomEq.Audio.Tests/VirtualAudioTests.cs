using System.IO.Compression;
using System.Linq;
using RoomEq.Audio.Routing;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Tests de la couche multi-protocoles de cartes son virtuelles : détection par
/// motifs, catalogue de fournisseurs, sécurité de l'installateur (URL, ZipSlip,
/// recherche d'exécutable, signature PE) et persistance du protocole choisi.
/// </summary>
public class VirtualAudioTests
{
    // ---------------------------------------------------------------- fournisseurs

    [Fact]
    public void Vbcable_Detecte_Cable_Input_Et_Cable_Output()
    {
        var p = new VBCableProvider();
        Assert.True(p.IsRenderEndpoint("CABLE Input (VB-Audio Virtual Audio Device)"));
        Assert.True(p.IsCaptureEndpoint("CABLE Output (VB-Audio Virtual Audio Device)"));
        Assert.False(p.IsRenderEndpoint("Realtek High Definition Audio"));
        Assert.False(p.IsCaptureEndpoint("Microphone (Realtek Audio)"));
        Assert.False(p.IsRenderEndpoint("VoiceMeeter Input (VB-Audio VoiceMeeter VAIO)"));
    }

    [Fact]
    public void Voicemeeter_Detecte_Les_Paires_Vaio_Aux_Et_Vaio3()
    {
        var p = new VoiceMeeterProvider();
        Assert.True(p.IsRenderEndpoint("VoiceMeeter Input (VB-Audio VoiceMeeter VAIO)"));
        Assert.True(p.IsRenderEndpoint("VoiceMeeter Aux Input (VB-Audio VoiceMeeter AUX VAIO)"));
        Assert.True(p.IsRenderEndpoint("VoiceMeeter VAIO3 Input (VB-Audio VoiceMeeter VAIO3)"));
        Assert.True(p.IsCaptureEndpoint("VoiceMeeter Output (VB-Audio VoiceMeeter VAIO)"));
        Assert.True(p.IsCaptureEndpoint("VoiceMeeter Aux Output (VB-Audio VoiceMeeter AUX VAIO)"));
        Assert.False(p.IsCaptureEndpoint("CABLE Output (VB-Audio Virtual Audio Device)"));
    }

    [Fact]
    public void Vac_Detecte_Les_Lines_Virtuelles()
    {
        var p = new VacProvider();
        Assert.True(p.IsRenderEndpoint("Line 1 (Virtual Audio Cable)"));
        Assert.True(p.IsCaptureEndpoint("Line 1 (Virtual Audio Cable)"));
        Assert.False(p.IsRenderEndpoint("Haut-parleurs (Realtek High Definition Audio)"));
    }

    [Fact]
    public void Boucle_Native_Ne_Detecte_Rien_Et_Ne_S_Installe_Pas()
    {
        var p = new LoopbackProvider();
        Assert.True(p.IsPreinstalled);
        Assert.Equal(VirtualCardKind.Loopback, p.Kind);
        Assert.False(p.IsRenderEndpoint("CABLE Input"));
        Assert.False(p.IsCaptureEndpoint("CABLE Output"));
        Assert.False(p.Installer.Supported);
    }

    // ---------------------------------------------------------------- catalogue

    [Fact]
    public void Catalogue_Contient_Les_Quatre_Protocoles_Dans_Ordre()
    {
        var ids = VirtualAudioCatalog.All.Select(p => p.Id).ToArray();
        Assert.Equal(new[] { "vbcable", "voicemeeter", "vac", "loopback" }, ids);
        Assert.Equal("vbcable", VirtualAudioCatalog.Default.Id);
    }

    [Fact]
    public void Catalogue_Resout_Par_Id_Avec_Repli_Vbcable()
    {
        Assert.Equal("voicemeeter", VirtualAudioCatalog.GetById("voicemeeter").Id);
        Assert.Equal("loopback", VirtualAudioCatalog.GetById("Loopback").Id); // insensible à la casse
        Assert.Equal("vbcable", VirtualAudioCatalog.GetById(null).Id);
        Assert.Equal("vbcable", VirtualAudioCatalog.GetById("").Id);
        Assert.Equal("vbcable", VirtualAudioCatalog.GetById("protocole-inconnu").Id);
    }

    [Fact]
    public void Detection_Union_Couvre_Tous_Les_Cables_Virtuels()
    {
        Assert.True(AudioDeviceCatalog.IsVirtualRender("CABLE Input (VB-Audio Virtual Audio Device)"));
        Assert.True(AudioDeviceCatalog.IsVirtualRender("VoiceMeeter Aux Input (VB-Audio VoiceMeeter AUX VAIO)"));
        Assert.True(AudioDeviceCatalog.IsVirtualRender("Line 2 (Virtual Audio Cable)"));
        Assert.True(AudioDeviceCatalog.IsVirtualCapture("CABLE Output (VB-Audio Virtual Audio Device)"));
        Assert.True(AudioDeviceCatalog.IsVirtualCapture("VoiceMeeter Output (VB-Audio VoiceMeeter VAIO)"));
        Assert.False(AudioDeviceCatalog.IsVirtualRender("Haut-parleurs (Realtek High Definition Audio)"));
        Assert.False(AudioDeviceCatalog.IsVirtualCapture("Microphone array (Intel)"));
        Assert.False(AudioDeviceCatalog.IsVirtualRender(""));
        Assert.False(AudioDeviceCatalog.IsVirtualCapture(null!));
    }

    [Fact]
    public void Specs_Installation_Seulemnt_Pour_Protocoles_Gratuits_Telechargeables()
    {
        Assert.True(new VBCableProvider().Installer.Supported);
        Assert.True(new VoiceMeeterProvider().Installer.Supported);
        Assert.False(new VacProvider().Installer.Supported);      // payant : page produit
        Assert.True(new VacProvider().Installer.ProductPageUrl.Length > 0);
        Assert.False(new LoopbackProvider().Installer.Supported);
        Assert.Equal(0, new LoopbackProvider().Installer.ProductPageUrl.Length);
    }

    [Fact]
    public void Vbcable_Seuil_Taille_Compatible_Avec_L_Archive_Officielle()
    {
        // L'archive officielle VBCABLE_Driver_Pack45.zip mesure 1 318 877 octets
        // (vérifié le 09/10/2026) : un seuil trop élevé (4 Mo en v1.2) rejetait le
        // téléchargement AVANT le lancement de l'installateur — l'UAC n'apparaissait
        // jamais. Garde-fou de régression calé sur la taille mesurée.
        Assert.True(new VBCableProvider().Installer.MinBytes < 1_318_877,
            "Le seuil MinBytes de VB-CABLE doit rester sous la taille réelle de l'archive officielle (1 318 877 octets).");
    }

    [Fact]
    public void Voicemeeter_Installeur_Zip_Officiel_Avec_Decouverte_De_Version()
    {
        // VB-Audio ne publie plus l'installateur en .exe direct (404) : l'officiel
        // est un ZIP versionné référencé par la page produit.
        var spec = new VoiceMeeterProvider().Installer;
        Assert.Equal("zip", spec.Container);
        Assert.Contains("voicemeetersetup.exe", spec.SetupExecutableNames, StringComparer.OrdinalIgnoreCase);
        Assert.True(spec.MinBytes >= 8 * 1024 * 1024); // archive réelle ≈ 22 Mo
        Assert.NotEmpty(spec.DownloadUrls);
        foreach (var url in spec.DownloadUrls)
            InstallerCore.ValidateUrl(url);

        // page de découverte officielle valide + motif non vide
        InstallerCore.ValidatePageUrl(spec.DiscoveryPageUrl);
        Assert.NotEmpty(spec.DiscoveryPattern);

        // la version courante est bien extraite d'une page type
        var html = "<a href=\"https://download.vb-audio.com/Download_CABLE/VoicemeeterSetup_v1130.zip\">Download</a>";
        Assert.Equal("https://download.vb-audio.com/Download_CABLE/VoicemeeterSetup_v1130.zip",
            InstallerCore.ExtractLatestDownloadUrl(html, spec.DiscoveryPattern));
    }

    [Fact]
    public void Vbcable_Expose_Aussi_Une_Decouverte_De_Version()
    {
        var spec = new VBCableProvider().Installer;
        InstallerCore.ValidatePageUrl(spec.DiscoveryPageUrl);
        Assert.NotEmpty(spec.DiscoveryPattern);
        Assert.NotEmpty(spec.PostLaunchHint);
    }

    [Fact]
    public void Toutes_Les_Pages_De_Decouverte_Sont_Officielles_Et_Https()
    {
        foreach (var provider in VirtualAudioCatalog.All)
        {
            if (provider.Installer.DiscoveryPageUrl.Length > 0)
                InstallerCore.ValidatePageUrl(provider.Installer.DiscoveryPageUrl);
        }
    }

    [Fact]
    public void Toutes_Les_Urls_Officielles_Sont_Https_Domaine_Verrouille()
    {
        foreach (var provider in VirtualAudioCatalog.All)
        {
            foreach (var url in provider.Installer.DownloadUrls)
                InstallerCore.ValidateUrl(url); // doit lever si non officiel / non HTTPS
        }
    }

    // ---------------------------------------------------------------- sécurité installateur

    [Theory]
    [InlineData("https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip", true)]
    [InlineData("http://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip", false)]   // pas HTTPS
    [InlineData("https://malicious.example.com/VBCABLE_Driver_Pack45.zip", false)]                 // domaine inconnu
    [InlineData("https://download.vb-audio.com.evil.io/VBCABLE.zip", false)]                       // suffixe trompeur
    [InlineData("ftp://download.vb-audio.com/f.zip", false)]                                       // schéma interdit
    public void ValidateUrl_Accepte_Seuls_Les_Domaines_Officiels(string url, bool doitPasser)
    {
        if (doitPasser)
            InstallerCore.ValidateUrl(url);
        else
            Assert.Throws<InvalidOperationException>(() => InstallerCore.ValidateUrl(url));
    }

    [Theory]
    [InlineData("https://vb-audio.com/Voicemeeter/", true)]
    [InlineData("https://www.vb-audio.com/Cable/", true)]
    [InlineData("https://voicemeeter.com/", true)]
    [InlineData("http://vb-audio.com/Voicemeeter/", false)]        // pas HTTPS
    [InlineData("https://evil.example/page", false)]               // domaine inconnu
    [InlineData("https://vb-audio.com.evil.io/", false)]           // suffixe trompeur
    public void ValidatePageUrl_Accepte_Seuls_Les_Domaines_Officiels(string url, bool doitPasser)
    {
        if (doitPasser)
            InstallerCore.ValidatePageUrl(url);
        else
            Assert.Throws<InvalidOperationException>(() => InstallerCore.ValidatePageUrl(url));
    }

    [Fact]
    public void Extraction_Selectionne_La_Version_La_Plus_Recente()
    {
        const string pattern = "Download_CABLE/VoicemeeterSetup_v[0-9]+\\.zip";
        const string html = "<a HREF=\"https://download.vb-audio.com/Download_CABLE/VoicemeeterSetup_v1120.zip\">old</a>"
                          + "<a HREF=\"https://download.vb-audio.com/Download_CABLE/VoicemeeterSetup_v1130.zip\">new</a>";
        Assert.Equal("https://download.vb-audio.com/Download_CABLE/VoicemeeterSetup_v1130.zip",
            InstallerCore.ExtractLatestDownloadUrl(html, pattern));
    }

    [Fact]
    public void Extraction_Absolutise_Les_Chemins_Relatifs()
    {
        const string pattern = "Download_CABLE/VBCABLE_Driver_Pack[0-9]+\\.zip";
        const string html = "<a href=\"Download_CABLE/VBCABLE_Driver_Pack45.zip\">Pack</a>";
        Assert.Equal("https://download.vb-audio.com/Download_CABLE/VBCABLE_Driver_Pack45.zip",
            InstallerCore.ExtractLatestDownloadUrl(html, pattern));
    }

    [Fact]
    public void Extraction_Ignore_Les_Liens_Hors_Domaine_Officiel()
    {
        // motif ancré sur le domaine officiel (comme les specs réelles) : un lien
        // absolu vers un autre domaine ne correspond jamais
        const string pattern = "https://download\\.vb-audio\\.com/Download_CABLE/VoicemeeterSetup_v[0-9]+\\.zip";
        const string html = "<a href=\"https://evil.example/Download_CABLE/VoicemeeterSetup_v9999.zip\">piege</a>"
                          + "<a href=\"https://download.vb-audio.com/Download_CABLE/VoicemeeterSetup_v1100.zip\">officiel</a>";
        Assert.Equal("https://download.vb-audio.com/Download_CABLE/VoicemeeterSetup_v1100.zip",
            InstallerCore.ExtractLatestDownloadUrl(html, pattern));
    }

    [Theory]
    [InlineData("")]
    [InlineData("<html>rien</html>")]
    public void Extraction_Retourne_Null_Sans_Correspondance_Valide(string html)
    {
        Assert.Null(InstallerCore.ExtractLatestDownloadUrl(html, "Download_CABLE/VoicemeeterSetup_v[0-9]+\\.zip"));
        Assert.Null(InstallerCore.ExtractLatestDownloadUrl("<a href=\"https://download.vb-audio.com/Download_CABLE/VoicemeeterSetup_v1130.zip\"/>", ""));
    }

    [Theory]
    [InlineData("VBCABLE_Setup_X64.exe", true)]
    [InlineData("dossier/VBCABLE_Setup_X64.exe", true)]
    [InlineData("../evil.exe", false)]
    [InlineData("..\\evil.exe", false)]
    [InlineData("/absolu/evil.exe", false)]
    [InlineData("C:\\windows\\evil.exe", false)]
    [InlineData("dossier/../../evil.exe", false)]
    [InlineData("", false)]
    public void ValidateEntryName_Bloque_Les_Archives_Malveillantes(string entry, bool doitPasser)
    {
        if (doitPasser)
            InstallerCore.ValidateEntryName(entry);
        else
            Assert.Throws<InvalidOperationException>(() => InstallerCore.ValidateEntryName(entry));
    }

    [Fact]
    public void FindSetupExecutable_Preferer_La_Variante_X64()
    {
        string[] entries = { "VBCABLE_Setup.exe", "VBCABLE_Setup_X64.exe", "readme.txt", "license.txt" };
        var found = InstallerCore.FindSetupExecutable(entries, new[] { "VBCABLE_Setup_X64.exe", "VBCABLE_Setup.exe" });
        Assert.Equal("VBCABLE_Setup_X64.exe", found);
    }

    [Fact]
    public void FindSetupExecutable_Retourne_Null_Si_Absent()
    {
        string[] entries = { "driver.inf", "driver.sys", "readme.txt" };
        Assert.Null(InstallerCore.FindSetupExecutable(entries, new[] { "VBCABLE_Setup_X64.exe" }));
    }

    [Fact]
    public void FindSetupExecutable_Respecte_L_Ordre_Des_Candidats()
    {
        string[] entries = { "VoicemeeterProSetup.exe", "VoicemeeterSetup.exe" };
        var found = InstallerCore.FindSetupExecutable(entries, new[] { "VoicemeeterSetup.exe", "VoicemeeterProSetup.exe" });
        Assert.Equal("VoicemeeterSetup.exe", found);
    }

    [Fact]
    public void ValidateExecutable_Accepte_Un_Fichier_PE_Et_Rejette_Les_Autres()
    {
        // MZ + contenu : valide
        var pe = new MemoryStream(new byte[] { 0x4D, 0x5A, 0x90, 0x00, 0x03, 0x00 });
        InstallerCore.ValidateExecutable(pe);
        Assert.Equal(0, pe.Position); // le flux est rembobiné

        // texte quelconque : rejeté
        var faux = new MemoryStream(new byte[] { 0x50, 0x4B, 0x03, 0x04 }); // signature ZIP, pas PE
        Assert.Throws<InvalidOperationException>(() => InstallerCore.ValidateExecutable(faux));
    }

    [Fact]
    public void DefaultDeviceSetter_Expose_Les_Iid_Candidates_Ordonnees()
    {
        // l'IID classique d'abord (prouvée Win7 → Win11), puis les variantes de repli
        Assert.Equal(
            new[]
            {
                new Guid("F8679F50-850A-41CF-9C72-430F290290C8"),
                new Guid("CA286FC3-91FD-42C3-8E9B-525B6C284480"),
                new Guid("F8679F50-850A-41CF-9C26-2208C9FD032B")
            },
            DefaultDeviceSetter.CandidateIids);
    }

    [Fact]
    public void DefaultDeviceSetter_Rejette_Les_Identifiants_Vides_Sans_Toucher_Au_Com()
    {
        Assert.False(DefaultDeviceSetter.SetDefaultRender(""));
        Assert.False(DefaultDeviceSetter.SetDefaultRender("   "));
        Assert.False(DefaultDeviceSetter.SetDefaultCapture(""));
    }

    // ---------------------------------------------------------------- persistance du protocole

    [Fact]
    public void MixerStore_Conserve_L_Identifiant_De_Protocole()
    {
        var dir = Path.Combine(Path.GetTempPath(), "roomeq-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new MixerStore(dir);
            var stored = new StoredMixer
            {
                AutoStart = true,
                MasterFader = 0.8,
                VirtualProviderId = "voicemeeter",
                Channels =
                {
                    new StoredChannel { Kind = "capture", DeviceId = "dev-1", Name = "VoiceMeeter Output" }
                }
            };
            store.Save(stored);

            var loaded = store.Load();
            Assert.Equal("voicemeeter", loaded.VirtualProviderId);
            Assert.True(loaded.AutoStart);
            Assert.Equal(0.8, loaded.MasterFader, 5);
            Assert.Single(loaded.Channels);
            Assert.Equal("VoiceMeeter Output", loaded.Channels[0].Name);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MixerStore_Valeur_Protocole_Absente_Est_Nullable()
    {
        var dir = Path.Combine(Path.GetTempPath(), "roomeq-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new MixerStore(dir);
            store.Save(new StoredMixer()); // ancienne version de fichier : pas de VirtualProviderId
            var loaded = store.Load();
            Assert.Null(loaded.VirtualProviderId);
            Assert.Null(loaded.SavedDefaultRenderId);
            Assert.Null(loaded.SavedDefaultCaptureId);
            Assert.True(loaded.AutoRouteOnInstall);      // valeur par défaut
            Assert.True(loaded.SwitchDefaultCapture);    // valeur par défaut
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void MixerStore_Conserve_Options_De_Routage_Et_Defauts_Sauvegardes()
    {
        var dir = Path.Combine(Path.GetTempPath(), "roomeq-tests-" + Guid.NewGuid().ToString("N"));
        try
        {
            var store = new MixerStore(dir);
            store.Save(new StoredMixer
            {
                AutoRouteOnInstall = false,
                SwitchDefaultCapture = false,
                SavedDefaultRenderId = "{0.0.0.00000000}.{render-avant-bascule}",
                SavedDefaultCaptureId = "{0.0.0.00000000}.{capture-avant-bascule}"
            });

            var loaded = store.Load();
            Assert.False(loaded.AutoRouteOnInstall);
            Assert.False(loaded.SwitchDefaultCapture);
            Assert.Equal("{0.0.0.00000000}.{render-avant-bascule}", loaded.SavedDefaultRenderId);
            Assert.Equal("{0.0.0.00000000}.{capture-avant-bascule}", loaded.SavedDefaultCaptureId);
        }
        finally
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
    }

    // ---------------------------------------------------------------- guides

    [Fact]
    public void Chaque_Protocole_Expose_Un_Guide_Non_Vide()
    {
        foreach (var p in VirtualAudioCatalog.All)
        {
            Assert.NotEmpty(p.GuideLines);
            Assert.All(p.GuideLines, line => Assert.False(string.IsNullOrWhiteSpace(line)));
        }
    }
}
