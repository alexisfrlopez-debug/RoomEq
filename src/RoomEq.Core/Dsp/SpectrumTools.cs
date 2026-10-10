using System.Numerics;

namespace RoomEq.Core.Dsp;

/// <summary>Un point de courbe (fréquence, dB).</summary>
public readonly record struct CurvePoint(double Freq, double Db);

/// <summary>
/// Outils d'analyse spectrale : moyenne de Welch, conversion en bandes d'octave
/// fractionnaires, lissage en domaine logarithmique.
/// </summary>
public static class SpectrumTools
{
    /// <summary>Résolution de bandes standard de l'application : 1/6 d'octave.</summary>
    public const double SixthOctave = 1.0 / 6.0;

    /// <summary>
    /// Moyenne de Welch : spectrogramme en puissance moyenné sur des trames fenêtrées se chevauchant.
    /// Renvoie le spectre en dB d'amplitude (un sinus d'amplitude A lu à son bin vaut 20·log10(A)).
    /// </summary>
    public static double[] WelchDb(
        float[] samples, int sampleRate, int fftSize,
        out double[] binFreqs, WindowType window = WindowType.Hann, double overlap = 0.5)
    {
        if (!Fft.IsPowerOfTwo(fftSize)) throw new ArgumentException("fftSize doit être une puissance de 2.");
        if (samples.Length < fftSize / 4)
            throw new ArgumentException("Pas assez d'échantillons pour l'analyse.");

        var win = Windowing.Create(window, fftSize);
        double winSum = Windowing.Sum(win);

        int hop = Math.Max(1, (int)(fftSize * (1.0 - overlap)));
        int bins = fftSize / 2 + 1;

        // Puissance moyenne par bin
        var accPower = new double[bins];
        int frames = 0;
        var frame = new float[fftSize];

        for (int start = 0; start + fftSize <= samples.Length; start += hop)
        {
            Array.Copy(samples, start, frame, 0, fftSize);
            // amplitude d'un sinus A -> |X| = A/2 ; on moyenne |X|^2 puis on repasse en amplitude
            var mag = Fft.MagnitudeSpectrum(frame, win, winSum);
            for (int k = 0; k < bins; k++)
                accPower[k] += mag[k] * mag[k];
            frames++;
        }

        var db = new double[bins];
        binFreqs = new double[bins];
        double binHz = (double)sampleRate / fftSize;
        for (int k = 0; k < bins; k++)
        {
            binFreqs[k] = k * binHz;
            double amp = 2.0 * Math.Sqrt(accPower[k] / frames); // amplitude équivalente
            db[k] = 20.0 * Math.Log10(amp + 1e-12);
        }
        return db;
    }

    /// <summary>
    /// Agrège un spectre fin en niveaux par bandes d'octave fractionnaires (moyenne énergétique).
    /// </summary>
    public static void OctaveLevels(
        double[] binFreqs, double[] binDb,
        double fMin, double fMax, double fractionOctave,
        out double[] centers, out double[] levelsDb)
    {
        var centerList = new List<double>();
        var levelList = new List<double>();

        double f = fMin;
        double halfRatio = Math.Pow(2.0, fractionOctave / 2.0);

        while (f <= fMax * 1.0001)
        {
            double lo = f / halfRatio, hi = f * halfRatio;
            double sum = 0; int n = 0;
            for (int k = 0; k < binFreqs.Length; k++)
            {
                double bf = binFreqs[k];
                if (bf < lo || bf > hi) continue;
                sum += Math.Pow(10.0, binDb[k] / 10.0);
                n++;
            }
            if (n > 0)
            {
                centerList.Add(f);
                levelList.Add(10.0 * Math.Log10(sum / n + 1e-12));
            }
            f *= Math.Pow(2.0, fractionOctave);
        }

        centers = centerList.ToArray();
        levelsDb = levelList.ToArray();
    }

    /// <summary>
    /// Lissage fractionnaire d'octave : moyenne glissante rectangulaire dans le domaine log2(f).
    /// </summary>
    public static double[] FractionalOctaveSmooth(double[] freqs, double[] valuesDb, double fractionOctave)
    {
        int n = freqs.Length;
        if (n == 0 || fractionOctave <= 0) return (double[])valuesDb.Clone();

        var logF = new double[n];
        for (int i = 0; i < n; i++) logF[i] = Math.Log2(Math.Max(freqs[i], 1e-3));

        var result = new double[n];
        double half = fractionOctave / 2.0;

        int j0 = 0, j1 = 0;
        for (int i = 0; i < n; i++)
        {
            // fenêtre glissante à bornes monotones -> coût O(n) global
            while (j0 < n && logF[j0] < logF[i] - half) j0++;
            if (j1 < i) j1 = i;
            while (j1 + 1 < n && logF[j1 + 1] <= logF[i] + half) j1++;

            double sum = 0; int cnt = 0;
            for (int j = j0; j <= j1; j++) { sum += valuesDb[j]; cnt++; }
            result[i] = cnt > 0 ? sum / cnt : valuesDb[i];
        }
        return result;
    }

    /// <summary>Moyenne (en dB) d'une courbe sur une plage de fréquences.</summary>
    public static double MeanDb(IReadOnlyList<double> freqs, IReadOnlyList<double> values, double fMin, double fMax)
    {
        double sum = 0; int n = 0;
        for (int i = 0; i < freqs.Count; i++)
        {
            if (freqs[i] < fMin || freqs[i] > fMax) continue;
            sum += values[i]; n++;
        }
        return n > 0 ? sum / n : 0.0;
    }

    /// <summary>Interpolation linéaire en log-fréquence sur une courbe triée croissante.</summary>
    public static double InterpLog(IReadOnlyList<double> freqs, IReadOnlyList<double> values, double f)
    {
        int n = freqs.Count;
        if (n == 0) return 0.0;
        if (f <= freqs[0]) return values[0];
        if (f >= freqs[n - 1]) return values[n - 1];

        int lo = 0, hi = n - 1;
        while (hi - lo > 1)
        {
            int mid = (lo + hi) >> 1;
            if (freqs[mid] <= f) lo = mid; else hi = mid;
        }
        double t = (Math.Log(f) - Math.Log(freqs[lo])) / (Math.Log(freqs[hi]) - Math.Log(freqs[lo]));
        return values[lo] + t * (values[hi] - values[lo]);
    }

    /// <summary>Niveau crête d'un fragment (dBFS, 0 = pleine échelle). Utile pour
    /// détecter une capture muette (micro mal choisi, muet, débranché).</summary>
    public static double PeakDb(ReadOnlySpan<float> samples)
    {
        float peak = 0;
        foreach (float s in samples)
        {
            float a = Math.Abs(s);
            if (a > peak) peak = a;
        }
        return 20.0 * Math.Log10(peak + 1e-12);
    }

    /// <summary>Niveau efficace (RMS) d'un fragment en dBFS.</summary>
    public static double RmsDb(ReadOnlySpan<float> samples)
    {
        if (samples.Length == 0) return -240.0;
        double acc = 0;
        foreach (float s in samples) acc += (double)s * s;
        double rms = Math.Sqrt(acc / samples.Length);
        return 20.0 * Math.Log10(rms + 1e-12);
    }
}
