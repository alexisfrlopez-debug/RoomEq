using System.Security;
using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace RoomEq.Audio.SystemApo;

/// <summary>
/// Écriture registre refusée APRÈS la tentative de reprise de possession
/// (v3.9.3) : porte le DIAGNOSTIC du maillon défaillant (privilège absent,
/// WRITE_OWNER refusé, RegSetKeySecurity échoué…) pour que le verdict de
/// l'activation soit actionnable au premier retour terrain au lieu d'un
/// générique « clé protégée ». Dérive de <see cref="SecurityException"/> :
/// captée par le même gestionnaire du registrar, avant lui.
/// </summary>
public sealed class ApoRegistryRefusalException : SecurityException
{
    /// <summary>Diagnostic du maillon défaillant de la prise de possession.</summary>
    public string TakeoverDiagnostic { get; }

    public ApoRegistryRefusalException(string takeoverDiagnostic, string message)
        : base(message)
        => TakeoverDiagnostic = takeoverDiagnostic;
}

/// <summary>
/// Implémentation Windows de <see cref="IApoEndpointStore"/> : registre
/// HKLM\…\MMDevices\Audio\Render\{"{GUID}"}\FxProperties pour les valeurs,
/// énumérateur MMDevice (NAudio) pour les noms conviviaux.
/// Hors Windows (macOS/CI) : listes vides, lectures nulles, écritures sans
/// effet — jamais d'exception (invariant 10 : le module est inerte).
/// NB : chaque garde de plateforme est un <c>OperatingSystem.IsWindows()</c>
/// EN LIGNE — c'est la forme reconnue par l'analyseur CA1416.
/// </summary>
/// <remarks>
/// v3.9.1 — création de la sous-clé FxProperties : un pilote sans effets
/// déclarés (ex. pilote générique Windows « Realtek(R) Audio ») n'expose PAS
/// de sous-clé FxProperties, et l'activation échouait alors sur la plupart
/// des sorties réelles (« n'expose pas de slot d'effets système »). Or le
/// moteur audio lit le CONTENU de cette clé quel que soit son auteur :
/// « If no properties are present, or if the FxProperties key is absent
/// entirely, then no system effect APOs are used » (É. Dechamps, notes APO)
/// — la créer et y écrire notre CLSID EFX suffit. C'est le mécanisme
/// standalone mis en œuvre par Equalizer APO depuis 10+ ans (spec I-AUD-09
/// as-built v1.2).
/// </remarks>
public sealed class WindowsApoEndpointStore : IApoEndpointStore
{
    /// <summary>Endpoints de rendu actifs : clé "{GUID}" + nom convivial.</summary>
    public IReadOnlyList<ApoEndpointRecord> EnumerateRenderEndpoints()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<ApoEndpointRecord>();
        var list = new List<ApoEndpointRecord>();
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                list.Add(new ApoEndpointRecord(
                    ApoConstants.EndpointKeyFromMmdeviceId(d.ID), d.FriendlyName));
            }
        }
        catch
        {
            // sous-système audio indisponible (session sans service) : liste vide
        }
        return list;
    }

    // ------------------------------------------------------- accès au registre

    /// <summary>
    /// HKLM dans la vue 64 bits, EXPLICITEMENT : le service audio et audiodg
    /// (64 bits) écrivent MMDevices dans la vue native, et un processus 32
    /// bits serait redirigé vers WOW6432Node (ne rien y voir). L'application
    /// est publiée win-x64 — c'est une ceinture de sécurité anti-régression
    /// si un jour un build 32 bits voyait le jour.
    /// </summary>
    private static RegistryKey OpenHklm()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(); // garde CA1416 — appelants publics déjà gardés
        return RegistryKey.OpenBaseKey(
            RegistryHive.LocalMachine,
            Environment.Is64BitOperatingSystem ? RegistryView.Registry64 : RegistryView.Default);
    }

    private static string EndpointPath(string endpointKey)
        => ApoConstants.RenderEndpointsKey + @"\" + endpointKey;

    private static string FxPath(string endpointKey)
        => EndpointPath(endpointKey) + @"\" + ApoConstants.FxPropertiesSubkey;

    /// <summary>Clé registre FxProperties d'un endpoint (null si absente/inaccessible).</summary>
    private RegistryKey? OpenFx(string endpointKey, bool writable)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            using var hklm = OpenHklm();
            return hklm.OpenSubKey(FxPath(endpointKey), writable);
        }
        catch
        {
            return null; // clé absente ou accès refusé (lecture) : considérée absente
        }
    }

    /// <summary>
    /// Clé FxProperties pour ÉCRITURE, CRÉÉE si absente (correctif v3.9.1) :
    /// ouvre la clé de l'endpoint en écriture (exception claire si elle a
    /// disparu — périphérique retiré) puis crée la sous-clé FxProperties
    /// manquante. Jamais de clé « fantôme » : l'endpoint lui-même n'est
    /// jamais créé, seule sa sous-clé d'effets l'est.
    /// v3.9.2 : les clés d'endpoints MMDevices appartiennent à SYSTEM et leur
    /// DACL n'accorde PAS l'écriture aux administrateurs — l'écriture directe
    /// est refusée même élevée (« Requested registry access is not allowed »,
    /// retour terrain v3.9.1). Sur ce refus, on applique le mécanisme
    /// Equalizer APO (helpers/RegistryHelper.cpp, 10+ ans de terrain) : prise
    /// de possession pour les Administrateurs + ACE FullControl héritée,
    /// puis RÉESSAI — la désactivation restituera owner et DACL d'origine
    /// (snapshot capturé par le registrar AVANT la première écriture).
    /// v3.9.3 : si le RÉESSAI échoue encore, l'exception emporte le diagnostic
    /// du takeover (<see cref="ApoRegistryRefusalException"/>) — plus jamais
    /// d'échec muet à débusquer à l'aveugle.
    /// </summary>
    private RegistryKey OpenFxForWrite(string endpointKey)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(); // garde CA1416 — appelants publics déjà gardés
        try
        {
            return OpenOrCreateFx(endpointKey);
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException)
        {
            using var hklm = OpenHklm();
            string? why = ApoRegistrySecurity.EnsureWritable(hklm, EndpointPath(endpointKey));
            if (FxPropertiesExists(endpointKey))
            {
                string? whyFx = ApoRegistrySecurity.EnsureWritable(hklm, FxPath(endpointKey));
                why ??= whyFx; // le refus de l'endpoint a priorité, mais un
                               // échec propre à FxProperties ne doit pas se perdre
            }
            try
            {
                return OpenOrCreateFx(endpointKey);
            }
            catch (Exception ex2) when (ex2 is SecurityException or UnauthorizedAccessException)
            {
                throw new ApoRegistryRefusalException(
                    why ?? "la prise de possession a réussi mais l'écriture reste refusée",
                    "Le registre a refusé l'écriture sur cette sortie (clé protégée) : "
                    + (why ?? "la prise de possession a réussi mais l'écriture reste refusée")
                    + " — refus initial « " + ex.Message + " », après reprise « " + ex2.Message + " »");
            }
        }
    }

    /// <summary>Ouverture en écriture ou création de FxProperties (sans reprise).</summary>
    private RegistryKey OpenOrCreateFx(string endpointKey)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(); // garde CA1416 — appelée derrière OpenFxForWrite
        using var hklm = OpenHklm();
        var fx = hklm.OpenSubKey(FxPath(endpointKey), writable: true);
        if (fx != null)
            return fx;
        using var endpoint = hklm.OpenSubKey(EndpointPath(endpointKey), writable: true)
            ?? throw new InvalidOperationException(
                "la clé registre de cette sortie est introuvable (périphérique retiré ?)");
        return endpoint.CreateSubKey(ApoConstants.FxPropertiesSubkey);
    }

    // ------------------------------------------------------------- valeurs

    public string? GetString(string endpointKey, string valueName)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        using var k = OpenFx(endpointKey, writable: false);
        if (k == null)
            return null;
        try
        {
            return k.GetValue(valueName) as string;
        }
        catch
        {
            return null;
        }
    }

    public void SetString(string endpointKey, string valueName, string? value)
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var k = OpenFxForWrite(endpointKey);
        if (value == null)
            k.DeleteValue(valueName, throwOnMissingValue: false);
        else
            k.SetValue(valueName, value, RegistryValueKind.String);
    }

    public int? GetInt(string endpointKey, string valueName)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        using var k = OpenFx(endpointKey, writable: false);
        if (k == null)
            return null;
        try
        {
            return k.GetValue(valueName) as int?;
        }
        catch
        {
            return null;
        }
    }

    public void SetInt(string endpointKey, string valueName, int? value)
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var k = OpenFxForWrite(endpointKey);
        if (value == null)
            k.DeleteValue(valueName, throwOnMissingValue: false);
        else
            k.SetValue(valueName, value.Value, RegistryValueKind.DWord);
    }

    // ------------------------------------------- structure de la sous-clé (v3.9.1)

    public bool FxPropertiesExists(string endpointKey)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        using var k = OpenFx(endpointKey, writable: false);
        return k != null;
    }

    public IReadOnlyList<string> FxValueNames(string endpointKey)
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<string>();
        using var k = OpenFx(endpointKey, writable: false);
        if (k == null)
            return Array.Empty<string>();
        try
        {
            return k.GetValueNames();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    public void DeleteFxProperties(string endpointKey)
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var hklm = OpenHklm();
            hklm.DeleteSubKey(FxPath(endpointKey), throwOnMissingSubKey: false);
        }
        catch
        {
            // meilleur effort : une sous-clé restante mais vide est sans effet
            // pour le moteur audio (les VALEURS, elles, sont déjà restaurées)
        }
    }

    // --------------------------------------------- sécurité (v3.9.2, mécanisme
    // Equalizer APO : MMDevices appartient à SYSTEM, pas aux administrateurs)

    /// <summary>
    /// Capture la sécurité des clés qui devront être modifiées par
    /// l'activation — uniquement celles où les administrateurs n'ont PAS
    /// déjà FullControl (les autres ne changent pas). Hors Windows : null.
    /// </summary>
    public ApoSecuritySnapshot? CaptureSecurity(string endpointKey)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            using var hklm = OpenHklm();
            ApoKeySecurity? endpoint = null, fx = null;
            if (!ApoRegistrySecurity.AdminsHaveFullControl(hklm, EndpointPath(endpointKey)))
                endpoint = ApoRegistrySecurity.Capture(hklm, EndpointPath(endpointKey));
            if (FxPropertiesExists(endpointKey)
                && !ApoRegistrySecurity.AdminsHaveFullControl(hklm, FxPath(endpointKey)))
                fx = ApoRegistrySecurity.Capture(hklm, FxPath(endpointKey));
            return endpoint == null && fx == null
                ? null
                : new ApoSecuritySnapshot(endpoint, fx);
        }
        catch
        {
            return null; // lecture impossible : pas de snapshot, pas de garantie
        }
    }

    /// <summary>
    /// Restitue owner + DACL d'origine (après la restauration des valeurs et
    /// la suppression éventuelle de la sous-clé). Best effort, jamais
    /// d'exception — une sécurité non restituée ne change pas le son.
    /// </summary>
    public void RestoreSecurity(string endpointKey, ApoSecuritySnapshot? snapshot)
    {
        if (!OperatingSystem.IsWindows() || snapshot == null)
            return;
        try
        {
            using var hklm = OpenHklm();
            if (snapshot.Endpoint is { } endpoint)
                ApoRegistrySecurity.Restore(hklm, EndpointPath(endpointKey), endpoint);
            if (snapshot.Fx is { } fx)
                ApoRegistrySecurity.Restore(hklm, FxPath(endpointKey), fx);
        }
        catch
        {
            // les VALEURS sont déjà restaurées : une ACL/Admins restante est
            // sans effet auditif (et sans risque : admin uniquement)
        }
    }

    // ------------------------------------------------------------ COM (élevé)

    /// <summary>La DLL APO est-elle inscrite au CLSID RoomEq (chemin exact) ?</summary>
    public static bool ComRegistered(string expectedDllPath)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using var hklm = OpenHklm();
            using var k = hklm.OpenSubKey(ApoConstants.ComClsidKey + @"\InprocServer32");
            return string.Equals(k?.GetValue(null) as string, expectedDllPath,
                                 StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Inscrit la DLL APO au CLSID RoomEq (HKLM — processus élevé).</summary>
    public static void RegisterCom(string dllPath)
    {
        if (!OperatingSystem.IsWindows())
            return;
        using var hklm = OpenHklm();
        using var clsid = hklm.CreateSubKey(ApoConstants.ComClsidKey);
        clsid.SetValue(null, ApoConstants.ComFriendlyName);
        using var server = clsid.CreateSubKey("InprocServer32");
        server.SetValue(null, dllPath);
        server.SetValue("ThreadingModel", "Both");
    }

    /// <summary>Retire l'inscription COM du CLSID RoomEq (processus élevé).</summary>
    public static void UnregisterCom()
    {
        if (!OperatingSystem.IsWindows())
            return;
        try
        {
            using var hklm = OpenHklm();
            hklm.DeleteSubKeyTree(ApoConstants.ComClsidKey, throwOnMissingSubKey: false);
        }
        catch
        {
            // l'absence d'inscription n'est pas une erreur de désinstallation
        }
    }
}
