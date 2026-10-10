using System.Collections.Concurrent;
using NAudio.Wave;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;

namespace RoomEq.Audio.Playback;

public enum TestSignal { WhiteNoise, PinkNoise, BrownNoise, VioletNoise, Sine, LogSweep }

/// <summary>Applique un gain (volume × préampli) calculé dynamiquement.</summary>
public sealed class GainSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Func<double> _gain;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public GainSampleProvider(ISampleProvider source, Func<double> gain)
    {
        _source = source;
        _gain = gain;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        double g = _gain();
        if (Math.Abs(g - 1.0) > 1e-9)
        {
            for (int i = 0; i < read; i++)
                buffer[offset + i] = (float)(buffer[offset + i] * g);
        }
        return read;
    }
}

/// <summary>Adaptation du nombre de canaux (upmix / downmix simple).</summary>
public sealed class ChannelAdapterSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly int _sourceChannels;
    private float[] _tmp = Array.Empty<float>();

    public WaveFormat WaveFormat { get; }
    public int TargetChannels { get; }

    public ChannelAdapterSampleProvider(ISampleProvider source, int targetChannels)
    {
        _source = source;
        _sourceChannels = source.WaveFormat.Channels;
        TargetChannels = targetChannels;
        WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, targetChannels);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / TargetChannels;
        int srcSamples = frames * _sourceChannels;
        if (_tmp.Length < srcSamples) _tmp = new float[srcSamples];

        int read = _source.Read(_tmp, 0, srcSamples);
        int srcFrames = read / _sourceChannels;

        for (int f = 0; f < srcFrames; f++)
        {
            int so = f * _sourceChannels;
            int doff = offset + f * TargetChannels;

            if (_sourceChannels == TargetChannels)
            {
                for (int c = 0; c < TargetChannels; c++) buffer[doff + c] = _tmp[so + c];
            }
            else if (_sourceChannels == 1 && TargetChannels >= 2)
            {
                for (int c = 0; c < TargetChannels; c++) buffer[doff + c] = _tmp[so];
            }
            else if (_sourceChannels >= 2 && TargetChannels == 1)
            {
                double sum = 0;
                for (int c = 0; c < _sourceChannels; c++) sum += _tmp[so + c];
                buffer[doff] = (float)(sum / _sourceChannels);
            }
            else if (_sourceChannels < TargetChannels)
            {
                // duplique le dernier canal disponible
                for (int c = 0; c < TargetChannels; c++)
                    buffer[doff + c] = _tmp[so + Math.Min(c, _sourceChannels - 1)];
            }
            else
            {
                // garde les premiers canaux
                for (int c = 0; c < TargetChannels; c++) buffer[doff + c] = _tmp[so + c];
            }
        }
        return srcFrames * TargetChannels;
    }
}

/// <summary>
/// Tampon d'écoute (tap) : stocke les derniers fragments traités pour l'analyseur de spectre.
/// </summary>
public sealed class TapBuffer
{
    private readonly ConcurrentQueue<float[]> _chunks = new();
    private long _total;
    private readonly long _max = 192000; // ~2 s à 96 kHz stéréo

    public void Push(float[] buffer, int offset, int count)
    {
        var copy = new float[count];
        Array.Copy(buffer, offset, copy, 0, count);
        _chunks.Enqueue(copy);
        Interlocked.Add(ref _total, count);
        while (Volatile.Read(ref _total) > _max && _chunks.TryDequeue(out var old))
            Interlocked.Add(ref _total, -old.Length);
    }

    public bool TryDequeue(out float[] chunk)
    {
        if (_chunks.TryDequeue(out chunk!))
        {
            Interlocked.Add(ref _total, -chunk.Length);
            return true;
        }
        return false;
    }

    public void Clear()
    {
        while (_chunks.TryDequeue(out _)) { }
        Interlocked.Exchange(ref _total, 0);
    }
}

/// <summary>Dérivation du flux audio vers le tap (transparent).</summary>
public sealed class TapSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly TapBuffer _tap;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public TapSampleProvider(ISampleProvider source, TapBuffer tap)
    {
        _source = source;
        _tap = tap;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read > 0) _tap.Push(buffer, offset, read);
        return read;
    }
}

/// <summary>Insère la chaîne d'égalisation dans le flux.</summary>
public sealed class EqSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly EqChain _chain;

    public WaveFormat WaveFormat => _source.WaveFormat;

    public EqSampleProvider(ISampleProvider source, EqChain chain)
    {
        _source = source;
        _chain = chain;
        _chain.Configure(source.WaveFormat.SampleRate, source.WaveFormat.Channels);
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int read = _source.Read(buffer, offset, count);
        if (read > 0) _chain.Process(buffer, offset, read);
        return read;
    }
}

/// <summary>Générateur de signaux de test (bruits, sinus, balayage logarithmique).</summary>
public sealed class GeneratorSampleProvider : ISampleProvider
{
    private readonly NoiseGenerator _gen = new();
    private LogSweepGenerator? _sweep;
    public WaveFormat WaveFormat { get; }
    public TestSignal Signal { get; set; } = TestSignal.WhiteNoise;
    public double SineFrequency { get; set; } = 1000.0;
    public double Amplitude { get; set; } = 0.2;

    /// <summary>Spécification du balayage logarithmique (utilisée si Signal = LogSweep).</summary>
    public LogSweepSpec SweepSpec { get; set; } = LogSweepSpec.Default;

    public GeneratorSampleProvider(int sampleRate, int channels)
        => WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, Math.Max(1, channels));

    public int Read(float[] buffer, int offset, int count)
    {
        var span = buffer.AsSpan(offset, count);
        switch (Signal)
        {
            case TestSignal.WhiteNoise:
                _gen.FillWhite(span);
                break;
            case TestSignal.PinkNoise:
                _gen.FillPink(span);
                break;
            case TestSignal.BrownNoise:
                _gen.FillBrown(span);
                break;
            case TestSignal.VioletNoise:
                _gen.FillViolet(span);
                break;
            case TestSignal.Sine:
                _gen.FillSine(span, SineFrequency, WaveFormat.SampleRate, 1.0);
                break;
            case TestSignal.LogSweep:
                // un seul générateur par lecture : la progression du balayage est continue
                _sweep ??= new LogSweepGenerator(SweepSpec, WaveFormat.SampleRate);
                _sweep.Fill(span, 1.0);
                break;
        }
        double a = Amplitude;
        if (Math.Abs(a - 1.0) > 1e-9)
            for (int i = 0; i < count; i++)
                span[i] = (float)(span[i] * a);
        return count;
    }
}

/// <summary>Lecture en boucle d'une source relançable (callback de rembobinage).</summary>
public sealed class LoopingSampleProvider : ISampleProvider
{
    private readonly ISampleProvider _source;
    private readonly Action? _restart;

    public bool Loop { get; set; }
    public WaveFormat WaveFormat => _source.WaveFormat;

    public LoopingSampleProvider(ISampleProvider source, Action? restart)
    {
        _source = source;
        _restart = restart;
    }

    public int Read(float[] buffer, int offset, int count)
    {
        int total = 0;
        int guard = 0;
        while (total < count && guard++ < 16)
        {
            int read = _source.Read(buffer, offset + total, count - total);
            if (read == 0)
            {
                if (!Loop || _restart == null) break;
                _restart();
            }
            else total += read;
        }
        return total;
    }
}
