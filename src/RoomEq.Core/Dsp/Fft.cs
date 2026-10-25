using System.Numerics;

namespace RoomEq.Core.Dsp;

/// <summary>
/// FFT radix-2 itérative (Cooley-Tukey, découplage bit-reversal) — spec I-DSP-01.
/// Traitement en place, aucune allocation, complexité n·log2(n).
/// </summary>
public static class Fft
{
    /// <summary>Vrai si n est une puissance de deux strictement positive.</summary>
    public static bool IsPowerOfTwo(int n) => n > 0 && (n & (n - 1)) == 0;

    /// <summary>Transformée de Fourier directe en place.</summary>
    /// <exception cref="ArgumentException">La taille n'est pas une puissance de 2.</exception>
    public static void Forward(Complex[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        int n = data.Length;
        if (!IsPowerOfTwo(n))
            throw new ArgumentException("La taille FFT doit être une puissance de 2.", nameof(data));

        // Réorganisation bit-reversal (algorithme de Brigham)
        for (int i = 1, j = 0; i < n; i++)
        {
            int bit = n >> 1;
            for (; (j & bit) != 0; bit >>= 1)
                j ^= bit;
            j ^= bit;
            if (i < j)
                (data[i], data[j]) = (data[j], data[i]);
        }

        // Papillons par étapes, facteurs de rotation recalculés par étape
        for (int len = 2; len <= n; len <<= 1)
        {
            double ang = -2.0 * Math.PI / len;
            double wr = Math.Cos(ang), wi = Math.Sin(ang);
            int half = len >> 1;
            for (int i = 0; i < n; i += len)
            {
                double cr = 1.0, ci = 0.0;
                for (int k = 0; k < half; k++)
                {
                    int a = i + k, b = a + half;
                    var da = data[a];
                    var db = data[b];
                    double vr = db.Real * cr - db.Imaginary * ci;
                    double vi = db.Real * ci + db.Imaginary * cr;
                    data[a] = new Complex(da.Real + vr, da.Imaginary + vi);
                    data[b] = new Complex(da.Real - vr, da.Imaginary - vi);
                    double ncr = cr * wr - ci * wi;
                    ci = cr * wi + ci * wr;
                    cr = ncr;
                }
            }
        }
    }

    /// <summary>Transformée de Fourier inverse en place (résultat divisé par n).</summary>
    /// <exception cref="ArgumentException">La taille n'est pas une puissance de 2.</exception>
    public static void Inverse(Complex[] data)
    {
        ArgumentNullException.ThrowIfNull(data);
        int n = data.Length;
        if (!IsPowerOfTwo(n))
            throw new ArgumentException("La taille FFT doit être une puissance de 2.", nameof(data));

        // conj -> FFT -> conj -> /n
        for (int i = 0; i < n; i++)
            data[i] = Complex.Conjugate(data[i]);
        Forward(data);
        double inv = 1.0 / n;
        for (int i = 0; i < n; i++)
            data[i] = Complex.Conjugate(data[i]) * inv;
    }

    /// <summary>Taille FFT (puissance de 2) minimale pour contenir n échantillons.</summary>
    /// <exception cref="ArgumentException">n &lt; 1.</exception>
    public static int NextPow2(int n)
    {
        if (n < 1)
            throw new ArgumentException("n doit être positif.", nameof(n));
        int p = 1;
        while (p < n)
            p <<= 1;
        return p;
    }

    /// <summary>
    /// Magnitude (linéaire, normalisée par la somme de la fenêtre) des n/2+1 premiers
    /// bins de la trame fenêtrée : un sinus d'amplitude A centré sur un bin donne une
    /// magnitude de A/2. La trame est complétée par des zéros si plus courte que la
    /// fenêtre.
    /// </summary>
    public static double[] MagnitudeSpectrum(float[] frame, double[] window, double windowSum)
    {
        ArgumentNullException.ThrowIfNull(frame);
        ArgumentNullException.ThrowIfNull(window);
        int n = window.Length;
        if (n == 0 || !IsPowerOfTwo(n))
            throw new ArgumentException("La fenêtre doit être de taille puissance de 2 non nulle.", nameof(window));

        var buf = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            float x = i < frame.Length ? frame[i] : 0f;
            buf[i] = new Complex(x * window[i], 0.0);
        }
        Forward(buf);

        int bins = n / 2 + 1;
        var mag = new double[bins];
        double inv = 1.0 / Math.Max(windowSum, 1e-12);
        for (int k = 0; k < bins; k++)
            mag[k] = buf[k].Magnitude * inv;
        return mag;
    }
}
