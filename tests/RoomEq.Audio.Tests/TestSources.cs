using NAudio.Wave;

namespace RoomEq.Audio.Tests;

/// <summary>
/// Sources de test pures (aucun périphérique) pour exercer les briques
/// ISampleProvider de la spec I-AUD-02 sous Linux comme sous Windows.
/// </summary>
internal static class TestSources
{
    /// <summary>Source finie : rend un tableau une seule fois, puis 0 (fin de lecture).</summary>
    public sealed class ArraySampleProvider(float[] data, int sampleRate = 48000, int channels = 2)
        : ISampleProvider
    {
        private int _pos;

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

        public int Read(float[] buffer, int offset, int count)
        {
            int n = Math.Min(count, data.Length - _pos);
            if (n > 0)
            {
                Array.Copy(data, _pos, buffer, offset, n);
                _pos += n;
            }
            if (n < count)
                Array.Clear(buffer, offset + Math.Max(0, n), count - Math.Max(0, n));
            return n;
        }
    }

    /// <summary>Sinus infini déterministe (test fréquentiel via FFT).</summary>
    public sealed class SineSource(double frequency, double amplitude, int sampleRate = 48000)
        : ISampleProvider
    {
        private double _phase;

        public WaveFormat WaveFormat { get; } =
            WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);

        public int Read(float[] buffer, int offset, int count)
        {
            for (int i = 0; i < count; i++)
            {
                buffer[offset + i] = (float)(amplitude * Math.Sin(_phase));
                _phase += 2.0 * Math.PI * frequency / sampleRate;
                if (_phase > 2.0 * Math.PI) _phase -= 2.0 * Math.PI;
            }
            return count;
        }
    }
}
