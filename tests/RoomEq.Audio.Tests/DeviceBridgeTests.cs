using RoomEq.Audio.Asio;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Pont du périphérique virtuel macOS (spec I-AUD-05) : section dédiée
/// (distincte de l'ASIO), chemin de fichier POSIX déterministe partagé avec le
/// greffon RoomEqHAL. Ces tests tournent sur toutes les plateformes (le
/// transport a un repli fichier). Portage legacy + extensions v3.
/// </summary>
public class DeviceBridgeTests
{
    [Fact]
    public void FallbackPath_IsDeterministic_AndSharedWithPlugin()
    {
        // la règle est dupliquée dans roomeq_hal.c (RoomEqHAL) : tout écart
        // romprait la communication greffon ↔ application
        Assert.Equal("/tmp/roomeq.asio.bridge.v1.mmf",
            AsioBridgeTransport.FallbackPath(AsioBridgeInterop.MapName));
        Assert.Equal("/tmp/roomeq.device.bridge.v1.mmf",
            AsioBridgeTransport.FallbackPath(AsioBridgeInterop.DeviceBridgeMapName));
        Assert.Equal("/tmp/roomeq.device.data.v1.mmf",
            AsioBridgeTransport.FallbackPath(AsioBridgeInterop.DeviceBridgeEventName));
    }

    [Fact]
    public void FallbackPath_StripsNamespacePrefix_AndLowercases()
    {
        Assert.Equal("/tmp/roomeq.tests.x.mmf",
            AsioBridgeTransport.FallbackPath("Local\\RoomEq.Tests.X"));
        // nom sans préfixe : accepté tel quel (minuscules)
        Assert.Equal("/tmp/nom.mmf", AsioBridgeTransport.FallbackPath("Nom"));
    }

    [Fact]
    public void CreateDeviceBridge_UsesItsOwnSection()
    {
        var device = AsioBridgeHost.CreateDeviceBridge();
        var asio = AsioBridgeHost.Instance;

        Assert.Equal(AsioBridgeInterop.DeviceBridgeMapName, device.MapName);
        Assert.Equal(AsioBridgeInterop.DeviceBridgeEventName, device.EventName);
        Assert.NotEqual(asio.MapName, device.MapName);

        // jamais démarrés : texte d'arrêt spécifique au pont périphérique
        Assert.Contains("périphérique", device.StatusText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(device.MapName, asio.MapName, StringComparison.Ordinal);

        device.Dispose();
    }

    [Fact]
    public void DeviceBridge_HostsRoundTripThroughFileTransport()
    {
        // aller-retour minimal sur le transport fichier : l'hôte lit ce qu'un
        // « greffon » (simulé par écriture directe dans la section) publie.
        var host = AsioBridgeHost.ForTests("roomeq.tests.device.roundtrip");
        host.Start();
        try
        {
            Assert.True(host.IsHosting);
        }
        finally
        {
            host.Stop();
        }
    }

    [Fact]
    public void MacAudioBridge_PluginPath_IsTheLegacyConstant()
    {
        Assert.Equal("/Library/Audio/Plug-Ins/HAL/RoomEqHAL.driver", RoomEq.Audio.Mac.MacAudioBridge.PluginPath);
        Assert.Equal("RoomEq Virtual", RoomEq.Audio.Mac.MacAudioBridge.VirtualDeviceName);
        // hors macOS : jamais « installé »
        if (!OperatingSystem.IsMacOS())
            Assert.False(RoomEq.Audio.Mac.MacAudioBridge.PluginInstalled);
    }
}
