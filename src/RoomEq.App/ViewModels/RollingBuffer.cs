namespace RoomEq.App.ViewModels;

/// <summary>
/// Tampon circulaire thread-safe pour l'analyse temps réel (flux tap sortie ou
/// micro) — spec I-APP-05.
/// </summary>
public sealed class RollingBuffer
{
    private readonly float[] _data;
    private readonly object _gate = new();
    private long _written;

    public int SampleRate { get; set; } = 48000;

    public RollingBuffer(int capacitySeconds, int sampleRate = 48000)
    {
        _data = new float[sampleRate * capacitySeconds];
        SampleRate = sampleRate;
    }

    public int Capacity => _data.Length;

    public void Add(ReadOnlySpan<float> samples)
    {
        lock (_gate)
        {
            // copie en 2 segments (pas de modulo par échantillon : appelé depuis le
            // callback de capture, doit rester le plus court possible)
            int len = _data.Length;
            int pos = (int)(_written % len);
            int first = Math.Min(samples.Length, len - pos);
            samples[..first].CopyTo(_data.AsSpan(pos));
            if (samples.Length > first)
                samples[first..].CopyTo(_data.AsSpan(0, samples.Length - first));
            _written += samples.Length;
        }
    }

    /// <summary>Copie les N dernières secondes (ou moins si pas encore remplies).</summary>
    public float[] Snapshot(int maxSamples)
    {
        lock (_gate)
        {
            int available = (int)Math.Min(_written, _data.Length);
            int count = Math.Min(maxSamples, available);
            var result = new float[count];
            int len = _data.Length;
            if (count > 0)
            {
                int pos = (int)((_written - count) % len);
                if (pos < 0) pos += len;
                int first = Math.Min(count, len - pos);
                _data.AsSpan(pos, first).CopyTo(result);
                if (count > first)
                    _data.AsSpan(0, count - first).CopyTo(result.AsSpan(first));
            }
            return result;
        }
    }

    public void Clear()
    {
        lock (_gate) _written = 0;
    }
}
