using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using Microsoft.Win32;

namespace RoomEq.Audio.SystemApo;

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
/// — réattribuer un propriétaire autre que soi). Tout échec retourne
/// false/null : JAMAIS d'exception vers l'appelant (invariant 10).
/// </summary>
[SupportedOSPlatform("windows")]
internal static class ApoRegistrySecurity
{
    // ------------------------------------------------------- P/Invoke advapi32

    private const uint TOKEN_ADJUST_PRIVILEGES = 0x20;
    private const uint TOKEN_QUERY = 0x8;
    private const int SE_PRIVILEGE_ENABLED = 0x2;
    private const int ERROR_SUCCESS = 0;
    private const int ERROR_NOT_ALL_ASSIGNED = 1300; // privilège absent du token
    private const int OWNER_SECURITY_INFORMATION = 0x1;
    private const int DACL_SECURITY_INFORMATION = 0x4;

    /// <summary>TOKEN_PRIVILEGES réduit à UN privilège (LUID_AND_ATTRIBUTES).</summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct TokenPrivileges1
    {
        public int PrivilegeCount;
        public long Luid;
        public int Attributes;
    }

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool OpenProcessToken(IntPtr process, uint desiredAccess, out IntPtr token);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr handle);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess(); // pseudo-handle, ne jamais fermer

    [DllImport("advapi32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    private static extern bool LookupPrivilegeValue(string? systemName, string privilegeName, out long luid);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool AdjustTokenPrivileges(IntPtr token, bool disableAll,
        ref TokenPrivileges1 newState, int bufferLength, IntPtr previousState, IntPtr returnLength);

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
    /// Active un privilège du token courant. Retourne false s'il est absent
    /// du token — c'est le marqueur « processus non élevé » : SeTakeOwnership
    /// et SeRestore sont portés par tout token administrateur élevé.
    /// </summary>
    private static bool EnablePrivilege(string name)
    {
        try
        {
            if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ADJUST_PRIVILEGES | TOKEN_QUERY, out var token))
                return false;
            try
            {
                if (!LookupPrivilegeValue(null, name, out long luid))
                    return false;
                var tp = new TokenPrivileges1
                {
                    PrivilegeCount = 1,
                    Luid = luid,
                    Attributes = SE_PRIVILEGE_ENABLED,
                };
                if (!AdjustTokenPrivileges(token, false, ref tp, 0, IntPtr.Zero, IntPtr.Zero))
                    return false;
                // AdjustTokenPrivileges « réussit » même sans le privilège :
                // ERROR_NOT_ALL_ASSIGNED dit qu'il n'a rien pu activer
                return Marshal.GetLastWin32Error() != ERROR_NOT_ALL_ASSIGNED;
            }
            finally
            {
                CloseHandle(token);
            }
        }
        catch
        {
            return false;
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
    /// Idempotent et silencieux : false si un maillon manque (non élevé,
    /// politique restrictive) — l'écriture de l'appelant décidera alors.
    /// </summary>
    public static bool EnsureWritable(RegistryKey baseKey, string subKeyPath)
    {
        try
        {
            if (AdminsHaveFullControl(baseKey, subKeyPath))
                return true; // déjà inscriptible (ou clé déjà prise en main)
            return TakeOwnership(baseKey, subKeyPath)
                && GrantAdminsFullControl(baseKey, subKeyPath);
        }
        catch
        {
            return false;
        }
    }

    private static bool TakeOwnership(RegistryKey baseKey, string subKeyPath)
    {
        if (!EnablePrivilege("SeTakeOwnershipPrivilege"))
            return false;
        try
        {
            using var key = baseKey.OpenSubKey(subKeyPath,
                RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.TakeOwnership);
            if (key == null)
                return false;
            // descripteur OWNER SEUL, appliqué avec OWNER_SECURITY_INFORMATION :
            // le DACL en place n'est JAMAIS touché par cette étape
            var security = new RegistrySecurity();
            security.SetOwner(AdminsSid);
            return RegSetKeySecurity(key.Handle, OWNER_SECURITY_INFORMATION,
                security.GetSecurityDescriptorBinaryForm()) == ERROR_SUCCESS;
        }
        catch
        {
            return false;
        }
    }

    private static bool GrantAdminsFullControl(RegistryKey baseKey, string subKeyPath)
    {
        try
        {
            using var key = baseKey.OpenSubKey(subKeyPath,
                RegistryKeyPermissionCheck.ReadWriteSubTree,
                RegistryRights.ReadPermissions | RegistryRights.ChangePermissions);
            if (key == null)
                return false;
            // lecture → ajout d'UNE ACE → réécriture : le DACL persisté est
            // l'ancien + notre règle (pattern standard Get/SetAccessControl)
            var security = key.GetAccessControl();
            security.AddAccessRule(new RegistryAccessRule(
                AdminsSid, RegistryRights.FullControl,
                InheritanceFlags.ContainerInherit, PropagationFlags.None,
                AccessControlType.Allow));
            key.SetAccessControl(security);
            return true;
        }
        catch
        {
            return false;
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
            if (!EnablePrivilege("SeRestorePrivilege"))
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
