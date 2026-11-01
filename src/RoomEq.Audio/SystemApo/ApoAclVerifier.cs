using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;

namespace RoomEq.Audio.SystemApo;

/// <summary>
/// Vérification des ACL du dossier partagé APO (correctif v3.9.5). FAIT
/// ÉTABLI (retour terrain « l'EQ est activé et chargé mais inaudible ») :
/// <c>ApoSetupCommands</c> appliquait les permissions via icacls « au mieux
/// effort » — un échec silencieux d'icacls laissait
/// %ProgramData%\RoomEq\apo SANS l'entrée « Utilisateurs : Modifier » : la
/// DLL se chargeait (le moteur audio lit via SYSTEM/Admins) mais
/// l'application non élevée ne pouvait PLUS écrire active.json — la courbe
/// n'atteignait jamais l'APO, sans le moindre message. Désormais
/// l'activation ÉCHOUE avec un verdict explicite si les Utilisateurs
/// (S-1-5-32-545) n'ont pas Modifier sur le dossier. La vérification de
/// règles est PURE (testable sous Linux) ; seule la lecture du système de
/// fichiers réel exige Windows.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ApoAclVerifier
{
    /// <summary>
    /// Le groupe Utilisateurs (S-1-5-32-545) a-t-il Modifier (ou tout
    /// contrôle) sur CE dossier ? La surcharge accepte aussi FullControl —
    /// les ACE appliquées par icacls accordent M, un M manuel suffit.
    /// False en cas d'erreur (prudence : refuser l'activation plutôt que
    /// livrer un EQ muet).
    /// </summary>
    public static bool UsersCanModify(string directory)
    {
        try
        {
            var rules = new DirectoryInfo(directory)
                .GetAccessControl()
                .GetAccessRules(true, true, typeof(SecurityIdentifier));
            return UsersCanModify(rules);
        }
        catch
        {
            return false; // illisible / non supporté : considérer non conforme
        }
    }

    /// <summary>Surcharge PURE : les règles données accordent-elles Modifier
    /// (ou FullControl) au groupe Utilisateurs ? (test Linux : construction
    /// d'objets ACL sans toucher au système de fichiers)</summary>
    public static bool UsersCanModify(AuthorizationRuleCollection rules)
    {
        var users = new SecurityIdentifier(WellKnownSidType.BuiltinUsersSid, null);
        foreach (var rule in rules.OfType<FileSystemAccessRule>())
        {
            if (rule.AccessControlType != AccessControlType.Allow)
                continue;
            if (rule.IdentityReference is not SecurityIdentifier sid || sid != users)
                continue;
            if ((rule.FileSystemRights & FileSystemRights.Modify) != 0
                || (rule.FileSystemRights & FileSystemRights.FullControl) != 0)
                return true;
        }
        return false;
    }
}
