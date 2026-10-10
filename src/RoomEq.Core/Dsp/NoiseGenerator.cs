namespace RoomEq.Core.Dsp;

/// <summary>
/// Générateurs de signaux de test : bruit blanc (spectre plat), bruit rose
/// (filtre de Paul Kellet, -3 dB/octave), bruit brun (intégré, -6 dB/octave,
/// énergie concentrée dans les graves) et bruit violet (dérivé, +6 dB/octave,
/// énergie concentrée dans les aigus).
/// </summary>
public sealed class NoiseGenerator
{
    private readonly Random _rng;
    private double _b0, _b1, _b2, _b3, _b4, _b5, _b6;
    private double _sinePhase;
    private double _brownState;
    private double _prevWhite;

    public NoiseGenerator(int? seed = null)
        => _rng = seed.HasValue ? new Random(seed.Value) : new Random();

    /// <summary>Remplit le buffer de bruit blanc uniforme [-1, 1] (PSD plat).</summary>
    public void FillWhite(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
            buffer[i] = (float)(_rng.NextDouble() * 2.0 - 1.0);
    }

    /// <summary>Remplit le buffer de bruit rose (-3 dB/octave).</summary>
    public void FillPink(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            double white = _rng.NextDouble() * 2.0 - 1.0;
            _b0 = 0.99886 * _b0 + white * 0.0555179;
            _b1 = 0.99332 * _b1 + white * 0.0750759;
            _b2 = 0.96900 * _b2 + white * 0.1538520;
            _b3 = 0.86650 * _b3 + white * 0.3104856;
            _b4 = 0.55000 * _b4 + white * 0.5329522;
            _b5 = -0.7616 * _b5 - white * 0.0168980;
            double pink = _b0 + _b1 + _b2 + _b3 + _b4 + _b5 + _b6 + white * 0.5362;
            _b6 = white * 0.115926;
            buffer[i] = (float)(pink * 0.11); // normalisation pour éviter l'écrêtage
        }
    }

    /// <summary>
    /// Remplit le buffer de bruit brun (intégration fuyante du bruit blanc, -6 dB/octave).
    /// L'intégrateur fuyant évite la dérive continue du vrai bruit brun tout en
    /// conservant la pente spectrale dans la bande utile (> ~10 Hz).
    /// </summary>
    public void FillBrown(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            double white = _rng.NextDouble() * 2.0 - 1.0;
            _brownState = (_brownState + 0.02 * white) / 1.02;
            buffer[i] = (float)(_brownState * 3.5); // normalisation RMS ≈ 0,2
        }
    }

    /// <summary>Remplit le buffer de bruit violet (différence d'échantillons blancs, +6 dB/octave).</summary>
    public void FillViolet(Span<float> buffer)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            double white = _rng.NextDouble() * 2.0 - 1.0;
            double v = white - _prevWhite;
            _prevWhite = white;
            buffer[i] = (float)(v * 0.25); // RMS ≈ 0,29
        }
    }

    /// <summary>Remplit le buffer d'un sinus à fréquence et amplitude fixées.</summary>
    public void FillSine(Span<float> buffer, double freq, double sampleRate, double amplitude)
    {
        double inc = 2.0 * Math.PI * freq / sampleRate;
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (float)(Math.Sin(_sinePhase) * amplitude);
            _sinePhase += inc;
            if (_sinePhase > 2.0 * Math.PI) _sinePhase -= 2.0 * Math.PI;
        }
    }
}
