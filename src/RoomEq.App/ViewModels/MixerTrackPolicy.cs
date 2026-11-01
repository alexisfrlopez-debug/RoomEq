using RoomEq.Audio.Routing;

namespace RoomEq.App.ViewModels;

/// <summary>
/// Politique des pistes de la console (décision D25, v3.8.0) : depuis que
/// l'égaliseur s'applique DANS le moteur audio Windows (EQ système APO,
/// I-AUD-09), les tranches liées à des périphériques Windows — captures
/// (« entrée » : micros, lignes, sortie du câble virtuel) et boucles
/// (« sortie » : loopback WASAPI) — n'ont plus d'usage : elles doublaient la
/// correction, déclenchaient les protections anti-boucle et désorientaient.
/// La console ne propose donc plus que ses sources INTERNES : lecteur,
/// générateur, pont DAW (ASIO) et périphérique virtuel macOS (l'EQ système
/// n'existe pas sur macOS — le routage par périphérique virtuel y reste la
/// voie principale).
/// La couche audio (M-AUD-03) conserve ses capacités capture/loopback —
/// défense en profondeur testée — mais l'application ne crée plus ces pistes.
/// </summary>
public static class MixerTrackPolicy
{
    /// <summary>Une source autorisée dans la console (source interne).</summary>
    public static bool IsConsoleSource(TrackSourceKind kind)
        => kind is TrackSourceKind.Player or TrackSourceKind.Generator
            or TrackSourceKind.AsioBridge or TrackSourceKind.MacVirtualDevice;

    /// <summary>Une source de l'ancien routage par périphérique (refusée, D25).</summary>
    public static bool IsLegacyDeviceSource(TrackSourceKind kind)
        => kind is TrackSourceKind.Capture or TrackSourceKind.Loopback;

    /// <summary>Kind persisté (mixer.json) d'une ancienne piste périphérique.</summary>
    public static bool IsLegacyDeviceKind(string? storedKind)
        => storedKind is "capture" or "loopback";
}
