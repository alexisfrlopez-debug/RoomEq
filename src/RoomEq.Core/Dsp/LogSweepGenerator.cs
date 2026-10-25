namespace RoomEq.Core.Dsp;

/// <summary>
/// Spécification d'un balayage sinusoïdal logarithmique (ESS — exponential sine
/// sweep) : la fréquence instantanée monte de F1 à F2 selon une loi exponentielle,
/// chaque octave occupant la même durée (spec I-DSP-05).
/// </summary>
/// <param name="F1">Fréquence de départ (Hz), strictement positive.</param>
/// <param name="F2">Fréquence d'arrivée souhaitée (Hz), strictement supérieure à F1.</param>
/// <param name="DurationSeconds">Durée totale du balayage (s), au moins 0,5.</param>
public sealed record LogSweepSpec(double F1, double F2, double DurationSeconds)
{
    /// <summary>Balayage par défaut : 20 Hz → 20 kHz en 12 s.</summary>
    public static LogSweepSpec Default { get; } = new(20.0, 20000.0, 12.0);

    /// <summary>F2 effectif : borné par Nyquist (avec marge) à la fréquence d'échantillonnage donnée.</summary>
    public double EffectiveF2(int sampleRate) => Math.Min(F2, sampleRate * 0.45);
}

/// <summary>
/// Générateur de balayage logarithmique (spec I-DSP-05), utilisable en flux
/// (lecture audio par blocs) comme pour la régénération exacte de la référence
/// d'analyse. La phase est calculée en forme fermée (intégrale exacte de la
/// fréquence instantanée) : la référence régénérée est identique bit à bit au
/// signal émis — condition de la déconvolution de Farina. Fondus cubiques
/// d'attaque et de fin (smoothstep, 10 ms) contre les clics.
/// </summary>
public sealed class LogSweepGenerator
{
    private readonly LogSweepSpec _spec;
    private readonly int _sampleRate;
    private readonly double _logRate;   // ln(F2eff/F1) / T
    private readonly double _phaseK;    // 2π·F1·T / ln(F2eff/F1)
    private readonly long _totalSamples;
    private readonly int _fadeSamples;
    private long _produced;

    public LogSweepGenerator(LogSweepSpec spec, int sampleRate)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (spec.F1 <= 0 || spec.F2 <= spec.F1)
            throw new ArgumentException("Le balayage exige 0 < F1 < F2.", nameof(spec));
        if (spec.DurationSeconds <= 0.5)
            throw new ArgumentException("Durée de balayage trop courte (0,5 s minimum).", nameof(spec));
        if (sampleRate <= 0)
            throw new ArgumentOutOfRangeException(nameof(sampleRate), "La fréquence d'échantillonnage doit être positive.");

        _spec = spec;
        _sampleRate = sampleRate;
        double lnRatio = Math.Log(spec.EffectiveF2(sampleRate) / spec.F1);
        _logRate = lnRatio / spec.DurationSeconds;
        _phaseK = 2.0 * Math.PI * spec.F1 * spec.DurationSeconds / lnRatio;
        _totalSamples = (long)Math.Ceiling(spec.DurationSeconds * sampleRate);
        _fadeSamples = Math.Max(16, (int)(0.01 * sampleRate)); // 10 ms
    }

    public int SampleRate => _sampleRate;
    public long TotalSamples => _totalSamples;
    public LogSweepSpec Spec => _spec;

    /// <summary>Phase instantanée exacte (rad) à l'échantillon n — forme fermée.</summary>
    public double PhaseAt(long n)
        => _phaseK * (Math.Exp(n * _logRate / _sampleRate) - 1.0);

    /// <summary>Fréquence instantanée (Hz) à l'échantillon n.</summary>
    public double FrequencyAt(long n)
        => _spec.F1 * Math.Exp(n * _logRate / _sampleRate);

    /// <summary>
    /// Remplit le buffer avec la suite du balayage (fondus, silence au-delà de la
    /// durée totale). Déterministe : deux générateurs de même spécification
    /// produisent exactement le même flux.
    /// </summary>
    public void Fill(Span<float> buffer, double amplitude)
    {
        for (int i = 0; i < buffer.Length; i++)
        {
            long n = _produced + i;
            buffer[i] = n >= _totalSamples
                ? 0f
                : (float)(Math.Sin(PhaseAt(n)) * amplitude * Fade(n));
        }
        _produced += buffer.Length;
    }

    /// <summary>Régénère l'intégralité du balayage (référence d'analyse).</summary>
    public float[] GenerateFull(double amplitude = 1.0)
    {
        var result = new float[_totalSamples];
        Fill(result, amplitude);
        return result;
    }

    private double Fade(long n)
    {
        if (n < _fadeSamples)
        {
            double t = n / (double)_fadeSamples;            // 0..1
            return t * t * (3.0 - 2.0 * t);                 // smoothstep
        }
        long remaining = _totalSamples - n;
        if (remaining < _fadeSamples)
        {
            double t = remaining / (double)_fadeSamples;    // 1..0
            return t * t * (3.0 - 2.0 * t);
        }
        return 1.0;
    }
}
