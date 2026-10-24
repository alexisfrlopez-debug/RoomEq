using NAudio.CoreAudioApi;
using RoomEq.Audio.Playback;

namespace RoomEq.Audio.Routing;

/// <summary>Origine d'une piste de la table de mixage.</summary>
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
    /// <summary>Périphérique virtuel macOS (« RoomEq Virtual », greffon RoomEqHAL) :
    /// le son que macOS joue dans le périphérique arrive par le pont intégré.</summary>
    MacVirtualDevice
}

/// <summary>
/// Descripteur stable d'une source de piste : périphérique Windows (entrée ou sortie
/// en loopback) ou source interne. Persisté en JSON dans mixer.json.
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
/// État de détection d'une infrastructure de carte son virtuelle.
/// Nommage historique « Cable » conservé : CableInput* désigne le côté render
/// (Windows y joue) et CableOutput* le côté capture (RoomEq y lit) — carte
/// virtuelle RoomEq ou boucle WASAPI.
/// </summary>
public sealed class VirtualCardStatus
{
    /// <summary>Identifiant du fournisseur sondé (roomeq, loopback…).</summary>
    public string ProviderId { get; init; } = "roomeq";
    /// <summary>Nom affiché du fournisseur sondé.</summary>
    public string ProviderDisplayName { get; init; } = "RoomEq Virtual Audio";
    public bool CableInputFound { get; init; }   // côté render : Windows y joue
    public bool CableOutputFound { get; init; }  // côté capture : RoomEq y lit
    public bool IsDefaultRender { get; init; }   // le render du câble est-il la sortie par défaut ?
    public string? CableInputName { get; init; }
    public string? CableInputId { get; init; }
    public string? CableOutputName { get; init; }
    public string? CableOutputId { get; init; }
    public string? DefaultRenderName { get; init; }
    public string? DefaultRenderId { get; init; }

    public bool Installed => CableInputFound && CableOutputFound;
    public bool FullyRouted => Installed && IsDefaultRender;
    /// <summary>Au moins un des deux côtés est présent (installé partiellement).</summary>
    public bool PartiallyInstalled => CableInputFound || CableOutputFound;
}

/// <summary>
/// Catalogue des périphériques audio Windows pour la table de mixage : toutes les
/// entrées, toutes les sorties (utilisables en loopback), détection de la carte son
/// virtuelle RoomEq (pilote intégré) et périphériques par défaut.
/// </summary>
public sealed class AudioDeviceCatalog
{
    private MMDeviceEnumerator? _enumerator;

    private MMDeviceEnumerator Enumer()
        => _enumerator ??= new MMDeviceEnumerator();

    /// <summary>Toutes les entrées actives (micros, lignes, RoomEq Output…).</summary>
    public IReadOnlyList<TrackSource> EnumerateCaptureSources()
    {
        var list = new List<TrackSource>();
        try
        {
            foreach (var d in Enumer().EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
                list.Add(TrackSource.CaptureDevice(d.ID, d.FriendlyName));
        }
        catch
        {
            // sous-système audio indisponible : liste vide
        }
        return list;
    }

    /// <summary>Toutes les sorties actives, utilisables comme sources loopback.</summary>
    public IReadOnlyList<TrackSource> EnumerateLoopbackSources()
    {
        var list = new List<TrackSource>();
        try
        {
            foreach (var d in Enumer().EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                list.Add(TrackSource.LoopbackDevice(d.ID, d.FriendlyName + " (boucle)"));
        }
        catch
        {
            // sous-système audio indisponible : liste vide
        }
        return list;
    }

    /// <summary>Toutes les sorties actives (destinations du bus master).</summary>
    public IReadOnlyList<OutputDeviceInfo> EnumerateRenderDevices()
    {
        var list = new List<OutputDeviceInfo>();
        try
        {
            foreach (var d in Enumer().EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
                list.Add(new OutputDeviceInfo("wasapi|" + d.ID, d.FriendlyName, "WASAPI"));
        }
        catch
        {
            // sous-système audio indisponible
        }
        return list;
    }

    /// <summary>Résout un descripteur vers un MMDevice actif (null si disparu).</summary>
    public MMDevice? ResolveDevice(TrackSource source)
    {
        try
        {
            return source.Kind switch
            {
                TrackSourceKind.Capture => Enumer().EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active)
                    .FirstOrDefault(d => d.ID == source.DeviceId),
                TrackSourceKind.Loopback => Enumer().EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
                    .FirstOrDefault(d => d.ID == source.DeviceId),
                TrackSourceKind.AsioBridge => null,   // pas un périphérique WASAPI
                _ => null
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sonde la carte son virtuelle du fournisseur donné (carte RoomEq par défaut) :
    /// périphériques render et capture reconnus par ses motifs, et vérifie si le
    /// render du câble est la sortie par défaut de Windows.
    /// </summary>
    public VirtualCardStatus ProbeVirtualCard(IVirtualAudioProvider? provider = null)
    {
        provider ??= VirtualAudioCatalog.Default;
        bool hasIn = false, hasOut = false, isDefault = false;
        string? inName = null, inId = null, outName = null, outId = null;
        string? defName = null, defId = null;

        try
        {
            foreach (var d in Enumer().EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                if (provider.IsRenderEndpoint(d.FriendlyName) && !hasIn)
                {
                    hasIn = true;
                    inName = d.FriendlyName;
                    inId = d.ID;
                }
            }
            foreach (var d in Enumer().EnumerateAudioEndPoints(DataFlow.Capture, DeviceState.Active))
            {
                if (provider.IsCaptureEndpoint(d.FriendlyName) && !hasOut)
                {
                    hasOut = true;
                    outName = d.FriendlyName;
                    outId = d.ID;
                }
            }

            var def = Enumer().GetDefaultAudioEndpoint(DataFlow.Render, Role.Console);
            defName = def.FriendlyName;
            defId = def.ID;
            isDefault = hasIn && defId == inId;
        }
        catch
        {
            // sous-système audio indisponible
        }

        return new VirtualCardStatus
        {
            ProviderId = provider.Id,
            ProviderDisplayName = provider.DisplayName,
            CableInputFound = hasIn,
            CableOutputFound = hasOut,
            IsDefaultRender = isDefault,
            CableInputName = inName,
            CableInputId = inId,
            CableOutputName = outName,
            CableOutputId = outId,
            DefaultRenderName = defName,
            DefaultRenderId = defId
        };
    }

    /// <summary>
    /// Un périphérique de rendu est-il une entrée de câble virtuel ? (union des
    /// motifs de tous les fournisseurs : carte RoomEq…)
    /// </summary>
    public static bool IsVirtualRender(string friendlyName)
        => VirtualAudioCatalog.IsVirtualRender(friendlyName);

    /// <summary>ID de l'entrée par défaut de Windows (null si sous-système indisponible).</summary>
    public string? GetDefaultCaptureId()
    {
        try
        {
            return Enumer().GetDefaultAudioEndpoint(DataFlow.Capture, Role.Console).ID;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Un périphérique de capture est-il une sortie de câble virtuel ? (union des
    /// motifs de tous les fournisseurs.)
    /// </summary>
    public static bool IsVirtualCapture(string friendlyName)
        => VirtualAudioCatalog.IsVirtualCapture(friendlyName);
}
