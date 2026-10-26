using System.Runtime.InteropServices;
using NAudio.Wave;
using RoomEq.Audio.Playback;

namespace RoomEq.Audio.Mac;

/// <summary>
/// Sortie CoreAudio (macOS, spec I-AUD-05) via AudioUnit HAL (AUHAL) : RoomEq
/// tire son graphe dans le rappel de rendu, au format unique du projet
/// (48 kHz, stéréo, float entrelacé) — l'AUHAL assure la conversion vers le
/// format réel du périphérique. Équivalent macOS de <c>WasapiOutput</c>.
/// Le délégué de rendu est gardé vivant par champ : le GC ne peut pas le
/// collecter pendant que CoreAudio l'appelle.
/// </summary>
public sealed class CoreAudioOutput : IAudioOutput
{
    private const int MaxBuffers = 4;

    private readonly uint _deviceId;
    private readonly int _sampleRate;
    private readonly int _channels;

    private IntPtr _unit;
    private CoreAudioApi.AURenderCallback? _renderDelegate; // garde le délégué vivant
    private ISampleProvider? _source;
    private readonly float[] _scratch = new float[8192];
    private bool _playing;

    public string DisplayName { get; }
    public string Kind => "CoreAudio";
    public int SampleRate => _sampleRate;
    public int Channels => _channels;
    public event EventHandler? PlaybackStopped;

    /// <summary>Numérotation volatile : l'appelant résout l'UID AVANT la création.</summary>
    internal CoreAudioOutput(CoreAudioDeviceInfo device, int sampleRate = 48000, int channels = 2)
    {
        _deviceId = device.DeviceId;
        DisplayName = device.Name;
        _sampleRate = sampleRate;
        _channels = channels;
    }

    public void Init(ISampleProvider adaptedSource)
    {
        TeardownUnit();
        _source = adaptedSource;

        // 1 · composant AUHAL
        var desc = new CoreAudioApi.AudioComponentDescription
        {
            componentType = CoreAudioApi.TypeOutput,
            componentSubType = CoreAudioApi.SubTypeHalOutput,
            componentManufacturer = CoreAudioApi.ManufacturerApple,
        };
        IntPtr comp = CoreAudioApi.AudioComponentFindNext(IntPtr.Zero, ref desc);
        if (comp == IntPtr.Zero)
            throw new InvalidOperationException("Composant AudioUnit HAL introuvable (CoreAudio).");

        int err = CoreAudioApi.AudioComponentInstanceNew(comp, out _unit);
        if (err != CoreAudioApi.NoErr)
            throw new InvalidOperationException("Création AudioUnit impossible : " + CoreAudioApi.DescribeError(err));

        try
        {
            // 2 · périphérique cible
            uint targetDevice = _deviceId;
            err = SetProperty(CoreAudioApi.KuOutputCurrentDevice, CoreAudioApi.KuScopeGlobal, 0,
                ref targetDevice);
            if (err != CoreAudioApi.NoErr)
                throw new InvalidOperationException("Périphérique de sortie indisponible : " + CoreAudioApi.DescribeError(err));

            // 3 · format client (ce que RoomEq fournit dans le rappel)
            var asbd = CoreAudioApi.AudioStreamBasicDescription.InterleavedFloat(_sampleRate, (uint)_channels);
            err = SetPropertyStruct(CoreAudioApi.KuStreamFormat, CoreAudioApi.KuScopeInput, 0, ref asbd);
            if (err != CoreAudioApi.NoErr)
                throw new InvalidOperationException("Format 48 kHz stéréo refusé : " + CoreAudioApi.DescribeError(err));

            // 4 · taille de tranche maximale (évite les coupures des réglages par défaut)
            uint maxFrames = 4096;
            err = SetProperty(CoreAudioApi.KuMaximumFramesPerSlice, CoreAudioApi.KuScopeGlobal, 0, ref maxFrames);
            if (err != CoreAudioApi.NoErr)
                throw new InvalidOperationException("MaximumFramesPerSlice refusé : " + CoreAudioApi.DescribeError(err));

            // 5 · rappel de rendu
            _renderDelegate = RenderCallback;
            var cb = new CoreAudioApi.AURenderCallbackStruct
            {
                inputProc = Marshal.GetFunctionPointerForDelegate(_renderDelegate),
                inputProcRefCon = IntPtr.Zero,
            };
            err = SetPropertyStruct(CoreAudioApi.KuSetRenderCallback, CoreAudioApi.KuScopeInput, 0, ref cb);
            if (err != CoreAudioApi.NoErr)
                throw new InvalidOperationException("Rappel de rendu refusé : " + CoreAudioApi.DescribeError(err));

            err = CoreAudioApi.AudioUnitInitialize(_unit);
            if (err != CoreAudioApi.NoErr)
                throw new InvalidOperationException("Initialisation AudioUnit impossible : " + CoreAudioApi.DescribeError(err));
        }
        catch
        {
            TeardownUnit();
            throw;
        }
    }

    public void Play()
    {
        if (_unit == IntPtr.Zero) return;
        int err = CoreAudioApi.AudioUnitStart(_unit);
        if (err != CoreAudioApi.NoErr)
            throw new InvalidOperationException("Démarrage CoreAudio impossible : " + CoreAudioApi.DescribeError(err));
        _playing = true;
    }

    public void Pause()
    {
        if (_unit == IntPtr.Zero) return;
        try { CoreAudioApi.AudioUnitStop(_unit); } catch { /* arrêt tolérant */ }
        _playing = false;
    }

    public void Stop()
    {
        bool wasPlaying = _playing;
        Pause();
        if (wasPlaying)
            PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }

    public PlaybackState State =>
        _playing ? PlaybackState.Playing : PlaybackState.Stopped;

    public void Dispose()
    {
        TeardownUnit();
        _source = null;
        _renderDelegate = null;
    }

    // ---------------------------------------------------------------- rendu

    private int RenderCallback(IntPtr refCon, ref uint flags, ref CoreAudioApi.AudioTimeStamp ts,
        uint bus, uint frames, IntPtr ioData)
    {
        var source = _source;
        if (ioData == IntPtr.Zero || source == null)
            return CoreAudioApi.NoErr; // rien à faire : silence

        // AudioBufferList : uint count, puis { uint channels; uint bytes; IntPtr data }
        int count = Math.Min(Marshal.ReadInt32(ioData, 0), MaxBuffers);
        int header = sizeof(uint);
        int stride = sizeof(uint) * 2 + IntPtr.Size;

        for (int b = 0; b < count; b++)
        {
            IntPtr buffer = ioData + header + b * stride;
            uint channels = (uint)Marshal.ReadInt32(buffer);
            uint bytes = (uint)Marshal.ReadInt32(buffer, sizeof(uint));
            IntPtr data = Marshal.ReadIntPtr(buffer, sizeof(uint) * 2);

            int floats = (int)Math.Min(bytes / 4u, (uint)_scratch.Length);
            if (floats <= 0 || data == IntPtr.Zero) continue;

            if (b == 0 && channels == (uint)_channels)
            {
                // tampon principal (normalement unique : format entrelacé demandé)
                int done = 0;
                while (done < floats)
                {
                    int take = Math.Min(floats - done, _scratch.Length);
                    int got = source.Read(_scratch, 0, take);
                    if (got <= 0) break;
                    Marshal.Copy(_scratch, 0, data + done * 4, got);
                    done += got;
                }
                if (done < floats)
                    WriteZeros(data + done * 4, floats - done);
            }
            else
            {
                // tampon inattendu (désentrelacé imposé ?) : silence propre
                WriteZeros(data, (int)(bytes / 4u));
            }
        }
        return CoreAudioApi.NoErr;
    }

    private static readonly float[] Zeros = new float[2048];

    private static void WriteZeros(IntPtr dest, int floats)
    {
        int done = 0;
        while (done < floats)
        {
            int take = Math.Min(floats - done, Zeros.Length);
            Marshal.Copy(Zeros, 0, dest + done * 4, take);
            done += take;
        }
    }

    // ---------------------------------------------------------------- helpers

    private int SetProperty<T>(uint id, uint scope, uint element, ref T value)
        where T : struct
    {
        IntPtr p = Marshal.AllocHGlobal(Marshal.SizeOf<T>());
        try
        {
            Marshal.StructureToPtr(value, p, false);
            return CoreAudioApi.AudioUnitSetProperty(_unit, id, scope, element, p, (uint)Marshal.SizeOf<T>());
        }
        finally
        {
            Marshal.FreeHGlobal(p);
        }
    }

    private int SetPropertyStruct<T>(uint id, uint scope, uint element, ref T value)
        where T : struct
        => SetProperty(id, scope, element, ref value);

    private void TeardownUnit()
    {
        _playing = false;
        if (_unit != IntPtr.Zero)
        {
            try { CoreAudioApi.AudioUnitStop(_unit); } catch { /* déjà arrêté */ }
            try { CoreAudioApi.AudioUnitUninitialize(_unit); } catch { /* déjà libéré */ }
            try { CoreAudioApi.AudioComponentInstanceDispose(_unit); } catch { /* déjà libéré */ }
            _unit = IntPtr.Zero;
        }
    }
}
