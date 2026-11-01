using RoomEq.App.ViewModels;
using RoomEq.Audio.Routing;
using Xunit;

namespace RoomEq.App.Tests;

/// <summary>
/// Politique des pistes de la console (D25, v3.8.0 — spec I-APP-03 v2) :
/// depuis l'EQ système APO (I-AUD-09), la console n'expose plus que ses
/// sources internes ; les tranches liées à des périphériques Windows
/// (« entrée » = capture, « sortie » = boucle) sont refusées — plus de
/// double correction ni de conflits anti-boucle.
/// </summary>
public class MixerTrackPolicyTests
{
    [Theory]
    [InlineData(TrackSourceKind.Player, true)]
    [InlineData(TrackSourceKind.Generator, true)]
    [InlineData(TrackSourceKind.AsioBridge, true)]
    [InlineData(TrackSourceKind.MacVirtualDevice, true)]
    [InlineData(TrackSourceKind.Capture, false)]
    [InlineData(TrackSourceKind.Loopback, false)]
    public void IsConsoleSource_ClassifiesEveryKind(TrackSourceKind kind, bool expected)
    {
        Assert.Equal(expected, MixerTrackPolicy.IsConsoleSource(kind));
        Assert.Equal(!expected, MixerTrackPolicy.IsLegacyDeviceSource(kind));
    }

    [Theory]
    [InlineData("capture", true)]    // ancienne tranche « entrée »
    [InlineData("loopback", true)]   // ancienne tranche « sortie en boucle »
    [InlineData("player", false)]
    [InlineData("generator", false)]
    [InlineData("asio", false)]
    [InlineData("mac", false)]
    [InlineData(null, false)]
    [InlineData("inconnu", false)]
    public void IsLegacyDeviceKind_DetectsPersistedDeviceTracks(string? stored, bool expected)
        => Assert.Equal(expected, MixerTrackPolicy.IsLegacyDeviceKind(stored));

    [Fact]
    public void EveryTrackSourceKind_IsCoveredByThePolicy()
    {
        // aucun genre ne doit tomber hors de la classification (sinon une
        // piste pourrait ni être autorisée ni être identifiée comme héritée)
        foreach (var kind in Enum.GetValues<TrackSourceKind>())
            Assert.True(MixerTrackPolicy.IsConsoleSource(kind) ^ MixerTrackPolicy.IsLegacyDeviceSource(kind));
    }

    // ================================================================ badges

    [Theory]
    [InlineData(TrackSourceKind.AsioBridge)]
    [InlineData(TrackSourceKind.MacVirtualDevice)]
    [InlineData(TrackSourceKind.Generator)]
    public void Channel_InternalSources_CarryTheInternalBadge(TrackSourceKind kind)
    {
        // D25 : plus aucun badge « ENTRÉE » / « BOUCLE WINDOWS » /
        // « CARTE VIRTUELLE » — la console n'a plus de pistes périphériques
        var source = kind switch
        {
            TrackSourceKind.AsioBridge => TrackSource.AsioBridgeSource(),
            TrackSourceKind.MacVirtualDevice => TrackSource.MacVirtualSource(),
            _ => TrackSource.InternalGenerator(),
        };
        var vm = new ChannelViewModel(new ConsoleChannel(source));

        Assert.Equal("INTERNE", vm.Badge);
        Assert.False(vm.IsDevice);
    }
}
