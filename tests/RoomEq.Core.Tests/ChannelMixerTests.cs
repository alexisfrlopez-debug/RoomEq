using RoomEq.Core.Dsp;
using Xunit;

namespace RoomEq.Core.Tests;

/// <summary>
/// Réduction mono de la capture micro : mixage stéréo par défaut ou extraction
/// d'un canal unique (micros multi-canaux dont un canal est muet ou parasite).
/// </summary>
public class ChannelMixerTests
{
    [Fact]
    public void ToMono_SingleChannel_Copies()
    {
        float[] src = { 0.1f, 0.2f, 0.3f, -0.4f };
        var mono = ChannelMixer.ToMono(src, 1, ChannelMixer.MixFirstTwo);
        Assert.Equal(src, mono);
    }

    [Fact]
    public void ToMono_MixFirstTwo_AveragesFirstTwoChannels()
    {
        // entrelacé : L R L R …
        float[] src = { 1f, 3f, 5f, 7f, -1f, 1f };
        var mono = ChannelMixer.ToMono(src, 2, ChannelMixer.MixFirstTwo);
        Assert.Equal(new[] { 2f, 6f, 0f }, mono);
    }

    [Fact]
    public void ToMono_SingleChannelExtraction_IsolatesRequestedChannel()
    {
        float[] src = { 1f, 3f, 5f, 7f, -1f, 1f };
        Assert.Equal(new[] { 1f, 5f, -1f }, ChannelMixer.ToMono(src, 2, 0));
        Assert.Equal(new[] { 3f, 7f, 1f }, ChannelMixer.ToMono(src, 2, 1));
    }

    [Fact]
    public void ToMono_MultiChannel_ExtractsHighChannelWithoutMixingOthers()
    {
        // 4 canaux : canal 4 (index 3) seul
        float[] src = { 1f, 2f, 3f, 9f, 4f, 5f, 6f, 8f };
        var mono = ChannelMixer.ToMono(src, 4, 3);
        Assert.Equal(new[] { 9f, 8f }, mono);
    }

    [Fact]
    public void ToMono_ChannelOutOfRange_FallsBackToMix()
    {
        // canal 5 demandé sur un flux stéréo : repli sur le mixage, jamais d'exception
        float[] src = { 1f, 3f, 5f, 7f };
        var mono = ChannelMixer.ToMono(src, 2, 4);
        Assert.Equal(new[] { 2f, 6f }, mono);
    }

    [Fact]
    public void ToMono_TruncatedFrame_IsSafe()
    {
        // longueur non multiple du nombre de canaux : pas d'exception
        float[] src = { 1f, 3f, 5f };
        var mono = ChannelMixer.ToMono(src, 2, 0);
        Assert.Single(mono);
        Assert.Equal(1f, mono[0]);
    }
}
