namespace RoomEq.Audio.SystemApo;

/// <summary>
/// Prérequis machine du chargement APO, tels que lus par le processus élevé
/// (v3.9.8) — miroir de <c>WindowsApoSystem.ApoEngineStatus</c> pour le
/// diagnostic embarqué dans les verdicts.
/// </summary>
public sealed record ApoEnginePrerequisites(
    bool ProtectedAudioDgDisabled,
    bool EngineRegistered,
    bool ComRegistered);

/// <summary>
/// Inventaire des positions FxProperties au moment de l'activation (v3.9.8) :
/// SFX/MFX/EFX (CLSID simples) et présence des variantes « multi » — quand
/// elles existent, Windows IGNORE les CLSID simples (constat Realtek UAD,
/// ticket Equalizer APO #77) : le diagnostic doit le montrer.
/// </summary>
public sealed record ApoSlotInventory(
    string? Sfx, string? Mfx, string? Efx,
    bool MultiSfx, bool MultiMfx, bool MultiEfx)
{
    /// <summary>Un slot CLSID simple est-il occupé (les nôtres inclus) ?</summary>
    public bool AnySimpleSlotUsed
        => Sfx != null || Mfx != null || Efx != null;
}

/// <summary>
/// Contexte complet du verdict « sortie morte » (v3.9.8) : chaque maillon de
/// la chaîne de chargement, pour un diagnostic DIFFÉRENTIEL au premier
/// retour terrain — au lieu du générique « le moteur n'a pas réussi à
/// charger l'égaliseur » des versions précédentes.
/// </summary>
public sealed record ApoDeadEndpointContext(
    ApoEnginePrerequisites? Engine,
    bool LogShowsLoad,
    IReadOnlyList<string> LogTail,
    string? ProbeError,
    ApoSelfTestReport? SelfTest,
    ApoSlotInventory? Inventory,
    string SlotChosen,
    string OsVersion,
    bool AggregatedSeen = false);

/// <summary>
/// Compositeur PUR des verdicts de diagnostic v3.9.8 (correctif « EQ activé
/// = sortie muette ») : formate en français les messages qui embarquent les
/// éléments différentiels (auto-test DLL, prérequis moteur, journal de
/// l'APO, erreur exacte de la sonde WASAPI, inventaire des positions,
/// version de Windows). Aucune dépendance Windows — testable sous Linux ;
/// les données vivent dans <see cref="ApoSetupCommands"/> (processus élevé).
/// </summary>
public static class ApoFailureDiagnostics
{
    /// <summary>
    /// Verdict d'activation bloquée par l'auto-test : la sortie n'a PAS été
    /// touchée, l'étape exacte et son code sont nommés.
    /// </summary>
    public static string SelfTestBlocked(ApoSelfTestReport selfTest, string dllPath)
        => "ACTIVATION BLOQUÉE AVANT TOUTE MODIFICATION : l'auto-test de la DLL de " +
           "l'égaliseur a échoué — " + selfTest.Summary() + ". La sortie n'a PAS été " +
           "touchée (aucun risque de coupure du son). DLL : " + dllPath +
           ". Transmettez ce message complet : il désigne l'étape exacte qui échoue.";

    /// <summary>Prérequis moteur en une ligne lisible.</summary>
    public static string DescribeEngine(ApoEnginePrerequisites p)
        => "signature non vérifiée (DisableProtectedAudioDG=1) : " + OuiNon(p.ProtectedAudioDgDisabled)
         + " · inscription moteur audio : " + OuiNon(p.EngineRegistered)
         + " · inscription COM : " + OuiNon(p.ComRegistered);

    /// <summary>Inventaire des positions en une ligne lisible.</summary>
    public static string DescribeInventory(ApoSlotInventory i)
    {
        var parts = new List<string>
        {
            "SFX=" + Slot(i.Sfx),
            "MFX=" + Slot(i.Mfx),
            "EFX=" + Slot(i.Efx),
        };
        if (i.MultiSfx) parts.Add("multiSFX PRÉSENT (Windows ignore alors les CLSID simples)");
        if (i.MultiMfx) parts.Add("multiMFX PRÉSENT (Windows ignore alors les CLSID simples)");
        if (i.MultiEfx) parts.Add("multiEFX PRÉSENT (Windows ignore alors les CLSID simples)");
        return string.Join(" · ", parts);
    }

    /// <summary>
    /// Le verdict « LE SON A ÉTÉ PRÉSERVÉ » avec le diagnostic complet —
    /// chaque maillon nommé pour que le message transmis désigne le coupable.
    /// </summary>
    public static string ComposeDeadEndpoint(ApoDeadEndpointContext c)
    {
        var diag = new List<string>();

        // 1. Auto-test DLL : si OK, la DLL s'instancie hors moteur — le refus
        //    vient du moteur audio (politique, contexte de service) ou de la
        //    position choisie, PAS de la DLL elle-même.
        diag.Add(c.SelfTest == null
            ? "Auto-test DLL : non exécuté"
            : "Auto-test DLL : " + c.SelfTest.Summary());

        // 2. Prérequis machine lus APRÈS l'échec (les trois maillon v3.9.6).
        diag.Add(c.Engine == null
            ? "Prérequis moteur : illisibles"
            : "Prérequis moteur : " + DescribeEngine(c.Engine));

        // 3. Journal de l'APO (v3.9.9 : CLASSÉ — le verdict v3.9.8 lisait les
        //    lignes de l'AUTO-TEST et en déduisait à tort « instanciée par le
        //    moteur » ; seules les lignes postérieures au marqueur comptent,
        //    et l'instanciation AGRÉGÉE est la preuve directe du moteur).
        diag.Add(c.LogShowsLoad
            ? c.AggregatedSeen
                ? "Journal de l'APO : instanciation AGRÉGÉE par le moteur audio (preuve directe — la DLL a été créée ET pilotée par audiodg)"
                : "Journal de l'APO : lignes de cycle de vie présentes mais SANS instanciation agrégée (une création directe les écrit aussi — pas une preuve du moteur)"
            : "Journal de l'APO : AUCUNE trace postérieure à l'auto-test — l'APO n'a pas été instanciée par le moteur audio");
        if (c.LogTail.Count > 0)
            diag.Add("Dernières lignes du journal : " + string.Join(" | ", c.LogTail));

        // 4. Erreur exacte de la sonde WASAPI (AUDCLNT_E_*).
        diag.Add("Sonde WASAPI : " + (string.IsNullOrEmpty(c.ProbeError)
            ? "échec (détail indisponible)"
            : c.ProbeError));

        // 5. Position choisie + inventaire (multi* = CLSID simples ignorés).
        diag.Add("Position choisie : " + c.SlotChosen);
        if (c.Inventory != null)
            diag.Add("Positions présentes : " + DescribeInventory(c.Inventory));

        // 6. Version de Windows (comportements moteur différents 10/11/24H2).
        diag.Add("Windows : " + c.OsVersion);

        return "LE SON A ÉTÉ PRÉSERVÉ : le moteur audio de Windows n'a pas réussi à " +
               "charger l'égaliseur sur cette sortie (sortie devenue muette → restaurée " +
               "automatiquement à son état d'origine, service audio redémarré)." +
               " DIAGNOSTIC (à transmettre tel quel) : " + string.Join(" ; ", diag) + "." +
               " Pistes : un redémarrage de Windows peut débloquer le chargement (le " +
               "réglage de signature est lu à la création du moteur) ; si l'auto-test " +
               "est OK et les prérequis verts, le refus vient du contexte du service " +
               "audio — transmettez ce message complet.";
    }

    private static string OuiNon(bool b) => b ? "oui" : "NON";

    private static string Slot(string? clsid)
    {
        if (clsid == null)
            return "—";
        return string.Equals(clsid, ApoConstants.Clsid, StringComparison.OrdinalIgnoreCase)
            ? "RoomEq"
            : clsid;
    }
}
