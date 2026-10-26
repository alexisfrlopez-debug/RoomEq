using RoomEq.Audio.Asio;
using RoomEq.Audio.Capture;
using RoomEq.Audio.Measurement;
using RoomEq.Audio.Playback;
using RoomEq.Audio.Routing;
using Xunit;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Moteur de mesure (spec I-AUD-06) : constantes verrouillées + assemblage des
/// segments (rognage tête/queue, concaténation). Les parties nécessitant des
/// périphériques réels ne sont pas exécutables en CI — leur comportement est
/// couvert par les specs et les tests Core (SweepAnalyzer, MultiSignalAnalysis,
/// CorrectionDesigner).
/// </summary>
public class MeasurementEngineTests
{
    [Fact]
    public void SilenceThreshold_IsMinus66Dbfs()
    {
        Assert.Equal(-66.0, RoomMeasurementEngine.SilencePeakDbfs, 6);
    }

    [Fact]
    public void AssembleSegment_TrimsHeadAndTail_Exactly()
    {
        // 3 chunks de 1000 échantillons (3000 au total), rate 1000 Hz
        // rognage 0,5 s en tête + 0,25 s en queue = 500 + 250 = 750 écartés
        var chunks = new List<float[]>();
        for (int c = 0; c < 3; c++)
            chunks.Add(Enumerable.Range(0, 1000).Select(i => (float)(c * 1000 + i)).ToArray());
        var gate = new object();

        var (all, rate) = RoomMeasurementEngine.AssembleSegment(chunks, gate, 1000,
            trimStartSeconds: 0.5, trimEndSeconds: 0.25);

        Assert.Equal(1000, rate);
        Assert.Equal(2250, all.Length);
        // le premier échantillon conservé est l'index 500 du flux (chunk 0)
        Assert.Equal(500f, all[0], 3);
        Assert.Equal(2749f, all[^1], 3);
    }

    [Fact]
    public void AssembleSegment_OverTrim_YieldsEmpty()
    {
        var chunks = new List<float[]> { new float[100] };
        var (all, _) = RoomMeasurementEngine.AssembleSegment(chunks, new object(), 1000,
            trimStartSeconds: 5.0, trimEndSeconds: 5.0);
        Assert.Empty(all);
    }

    [Fact]
    public void AssembleSegment_EmptyChunks_YieldsEmpty()
    {
        var (all, _) = RoomMeasurementEngine.AssembleSegment(new List<float[]>(), new object(), 48000,
            0.5, 0.25);
        Assert.Empty(all);
    }

    [Fact]
    public void AssembleSegment_TrimsLongerThanFirstChunk_CrossesBoundaries()
    {
        // rognage de tête qui traverse plusieurs chunks
        var chunks = new List<float[]>
        {
            Enumerable.Range(0, 300).Select(i => (float)i).ToArray(),
            Enumerable.Range(300, 300).Select(i => (float)i).ToArray(),
            Enumerable.Range(600, 300).Select(i => (float)i).ToArray(),
        };
        var (all, _) = RoomMeasurementEngine.AssembleSegment(chunks, new object(), 1000,
            trimStartSeconds: 0.4, trimEndSeconds: 0.0);

        Assert.Equal(500, all.Length);
        Assert.Equal(400f, all[0], 3);
        Assert.Equal(899f, all[^1], 3);
    }
}

/// <summary>Capture et lecture sans sous-système audio (Linux/CI) : jamais d'exception non sollicitée.</summary>
public class PlatformInertnessTests
{
    [Fact]
    public void CaptureEngine_EnumerateDevices_EmptyWithoutException()
    {
        using var capture = new CaptureEngine();
        var devices = capture.EnumerateDevices();
        Assert.NotNull(devices);
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            Assert.Empty(devices);
    }

    [Fact]
    public void CaptureEngine_GetChannelCount_NullOnLinux()
    {
        using var capture = new CaptureEngine();
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            Assert.Null(capture.GetChannelCount(null));
    }

    [Fact]
    public void CaptureEngine_StopDispose_Idempotent()
    {
        using var capture = new CaptureEngine();
        capture.Stop();      // sans capture démarrée : inoffensif
        capture.Stop();
    }

    [Fact]
    public void PlaybackEngine_EnumerateOutputs_EmptyWithoutException()
    {
        using var engine = new PlaybackEngine();
        string? error = null;
        engine.ErrorOccurred += e => error = e;

        var outputs = engine.EnumerateOutputs();

        Assert.NotNull(outputs);
        if (!OperatingSystem.IsWindows() && !OperatingSystem.IsMacOS())
            Assert.Empty(outputs);
        // une erreur de sous-système est SIGNALÉE (jamais levée)
        if (error != null)
            Assert.Contains("impossible", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void PlaybackEngine_TransportControls_IdempotentWithoutOutput()
    {
        using var engine = new PlaybackEngine();
        engine.Stop();
        engine.Pause();
        engine.Resume();
        Assert.Equal(NAudio.Wave.PlaybackState.Stopped, engine.State);
        Assert.Equal(48000, engine.CurrentSampleRate);
        Assert.Equal(2, engine.CurrentChannels);
        Assert.Null(engine.CurrentOutputId);
    }

    [Fact]
    public void PlaybackEngine_SetOutputUnknownId_SignalsErrorAndStaysUsable()
    {
        using var engine = new PlaybackEngine();
        string? error = null;
        engine.ErrorOccurred += e => error = e;

        engine.SetOutput("inconnu|xyz");

        Assert.Null(engine.CurrentOutputId);
        // l'identifiant inconnu est signalé, pas levé
        Assert.NotNull(error);
        Assert.Contains("inconnu", error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void AudioDeviceCatalog_Enumerations_EmptyWithoutSubsystem()
    {
        var catalog = new AudioDeviceCatalog();
        if (!OperatingSystem.IsWindows())
        {
            Assert.Empty(catalog.EnumerateCaptureSources());
            Assert.Empty(catalog.EnumerateLoopbackSources());
            Assert.Empty(catalog.EnumerateRenderDevices());
            Assert.Null(catalog.GetDefaultCaptureId());
        }
    }

    [Fact]
    public void AudioDeviceCatalog_ProbeVirtualCard_EmptyWithoutSubsystem()
    {
        if (OperatingSystem.IsWindows()) return;   // sonde réelle sous Windows

        var catalog = new AudioDeviceCatalog();
        var status = catalog.ProbeVirtualCard();
        Assert.NotNull(status);
        Assert.False(status.Installed);
        Assert.False(status.FullyRouted);
    }

    [Fact]
    public void AudioDeviceCatalog_VirtualRenderDetection_UsesPatterns()
    {
        // union des motifs des fournisseurs à câble (hors plateforme, motifs purs)
        Assert.True(AudioDeviceCatalog.IsVirtualRender("RoomEq Input (RoomEq Virtual Audio)"));
        Assert.False(AudioDeviceCatalog.IsVirtualRender("Enceintes Bose"));
        Assert.True(AudioDeviceCatalog.IsVirtualCapture("RoomEq Output (RoomEq Virtual Audio)"));
        Assert.False(AudioDeviceCatalog.IsVirtualCapture("Micro USB"));
    }

    [Fact]
    public void EndpointVolumeController_NonWasapiTarget_NotAvailable()
    {
        using var controller = new EndpointVolumeController();
        Assert.False(controller.SetTarget(null));
        Assert.False(controller.SetTarget("asio|UnPilote"));
        if (!OperatingSystem.IsWindows())
        {
            Assert.False(controller.SetTarget("wasapi|{inexistant}"));
            Assert.False(controller.Available);
            Assert.Equal(-1.0, controller.VolumeScalar, 6);
            Assert.Null(controller.ToggleMute());
        }
    }

    [Fact]
    public void AsioManager_DriverNames_EmptyWithoutException()
    {
        if (OperatingSystem.IsWindows()) return;   // registre réel sous Windows
        Assert.Empty(AsioManager.DriverNames);
        Assert.False(AsioManager.HasDrivers);
    }
}
