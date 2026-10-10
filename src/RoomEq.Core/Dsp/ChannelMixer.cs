namespace RoomEq.Core.Dsp;

/// <summary>
/// Utilitaires de mixage de canaux pour la capture micro : réduction entrelacé
/// → mono, soit en mixant les deux premiers canaux, soit en isolant UN canal
/// précis (micro multi-canaux dont certains canaux sont muets ou parasites).
/// </summary>
public static class ChannelMixer
{
    /// <summary>Index de canal spécial : mixage des deux premiers canaux (comportement historique).</summary>
    public const int MixFirstTwo = -1;

    /// <summary>
    /// Réduit un buffer entrelacé en mono.
    /// <paramref name="channelIndex"/> = <see cref="MixFirstTwo"/> → moyenne des deux
    /// premiers canaux ; 0 → canal 1 seul, 1 → canal 2 seul, etc. Si l'index demandé
    /// dépasse le nombre réel de canaux (périphérique reconfiguré entre-temps), le
    /// mixage par défaut est appliqué en repli.
    /// </summary>
    public static float[] ToMono(float[] interleaved, int channels, int channelIndex)
    {
        if (channels < 1) channels = 1;
        int frames = interleaved.Length / channels;
        var mono = new float[frames];

        if (channels == 1 || channelIndex == MixFirstTwo)
        {
            if (channels == 1)
            {
                Array.Copy(interleaved, mono, frames);
                return mono;
            }
            for (int i = 0; i < frames; i++)
                mono[i] = (interleaved[i * channels] + interleaved[i * channels + 1]) * 0.5f;
            return mono;
        }

        if (channelIndex < 0 || channelIndex >= channels)
        {
            // repli : canal demandé indisponible → moyenne des deux premiers
            return ToMono(interleaved, channels, MixFirstTwo);
        }

        for (int i = 0; i < frames; i++)
            mono[i] = interleaved[i * channels + channelIndex];
        return mono;
    }
}
