namespace RoomEq.Core.Dsp;

/// <summary>Type de filtre supporté par une bande de l'égaliseur — spec I-DSP-03.</summary>
public enum FilterType
{
    Peaking,
    LowShelf,
    HighShelf,
    LowPass,
    HighPass,
    Notch
}

/// <summary>
/// Filtre biquad numériquement conforme aux formules du RBJ Audio EQ Cookbook
/// (spec I-DSP-03). Implémentation Transposed Direct Form II : meilleurs
/// comportements en virgule flottante que la forme directe I (erreurs d'arrondi
/// indépendantes des niveaux absolus). Chaque instance porte l'état d'UN canal.
/// </summary>
public sealed class BiquadFilter
{
    // Coefficients normalisés (a0 = 1)
    private double _b0, _b1, _b2, _a1, _a2;
    // État Transposed Direct Form II
    private double _z1, _z2;

    public FilterType Type { get; private set; } = FilterType.Peaking;
    public double Frequency { get; private set; } = 1000.0;
    public double GainDb { get; private set; }
    public double Q { get; private set; } = 1.0;
    public double SampleRate { get; private set; } = 48000.0;

    public BiquadFilter() { }

    public BiquadFilter(FilterType type, double fs, double f0, double gainDb, double q)
        => Configure(type, fs, f0, gainDb, q);

    /// <summary>Remet l'état temporel à zéro (les coefficients sont conservés).</summary>
    public void Reset() => _z1 = _z2 = 0.0;

    /// <summary>
    /// Reconfigure le filtre : coefficients RBJ recalculés, état temporel remis à
    /// zéro, bornes de sécurité appliquées (f0 ∈ [1 ; fs·0,499], Q ∈ [0,05 ; 50]).
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">fs ≤ 0.</exception>
    public void Configure(FilterType type, double fs, double f0, double gainDb, double q)
    {
        if (fs <= 0)
            throw new ArgumentOutOfRangeException(nameof(fs), "La fréquence d'échantillonnage doit être positive.");
        f0 = Math.Clamp(f0, 1.0, fs * 0.499);
        q = Math.Clamp(q, 0.05, 50.0);

        Type = type;
        SampleRate = fs;
        Frequency = f0;
        GainDb = gainDb;
        Q = q;

        double A = Math.Pow(10.0, gainDb / 40.0);
        double w0 = 2.0 * Math.PI * f0 / fs;
        double cw = Math.Cos(w0), sw = Math.Sin(w0);
        double alpha = sw / (2.0 * q);
        double twoSqrtAalpha = 2.0 * Math.Sqrt(A) * alpha;

        double b0, b1, b2, a0, a1, a2;
        switch (type)
        {
            case FilterType.Peaking:
                b0 = 1.0 + alpha * A;
                b1 = -2.0 * cw;
                b2 = 1.0 - alpha * A;
                a0 = 1.0 + alpha / A;
                a1 = -2.0 * cw;
                a2 = 1.0 - alpha / A;
                break;

            case FilterType.LowShelf:
                b0 = A * ((A + 1.0) - (A - 1.0) * cw + twoSqrtAalpha);
                b1 = 2.0 * A * ((A - 1.0) - (A + 1.0) * cw);
                b2 = A * ((A + 1.0) - (A - 1.0) * cw - twoSqrtAalpha);
                a0 = (A + 1.0) + (A - 1.0) * cw + twoSqrtAalpha;
                a1 = -2.0 * ((A - 1.0) + (A + 1.0) * cw);
                a2 = (A + 1.0) + (A - 1.0) * cw - twoSqrtAalpha;
                break;

            case FilterType.HighShelf:
                b0 = A * ((A + 1.0) + (A - 1.0) * cw + twoSqrtAalpha);
                b1 = -2.0 * A * ((A - 1.0) + (A + 1.0) * cw);
                b2 = A * ((A + 1.0) + (A - 1.0) * cw - twoSqrtAalpha);
                a0 = (A + 1.0) - (A - 1.0) * cw + twoSqrtAalpha;
                a1 = 2.0 * ((A - 1.0) - (A + 1.0) * cw);
                a2 = (A + 1.0) - (A - 1.0) * cw - twoSqrtAalpha;
                break;

            case FilterType.LowPass:
                b0 = (1.0 - cw) / 2.0;
                b1 = 1.0 - cw;
                b2 = (1.0 - cw) / 2.0;
                a0 = 1.0 + alpha;
                a1 = -2.0 * cw;
                a2 = 1.0 - alpha;
                break;

            case FilterType.HighPass:
                b0 = (1.0 + cw) / 2.0;
                b1 = -(1.0 + cw);
                b2 = (1.0 + cw) / 2.0;
                a0 = 1.0 + alpha;
                a1 = -2.0 * cw;
                a2 = 1.0 - alpha;
                break;

            case FilterType.Notch:
                b0 = 1.0;
                b1 = -2.0 * cw;
                b2 = 1.0;
                a0 = 1.0 + alpha;
                a1 = -2.0 * cw;
                a2 = 1.0 - alpha;
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Type de filtre inconnu.");
        }

        _b0 = b0 / a0;
        _b1 = b1 / a0;
        _b2 = b2 / a0;
        _a1 = a1 / a0;
        _a2 = a2 / a0;
        _z1 = _z2 = 0.0;
    }

    /// <summary>Filtre un échantillon (Transposed Direct Form II).</summary>
    public double Process(double x)
    {
        double y = _b0 * x + _z1;
        _z1 = _b1 * x - _a1 * y + _z2;
        _z2 = _b2 * x - _a2 * y;
        return y;
    }

    /// <summary>Filtre un flot d'échantillons en place — équivalent à autant d'appels
    /// à <see cref="Process(double)"/> (utilisable en flux, sans allocation).</summary>
    public void Process(Span<float> block)
    {
        for (int i = 0; i < block.Length; i++)
            block[i] = (float)Process(block[i]);
    }

    /// <summary>Module de la réponse fréquentielle |H(e^{jw})| à la fréquence f
    /// pour les coefficients courants (évaluation directe sur le cercle unité).</summary>
    public double MagnitudeAt(double f)
    {
        double w = 2.0 * Math.PI * f / SampleRate;
        double cw = Math.Cos(w), sw = Math.Sin(w);
        // H(e^{jw}) = (b0 + b1·z⁻¹ + b2·z⁻²) / (1 + a1·z⁻¹ + a2·z⁻²)
        double nr = _b0 + _b1 * cw + _b2 * Math.Cos(2.0 * w);
        double ni = -(_b1 * sw + _b2 * Math.Sin(2.0 * w));
        double dr = 1.0 + _a1 * cw + _a2 * Math.Cos(2.0 * w);
        double di = -(_a1 * sw + _a2 * Math.Sin(2.0 * w));
        double num2 = nr * nr + ni * ni;
        double den2 = dr * dr + di * di;
        return Math.Sqrt(num2 / Math.Max(den2, 1e-30));
    }

    /// <summary>Réponse en dB (20·log10|H|) à la fréquence f.</summary>
    public double ResponseDb(double f) => 20.0 * Math.Log10(Math.Max(MagnitudeAt(f), 1e-12));
}
