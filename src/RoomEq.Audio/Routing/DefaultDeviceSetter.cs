using System.Runtime.InteropServices;
using NAudio.CoreAudioApi;

namespace RoomEq.Audio.Routing;

/// <summary>Rôles de périphérique par défaut Windows.</summary>
internal enum ERole : uint
{
    Console = 0,
    Multimedia = 1,
    Communications = 2,
}

/// <summary>
/// Interface COM non documentée IPolicyConfig — déclinaison classique, valable de
/// Windows 7 à Windows 11 (utilisée par SoundSwitch, EarTrumpet…). L'ordre des
/// méthodes est imposé par la vtable réelle : ne pas le modifier.
/// </summary>
[ComImport]
[Guid("F8679F50-850A-41CF-9C72-430F290290C8")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfigClassic
{
    [PreserveSig] int GetMixFormat();
    [PreserveSig] int GetDeviceFormat();
    [PreserveSig] int ResetDeviceFormat();
    [PreserveSig] int SetDeviceFormat();
    [PreserveSig] int GetProcessingPeriod();
    [PreserveSig] int SetProcessingPeriod();
    [PreserveSig] int GetShareMode();
    [PreserveSig] int SetShareMode();
    [PreserveSig] int GetPropertyValue();
    [PreserveSig] int SetPropertyValue();
    [PreserveSig] int SetDefaultEndpoint(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool visible);
}

/// <summary>
/// Déclinaison alternative exposée par certains builds Windows récents (même
/// ordre de méthodes, IID différent). Tentée si la déclinaison classique échoue.
/// </summary>
[ComImport]
[Guid("CA286FC3-91FD-42C3-8E9B-525B6C284480")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfigRecent
{
    [PreserveSig] int GetMixFormat();
    [PreserveSig] int GetDeviceFormat();
    [PreserveSig] int ResetDeviceFormat();
    [PreserveSig] int SetDeviceFormat();
    [PreserveSig] int GetProcessingPeriod();
    [PreserveSig] int SetProcessingPeriod();
    [PreserveSig] int GetShareMode();
    [PreserveSig] int SetShareMode();
    [PreserveSig] int GetPropertyValue();
    [PreserveSig] int SetPropertyValue();
    [PreserveSig] int SetDefaultEndpoint(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool visible);
}

/// <summary>IID historique utilisée par RoomEq ≤ 1.2, conservée en dernier recours.</summary>
[ComImport]
[Guid("F8679F50-850A-41CF-9C26-2208C9FD032B")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IPolicyConfigLegacy
{
    [PreserveSig] int GetMixFormat();
    [PreserveSig] int GetDeviceFormat();
    [PreserveSig] int ResetDeviceFormat();
    [PreserveSig] int SetDeviceFormat();
    [PreserveSig] int GetProcessingPeriod();
    [PreserveSig] int SetProcessingPeriod();
    [PreserveSig] int GetShareMode();
    [PreserveSig] int SetShareMode();
    [PreserveSig] int GetPropertyValue();
    [PreserveSig] int SetPropertyValue();
    [PreserveSig] int SetDefaultEndpoint(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId, ERole role);
    [PreserveSig] int SetEndpointVisibility(
        [MarshalAs(UnmanagedType.LPWStr)] string deviceId, bool visible);
}

[ComImport]
[Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
internal class PolicyConfigClient
{
}

/// <summary>
/// Définit le périphérique par défaut de Windows (sortie OU entrée) via l'API COM
/// non documentée PolicyConfig (spec I-AUD-03 — standard du secteur : SoundSwitch,
/// EarTrumpet…). Plusieurs IID sont tentées dans l'ordre — les builds Windows
/// exposent des variantes — et chaque tentative est CONFIRMÉE par relecture du
/// périphérique par défaut : une IID défaillante ne peut pas passer inaperçue.
/// </summary>
public static class DefaultDeviceSetter
{
    /// <summary>
    /// IID candidates de IPolicyConfig, dans l'ordre d'essai (la classique d'abord,
    /// prouvée de Windows 7 à Windows 11).
    /// </summary>
    public static readonly Guid[] CandidateIids =
    {
        new("F8679F50-850A-41CF-9C72-430F290290C8"), // classique (Win7 → Win11)
        new("CA286FC3-91FD-42C3-8E9B-525B6C284480"), // builds récents
        new("F8679F50-850A-41CF-9C26-2208C9FD032B"), // historique RoomEq 1.x
    };

    /// <summary>Définit <paramref name="deviceId"/> comme sortie par défaut (tous rôles).</summary>
    public static bool SetDefaultRender(string deviceId) => SetDefault(deviceId, DataFlow.Render);

    /// <summary>Définit <paramref name="deviceId"/> comme entrée par défaut (tous rôles).</summary>
    public static bool SetDefaultCapture(string deviceId) => SetDefault(deviceId, DataFlow.Capture);

    private static bool SetDefault(string deviceId, DataFlow flow)
    {
        if (string.IsNullOrWhiteSpace(deviceId) || !OperatingSystem.IsWindows())
            return false;

        foreach (var iid in CandidateIids)
        {
            try
            {
                object client = new PolicyConfigClient();
                int hr = iid == CandidateIids[0]
                    ? ((IPolicyConfigClassic)client).SetDefaultEndpoint(deviceId, ERole.Console)
                    : iid == CandidateIids[1]
                        ? ((IPolicyConfigRecent)client).SetDefaultEndpoint(deviceId, ERole.Console)
                        : ((IPolicyConfigLegacy)client).SetDefaultEndpoint(deviceId, ERole.Console);
                if (hr != 0 || !ConfirmedDefault(deviceId, flow))
                    continue; // IID suivante

                // rôles restants (Console ayant réussi, les autres suivent normalement)
                try
                {
                    if (iid == CandidateIids[0])
                    {
                        var c = (IPolicyConfigClassic)client;
                        c.SetDefaultEndpoint(deviceId, ERole.Multimedia);
                        c.SetDefaultEndpoint(deviceId, ERole.Communications);
                    }
                    else if (iid == CandidateIids[1])
                    {
                        var c = (IPolicyConfigRecent)client;
                        c.SetDefaultEndpoint(deviceId, ERole.Multimedia);
                        c.SetDefaultEndpoint(deviceId, ERole.Communications);
                    }
                    else
                    {
                        var c = (IPolicyConfigLegacy)client;
                        c.SetDefaultEndpoint(deviceId, ERole.Multimedia);
                        c.SetDefaultEndpoint(deviceId, ERole.Communications);
                    }
                }
                catch
                {
                    // rôles secondaires : non bloquant
                }
                return true;
            }
            catch (InvalidCastException)
            {
                continue; // IID absente de ce build Windows : suivante
            }
            catch
            {
                continue; // COM momentanément indisponible : IID suivante
            }
        }
        return false;
    }

    /// <summary>
    /// Confirme la bascule en relisant le périphérique par défaut. Si la relecture
    /// elle-même échoue (sous-système audio occupé), on fait confiance au HRESULT.
    /// </summary>
    private static bool ConfirmedDefault(string deviceId, DataFlow flow)
    {
        try
        {
            return new MMDeviceEnumerator().GetDefaultAudioEndpoint(flow, Role.Console).ID == deviceId;
        }
        catch
        {
            return true;
        }
    }
}
