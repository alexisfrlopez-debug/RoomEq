namespace RoomEq.Core.Dsp;

/// <summary>
/// Utilitaires de mixage de canaux pour la capture micro (spec I-DSP-07) :
/// réduction entrelacé → mono, soit en mixant les deux premiers canaux, soit en
/// isolant un canal précis.
/// </summary>
public static class ChannelMixer
{
    /// <summary>Index de canal spécial : mixage des deux premiers canaux (comportement historique).</summary>
    public const int MixFirstTwo = -1;

    /// <summary>
    /// Réduit un buffer entrelacé en mono. <paramref name="channelIndex"/> =
    /// <see cref="MixFirstTwo"/> → moyenne des deux premiers canaux ; 0 → canal 1
    /// seul, 1 → canal 2 seul, etc. Index hors plage (périphérique reconfiguré
    /// entre-temps) → repli sur le mixage des deux premiers, jamais d'exception.
    /// </summary>
    public static float[] ToMono(float[] interleaved, int channels, int channelIndex)
    {
        ArgumentNullException.ThrowIfNull(interleaved);
        if (channels < 1)
            channels = 1;
        int frames = interleaved.Length / channels;
        var mono = new float[frames];

        if (channels == 1)
        {
            Array.Copy(interleaved, mono, frames);
            return mono;
        }

        if (channelIndex == MixFirstTwo)
        {
            for (int i = 0; i < frames; i++)
                mono[i] = (interleaved[i * channels] + interleaved[i * channels + 1]) * 0.5f;
            return mono;
        }

        if (channelIndex < 0 || channelIndex >= channels)
            return ToMono(interleaved, channels, MixFirstTwo); // repli robuste

        for (int i = 0; i < frames; i++)
            mono[i] = interleaved[i * channels + channelIndex];
        return mono;
    }
}
