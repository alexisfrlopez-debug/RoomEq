using System.Runtime.InteropServices;

namespace RoomEq.Audio.Mac;

/// <summary>
/// Capture micro macOS via AudioUnit HAL (AUHAL, entrée du périphérique cible),
/// format de travail 48 kHz float mono — l'AUHAL convertit depuis le format réel
/// du périphérique. Équivalent macOS de la capture WASAPI de CaptureEngine.
///
/// Note permissions : macOS exige le consentement utilisateur (TCC) pour le
/// micro ; le bundle .app embarque NSMicrophoneUsageDescription et le premier
/// démarrage déclenche la demande système.
/// </summary>
public sealed class CoreAudioCapture : IDisposable
{
    private const int Rate = 48000;
    private const uint MaxFrames = 4096;

    private IntPtr _unit;
    private CoreAudioApi.AURenderCallback? _inputDelegate; // garde le délégué vivant
    private readonly Action<float[], int, int> _onChunk;
    private readonly int _channels;

    private IntPtr _bufferList;       // AudioBufferList non managée (1 tampon)
    private IntPtr _bufferData;       // données du tampon
    private readonly int _bufferBytes;

    private readonly uint _deviceId;
    private bool _started;
    private bool _disposed;

    /// <summary>Callback : (échantillons entrelacés float, canaux, fréquence).</summary>
    internal CoreAudioCapture(CoreAudioDeviceInfo device, Action<float[], int, int> onChunk, int channels = 1)
    {
        _deviceId = device.DeviceId;
        _onChunk = onChunk;
        _channels = Math.Max(1, channels);
        _bufferBytes = (int)(MaxFrames * _channels * sizeof(float));
    }

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(CoreAudioCapture));

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
            // 1 · activer l'ENTRÉE (élément 0), désactiver la sortie (élément 1)
            uint one = 1, zero = 0;
            err = SetProperty(CoreAudioApi.KuOutputEnableIO, CoreAudioApi.KuScopeInput, 0, ref one);
            if (err != CoreAudioApi.NoErr) throw Fail("Activation de l'entrée impossible", err);
            err = SetProperty(CoreAudioApi.KuOutputEnableIO, CoreAudioApi.KuScopeOutput, 1, ref zero);
            if (err != CoreAudioApi.NoErr) throw Fail("Neutralisation de la sortie impossible", err);

            // 2 · périphérique source
            uint sourceDevice = _deviceId;
            err = SetProperty(CoreAudioApi.KuOutputCurrentDevice, CoreAudioApi.KuScopeGlobal, 0, ref sourceDevice);
            if (err != CoreAudioApi.NoErr) throw Fail("Périphérique d'entrée indisponible", err);

            // 3 · format client : 48 kHz float entrelacé, N canaux
            var asbd = CoreAudioApi.AudioStreamBasicDescription.InterleavedFloat(Rate, (uint)_channels);
            err = SetPropertyStruct(CoreAudioApi.KuStreamFormat, CoreAudioApi.KuScopeOutput, 0, ref asbd);
            if (err != CoreAudioApi.NoErr)
                throw Fail("Format de capture 48 kHz refusé (micro verrouillé ?)", err);

            // 4 · tampon de réception + rappel d'entrée
            _bufferData = Marshal.AllocHGlobal(_bufferBytes);
            var abl = new CoreAudioApi.AudioBufferList
            {
                mNumberBuffers = 1,
                mBuffers = new CoreAudioApi.AudioBuffer
                {
                    mNumberChannels = (uint)_channels,
                    mDataByteSize = (uint)_bufferBytes,
                    mData = _bufferData,
                },
            };
            _bufferList = Marshal.AllocHGlobal(Marshal.SizeOf<CoreAudioApi.AudioBufferList>());
            Marshal.StructureToPtr(abl, _bufferList, false);

            _inputDelegate = InputCallback;
            var cb = new CoreAudioApi.AURenderCallbackStruct
            {
                inputProc = Marshal.GetFunctionPointerForDelegate(_inputDelegate),
                inputProcRefCon = IntPtr.Zero,
            };
            err = SetPropertyStruct(CoreAudioApi.KuOutputSetInputCallback, CoreAudioApi.KuScopeGlobal, 0, ref cb);
            if (err != CoreAudioApi.NoErr) throw Fail("Rappel d'entrée refusé", err);

            err = CoreAudioApi.AudioUnitInitialize(_unit);
            if (err != CoreAudioApi.NoErr) throw Fail("Initialisation AudioUnit impossible", err);

            err = CoreAudioApi.AudioUnitStart(_unit);
            if (err != CoreAudioApi.NoErr)
                throw Fail("Démarrage de la capture impossible (permissions micro ?)", err);

            _started = true;
        }
        catch
        {
            Teardown();
            throw;
        }
    }

    public void Stop()
    {
        if (!_started) return;
        try { CoreAudioApi.AudioUnitStop(_unit); } catch { /* tolérant */ }
        _started = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Teardown();
        _disposed = true;
    }

    // ---------------------------------------------------------------- rappel

    private int InputCallback(IntPtr refCon, ref uint flags, ref CoreAudioApi.AudioTimeStamp ts,
        uint bus, uint frames, IntPtr ioData)
    {
        if (_disposed || !_started) return CoreAudioApi.NoErr;

        // tirer les échantillons du matériel vers NOTRE tampon, puis livrer au callback
        uint localFlags = 0;
        int err = CoreAudioApi.AudioUnitRender(_unit, ref localFlags, ref ts, 0, frames, _bufferList);
        if (err != CoreAudioApi.NoErr) return err; // l'OS rejouera au cycle suivant

        int bytes = (int)Math.Min(frames * _channels * sizeof(float), (uint)_bufferBytes);
        if (bytes <= 0) return CoreAudioApi.NoErr;

        var samples = new float[bytes / 4];
        Marshal.Copy(_bufferData, samples, 0, samples.Length);
        try
        {
            _onChunk(samples, _channels, Rate);
        }
        catch
        {
            // le callback client ne doit jamais casser le thread audio
        }
        return CoreAudioApi.NoErr;
    }

    // ---------------------------------------------------------------- helpers

    private static InvalidOperationException Fail(string what, int osStatus) =>
        new(what + " : " + CoreAudioApi.DescribeError(osStatus));

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

    private void Teardown()
    {
        _started = false;
        if (_unit != IntPtr.Zero)
        {
            try { CoreAudioApi.AudioUnitStop(_unit); } catch { /* déjà arrêté */ }
            try { CoreAudioApi.AudioUnitUninitialize(_unit); } catch { /* déjà libéré */ }
            try { CoreAudioApi.AudioComponentInstanceDispose(_unit); } catch { /* déjà libéré */ }
            _unit = IntPtr.Zero;
        }
        if (_bufferList != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_bufferList);
            _bufferList = IntPtr.Zero;
        }
        if (_bufferData != IntPtr.Zero)
        {
            Marshal.FreeHGlobal(_bufferData);
            _bufferData = IntPtr.Zero;
        }
        _inputDelegate = null;
    }
}
