using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace RoomEq.Audio.SystemApo;

/// <summary>
/// LUID natif (winnt.h) : DEUX DWORD de 4 octets, alignement 4 — jamais un
/// long C# (alignement 8) qui insérerait 4 octets de padding dans
/// <see cref="ApoTokenPrivileges"/> et falsifierait le LUID lu par le noyau
/// (correctif v3.9.3). Déclaration de marshaling SANS dépendance Windows :
/// le layout est vérifiable par test sous Linux.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ApoLuid
{
    public uint LowPart;
    public int HighPart;
}

/// <summary>
/// TOKEN_PRIVILEGES natif réduit à UN privilège (LUID_AND_ATTRIBUTES) —
/// 16 octets exactement : PrivilegeCount à l'offset 0, Luid à 4, Attributes
/// à 12. RÉGRESSION v3.9.2 verrouillée par ApoRegistrySecurityLayoutTests :
/// « long Luid » marshale en 24 octets (padding après PrivilegeCount) →
/// AdjustTokenPrivileges voyait Luid.LowPart = 0 et Attributes = 0 →
/// ERROR_NOT_ALL_ASSIGNED → prise de possession jamais tentée.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal struct ApoTokenPrivileges
{
    public int PrivilegeCount;
    public ApoLuid Luid;
    public int Attributes;
}

/// <summary>
/// Mécanique de sécurité du registre pour l'activation APO (v3.9.2) —
/// portage C# fidèle de <c>takeOwnership</c>/<c>makeWritable</c> d'Equalizer
/// APO (helpers/RegistryHelper.cpp, mécanisme en production depuis 10+ ans).
/// FAIT ÉTABLI (retour terrain v3.9.1 + code source d'Equalizer APO) : les
/// clés d'endpoints <c>MMDevices\Audio\Render</c> sont créées par le service
/// audio (SYSTEM) et leur DACL n'accorde PAS l'écriture aux administrateurs —
/// même élevé, <c>OpenSubKey(writable)</c> est refusé (« Requested registry
/// access is not allowed »). Le processus d'installation étant élevé, il :
/// (1) active SeTakeOwnershipPrivilege, (2) prend possession de la clé pour
/// le groupe Administrateurs (WRITE_OWNER), (3) s'ajoute une ACE FullControl
/// héritée par les sous-clés (WRITE_DAC — droit implicite de l'owner), puis
/// crée/écrit FxProperties. La sécurité d'origine (owner + DACL) est capturée
/// AVANT par le registrar et restituée à la désactivation (SeRestorePrivilege
/// — réattribuer un propriétaire autre que soi). Tout échec retourne un
/// diagnostic (null = succès) : JAMAIS d'exception vers l'appelant
/// (invariant 10).
/// </summary>
/// <remarks>
/// v3.9.3 — CORRECTIF CRITIQUE d'alignement de structure : la structure
/// interop <c>TOKEN_PRIVILEGES</c> déclarait le LUID comme <c>long</c> C#
/// (alignement 8 octets), qui insérait 4 octets de padding après
/// <c>PrivilegeCount</c> : le marshaling produisait 24 octets au lieu de 16
/// et le noyau lisait <c>Luid.LowPart = 0</c> (le padding !) et
/// <c>Attributes = 0</c> (l'octet haut du long) — donc un LUID falsifié
/// inexistant dans le jeton. <c>AdjustTokenPrivileges</c> « réussissait »
/// avec <c>ERROR_NOT_ALL_ASSIGNED</c> : <c>EnablePrivilege</c> croyait à
/// tort « processus non élevé », la prise de possession n'a JAMAIS été
/// tentée et l'activation échouait en « clé protégée » malgré le portage
/// v3.9.2 (cause exacte du « toujours le problème de clé protégée »).
/// Le LUID est désormais deux DWORD de 4 octets, alignement 4, exactement
/// comme le natif winnt.h — verrouillé par test de marshaling
/// (ApoRegistrySecurityLayoutTests).
/// v3.9.4 — CORRECTIF DE BINDING P/INVOKE : <c>CloseHandle</c> était importé
/// d'advapi32.dll alors que ce point d'entrée n'existe que dans
/// kernel32.dll. Le nettoyage du jeton (finally de EnablePrivilege) levait
/// EntryPointNotFoundException qui MASQUAIT le succès : le privilège
/// SeTakeOwnershipPrivilege était réellement activé (le LUID v3.9.3 était
/// correct) mais EnablePrivilege rapportait un échec, donc le takeover
/// était abandonné avant sa première écriture. La DLL d'origine de chaque
/// import est désormais verrouillée par test (ApoRegistrySecurityPinvokeTests).
/// </remarks>
[SupportedOSPlatform("windows")]
internal static class ApoRegistrySecurity
{
    // ------------------------------------------ P/Invoke (advapi32, kernel32)

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x20;
    private const uint TOKEN_QUERY = 0x8;
    private const int SE_PRIVILEGE_ENABLED = 0x2;
    private const int ERROR_SUCCESS = 0;
    private const int ERROR_NOT_ALL_ASSIGNED = 1300; // privilège absent du token
    private const int OWNER_SECURITY_INFORMATION = 0x1;
    private const int DACL_SECURITY_INFORMATION = 0x4;

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    /// <summary>
    /// CloseHandle est exporté par KERNEL32.DLL — il n'existe PAS dans
    /// advapi32.dll (correctif v3.9.4). Importé d'advapi32 à tort depuis la
    /// v3.9.2, chaque appel levait EntryPointNotFoundException : « le point
    /// d'entrée CloseHandle est introuvable dans la DLL advapi32.dll ». Or ce
    /// nettoyage a lieu dans le finally de EnablePrivilege : l'exception
    /// REMPLAÇAIT le « return null » de succès — le privilège était BIEN
    /// activé (le LUID v3.9.3 était correct, AdjustTokenPrivileges avait
    /// réussi) mais EnablePrivilege répondait « impossible d'activer le
    /// privilège », le takeover n'était jamais tenté et l'activation
    /// échouait en « clé protégée » (retour terrain 3.9.3, diagnostic par
    /// maillon v3.9.3 : c'est LUI qui a nommé le coupable). Verrouillé par
    /// ApoRegistrySecurityPinvokeTests (réflexion pure, exécutable Linux).
    /// </summary>
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess(); // pseudo-handle, ne jamais fermer

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? systemName, string privilegeName, out ApoLuid luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll,
        ref ApoTokenPrivileges newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

    /// <summary>
    /// RegSetKeySecurity brut : contrairement à RegistryKey.SetAccessControl
    /// (qui persiste owner + groupe + DACL et remplacerait un DACL absent par
    /// « accès total »), on applique EXACTEMENT les sections demandées — le
    /// propriétaire seul ne touche jamais au DACL, et vice-versa.
    /// </summary>
    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern int RegSetKeySecurity(Microsoft.Win32.SafeHandles.SafeRegistryHandle hKey,
        int securityInformation, byte[] securityDescriptor);

    // ------------------------------------------------------------ primitives

    /// <summary>Le groupe Administrateurs (S-1-5-32-544), par SID —
    /// indépendant de la langue de Windows (cf. icacls par SID, D26).</summary>
    private static SecurityIdentifier AdminsSid { get; } =
        new(WellKnownSidType.BuiltinAdministratorsSid, null);

    /// <summary>
    /// Active un privilège du token courant. Retourne null (succès) ou le
    /// diagnostic du maillon défaillant. ERROR_NOT_ALL_ASSIGNED = le privilège
    /// est absent du jeton — c'est le marqueur « processus non élevé » :
    /// SeTakeOwnership et SeRestore sont portés par tout token
    /// administrateur élevé.
    /// </summary>
    private static string? EnablePrivilege(string name)
    {
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
                return "OpenProcessToken a échoué (code " + Marshal.GetLastWin32Error() + ")";
            try
            {
                if (!LookupPrivilegeValue(null, name, out ApoLuid luid))
                    return "LookupPrivilegeValue(" + name + ") a échoué (code "
                        + Marshal.GetLastWin32Error() + ")";
                var tp = new ApoTokenPrivileges
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED,
                };
                if (!AdjustTokenPrivileges(token, false, ref tp,
                        Marshal.SizeOf<ApoTokenPrivileges>(), IntPtr.Zero, IntPtr.Zero))
                    return "AdjustTokenPrivileges(" + name + ") a échoué (code "
                        + Marshal.GetLastWin32Error() + ")";
                // AdjustTokenPrivileges « réussit » même sans le privilège :
                // ERROR_NOT_ALL_ASSIGNED dit qu'il n'a rien pu activer
                int assigned = Marshal.GetLastWin32Error();
                if (assigned == ERROR_NOT_ALL_ASSIGNED)
                    return "le privilège " + name + " est absent du jeton du processus "
                        + "(élévation administrateur refusée ou non demandée)";
                return null;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        catch (Exception ex)
        {
            return "activation du privilège " + name + " impossible (" + ex.Message + ")";
        }
    }

    /// <summary>
    /// Les Administrateurs ont-ils FullControl sur cette clé ? (lecture pure).
    /// C'est le test « l'activation devra-t-elle prendre possession ? » : une
    /// clé où les admins ont déjà tout ne sera ni capturée ni modifiée.
    /// </summary>
    public static bool AdminsHaveFullControl(RegistryKey baseKey, string subKeyPath)
    {
        try
        {
            using var key = baseKey.OpenSubKey(subKeyPath,
                RegistryKeyPermissionCheck.Default, RegistryRights.ReadPermissions);
            if (key == null)
                return false; // clé absente : rien à évaluer
            var rules = key.GetAccessControl()
                .GetAccessRules(true, true, typeof(SecurityIdentifier));
            foreach (RegistryAccessRule rule in rules)
            {
                if (rule.AccessControlType != AccessControlType.Allow)
                    continue;
                if (rule.IdentityReference is not SecurityIdentifier sid || sid != AdminsSid)
                    continue;
                if ((rule.RegistryRights & RegistryRights.FullControl) == RegistryRights.FullControl)
                    return true;
            }
            return false;
        }
        catch
        {
            return false; // illisible : supposer restrictif (prudence → snapshot)
        }
    }

    /// <summary>
    /// Capture owner + DACL d'une clé (descripteur de sécurité binaire en
    /// Base64 + SID du propriétaire lisible) pour restitution exacte.
    /// Null si la clé est absente ou illisible.
    /// </summary>
    public static ApoKeySecurity? Capture(RegistryKey baseKey, string subKeyPath)
    {
        try
        {
            using var key = baseKey.OpenSubKey(subKeyPath,
                RegistryKeyPermissionCheck.Default, RegistryRights.ReadPermissions);
            if (key == null)
                return null;
            var security = key.GetAccessControl(
                AccessControlSections.Owner | AccessControlSections.Access);
            var owner = security.GetOwner(typeof(SecurityIdentifier)) as SecurityIdentifier;
            return new ApoKeySecurity(
                owner?.Value ?? "",
                Convert.ToBase64String(security.GetSecurityDescriptorBinaryForm()));
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Rend une clé inscriptible par les Administrateurs :
    /// (1) prise de possession pour le groupe Administrateurs —
    ///     SeTakeOwnershipPrivilege, WRITE_OWNER ;
    /// (2) ACE FullControl héritée par les sous-clés — WRITE_DAC, droit
    ///     implicite du propriétaire (Equalizer APO : makeWritable).
    /// Idempotent. Retourne null (inscriptible — déjà prise en main ou
    /// takeover réussi) ou le diagnostic du PREMIER maillon défaillant :
    /// jamais d'exception, jamais d'information perdue (correctif v3.9.3 —
    /// un échec silencieux ici se paie en allers-retours terrain).
    /// </summary>
    public static string? EnsureWritable(RegistryKey baseKey, string subKeyPath)
    {
        try
        {
            if (AdminsHaveFullControl(baseKey, subKeyPath))
                return null; // déjà inscriptible (ou clé déjà prise en main)
            string? take = TakeOwnership(baseKey, subKeyPath);
            if (take != null)
                return take;
            return GrantAdminsFullControl(baseKey, subKeyPath);
        }
        catch (Exception ex)
        {
            return "prise de possession interrompue (" + ex.Message + ")";
        }
    }

    /// <summary>null = succès, sinon diagnostic (correctif v3.9.3).</summary>
    private static string? TakeOwnership(RegistryKey baseKey, string subKeyPath)
    {
        string? privilege = EnablePrivilege("SeTakeOwnershipPrivilege");
        if (privilege != null)
            return privilege; // v3.9.2 : c'est ICI que tout s'arrêtait (LUID
                              // falsifié → ERROR_NOT_ALL_ASSIGNED → faux
                              // « non élevé ») sans jamais essayer d'écrire
        try
        {
            using var key = baseKey.OpenSubKey(subKeyPath,
                RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.TakeOwnership);
            if (key == null)
                return "la clé « " + subKeyPath + " » est introuvable (périphérique retiré ?)";
            // descripteur OWNER SEUL, appliqué avec OWNER_SECURITY_INFORMATION :
            // le DACL en place n'est JAMAIS touché par cette étape
            var security = new RegistrySecurity();
            security.SetOwner(AdminsSid);
            int rc = RegSetKeySecurity(key.Handle, OWNER_SECURITY_INFORMATION,
                security.GetSecurityDescriptorBinaryForm());
            if (rc != ERROR_SUCCESS)
                return "RegSetKeySecurity(owner) a échoué (code " + rc + ")";
            return null;
        }
        catch (Exception ex)
        {
            return "ouverture WRITE_OWNER de « " + subKeyPath + " » refusée ("
                + ex.Message + ")";
        }
    }

    /// <summary>null = succès, sinon diagnostic (correctif v3.9.3).</summary>
    private static string? GrantAdminsFullControl(RegistryKey baseKey, string subKeyPath)
    {
        try
        {
            using var key = baseKey.OpenSubKey(subKeyPath,
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.ReadPermissions | RegistryRights.ChangePermissions);
            if (key == null)
                return "la clé « " + subKeyPath + " » est introuvable (périphérique retiré ?)";
            // lecture → ajout d'UNE ACE → réécriture : le DACL persisté est
            // l'ancien + notre règle (RegistrySecurity.Persist n'écrit QUE la
            // section modifiée — le DACL —, propriétaire et groupe intouchés)
            var security = key.GetAccessControl();
            security.AddAccessRule(new RegistryAccessRule(
                AdminsSid, RegistryRights.FullControl,
                InheritanceFlags.ContainerInherit, PropagationFlags.None,
                AccessControlType.Allow));
            key.SetAccessControl(security);
            return null;
        }
        catch (Exception ex)
        {
            return "octroi de l'accès aux administrateurs refusé sur « " + subKeyPath
                + " » (" + ex.Message + ")";
        }
    }

    /// <summary>
    /// Restitue owner + DACL capturés avant la prise de possession (la clé
    /// redevient la propriété de SYSTEM). SeRestorePrivilege est requis pour
    /// réattribuer un propriétaire autre que soi-même (token élevé requis).
    /// Best effort : false silencieux si la clé a disparu ou le privilège manque.
    /// </summary>
    public static bool Restore(RegistryKey baseKey, string subKeyPath, ApoKeySecurity snapshot)
    {
        try
        {
            byte[] descriptor;
            try
            {
                descriptor = Convert.FromBase64String(snapshot.SdBase64);
            }
            catch (FormatException)
            {
                return false; // capture corrompue : ne rien toucher plutôt
            }
            using var key = baseKey.OpenSubKey(subKeyPath,
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.ChangePermissions | RegistryRights.TakeOwnership);
            if (key == null)
                return false; // clé disparue (périphérique retiré)
            if (EnablePrivilege("SeRestorePrivilege") != null)
                return false;
            return RegSetKeySecurity(key.Handle,
                OWNER_SECURITY_INFORMATION | DACL_SECURITY_INFORMATION,
                descriptor) == ERROR_SUCCESS;
        }
        catch
        {
            return false;
        }
    }
}
