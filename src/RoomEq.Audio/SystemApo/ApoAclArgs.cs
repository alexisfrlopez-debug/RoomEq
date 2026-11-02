namespace RoomEq.Audio.SystemApo;

/// <summary>
/// Arguments icacls des dossiers partagés APO (v3.9.7) — construction PURE
/// et testable, les mêmes chaînes servant au déploiement élevé et aux tests
/// de régression Linux (verrou : les SID ne doivent JAMAIS régresser).
///
/// Retour terrain v3.9.6 (« dès que l'EQ est activé sur une sortie, je n'ai
/// plus de son sur cette sortie ») : une APO que le moteur audio n'arrive
/// pas à instancier tue TOUTE la sortie (le graphe ne se construit pas —
/// comportement documenté par le terrain : « Failed to play test tone »,
/// mixeur vide). Or audiodg tourne sous une identité de SERVICE — les ACL
/// précédentes ne lui accordaient la lecture du dossier bin QUE via
/// « NT SERVICE\Audiosrv », un octroi icacls meilleur-effort dont l'échec
/// était invisible : si cette entrée manquait, LoadLibrary échouait
/// silencieusement dans audiodg et la sortie devenait MUETTE.
///
/// v3.9.7 — bulletproof par conception :
/// · bin (les DLL) : lecture + exécution pour TOUS (S-1-1-0), comme
///   Program Files — aucune identité de service ne peut être refusée ;
///   l'écriture reste réservée à SYSTEM et aux administrateurs ;
/// · racine apo (courbe + journal) : modification pour les Utilisateurs,
///   le service audio (par SON nom ET par le compte LocalService
///   S-1-5-19, résolu par SID donc indépendant de la langue) — le journal
///   de l'APO doit toujours pouvoir être écrit par audiodg.
/// </summary>
public static class ApoAclArgs
{
    /// <summary>SID de « Tout le monde » (lecture + exécution sur bin).</summary>
    public const string EveryoneSid = "*S-1-1-0";

    /// <summary>SID de LOCAL SERVICE — identité d'audiodg (S-1-5-19).</summary>
    public const string LocalServiceSid = "*S-1-5-19";

    /// <summary>SID de SYSTEM.</summary>
    public const string SystemSid = "*S-1-5-18";

    /// <summary>SID des administrateurs.</summary>
    public const string AdministratorsSid = "*S-1-5-32-544";

    /// <summary>SID des utilisateurs (S-1-5-32-545).</summary>
    public const string UsersSid = "*S-1-5-32-545";

    /// <summary>Compte du service audio (SID de service — nom canonique).</summary>
    public const string AudiosrvAccount = "NT SERVICE\\Audiosrv";

    /// <summary>
    /// Arguments icacls du dossier bin : ACL PROTGÉE contre l'héritage,
    /// SYSTEM et administrateurs tout pouvoir, TOUT LE MONDE lecture +
    /// exécution (le chargement par audiodg ne peut plus être refusé).
    /// </summary>
    public static string ForBin(string binDirectory) =>
        $"\"{binDirectory}\" /inheritance:r " +
        $"/grant {SystemSid}:(OI)(CI)F /grant {AdministratorsSid}:(OI)(CI)F " +
        $"/grant {EveryoneSid}:(OI)(CI)RX";

    /// <summary>
    /// Arguments icacls de la racine %ProgramData%\RoomEq\apo : ACL protégée,
    /// SYSTEM et administrateurs tout pouvoir, Utilisateurs + service audio
    /// (nom ET LocalService par SID) en modification — l'application écrit la
    /// courbe, audiodg écrit son journal.
    /// </summary>
    public static string ForRoot(string rootDirectory) =>
        $"\"{rootDirectory}\" /inheritance:r " +
        $"/grant {SystemSid}:(OI)(CI)F /grant {AdministratorsSid}:(OI)(CI)F " +
        $"/grant {UsersSid}:(OI)(CI)M " +
        $"/grant \"{AudiosrvAccount}:(OI)(CI)M\" /grant {LocalServiceSid}:(OI)(CI)M";
}
