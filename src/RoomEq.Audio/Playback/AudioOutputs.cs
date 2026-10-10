using NAudio.CoreAudioApi;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace RoomEq.Audio.Playback;

/// <summary>Informations sur un périphérique audio (sortie WASAPI, driver ASIO, entrée).</summary>
public sealed record OutputDeviceInfo(string Id, string DisplayName, string Kind);

/// <summary>Abstraction d'une sortie de lecture (WASAPI partagé ou ASIO exclusif).</summary>
public interface IAudioOutput : IDisposable
{
    string DisplayName { get; }
    string Kind { get; }
    int SampleRate { get; }
    int Channels { get; }
    PlaybackState State { get; }
    event EventHandler? PlaybackStopped;
    /// <summary>Prépare la lecture. La source doit déjà être au bon format (taux/canaux, float).</summary>
    /// <remarks>Peut être appelé plusieurs fois : les implémentations recréent leur sortie.</remarks>
    void Init(ISampleProvider adaptedSource);
    void Play();
    void Pause();
    void Stop();
}

/// <summary>Sortie WASAPI en mode partagé (mixée avec le reste du système).</summary>
public sealed class WasapiOutput : IAudioOutput
{
    private readonly MMDevice _device;
    private WasapiOut? _out;

    public string DisplayName { get; }
    public string Kind => "WASAPI";
    public int SampleRate { get; }
    public int Channels { get; }
    public event EventHandler? PlaybackStopped;

    public WasapiOutput(MMDevice device)
    {
        _device = device;
        DisplayName = device.FriendlyName;
        using var client = device.AudioClient; // COM IDisposable : libéré immédiatement
        var mix = client.MixFormat;
        SampleRate = mix.SampleRate;
        Channels = mix.Channels;
    }

    public void Init(ISampleProvider adaptedSource)
    {
        _out?.Dispose();
        _out = new WasapiOut(_device, AudioClientShareMode.Shared, true, 80);
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

/// <summary>
/// Sortie ASIO exclusive (contourne le mixeur Windows, latence minimale).
/// NAudio configure automatiquement le taux du pilote depuis le format de la source
/// (le taux choisi ici est validé via IsSampleRateSupported) et le nombre de canaux.
/// </summary>
public sealed class AsioOutput : IAudioOutput
{
    private static readonly int[] PreferredRates = { 48000, 44100, 96000, 88200, 192000 };

    private AsioOut? _out;

    public string DriverName { get; }
    public string DisplayName => DriverName;
    public string Kind => "ASIO";
    public int SampleRate { get; }
    public int Channels { get; }
    public event EventHandler? PlaybackStopped;

    public AsioOutput(string driverName)
    {
        DriverName = driverName;

        using (var probe = new AsioOut(driverName))
        {
            int driverOutputs = Math.Max(1, probe.DriverOutputChannelCount);
            Channels = Math.Clamp(2, 1, driverOutputs);

            foreach (int r in PreferredRates)
            {
                try
                {
                    if (probe.IsSampleRateSupported(r))
                    {
                        SampleRate = r;
                        break;
                    }
                }
                catch
                {
                    // certains pilotes lèvent sur cette requête : on tente le taux suivant
                }
            }
            if (SampleRate == 0) SampleRate = 48000;
        }
    }

    public void Init(ISampleProvider adaptedSource)
    {
        // AsioOut n'accepte qu'un seul Init par instance : on recrée le pilote.
        _out?.Dispose();
        _out = new AsioOut(DriverName);
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
