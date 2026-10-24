using System.Runtime.InteropServices;

namespace RoomEq.Audio.Asio;

/// <summary>
/// Miroir C# du protocole du pont ASIO RoomEq (<c>asio/bridge_core/roomeq_asio_protocol.h</c>).
/// Les constantes et la disposition binaire doivent rester strictement identiques
/// des deux côtés (C++ DLL / C# hôte) — un test unitaire verrouille les tailles.
/// </summary>
public static class AsioBridgeInterop
{
    public const uint Magic = 0x52454153u;          // « ROOMEQ ASIO »
    public const uint Version = 1u;

    /// <summary>Section partagée (espace de noms Local : même session Windows).</summary>
    public const string MapName = "Local\\RoomEq.Asio.Bridge.v1";
    /// <summary>Événement signalé par la DLL à chaque bloc écrit.</summary>
    public const string EventName = "Local\\RoomEq.Asio.Data.v1";

    /// <summary>
    /// Section du pont du périphérique virtuel macOS : le greffon système RoomEqHAL
    /// (coreaudiod) y publie ce que macOS joue dans « RoomEq Virtual ». Sous POSIX,
    /// le fichier de repli est /tmp/roomeq.device.bridge.v1.mmf (voir AsioBridgeTransport).
    /// </summary>
    public const string DeviceBridgeMapName = "Local\\RoomEq.Device.Bridge.v1";
    /// <summary>Événement optionnel du pont périphérique (absent sous POSIX : sondage).</summary>
    public const string DeviceBridgeEventName = "Local\\RoomEq.Device.Data.v1";

    public const int RingFrames = 32768;   // puissance de 2 (~0,68 s à 48 kHz)
    public const int Channels = 2;
    public const int HeaderBytes = 256;
    public const int BridgeRate = 48000;   // taux unique du pont (v1)

    /// <summary>Taille totale de la section partagée.</summary>
    public static long MappingBytes => HeaderBytes + (long)RingFrames * Channels * sizeof(float);

    /// <summary>Codes d'état (voir protocole).</summary>
    public const int Ok = 0, ErrNoHost = 1, ErrOverflow = 2, ErrStalled = 3;
}

/// <summary>
/// Bloc d'en-tête du pont — miroir EXACT de <c>RoomeqAsioHeader</c> (Pack 8).
/// Accès via MemoryMappedViewAccessor aux décalages ci-dessous.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 8)]
public struct AsioBridgeHeader
{
    public uint Magic;
    public uint Version;
    public uint SampleRate;
    public uint BufferFrames;
    public uint RingFrames;
    public uint Channels;
    public uint HostActive;
    public uint ClientActive;
    public uint Session;
    public uint Pad0;
    public long WritePos;
    public long ReadPos;
    public long DroppedFrames;
    public long HostClock;
    public long HostBeat;
    public uint HostSampleRate;
    public uint Xruns;
    // suivi : 168 octets de remplissage jusqu'à HeaderBytes (256)

    /// <summary>Offsets binaires vérifiés par les tests (miroir du C).</summary>
    public static class Offsets
    {
        public const int Magic = 0;
        public const int Version = 4;
        public const int SampleRate = 8;
        public const int BufferFrames = 12;
        public const int RingFrames = 16;
        public const int Channels = 20;
        public const int HostActive = 24;
        public const int ClientActive = 28;
        public const int Session = 32;
        public const int Pad0 = 36;
        public const int WritePos = 40;
        public const int ReadPos = 48;
        public const int DroppedFrames = 56;
        public const int HostClock = 64;
        public const int HostBeat = 72;
        public const int HostSampleRate = 80;
        public const int Xruns = 84;
        public const int Ring = AsioBridgeInterop.HeaderBytes;
    }
}
