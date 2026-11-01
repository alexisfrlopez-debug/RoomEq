using Microsoft.Win32;
using NAudio.CoreAudioApi;

namespace RoomEq.Audio.SystemApo;

/// <summary>
/// Implémentation Windows de <see cref="IApoEndpointStore"/> : registre
/// HKLM\…\MMDevices\Audio\Render\{"{GUID}"}\FxProperties pour les valeurs,
/// énumérateur MMDevice (NAudio) pour les noms conviviaux.
/// Hors Windows (macOS/CI) : listes vides, lectures nulles, écritures sans
/// effet — jamais d'exception (invariant 10 : le module est inerte).
/// NB : chaque garde de plateforme est un <c>OperatingSystem.IsWindows()</c>
/// EN LIGNE — c'est la forme reconnue par l'analyseur CA1416.
/// </summary>
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

    /// <summary>Clé registre FxProperties d'un endpoint (null si absente/inaccessible).</summary>
    private RegistryKey? OpenFx(string endpointKey, bool writable)
    {
        if (!OperatingSystem.IsWindows())
            return null;
        try
        {
            return Registry.LocalMachine.OpenSubKey(
                ApoConstants.RenderEndpointsKey + @"\" + endpointKey + @"\" + ApoConstants.FxPropertiesSubkey,
                writable);
        }
        catch
        {
            return null; // clé absente (endpoint sans slot d'effets) ou accès refusé
        }
    }

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
        using var k = OpenFx(endpointKey, writable: true)
            ?? throw new InvalidOperationException(
                "Ce périphérique n'expose pas de slot d'effets système (FxProperties) : " +
                "impossible d'y inscrire l'égaliseur.");
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
        using var k = OpenFx(endpointKey, writable: true)
            ?? throw new InvalidOperationException(
                "Ce périphérique n'expose pas de slot d'effets système (FxProperties) : " +
                "impossible d'y inscrire l'égaliseur.");
        if (value == null)
            k.DeleteValue(valueName, throwOnMissingValue: false);
        else
            k.SetValue(valueName, value, RegistryValueKind.DWord);
    }

    // ------------------------------------------------------------ COM (élevé)

    /// <summary>La DLL APO est-elle inscrite au CLSID RoomEq (chemin exact) ?</summary>
    public static bool ComRegistered(string expectedDllPath)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(ApoConstants.ComClsidKey + @"\InprocServer32");
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
        using var clsid = Registry.LocalMachine.CreateSubKey(ApoConstants.ComClsidKey);
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
            Registry.LocalMachine.DeleteSubKeyTree(ApoConstants.ComClsidKey, throwOnMissingSubKey: false);
        }
        catch
        {
            // l'absence d'inscription n'est pas une erreur de désinstallation
        }
    }
}
