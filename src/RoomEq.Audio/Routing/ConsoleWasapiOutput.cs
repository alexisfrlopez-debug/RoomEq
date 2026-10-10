using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using RoomEq.Audio.Playback;

namespace RoomEq.Audio.Routing;

/// <summary>
/// Sortie WASAPI partagée dédiée au bus master de la table de mixage : buffer plus
/// court que la lecture classique (50 ms) pour contenir la latence du routage
/// système (câble virtuel + capture + mixage).
/// </summary>
public sealed class ConsoleWasapiOutput : IAudioOutput
{
    private readonly MMDevice _device;
    private WasapiOut? _out;

    public string DisplayName { get; }
    public string Kind => "WASAPI";
    public int SampleRate { get; }
    public int Channels { get; }
    public int LatencyMs { get; }
    public event EventHandler? PlaybackStopped;

    public ConsoleWasapiOutput(MMDevice device, int latencyMs = 50)
    {
        _device = device;
        DisplayName = device.FriendlyName;
        LatencyMs = Math.Clamp(latencyMs, 20, 200);
        using var ac = device.AudioClient;
        var mix = ac.MixFormat;
        SampleRate = mix.SampleRate;
        Channels = mix.Channels;
    }

    public void Init(ISampleProvider adaptedSource)
    {
        _out?.Dispose();
        _out = new WasapiOut(_device, AudioClientShareMode.Shared, true, LatencyMs);
        _out.Init(new SampleToWaveProvider(adaptedSource));
        _out.PlaybackStopped += (_, _) => PlaybackStopped?.Invoke(this, EventArgs.Empty);
    }

    public void Play() => _out?.Play();
    public void Pause() => _out?.Pause();
    public void Stop() => _out?.Stop();
    public PlaybackState State => _out?.PlaybackState ?? PlaybackState.Stopped;

    public void Dispose()
    {
        _out?.Dispose();
        _out = null;
    }
}
