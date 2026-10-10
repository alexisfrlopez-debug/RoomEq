using System.Numerics;

namespace RoomEq.Core.Dsp;

/// <summary>Type de filtre supporté par une bande de l'égaliseur.</summary>
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
/// Filtre biquad numériquement équivalent aux formules du RBJ Audio EQ Cookbook.
/// Chaque instance porte l'état d'UN canal : utiliser une instance par canal actif.
/// </summary>
public sealed class BiquadFilter
{
    // Coefficients normalisés (a0 = 1)
    private double _b0, _b1, _b2, _a1, _a2;
    // État Direct Form I
    private double _x1, _x2, _y1, _y2;

    public FilterType Type { get; private set; } = FilterType.Peaking;
    public double Frequency { get; private set; } = 1000.0;
    public double GainDb { get; private set; }
    public double Q { get; private set; } = 1.0;
    public double SampleRate { get; private set; } = 48000.0;

    public BiquadFilter() { }

    public BiquadFilter(FilterType type, double fs, double f0, double gainDb, double q)
        => Configure(type, fs, f0, gainDb, q);

    public void Reset() => _x1 = _x2 = _y1 = _y2 = 0.0;

    /// <summary>Reconfigure le filtre (les états temporels sont remis à zéro).</summary>
    public void Configure(FilterType type, double fs, double f0, double gainDb, double q)
    {
        if (fs <= 0) throw new ArgumentOutOfRangeException(nameof(fs));
        f0 = Math.Clamp(f0, 1.0, fs * 0.499);
        q = Math.Clamp(q, 0.05, 50.0);

        Type = type; SampleRate = fs; Frequency = f0; GainDb = gainDb; Q = q;

        double A = Math.Pow(10.0, gainDb / 40.0);
        double w0 = 2.0 * Math.PI * f0 / fs;
        double cw = Math.Cos(w0), sw = Math.Sin(w0);
        double alpha = sw / (2.0 * q);
        double sqrtAalpha = 2.0 * Math.Sqrt(A) * alpha;

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
                b0 = A * ((A + 1.0) - (A - 1.0) * cw + sqrtAalpha);
                b1 = 2.0 * A * ((A - 1.0) - (A + 1.0) * cw);
                b2 = A * ((A + 1.0) - (A - 1.0) * cw - sqrtAalpha);
                a0 = (A + 1.0) + (A - 1.0) * cw + sqrtAalpha;
                a1 = -2.0 * ((A - 1.0) + (A + 1.0) * cw);
                a2 = (A + 1.0) + (A - 1.0) * cw - sqrtAalpha;
                break;

            case FilterType.HighShelf:
                b0 = A * ((A + 1.0) + (A - 1.0) * cw + sqrtAalpha);
                b1 = -2.0 * A * ((A - 1.0) + (A + 1.0) * cw);
                b2 = A * ((A + 1.0) + (A - 1.0) * cw - sqrtAalpha);
                a0 = (A + 1.0) - (A - 1.0) * cw + sqrtAalpha;
                a1 = 2.0 * ((A - 1.0) - (A + 1.0) * cw);
                a2 = (A + 1.0) - (A - 1.0) * cw - sqrtAalpha;
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
                throw new ArgumentOutOfRangeException(nameof(type));
        }

        _b0 = b0 / a0; _b1 = b1 / a0; _b2 = b2 / a0;
        _a1 = a1 / a0; _a2 = a2 / a0;
        Reset();
    }

    /// <summary>Traite un échantillon (Direct Form I, double précision).</summary>
    public double ProcessSample(double x)
    {
        double y = _b0 * x + _b1 * _x1 + _b2 * _x2 - _a1 * _y1 - _a2 * _y2;
        _x2 = _x1; _x1 = x;
        _y2 = _y1; _y1 = y;
        return y;
    }

    /// <summary>Réponse fréquentielle complexe H(e^jw) du filtre.</summary>
    public Complex ResponseAt(double freq, double fs)
    {
        double w = 2.0 * Math.PI * freq / fs;
        double zr = Math.Cos(w), zi = -Math.Sin(w);            // e^-jw
        double z2r = Math.Cos(2 * w), z2i = -Math.Sin(2 * w);  // e^-2jw

        double nr = _b0 + _b1 * zr + _b2 * z2r;
        double ni = _b1 * zi + _b2 * z2i;
        double dr = 1.0 + _a1 * zr + _a2 * z2r;
        double di = _a1 * zi + _a2 * z2i;

        double den = dr * dr + di * di;
        if (den < 1e-30) den = 1e-30;
        double rr = (nr * dr + ni * di) / den;
        double ri = (ni * dr - nr * di) / den;
        return new Complex(rr, ri);
    }

    /// <summary>Réponse fréquentielle en dB à la fréquence donnée.</summary>
    public double ResponseDb(double freq, double fs)
        => 20.0 * Math.Log10(ResponseAt(freq, fs).Magnitude + 1e-30);
}
