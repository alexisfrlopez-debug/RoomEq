using System.Collections.Concurrent;
using NAudio.Wave;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;

namespace RoomEq.Audio.Playback;

/// <summary>Signaux de test générables à la volée par le moteur de lecture (spec I-AUD-02).</summary>
public enum TestSignal
{
    WhiteNoise,
    PinkNoise,
    BrownNoise,
    VioletNoise,
    Sine,
    LogSweep,
}

/// <summary>
/// Applique un gain recalculé à chaque bloc (volume maître × préampli EQ) —
/// les changements de curseur prennent effet au bloc suivant sans re-construire
/// la chaîne.
/// </summary>
public sealed class GainSampleProvider(ISampleProvider source, Func<double> gain) : ISampleProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        double g = gain();
        if (Math.Abs(g - 1.0) > 1e-9)
        {
            for (int i = 0; i < read; i++)
                buffer[offset + i] = (float)(buffer[offset + i] * g);
        }
        return read;
    }
}

/// <summary>
/// Adaptation du nombre de canaux (upmix / downmix) : 1→N duplique le mono,
/// N→1 moyenne, N→M (M&gt;N) duplique le dernier canal, N→M (M&lt;N) garde les
/// premiers canaux. Travaille par frames entières.
/// </summary>
public sealed class ChannelAdapterSampleProvider(ISampleProvider source, int targetChannels) : ISampleProvider
{
    private readonly int _sourceChannels = source.WaveFormat.Channels;
    private float[] _tmp = Array.Empty<float>();

    public WaveFormat WaveFormat { get; } =
        WaveFormat.CreateIeeeFloatWaveFormat(source.WaveFormat.SampleRate, Math.Max(1, targetChannels));

    public int TargetChannels { get; } = Math.Max(1, targetChannels);

    public int Read(float[] buffer, int offset, int count)
    {
        int frames = count / TargetChannels;
        if (frames <= 0) return 0;
        int srcSamples = frames * _sourceChannels;
        if (_tmp.Length < srcSamples) _tmp = new float[srcSamples];

        int read = source.Read(_tmp, 0, srcSamples);
        int srcFrames = read / _sourceChannels;

        for (int f = 0; f < srcFrames; f++)
        {
            int so = f * _sourceChannels;
            int doff = offset + f * TargetChannels;

            if (_sourceChannels == TargetChannels)
            {
                for (int c = 0; c < TargetChannels; c++) buffer[doff + c] = _tmp[so + c];
            }
            else if (_sourceChannels == 1)
            {
                for (int c = 0; c < TargetChannels; c++) buffer[doff + c] = _tmp[so];
            }
            else if (TargetChannels == 1)
            {
                double sum = 0;
                for (int c = 0; c < _sourceChannels; c++) sum += _tmp[so + c];
                buffer[doff] = (float)(sum / _sourceChannels);
            }
            else if (_sourceChannels < TargetChannels)
            {
                for (int c = 0; c < TargetChannels; c++)
                    buffer[doff + c] = _tmp[so + Math.Min(c, _sourceChannels - 1)];
            }
            else
            {
                for (int c = 0; c < TargetChannels; c++) buffer[doff + c] = _tmp[so + c];
            }
        }
        return srcFrames * TargetChannels;
    }
}

/// <summary>
/// Tampon d'écoute (tap) : stocke les derniers fragments traités pour
/// l'analyseur de spectre temps réel. File bornée à ~2 s (192 000 échantillons
/// ≈ 96 kHz stéréo) : en saturation les plus anciens sont éjectés.
/// </summary>
public sealed class TapBuffer
{
    private const long MaxSamples = 192_000;

    private readonly ConcurrentQueue<float[]> _chunks = new();
    private long _total;

    /// <summary>Nombre d'échantillons actuellement tamponnés (toutes voies).</summary>
    public long BufferedSamples => Volatile.Read(ref _total);

    public void Push(float[] buffer, int offset, int count)
    {
        var copy = new float[count];
        Array.Copy(buffer, offset, copy, 0, count);
        _chunks.Enqueue(copy);
        Interlocked.Add(ref _total, count);
        while (Volatile.Read(ref _total) > MaxSamples && _chunks.TryDequeue(out var old))
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

/// <summary>Dérivation transparente du flux vers le tap (spec I-AUD-02).</summary>
public sealed class TapSampleProvider(ISampleProvider source, TapBuffer tap) : ISampleProvider
{
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int read = source.Read(buffer, offset, count);
        if (read > 0) tap.Push(buffer, offset, read);
        return read;
    }
}

/// <summary>Insère la chaîne d'égalisation dans le flux (reconfiguration à chaud).
/// La chaîne est configurée au format de la source (déjà alignée sur la sortie).</summary>
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

/// <summary>
/// Générateur de signaux de test : bruits (blanc/rose/brun/violet), sinus,
/// balayage logarithmique. La progression du balayage est CONTINUE entre les
/// appels Read (un seul générateur par instance).
/// </summary>
public sealed class GeneratorSampleProvider(int sampleRate, int channels) : ISampleProvider
{
    private readonly NoiseGenerator _gen = new();
    private LogSweepGenerator? _sweep;

    public WaveFormat WaveFormat { get; } =
        WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, Math.Max(1, channels));

    public TestSignal Signal { get; set; } = TestSignal.WhiteNoise;
    public double SineFrequency { get; set; } = 1000.0;
    public double Amplitude { get; set; } = 0.2;

    /// <summary>Spécification du balayage (utilisée si Signal = LogSweep).</summary>
    public LogSweepSpec SweepSpec { get; set; } = LogSweepSpec.Default;

    public int Read(float[] buffer, int offset, int count)
    {
        var span = buffer.AsSpan(offset, count);
        switch (Signal)
        {
            case TestSignal.WhiteNoise: _gen.FillWhite(span); break;
            case TestSignal.PinkNoise: _gen.FillPink(span); break;
            case TestSignal.BrownNoise: _gen.FillBrown(span); break;
            case TestSignal.VioletNoise: _gen.FillViolet(span); break;
            case TestSignal.Sine: _gen.FillSine(span, SineFrequency, WaveFormat.SampleRate, 1.0); break;
            case TestSignal.LogSweep:
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

/// <summary>
/// Lecture en boucle d'une source relançable : fin de source + Loop + callback
/// de rembobinage → réappel du callback et poursuite. Garde-fou : 16 itérations
/// par Read (une source qui ne rembobine pas cède la main).
/// </summary>
public sealed class LoopingSampleProvider(ISampleProvider source, Action? restart) : ISampleProvider
{
    private const int MaxRestartsPerRead = 16;

    public bool Loop { get; set; }
    public WaveFormat WaveFormat => source.WaveFormat;

    public int Read(float[] buffer, int offset, int count)
    {
        int total = 0;
        int guard = 0;
        while (total < count && guard++ < MaxRestartsPerRead)
        {
            int read = source.Read(buffer, offset + total, count - total);
            if (read == 0)
            {
                if (!Loop || restart == null) break;
                restart();
            }
            else
            {
                total += read;
            }
        }
        return total;
    }
}
