namespace RoomEq.Audio.Routing;

/// <summary>Origine d'une piste de la table de mixage (spec I-AUD-03).</summary>
public enum TrackSourceKind
{
    /// <summary>Périphérique d'entrée WASAPI classique (micro, ligne, RoomEq Output…).</summary>
    Capture,

    /// <summary>Boucle de retour d'un périphérique de sortie (tout ce que Windows y joue).</summary>
    Loopback,

    /// <summary>Lecteur de fichiers interne de la console.</summary>
    Player,

    /// <summary>Générateur de signaux de test interne.</summary>
    Generator,

    /// <summary>Pont ASIO RoomEq : lecture d'un DAW via RoomEq ASIO (DLL).</summary>
    AsioBridge,

    /// <summary>Périphérique virtuel macOS (« RoomEq Virtual », greffon RoomEqHAL).</summary>
    MacVirtualDevice,
}

/// <summary>
/// Descripteur stable d'une source de piste : périphérique Windows (entrée ou
/// sortie en loopback) ou source interne. Persisté en JSON dans mixer.json.
/// </summary>
public sealed record TrackSource
{
    public TrackSourceKind Kind { get; init; }

    /// <summary>ID de périphérique WASAPI (pour Capture / Loopback), sinon vide.</summary>
    public string DeviceId { get; init; } = "";

    /// <summary>Nom affiché (recalculé depuis le périphérique quand c'est possible).</summary>
    public string Name { get; init; } = "";

    public static TrackSource CaptureDevice(string deviceId, string name) =>
        new() { Kind = TrackSourceKind.Capture, DeviceId = deviceId, Name = name };

    public static TrackSource LoopbackDevice(string deviceId, string name) =>
        new() { Kind = TrackSourceKind.Loopback, DeviceId = deviceId, Name = name };

    public static TrackSource InternalPlayer() =>
        new() { Kind = TrackSourceKind.Player, Name = "Lecteur" };

    public static TrackSource InternalGenerator() =>
        new() { Kind = TrackSourceKind.Generator, Name = "Générateur" };

    public static TrackSource AsioBridgeSource() =>
        new() { Kind = TrackSourceKind.AsioBridge, Name = "DAW (ASIO)" };

    public static TrackSource MacVirtualSource() =>
        new() { Kind = TrackSourceKind.MacVirtualDevice, Name = "RoomEq Virtual — macOS" };
}

/// <summary>
/// État de détection d'une infrastructure de carte son virtuelle. Nommage
/// historique « Cable » conservé : CableInput* désigne le côté render (Windows
/// y joue) et CableOutput* le côté capture (RoomEq y lit).
/// </summary>
public sealed class VirtualCardStatus
{
    /// <summary>Identifiant du fournisseur sondé (roomeq, loopback…).</summary>
    public string ProviderId { get; init; } = "roomeq";

    /// <summary>Nom affiché du fournisseur sondé.</summary>
    public string ProviderDisplayName { get; init; } = "RoomEq Virtual Audio";

    /// <summary>Côté render présent (Windows y joue).</summary>
    public bool CableInputFound { get; init; }

    /// <summary>Côté capture présent (RoomEq y lit).</summary>
    public bool CableOutputFound { get; init; }

    /// <summary>Le render du câble est-il la sortie par défaut ?</summary>
    public bool IsDefaultRender { get; init; }

    public string? CableInputName { get; init; }
    public string? CableInputId { get; init; }
    public string? CableOutputName { get; init; }
    public string? CableOutputId { get; init; }
    public string? DefaultRenderName { get; init; }
    public string? DefaultRenderId { get; init; }

    /// <summary>Les deux côtés du câble sont présents.</summary>
    public bool Installed => CableInputFound && CableOutputFound;

    /// <summary>Installé ET sortie par défaut : tout le système passe par l'EQ.</summary>
    public bool FullyRouted => Installed && IsDefaultRender;

    /// <summary>Au moins un des deux côtés est présent (installation partielle).</summary>
    public bool PartiallyInstalled => CableInputFound || CableOutputFound;
}
