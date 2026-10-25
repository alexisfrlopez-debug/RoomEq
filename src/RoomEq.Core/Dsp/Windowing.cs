namespace RoomEq.Core.Dsp;

/// <summary>Types de fenêtre spectrale disponibles — spec I-DSP-02.</summary>
public enum WindowType
{
    Hann,
    BlackmanHarris,
    Rectangular
}

/// <summary>
/// Génération des fenêtres d'analyse spectrale (forms symétriques, i ∈ [0, n−1]).
/// </summary>
public static class Windowing
{
    /// <summary>Construit la fenêtre du type demandé.</summary>
    /// <exception cref="ArgumentException">n &lt; 1.</exception>
    public static double[] Create(WindowType type, int n)
    {
        if (n < 1)
            throw new ArgumentException("La taille de fenêtre doit être positive.", nameof(n));
        var w = new double[n];
        switch (type)
        {
            case WindowType.Rectangular:
                Array.Fill(w, 1.0);
                break;

            case WindowType.Hann:
                if (n == 1) { w[0] = 1.0; break; }
                for (int i = 0; i < n; i++)
                    w[i] = 0.5 * (1.0 - Math.Cos(2.0 * Math.PI * i / (n - 1)));
                break;

            case WindowType.BlackmanHarris:
                if (n == 1) { w[0] = 1.0; break; }
                for (int i = 0; i < n; i++)
                {
                    double t = 2.0 * Math.PI * i / (n - 1);
                    w[i] = 0.35875 - 0.48829 * Math.Cos(t)
                         + 0.14128 * Math.Cos(2.0 * t) - 0.01168 * Math.Cos(3.0 * t);
                }
                break;

            default:
                throw new ArgumentOutOfRangeException(nameof(type), type, "Type de fenêtre inconnu.");
        }
        return w;
    }

    /// <summary>Somme des coefficients (dénominateur de normalisation spectrale).</summary>
    public static double Sum(double[] w)
    {
        ArgumentNullException.ThrowIfNull(w);
        double s = 0;
        foreach (double v in w)
            s += v;
        return s;
    }
}
