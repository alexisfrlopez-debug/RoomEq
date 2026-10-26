using System.Runtime.InteropServices;
using System.Text;

namespace RoomEq.Audio.Mac;

/// <summary>
/// Interop CoreAudio / AudioUnit (macOS, spec I-AUD-05) — pont P/Invoke minimal
/// pour RoomEq : énumération des périphériques HAL, sortie AUHAL (lecture vers
/// un périphérique) et entrée AUHAL (capture micro pour la calibration).
///
/// Toutes les liaisons sont résolues au PREMIER APPEL uniquement : ce fichier
/// compile et se charge sur toutes les plateformes (les tests Linux ne
/// l'exécutent jamais — les chemins sont gardés par OperatingSystem.IsMacOS()
/// côté appelants).
/// </summary>
internal static class CoreAudioApi
{
    // ---------------------------------------------------------- constantes

    internal const uint KAudioObjectSystemObject = 1;

    // scopes (codes 4 caractères)
    internal const uint ScopeGlobal = 0x676C6F62;         // 'glob'
    internal const uint ScopeInput = 0x696E7074;          // 'inpt'
    internal const uint ScopeOutput = 0x6F757470;         // 'outp'

    // sélecteurs
    internal const uint SelDevices = 0x64646576;              // 'ddev'
    internal const uint SelDefaultOutputDevice = 0x644F7574;  // 'dOut'
    internal const uint SelDefaultInputDevice = 0x64496E20;   // 'dIn '
    internal const uint SelPropertyName = 0x6E616D65;         // 'name'
    internal const uint SelStreams = 0x73737472;              // 'sstr'
    internal const uint SelDeviceUid = 0x75696420;            // 'uid '
    internal const uint SelNominalSampleRate = 0x6E737220;    // 'nsr '
    internal const uint SelStreamConfiguration = 0x7374236D;  // 'st#m'

    // types / sous-types de composants AudioUnit
    internal const uint TypeOutput = 0x61756F75;        // 'auou'
    internal const uint SubTypeHalOutput = 0x6168616C;  // 'ahal'
    internal const uint ManufacturerApple = 0x6170706C; // 'appl'

    // propriétés AudioUnit (valeurs numériques, AudioUnitProperties.h)
    internal const uint KuStreamFormat = 8;
    internal const uint KuMaximumFramesPerSlice = 14;
    internal const uint KuSetRenderCallback = 23;
    internal const uint KuOutputCurrentDevice = 2100;
    internal const uint KuOutputEnableIO = 2101;
    internal const uint KuOutputSetInputCallback = 2103;

    internal const uint KuScopeGlobal = 3;
    internal const uint KuScopeInput = 1;
    internal const uint KuScopeOutput = 2;

    internal const uint KAudioFormatLinearPcm = 0x6C70636D; // 'lpcm'
    internal const uint KAudioFormatFlagIsFloat = 1u << 0;   // 0x1
    internal const uint KAudioFormatFlagIsPacked = 1u << 3;  // 0x8
    internal const uint KAudioFormatFlagIsNonInterleaved = 1u << 5; // 0x20 (CoreAudioTypes.h)

    internal const int NoErr = 0;

    private const string CoreAudioLib = "/System/Library/Frameworks/CoreAudio.framework/CoreAudio";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const string AudioUnitLib = "/System/Library/Frameworks/AudioUnit.framework/AudioUnit";
    private const string AudioToolbox = "/System/Library/Frameworks/AudioToolbox.framework/AudioToolbox";

    internal const uint KcfStringEncodingUtf8 = 0x08000100;

    // ---------------------------------------------------------- structures

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioObjectPropertyAddress
    {
        public uint mSelector;
        public uint mScope;
        public uint mElement;

        public AudioObjectPropertyAddress(uint selector, uint scope, uint element = 0)
        {
            mSelector = selector; mScope = scope; mElement = element;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioStreamBasicDescription
    {
        public double mSampleRate;
        public uint mFormatID;
        public uint mFormatFlags;
        public uint mBytesPerPacket;
        public uint mFramesPerPacket;
        public uint mBytesPerFrame;
        public uint mChannelsPerFrame;
        public uint mBitsPerChannel;
        public uint mReserved;

        public static AudioStreamBasicDescription InterleavedFloat(double rate, uint channels) => new()
        {
            mSampleRate = rate,
            mFormatID = KAudioFormatLinearPcm,
            mFormatFlags = KAudioFormatFlagIsFloat | KAudioFormatFlagIsPacked,
            mBytesPerPacket = 4 * channels,
            mFramesPerPacket = 1,
            mBytesPerFrame = 4 * channels,
            mChannelsPerFrame = channels,
            mBitsPerChannel = 32,
        };
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioBuffer
    {
        public uint mNumberChannels;
        public uint mDataByteSize;
        public IntPtr mData;
    }

    /// <summary>AudioBufferList à 1 tampon en ligne (les suivants vivent juste après la structure).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioBufferList
    {
        public uint mNumberBuffers;
        public AudioBuffer mBuffers;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioComponentDescription
    {
        public uint componentType;
        public uint componentSubType;
        public uint componentManufacturer;
        public uint componentFlags;
        public uint componentFlagsMask;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AURenderCallbackStruct
    {
        public IntPtr inputProc;
        public IntPtr inputProcRefCon;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct AudioTimeStamp
    {
        public double mSampleTime;
        public double mHostTime;
        public double mRateScalar;
        public uint mWordClockTime;
        public uint mSmpteTimeType;
        public long mSmpteTime;
        public uint mFlags;
        public uint mReserved;
    }

    // ---------------------------------------------------------- délégué de rendu

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal delegate int AURenderCallback(
        IntPtr inRefCon,
        ref uint ioActionFlags,
        ref AudioTimeStamp inTimeStamp,
        uint inBusNumber,
        uint inNumberFrames,
        IntPtr ioData);

    // ---------------------------------------------------------- CoreAudio (HAL)

    [DllImport(CoreAudioLib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int AudioObjectGetPropertyDataSize(
        uint inObjectID, ref AudioObjectPropertyAddress inAddress, uint inQualifierDataSize,
        IntPtr inQualifierData, ref uint outDataSize);

    [DllImport(CoreAudioLib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int AudioObjectGetPropertyData(
        uint inObjectID, ref AudioObjectPropertyAddress inAddress, uint inQualifierDataSize,
        IntPtr inQualifierData, ref uint ioDataSize, IntPtr outData);

    // ---------------------------------------------------------- CoreFoundation

    [DllImport(CoreFoundation, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CFStringCreateWithCString(
        IntPtr alloc, byte[] cStr, uint encoding);

    [DllImport(CoreFoundation, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr CFStringGetCStringPtr(IntPtr theString, uint encoding);

    [DllImport(CoreFoundation, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.U1)]
    internal static extern bool CFStringGetCString(
        IntPtr theString, byte[] buffer, IntPtr bufferSize, uint encoding);

    [DllImport(CoreFoundation, CallingConvention = CallingConvention.Cdecl)]
    internal static extern void CFRelease(IntPtr cf);

    // ---------------------------------------------------------- AudioComponent

    [DllImport(AudioToolbox, CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr AudioComponentFindNext(
        IntPtr inComponent, ref AudioComponentDescription inDesc);

    [DllImport(AudioToolbox, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int AudioComponentInstanceNew(
        IntPtr inComponent, out IntPtr outInstance);

    [DllImport(AudioToolbox, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int AudioComponentInstanceDispose(IntPtr inInstance);

    // ---------------------------------------------------------- AudioUnit

    [DllImport(AudioUnitLib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int AudioUnitInitialize(IntPtr inUnit);

    [DllImport(AudioUnitLib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int AudioUnitUninitialize(IntPtr inUnit);

    [DllImport(AudioUnitLib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int AudioUnitStart(IntPtr inUnit);

    [DllImport(AudioUnitLib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int AudioUnitStop(IntPtr inUnit);

    [DllImport(AudioUnitLib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int AudioUnitSetProperty(
        IntPtr inUnit, uint inID, uint inScope, uint inElement,
        IntPtr inData, uint inDataSize);

    [DllImport(AudioUnitLib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int AudioUnitGetProperty(
        IntPtr inUnit, uint inID, uint inScope, uint inElement,
        IntPtr ioData, ref uint ioDataSize);

    [DllImport(AudioUnitLib, CallingConvention = CallingConvention.Cdecl)]
    internal static extern int AudioUnitRender(
        IntPtr inUnit, ref uint ioActionFlags, ref AudioTimeStamp inTimeStamp,
        uint inOutputBusNumber, uint inNumberFrames, IntPtr ioData);

    // ---------------------------------------------------------- helpers C# de haut niveau

    /// <summary>Lit une propriété binaire de taille fixe (uint, double…).</summary>
    internal static int ReadProperty<T>(uint objectId, uint selector, uint scope, out T value)
        where T : struct
    {
        var addr = new AudioObjectPropertyAddress(selector, scope);
        uint size = (uint)Marshal.SizeOf<T>();
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            int err = AudioObjectGetPropertyData(objectId, ref addr, 0, IntPtr.Zero, ref size, buf);
            if (err != NoErr)
            {
                value = default;
                return err;
            }
            value = Marshal.PtrToStructure<T>(buf)!;
            return NoErr;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    /// <summary>Lit un tableau d'AudioDeviceID ('ddev', 'sstr'…).</summary>
    internal static uint[] ReadDeviceIds(uint objectId, uint selector, uint scope)
    {
        var addr = new AudioObjectPropertyAddress(selector, scope);
        uint size = 0;
        int err = AudioObjectGetPropertyDataSize(objectId, ref addr, 0, IntPtr.Zero, ref size);
        if (err != NoErr || size < 4) return Array.Empty<uint>();

        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            err = AudioObjectGetPropertyData(objectId, ref addr, 0, IntPtr.Zero, ref size, buf);
            if (err != NoErr) return Array.Empty<uint>();
            int count = (int)(size / sizeof(uint));
            var ids = new uint[count];
            for (int i = 0; i < count; i++)
                ids[i] = (uint)Marshal.ReadInt32(buf, i * sizeof(uint));
            return ids;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    /// <summary>Nom lisible d'un objet audio (propriété CFStringRef, convertie puis libérée).</summary>
    internal static string ReadDeviceName(uint objectId, uint scope = ScopeGlobal)
    {
        var addr = new AudioObjectPropertyAddress(SelPropertyName, scope);
        uint size = (uint)IntPtr.Size;
        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            int err = AudioObjectGetPropertyData(objectId, ref addr, 0, IntPtr.Zero, ref size, buf);
            if (err != NoErr) return "";
            return CFStringToString(Marshal.ReadIntPtr(buf));
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    /// <summary>UID stable d'un périphérique (persisté dans les préférences de RoomEq).</summary>
    internal static string ReadDeviceUid(uint deviceId)
    {
        int err = ReadProperty(deviceId, SelDeviceUid, ScopeGlobal, out IntPtr cf);
        if (err != NoErr) return "";
        return CFStringToString(cf);
    }

    /// <summary>Nombre de canaux d'un périphérique dans un scope (somme des flux, 'st#m').</summary>
    internal static int ReadChannelCount(uint deviceId, uint scope)
    {
        var addr = new AudioObjectPropertyAddress(SelStreamConfiguration, scope);
        uint size = 0;
        int err = AudioObjectGetPropertyDataSize(deviceId, ref addr, 0, IntPtr.Zero, ref size);
        if (err != NoErr || size < 4) return 0;

        IntPtr buf = Marshal.AllocHGlobal((int)size);
        try
        {
            err = AudioObjectGetPropertyData(deviceId, ref addr, 0, IntPtr.Zero, ref size, buf);
            if (err != NoErr) return 0;

            int count = Marshal.ReadInt32(buf, 0) / sizeof(uint); // 1er uint = nombre de tampons
            int channels = 0;
            int offset = sizeof(uint);
            for (int i = 0; i < count; i++)
            {
                channels += Marshal.ReadInt32(buf, offset);
                offset += sizeof(uint) * 2 + IntPtr.Size; // mNumberChannels + mDataByteSize + mData
            }
            return channels;
        }
        finally
        {
            Marshal.FreeHGlobal(buf);
        }
    }

    /// <summary>Convertit un CFStringRef en chaîne .NET.</summary>
    internal static string CFStringToString(IntPtr cf)
    {
        if (cf == IntPtr.Zero) return "";

        IntPtr fast = CFStringGetCStringPtr(cf, KcfStringEncodingUtf8);
        if (fast != IntPtr.Zero)
        {
            int len = 0;
            while (Marshal.ReadByte(fast, len) != 0 && len < 4096) len++;
            byte[] raw = new byte[len];
            Marshal.Copy(fast, raw, 0, len);
            return Encoding.UTF8.GetString(raw);
        }

        byte[] buffer = new byte[1024];
        if (CFStringGetCString(cf, buffer, (IntPtr)buffer.Length, KcfStringEncodingUtf8))
        {
            int len = Array.IndexOf(buffer, (byte)0);
            if (len < 0) len = buffer.Length;
            return Encoding.UTF8.GetString(buffer, 0, len);
        }
        return "";
    }

    /// <summary>Crée un CFStringRef à partir d'une chaîne .NET (à libérer par CFRelease).</summary>
    internal static IntPtr StringToCFString(string s)
    {
        byte[] utf8 = Encoding.UTF8.GetBytes(s + "\0");
        return CFStringCreateWithCString(IntPtr.Zero, utf8, KcfStringEncodingUtf8);
    }

    /// <summary>Code d'erreur → message lisible (osstatus).</summary>
    internal static string DescribeError(int osStatus) =>
        osStatus switch
        {
            -66681 => "erreur de rendu AudioUnit (contexte invalide)",
            -10875 => "périphérique non disponible (débranché ou occupé)",
            -10877 => "périphérique inexistant",
            -10878 => "permissions microphone refusées — accordez l'accès dans Réglages Système › Confidentialité › Micro",
            -561017449 => "opération CoreAudio illégale",
            _ => $"erreur CoreAudio {osStatus}",
        };
}
