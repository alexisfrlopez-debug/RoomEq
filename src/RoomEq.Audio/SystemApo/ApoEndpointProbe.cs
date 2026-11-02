using System.Runtime.Versioning;
using NAudio.CoreAudioApi;
using NAudio.Wave;

namespace RoomEq.Audio.SystemApo;

/// <summary>Résultat d'une sonde WASAPI sur une sortie (v3.9.7).</summary>
public enum ApoProbeResult
{
    /// <summary>Le rendu partagé répond : le graphe de la sortie est construit.</summary>
    Alive,

    /// <summary>La sortie est énumérée mais REFUSE le rendu partagé : son graphe
    /// n'a pas pu être construit (APO refusée) — la sortie est muette.</summary>
    DeadGraph,

    /// <summary>La sortie n'est plus énumérée (débranchée) : pas un verdict.</summary>
    DeviceAbsent,
}

/// <summary>
/// Sonde WASAPI d'une sortie Windows (v3.9.7) : répond-elle au rendu
/// partagé ? C'est le discriminant terrain entre « l'EQ système fonctionne »
/// et « le moteur audio n'a pas su charger l'APO » : un endpoint dont le
/// graphe n'a pas pu être construit (APO refusée) refuse l'initialisation
/// WASAPI — exactement le « Failed to play test tone » / mixeur vide
/// documenté par le terrain. La sonde rend ~120 ms de SILENCE (aucune
/// interruption audible).
///
/// Windows uniquement ; inerte (DeviceAbsent) ailleurs, jamais d'exception.
/// </summary>
[SupportedOSPlatform("windows")]
public static class ApoEndpointProbe
{
    /// <summary>Sonde la sortie désignée par sa clé registre (« {GUID} »).</summary>
    public static ApoProbeResult Probe(string endpointKey)
    {
        if (!OperatingSystem.IsWindows() || string.IsNullOrEmpty(endpointKey))
            return ApoProbeResult.DeviceAbsent;
        MMDevice? dev = FindDevice(endpointKey);
        if (dev == null)
            return ApoProbeResult.DeviceAbsent; // débranché : pas un verdict
        using (dev)
        {
            return ProbeDevice(dev) ? ApoProbeResult.Alive : ApoProbeResult.DeadGraph;
        }
    }

    /// <summary>Une AUTRE sortie active répond-elle ? (témoin : si le témoin
    /// est mort aussi, le service audio redémarre encore — pas un verdict.)</summary>
    public static bool AnyOtherAlive(string endpointKey)
    {
        if (!OperatingSystem.IsWindows())
            return false;
        try
        {
            using var enumerator = new MMDeviceEnumerator();
            foreach (var d in enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active))
            {
                if (string.Equals(ApoConstants.EndpointKeyFromMmdeviceId(d.ID), endpointKey,
                        StringComparison.OrdinalIgnoreCase))
                    continue;
                using (d)
                {
                    if (ProbeDevice(d))
                        return true;
                }
            }
        }
        catch
        {
            // énumération indisponible : pas de témoin
        }
        return false;
    }

    private static MMDevice? FindDevice(string endpointKey)
    {
        using var enumerator = new MMDeviceEnumerator();
        return enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active)
            .FirstOrDefault(d => string.Equals(
                ApoConstants.EndpointKeyFromMmdeviceId(d.ID), endpointKey,
                StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Sonde un périphérique déjà résolu (rend ~120 ms de silence).</summary>
    private static bool ProbeDevice(MMDevice dev)
    {
        try
        {
            // format de mixage du périphérique : aucune conversion, aucun
            // chemin Media Foundation — la sonde reste minimale
            WaveFormat format;
            using (var ac = dev.AudioClient)
            {
                format = ac.MixFormat;
            }
            using var probe = new WasapiOut(dev, AudioClientShareMode.Shared, false, 40);
            probe.Init(new SilenceProvider(format));
            probe.Play();
            Thread.Sleep(120); // laisse le moteur initialiser le graphe réel
            probe.Stop();
            return true;
        }
        catch
        {
            return false; // AUDCLNT_E_* : le graphe de cette sortie ne se construit pas
        }
    }

    /// <summary>Fournisseur de silence strict (aucune allocation au Read).</summary>
    private sealed class SilenceProvider(WaveFormat format) : IWaveProvider
    {
        public WaveFormat WaveFormat { get; } = format;

        public int Read(byte[] buffer, int offset, int count)
        {
            Array.Clear(buffer, offset, count);
            return count;
        }
    }
}
