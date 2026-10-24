using RoomEq.Audio.Asio;
using RoomEq.Audio.Routing;

namespace RoomEq.Audio.Mac;

/// <summary>
/// Routage macOS : le greffon système RoomEqHAL (AudioServerPlugIn, installé par
/// le paquet .pkg dans /Library/Audio/Plug-Ins/HAL) crée le périphérique virtuel
/// « RoomEq Virtual ». macOS y joue ; le greffon boucle le son rendu vers le pont
/// intégré (même protocole que l'ASIO Windows) ; l'application lit le pont, passe
/// l'égaliseur et rend sur la vraie sortie CoreAudio.
///
/// Équivalent macOS du couple pilote noyau (roomeqvad.sys) + WASAPI.
/// </summary>
public static class MacAudioBridge
{
    /// <summary>Nom du périphérique virtuel exposé par le greffon.</summary>
    public const string VirtualDeviceName = "RoomEq Virtual";

    /// <summary>Chemin d'installation du greffon (paquet .pkg).</summary>
    public const string PluginPath = "/Library/Audio/Plug-Ins/HAL/RoomEqHAL.driver";

    /// <summary>
    /// Hôte du pont du périphérique virtuel — l'application le démarre dès
    /// l'ouverture pour que le greffon puisse s'y brancher à tout moment.
    /// </summary>
    public static AsioBridgeHost DeviceBridge { get; } = AsioBridgeHost.CreateDeviceBridge();

    /// <summary>Le greffon est-il installé sur ce Mac ?</summary>
    public static bool PluginInstalled =>
        OperatingSystem.IsMacOS() && Directory.Exists(PluginPath);

    /// <summary>
    /// Sondage de l'infrastructure virtuelle (équivalent macOS de
    /// AudioDeviceCatalog.ProbeVirtualCard). Le périphérique RoomEq Virtual
    /// est vu comme un câble : côté render = ce que macOS y joue ; côté
    /// capture = la boucle interne publiée dans le pont.
    /// </summary>
    public static VirtualCardStatus Probe()
    {
        var roomeq = CoreAudioCatalog.FindRoomEqVirtual();
        uint defaultId = CoreAudioCatalog.GetDefaultOutputDeviceId();
        string? defaultName = defaultId != 0 ? CoreAudioApi.ReadDeviceName(defaultId) : null;
        string? defaultUid = defaultId != 0 ? CoreAudioApi.ReadDeviceUid(defaultId) : null;

        bool found = roomeq != null;
        return new VirtualCardStatus
        {
            ProviderId = "roomeq",
            ProviderDisplayName = "Périphérique virtuel RoomEq (macOS)",
            CableInputFound = found,
            CableOutputFound = found, // boucle interne au greffon : les deux côtés vont ensemble
            CableInputName = found ? roomeq!.Name : null,
            CableInputId = found ? roomeq!.Uid : null,
            CableOutputName = found ? roomeq!.Name + " (boucle interne)" : null,
            CableOutputId = found ? roomeq!.Uid : null,
            IsDefaultRender = found && roomeq!.DeviceId == defaultId,
            DefaultRenderName = defaultName,
            DefaultRenderId = defaultUid,
        };
    }
}
