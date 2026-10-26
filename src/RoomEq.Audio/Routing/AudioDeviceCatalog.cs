using NAudio.CoreAudioApi;
using RoomEq.Audio.Playback;

namespace RoomEq.Audio.Routing;

/// <summary>
/// Catalogue des périphériques audio Windows pour la table de mixage (spec
/// I-AUD-03) : entrées actives, sorties actives (utilisables en loopback),
/// détection de la carte son virtuelle RoomEq, périphériques par défaut.
/// Les énumérations renvoient des listes VIDES quand le sous-système audio
/// manque (Linux/CI, session sans service) — jamais d'exception.
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
            // sous-système audio indisponible : liste vide
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
                _ => null,   // Player / Generator / AsioBridge / MacVirtualDevice : pas un périphérique WASAPI
            };
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Sonde la carte son virtuelle du fournisseur donné (carte RoomEq par
    /// défaut) : périphériques render et capture reconnus par ses motifs, et
    /// vérifie si le render du câble est la sortie par défaut de Windows.
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
            // sous-système audio indisponible : statut vide
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
            DefaultRenderId = defId,
        };
    }

    /// <summary>Un périphérique de rendu est-il une entrée de câble virtuel ? (union des motifs de tous les fournisseurs à câble.)</summary>
    public static bool IsVirtualRender(string friendlyName)
        => VirtualAudioCatalog.IsVirtualRender(friendlyName);

    /// <summary>Un périphérique de capture est-il une sortie de câble virtuel ? (union des motifs de tous les fournisseurs à câble.)</summary>
    public static bool IsVirtualCapture(string friendlyName)
        => VirtualAudioCatalog.IsVirtualCapture(friendlyName);

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
}
