using RoomEq.Audio.Playback;

namespace RoomEq.Audio.Mac;

/// <summary>Un périphérique CoreAudio (identifié par son UID stable).</summary>
internal sealed record CoreAudioDeviceInfo(uint DeviceId, string Uid, string Name, int Channels);

/// <summary>
/// Catalogue des périphériques CoreAudio (macOS) : énumération des sorties et
/// entrées via l'API HAL (AudioObjectGetPropertyData), périphériques par défaut.
/// Équivalent macOS de AudioDeviceCatalog (WASAPI).
/// </summary>
internal static class CoreAudioCatalog
{
    /// <summary>Sorties de lecture (celles qui possèdent des flux de rendu).</summary>
    public static List<CoreAudioDeviceInfo> EnumerateOutputDevices()
        => Enumerate(CoreAudioApi.ScopeOutput);

    /// <summary>Entrées d'enregistrement (micros, lignes).</summary>
    public static List<CoreAudioDeviceInfo> EnumerateInputDevices()
        => Enumerate(CoreAudioApi.ScopeInput);

    private static List<CoreAudioDeviceInfo> Enumerate(uint scope)
    {
        var result = new List<CoreAudioDeviceInfo>();
        uint[] devices = CoreAudioApi.ReadDeviceIds(
            CoreAudioApi.KAudioObjectSystemObject, CoreAudioApi.SelDevices, CoreAudioApi.ScopeGlobal);

        foreach (uint id in devices)
        {
            // le périphérique doit avoir au moins un flux dans la direction visée
            uint[] streams = CoreAudioApi.ReadDeviceIds(id, CoreAudioApi.SelStreams, scope);
            if (streams.Length == 0) continue;

            string name = CoreAudioApi.ReadDeviceName(id);
            string uid = CoreAudioApi.ReadDeviceUid(id);
            int channels = CoreAudioApi.ReadChannelCount(id, scope);
            if (channels <= 0) channels = 2;

            result.Add(new CoreAudioDeviceInfo(id, uid.Length > 0 ? uid : name, name, channels));
        }
        return result;
    }

    /// <summary>Identifiant numérique actuel du périphérique de sortie par défaut (0 si aucun).</summary>
    public static uint GetDefaultOutputDeviceId()
    {
        int err = CoreAudioApi.ReadProperty(
            CoreAudioApi.KAudioObjectSystemObject,
            CoreAudioApi.SelDefaultOutputDevice, CoreAudioApi.ScopeGlobal, out uint id);
        return err == CoreAudioApi.NoErr ? id : 0;
    }

    /// <summary>Identifiant numérique du périphérique d'entrée par défaut (0 si aucun).</summary>
    public static uint GetDefaultInputDeviceId()
    {
        int err = CoreAudioApi.ReadProperty(
            CoreAudioApi.KAudioObjectSystemObject,
            CoreAudioApi.SelDefaultInputDevice, CoreAudioApi.ScopeGlobal, out uint id);
        return err == CoreAudioApi.NoErr ? id : 0;
    }

    /// <summary>Résout un UID stable en périphérique courant (les id numériques changent au fil des branchements).</summary>
    public static CoreAudioDeviceInfo? Resolve(string uid, uint scope)
    {
        if (string.IsNullOrEmpty(uid)) return null;
        var list = Enumerate(scope);
        return list.FirstOrDefault(d => string.Equals(d.Uid, uid, StringComparison.OrdinalIgnoreCase))
            ?? list.FirstOrDefault(d => string.Equals(d.Name, uid, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Le périphérique virtuel RoomEq (greffon RoomEqHAL) est-il présent ?</summary>
    public static CoreAudioDeviceInfo? FindRoomEqVirtual()
        => Enumerate(CoreAudioApi.ScopeOutput)
            .FirstOrDefault(d => d.Name.Contains("RoomEq", StringComparison.OrdinalIgnoreCase));

    /// <summary>Informations de sortie (affichage + choix) pour l'énumérateur du moteur.</summary>
    public static List<OutputDeviceInfo> ToOutputInfos()
        => EnumerateOutputDevices()
            .Select(d => new OutputDeviceInfo("coreaudio|" + d.Uid, "CoreAudio · " + d.Name, "CoreAudio"))
            .ToList();
}
