using RoomEq.Audio.SystemApo;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Diagnostics d'échec d'activation (v3.9.8) — logique pure : décodage
/// HRESULT/Win32, résumé d'auto-test, description des prérequis moteur et
/// de l'inventaire des positions, verdict « sortie morte » EMBARQUANT le
/// diagnostic différentiel complet. Le correctif v3.9.7 laissait un message
/// générique (« le moteur n'a pas réussi à charger l'égaliseur ») sans un
/// seul élément pour départiquer les causes — chaque maillon est désormais
/// nommé dans le message transmis par l'utilisateur.
/// </summary>
public class ApoFailureDiagnosticsTests
{
    // ------------------------------------------------------------ HRESULT

    [Theory]
    [InlineData(unchecked((int)0x80070005), "accès refusé")]
    [InlineData(unchecked((int)0x8007007E), "module introuvable")]
    [InlineData(unchecked((int)0x800700C1), "mauvais format binaire")]
    [InlineData(unchecked((int)0x80040154), "classe non inscrite")]
    [InlineData(unchecked((int)0x80004002), "interface non supportée")]
    [InlineData(unchecked((int)0x88890008), "AUDCLNT_E_DEVICE_INVALIDATED")]
    public void Describe_NamesTheExactFailure(int hr, string fragment)
        => Assert.Contains(fragment, ApoHresult.Describe(hr));

    [Fact]
    public void Hex_IsStableEightDigits()
    {
        Assert.Equal("0x80070005", ApoHresult.Hex(unchecked((int)0x80070005)));
        Assert.Equal("0x00000000", ApoHresult.Hex(0));
        Assert.Equal("0x88890008", ApoHresult.Hex(unchecked((int)0x88890008)));
    }

    [Fact]
    public void DescribeFull_JoinsCodeAndText()
        => Assert.StartsWith("0x80070005 — accès refusé", ApoHresult.DescribeFull(unchecked((int)0x80070005)));

    // ------------------------------------------------------- auto-test DLL

    [Fact]
    public void SelfTestSummary_AllPassed_NamesTheStages()
    {
        var report = new ApoSelfTestReport(true, new[]
        {
            ApoSelfTestStep.Ok("Chargement de la DLL", "LoadLibrary OK"),
            ApoSelfTestStep.Ok("CoCreateInstance (inscription COM)", "objet créé"),
            ApoSelfTestStep.Ok("LockForProcess", "verrouillé"),
            ApoSelfTestStep.Ok("APOProcess", "signal traversé"),
        });

        string summary = report.Summary();

        Assert.StartsWith("OK (4 étapes", summary);
        Assert.Contains("la DLL elle-même est saine", summary); // verdict différenciel
    }

    [Fact]
    public void SelfTestSummary_Failure_NamesFirstFailingStepAndCode()
    {
        var report = new ApoSelfTestReport(false, new[]
        {
            ApoSelfTestStep.Ok("Chargement de la DLL", "LoadLibrary OK"),
            ApoSelfTestStep.Fail("CoCreateInstance (inscription COM)",
                ApoHresult.DescribeFull(unchecked((int)0x80040154))),
            ApoSelfTestStep.Fail("étape jamais atteinte", "—"),
        });

        string summary = report.Summary();

        // la PREMIÈRE étape en échec est nommée (pas la dernière)
        Assert.Contains("ÉCHEC à l'étape « CoCreateInstance (inscription COM) »", summary);
        Assert.Contains("0x80040154", summary);
        Assert.DoesNotContain("jamais atteinte", summary);
        Assert.NotNull(report.FirstFailure);
        Assert.Equal("CoCreateInstance (inscription COM)", report.FirstFailure!.Name);
    }

    [Fact]
    public void SelfTestBlocked_NamesBlockingAndKeepsSoundSafe()
    {
        var report = new ApoSelfTestReport(false, new[]
        {
            ApoSelfTestStep.Fail("Chargement de la DLL", ApoHresult.DescribeFull(unchecked((int)0x8007007E))),
        });

        string message = ApoFailureDiagnostics.SelfTestBlocked(report, @"C:\ProgramData\RoomEq\apo\bin\RoomEqApo_x64.dll");

        Assert.Contains("BLOQUÉE AVANT TOUTE MODIFICATION", message);
        Assert.Contains("La sortie n'a PAS été touchée", message);   // le son est hors de danger
        Assert.Contains("0x8007007E", message);                      // le code exact
        Assert.Contains("RoomEqApo_x64.dll", message);               // la DLL incriminée
    }

    // ----------------------------------------------- prérequis + inventaire

    [Fact]
    public void DescribeEngine_NamesEachPrerequisite()
    {
        var ok = ApoFailureDiagnostics.DescribeEngine(
            new ApoEnginePrerequisites(true, true, true));
        Assert.Contains("signature non vérifiée", ok);
        Assert.Contains("inscription moteur audio : oui", ok);
        Assert.Contains("inscription COM : oui", ok);

        var ko = ApoFailureDiagnostics.DescribeEngine(
            new ApoEnginePrerequisites(false, false, false));
        Assert.Contains("NON", ko); // un maillon faux est visible
    }

    [Fact]
    public void DescribeInventory_ShowsSlotsAndFlagsMultiPresence()
    {
        var plain = ApoFailureDiagnostics.DescribeInventory(
            new ApoSlotInventory(null, null, "{autre-apo}", false, false, false));
        Assert.Contains("EFX={autre-apo}", plain);
        Assert.DoesNotContain("multi", plain); // pas de bruit quand absent

        var multi = ApoFailureDiagnostics.DescribeInventory(
            new ApoSlotInventory(null, null, null, false, false, true));
        Assert.Contains("multiEFX PRÉSENT", multi);               // Windows ignore les CLSID simples
        Assert.Contains("Windows ignore alors les CLSID simples", multi);

        var ours = ApoFailureDiagnostics.DescribeInventory(
            new ApoSlotInventory(null, ApoConstants.Clsid, null, false, false, false));
        Assert.Contains("MFX=RoomEq", ours); // notre CLSID est lisible, pas un GUID brut
    }

    // --------------------------------------------- verdict « sortie morte »

    [Fact]
    public void ComposeDeadEndpoint_EmbarksEveryDifferentialLink()
    {
        var context = new ApoDeadEndpointContext(
            Engine: new ApoEnginePrerequisites(true, true, true),
            LogShowsLoad: false,
            LogTail: new[] { "APO instancie (RoomEqApo)" },
            ProbeError: ApoHresult.DescribeFull(unchecked((int)0x88890008)) + " (COMException: blabla)",
            SelfTest: new ApoSelfTestReport(true, new[] { ApoSelfTestStep.Ok("Chargement de la DLL", "OK") }),
            Inventory: new ApoSlotInventory(null, ApoConstants.Clsid, null, false, false, true),
            SlotChosen: "MFX",
            OsVersion: "Microsoft Windows 10.0.26100.1742");

        string message = ApoFailureDiagnostics.ComposeDeadEndpoint(context);

        // 1. le son a été préservé et la sortie restaurée (comportement v3.9.7)
        Assert.Contains("LE SON A ÉTÉ PRÉSERVÉ", message);
        Assert.Contains("restaurée automatiquement", message);
        // 2. le diagnostic différentiel est EMBARQUÉ
        Assert.Contains("DIAGNOSTIC", message);
        Assert.Contains("Auto-test DLL : OK", message);                          // DLL saine → cause côté moteur
        Assert.Contains("Prérequis moteur", message);
        Assert.Contains("AUCUNE trace postérieure à l'auto-test", message);         // jamais instanciée
        Assert.Contains("Dernières lignes du journal", message);
        Assert.Contains("0x88890008", message);                                  // erreur exacte de la sonde
        Assert.Contains("Position choisie : MFX", message);                      // position adaptée
        Assert.Contains("multiEFX PRÉSENT", message);                            // inventaire
        Assert.Contains("10.0.26100", message);                                  // version Windows
        Assert.Contains("redémarrage de Windows", message);                      // piste de déblocage
    }

    [Fact]
    public void ComposeDeadEndpoint_WithLogLoad_SaysSo()
    {
        var context = new ApoDeadEndpointContext(
            Engine: null,
            LogShowsLoad: true,
            LogTail: Array.Empty<string>(),
            ProbeError: null,
            SelfTest: null,
            Inventory: null,
            SlotChosen: "EFX",
            OsVersion: "test");

        string message = ApoFailureDiagnostics.ComposeDeadEndpoint(context);

        // v3.9.9 : sans instanciation AGRÉGÉE, le libellé ne PRÉTEND plus que
        // c'est le moteur — une création directe écrit les mêmes lignes
        // (le verdict v3.9.8 lisait celles de l'auto-test et se trompait)
        Assert.Contains("lignes de cycle de vie présentes", message);
        Assert.Contains("SANS instanciation agrégée", message);
        Assert.Contains("Auto-test DLL : non exécuté", message);   // dégradé mais honnête
        Assert.DoesNotContain("Dernières lignes", message);       // pas de journal → pas de section
        Assert.DoesNotContain("Positions présentes", message);    // pas d'inventaire → pas de section
    }

    [Fact]
    public void ComposeDeadEndpoint_WithAggregatedInstantiation_CallsItProof()
    {
        // v3.9.9 : la seule trace qui prouve le moteur SANS ambiguïté est
        // l'instanciation AGRÉGÉE (contrôleur COM non nul) — le libellé
        // l'énonce noir sur blanc
        var context = new ApoDeadEndpointContext(
            Engine: null,
            LogShowsLoad: true,
            AggregatedSeen: true,
            LogTail: new[] { "APO instanciee (AGREGEE par le moteur audio)" },
            ProbeError: null,
            SelfTest: null,
            Inventory: null,
            SlotChosen: "EFX",
            OsVersion: "test");

        string message = ApoFailureDiagnostics.ComposeDeadEndpoint(context);

        Assert.Contains("instanciation AGRÉGÉE par le moteur audio", message);
        Assert.Contains("preuve directe", message);
        Assert.DoesNotContain("SANS instanciation agrégée", message);
    }

    // --------- v3.9.10 : drapeau combiné (l'EXPLICATION du choix initial)
    // Retour terrain v3.9.9 : « Position choisie : EFX » sans dire POURQUOI
    // (détection ratée — propriété booléenne lue comme chaîne) : le verdict
    // énonce désormais le drapeau, et les positions des DEUX essais quand le
    // repli automatique a joué.

    [Theory]
    [InlineData(true, "Périphérique combiné : oui")]
    [InlineData(false, "Périphérique combiné : NON")]
    [InlineData(null, "Périphérique combiné : inconnu (registre illisible)")]
    public void ComposeDeadEndpoint_StatesCombinedFlag(bool? combined, string expected)
    {
        var context = new ApoDeadEndpointContext(
            Engine: null,
            LogShowsLoad: false,
            LogTail: Array.Empty<string>(),
            ProbeError: null,
            SelfTest: null,
            Inventory: null,
            SlotChosen: "MFX",
            OsVersion: "test",
            CombinedDevice: combined);

        string message = ApoFailureDiagnostics.ComposeDeadEndpoint(context);

        Assert.Contains(expected, message);
        Assert.Contains("Position choisie : MFX", message); // juste avant le drapeau
    }

    [Fact]
    public void ComposeDeadEndpoint_AfterFallback_ShowsBothSlots()
    {
        // les DEUX positions essayées par l'échelle de repli sont nommées —
        // le message transmis désigne ce qui a été tenté, pas seulement
        // la position (qui a échoué deux fois)
        var context = new ApoDeadEndpointContext(
            Engine: null,
            LogShowsLoad: true,
            AggregatedSeen: true,
            LogTail: Array.Empty<string>(),
            ProbeError: "position EFX : 0x88890008 ; repli MFX : 0x88890008",
            SelfTest: null,
            Inventory: null,
            SlotChosen: "EFX puis MFX (repli automatique)",
            OsVersion: "test",
            CombinedDevice: true);

        string message = ApoFailureDiagnostics.ComposeDeadEndpoint(context);

        Assert.Contains("Position choisie : EFX puis MFX (repli automatique)", message);
        Assert.Contains("Périphérique combiné : oui", message);
        Assert.Contains("repli MFX : 0x88890008", message);   // erreurs des DEUX sondes
    }
}
