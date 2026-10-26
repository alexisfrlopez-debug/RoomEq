using System.IO.MemoryMappedFiles;
using NAudio.Wave;
using RoomEq.Audio.Asio;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Pont ASIO RoomEq (spec I-AUD-04) : contrats du protocole (miroir C/C#),
/// hôte de la section partagée et fournisseur du mixeur. Ces tests tournent
/// sous Linux comme sous Windows (le transport a un repli fichier).
/// Portage des tests legacy v2.2.2.
/// </summary>
public class AsioBridgeTests : IDisposable
{
    private const string TestMap = "Local\\RoomEq.Asio.Tests." + nameof(AsioBridgeTests);
    private const string TestEvent = "Local\\RoomEq.Asio.Tests.Event." + nameof(AsioBridgeTests);

    private readonly string _uniqueMap = TestMap + "." + Guid.NewGuid().ToString("N")[..8];

    public void Dispose()
    {
        // chaque hôte de test est autonome ; rien de global à libérer ici
    }

    // ------------------------------------------------------------ protocole

    [Fact]
    public void Constants_MirrorTheNativeProtocol()
    {
        // valeurs verrouillées avec asio/bridge_core/roomeq_asio_protocol.h
        Assert.Equal(0x52454153u, AsioBridgeInterop.Magic);
        Assert.Equal(1u, AsioBridgeInterop.Version);
        Assert.Equal(32768, AsioBridgeInterop.RingFrames);
        Assert.Equal(2, AsioBridgeInterop.Channels);
        Assert.Equal(256, AsioBridgeInterop.HeaderBytes);
        Assert.Equal(48000, AsioBridgeInterop.BridgeRate);
        Assert.True((AsioBridgeInterop.RingFrames & (AsioBridgeInterop.RingFrames - 1)) == 0,
            "l'anneau doit être une puissance de 2");
        Assert.Equal("Local\\RoomEq.Asio.Bridge.v1", AsioBridgeInterop.MapName);
        Assert.Equal("Local\\RoomEq.Asio.Data.v1", AsioBridgeInterop.EventName);
        Assert.Equal("Local\\RoomEq.Device.Bridge.v1", AsioBridgeInterop.DeviceBridgeMapName);
        Assert.Equal("Local\\RoomEq.Device.Data.v1", AsioBridgeInterop.DeviceBridgeEventName);
    }

    [Fact]
    public void HeaderOffsets_MatchTheCLayout()
    {
        // le miroir managé couvre les 88 octets utiles ; le remplissage jusqu'à
        // 256 appartient au C (sizeof(RoomeqAsioHeader) == 256, testé en g++)
        Assert.Equal(88, System.Runtime.InteropServices.Marshal.SizeOf<AsioBridgeHeader>());
        Assert.True(System.Runtime.InteropServices.Marshal.SizeOf<AsioBridgeHeader>()
                    <= AsioBridgeInterop.HeaderBytes);
        Assert.Equal(40, AsioBridgeHeader.Offsets.WritePos);
        Assert.Equal(48, AsioBridgeHeader.Offsets.ReadPos);
        Assert.Equal(56, AsioBridgeHeader.Offsets.DroppedFrames);
        Assert.Equal(64, AsioBridgeHeader.Offsets.HostClock);
        Assert.Equal(72, AsioBridgeHeader.Offsets.HostBeat);
        Assert.Equal(80, AsioBridgeHeader.Offsets.HostSampleRate);
        Assert.Equal(84, AsioBridgeHeader.Offsets.Xruns);
        Assert.Equal(256, AsioBridgeHeader.Offsets.Ring);
    }

    [Fact]
    public void MappingBytes_IsHeaderPlusRing()
    {
        Assert.Equal(256 + 32768L * 2 * 4, AsioBridgeInterop.MappingBytes);
    }

    // ------------------------------------------------------------ hôte

    [Fact]
    public void Start_CreatesInitializedHeader()
    {
        using var host = new TestHost(_uniqueMap);
        host.Start();

        using var mmf = AsioBridgeTransport.OpenExisting(_uniqueMap);
        using var view = mmf.CreateViewAccessor(0, AsioBridgeInterop.MappingBytes);
        Assert.Equal(AsioBridgeInterop.Magic, view.ReadUInt32(AsioBridgeHeader.Offsets.Magic));
        Assert.Equal(AsioBridgeInterop.Version, view.ReadUInt32(AsioBridgeHeader.Offsets.Version));
        Assert.Equal((uint)AsioBridgeInterop.BridgeRate, view.ReadUInt32(AsioBridgeHeader.Offsets.SampleRate));
        Assert.Equal((uint)AsioBridgeInterop.RingFrames, view.ReadUInt32(AsioBridgeHeader.Offsets.RingFrames));
        Assert.Equal((uint)AsioBridgeInterop.Channels, view.ReadUInt32(AsioBridgeHeader.Offsets.Channels));
        Assert.Equal(1u, view.ReadUInt32(AsioBridgeHeader.Offsets.HostActive));
        Assert.True(host.IsHosting);
    }

    [Fact]
    public void Start_IsIdempotent_StopClearsHostActive()
    {
        using var host = new TestHost(_uniqueMap);
        host.Start();
        host.Start();   // ne lève pas, ne recrée pas

        // ouvrir la vue AVANT l'arrêt : sous Windows, la destruction du dernier
        // handle supprime la section nommée (OpenExisting échouerait après Stop) ;
        // la vue déjà mappée lit l'en-tête écrit par Stop (HostActive = 0)
        using var mmf = AsioBridgeTransport.OpenExisting(_uniqueMap);
        using var view = mmf.CreateViewAccessor(0, AsioBridgeInterop.MappingBytes);
        Assert.Equal(1u, view.ReadUInt32(AsioBridgeHeader.Offsets.HostActive));

        host.Stop();
        Assert.False(host.IsHosting);

        Assert.Equal(0u, view.ReadUInt32(AsioBridgeHeader.Offsets.HostActive));
    }

    [Fact]
    public void Provider_IsStereo48kFloat()
    {
        using var host = new TestHost(_uniqueMap);
        var fmt = host.Provider.WaveFormat;
        Assert.Equal(48000, fmt.SampleRate);
        Assert.Equal(2, fmt.Channels);
        Assert.Equal(32, fmt.BitsPerSample);
        Assert.True(fmt.Encoding.HasFlag(WaveFormatEncoding.IeeeFloat));
    }

    [Fact]
    public void Provider_ReadsSilenceWhenNoClient()
    {
        using var host = new TestHost(_uniqueMap);
        host.Start();
        var buf = new float[64];
        int n = host.Provider.Read(buf, 0, buf.Length);
        Assert.Equal(buf.Length, n);
        Assert.All(buf, v => Assert.Equal(0f, v));
    }

    [Fact]
    public async Task Provider_ReceivesWhatTheClientWrites()
    {
        using var host = new TestHost(_uniqueMap);
        host.Start();

        // simuler la DLL ASIO : ouvrir la section, écrire 32 trames stéréo
        using var mmf = AsioBridgeTransport.OpenExisting(_uniqueMap);
        using var view = mmf.CreateViewAccessor(0, AsioBridgeInterop.MappingBytes);
        view.Write(AsioBridgeHeader.Offsets.ClientActive, 1u);
        view.Write(AsioBridgeHeader.Offsets.BufferFrames, 256u);

        var frames = new float[32 * 2];
        for (int i = 0; i < frames.Length; i++) frames[i] = (i % 64) / 64f - 0.5f;
        view.WriteArray(AsioBridgeInterop.HeaderBytes, frames, 0, frames.Length);
        view.Write(AsioBridgeHeader.Offsets.WritePos, 32L);
        host.PokeReader();   // réveil immédiat du lecteur

        // laisser le lecteur pomper (délai borné)
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        long[] got = new long[32 * 2];
        while (DateTime.UtcNow < deadline)
        {
            var buf = new float[32 * 2];
            int n = host.Provider.Read(buf, 0, buf.Length);
            if (n == buf.Length && buf[^1] != 0f)
            {
                got = buf.Select(v => (long)Math.Round(v * 1000)).ToArray();
                break;
            }
            await Task.Delay(20);
        }
        Assert.NotEqual(0L, got[^1]);

        // readPos a avancé au niveau du pont
        Assert.Equal(32, view.ReadInt64(AsioBridgeHeader.Offsets.ReadPos));

        // l'horloge hôte est publiée (32 trames consommées → hostClock)
        Assert.True(view.ReadInt64(AsioBridgeHeader.Offsets.HostClock) > 0);
    }

    [Fact]
    public async Task SessionChange_RealignsTheReader()
    {
        using var host = new TestHost(_uniqueMap);
        host.Start();

        using var mmf = AsioBridgeTransport.OpenExisting(_uniqueMap);
        using var view = mmf.CreateViewAccessor(0, AsioBridgeInterop.MappingBytes);
        view.Write(AsioBridgeHeader.Offsets.ClientActive, 1u);

        // trame « périmée » (redémarrage du DAW) : writePos saute, session change
        view.Write(AsioBridgeHeader.Offsets.WritePos, 4096L);
        view.Write(AsioBridgeHeader.Offsets.Session, 2u);
        host.PokeReader();
        await Task.Delay(300);

        // le lecteur a sauté l'historique : readPos == writePos
        Assert.Equal(4096, view.ReadInt64(AsioBridgeHeader.Offsets.ReadPos));
    }

    [Fact]
    public void StatusText_ReportsIdleBeforeStart()
    {
        using var host = new TestHost(_uniqueMap);
        Assert.Contains("l'arrêt", host.StatusText);
    }

    [Fact]
    public void ClientConnected_ReflectsTheHeaderFlag()
    {
        using var host = new TestHost(_uniqueMap);
        Assert.False(host.ClientConnected);   // pas démarré : pas de vue
        host.Start();
        Assert.False(host.ClientConnected);   // démarré, sans client
    }

    /// <summary>Hôte de test : constructeur interne accessible + réveil du lecteur.</summary>
    private sealed class TestHost : IDisposable
    {
        private readonly AsioBridgeHost _host;
        public bool IsHosting => _host.IsHosting;
        public ISampleProvider Provider => _host.Provider;
        public string StatusText => _host.StatusText;
        public bool ClientConnected => _host.ClientConnected;

        public TestHost(string map)
        {
            _host = AsioBridgeHost.ForTests(map);
        }

        public void Start() => _host.Start();
        public void Stop() => _host.Stop();
        public void PokeReader() => _host.PokeReader();
        public void Dispose() => _host.Dispose();
    }
}
