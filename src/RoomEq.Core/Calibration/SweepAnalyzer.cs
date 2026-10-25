using System.Numerics;
using RoomEq.Core.Dsp;

namespace RoomEq.Core.Calibration;

/// <summary>Résultat de l'analyse d'un balayage sinusoïdal — spec I-CAL-02.</summary>
public sealed class SweepAnalysis
{
    /// <summary>Fréquences centrales des bandes (grille 1/6 d'octave, croissantes).</summary>
    public double[] BandFreqs { get; init; } = Array.Empty<double>();

    /// <summary>Niveau mesuré par bande (dB, gain de transfert de la pièce).</summary>
    public double[] BandLevelsDb { get; init; } = Array.Empty<double>();

    /// <summary>Retard du pic de la réponse impulsionnelle (s) — diagnostic.</summary>
    public double PeakDelaySeconds { get; init; }

    /// <summary>Amplitude du pic de la réponse impulsionnelle — diagnostic.</summary>
    public double PeakMagnitude { get; init; }

    /// <summary>Durée de la fenêtre de réponse impulsionnelle analysée (s).</summary>
    public double IrWindowSeconds { get; init; }
}

/// <summary>
/// Analyse d'un balayage sinusoïdal logarithmique par déconvolution (méthode de
/// Farina, ESS) — spec I-CAL-02 : le signal capturé est divisé, dans le domaine
/// de Fourier, par la référence régénérée à l'identique ; la région utile (son
/// direct + réverbération courte) est fenêtrée — écartant aussi les harmoniques
/// de rang 2+ que l'ESS repousse après le pic — puis transformée en niveaux de
/// bandes 1/6 d'octave.
/// </summary>
public static class SweepAnalyzer
{
    /// <summary>Précision des bandes de sortie (1/6 d'octave).</summary>
    public const double FractionOctave = 1.0 / 6.0;

    /// <summary>
    /// Seuil de détection du balayage : rapport minimal pic/médiane de la réponse
    /// impulsionnelle. Amélioration v3 (décision D8) : la v2.2.2 utilisait 8, ce
    /// qui laissait passer une capture de bruit seul (ratio mesuré ~186 par effet
    /// différenciateur du 1/|X|²) et produisait des bandes absurdes au lieu du
    /// message d'aide. Un balayage réel, même faible, dépasse 1e5 (mesuré ~1,2e7) ;
    /// le seuil 1000 rejette le bruit seul avec quatre décades de marge.
    /// </summary>
    public const double DetectionSnr = 1000.0;

    /// <summary>Analyse un fragment capturé pendant un balayage.</summary>
    /// <param name="captured">Signal mono capturé.</param>
    /// <param name="sampleRate">Fréquence d'échantillonnage de la capture.</param>
    /// <param name="spec">Spécification du balayage émis.</param>
    /// <exception cref="ArgumentException">Fragment trop court ou fs trop basse.</exception>
    /// <exception cref="InvalidOperationException">Balayage non détecté ou trop peu de bandes.</exception>
    public static SweepAnalysis Analyze(float[] captured, int sampleRate, LogSweepSpec spec)
    {
        ArgumentNullException.ThrowIfNull(captured);
        ArgumentNullException.ThrowIfNull(spec);
        if (captured.Length < sampleRate / 2)
            throw new ArgumentException("Fragment trop court pour analyser le balayage.");
        if (sampleRate < 8000)
            throw new ArgumentException("Fréquence d'échantillonnage trop basse.");

        // 1) référence régénérée à l'identique (même taux que la capture)
        var gen = new LogSweepGenerator(spec, sampleRate);
        float[] reference = gen.GenerateFull(1.0);
        double effF2 = spec.EffectiveF2(sampleRate);
        double lnRatio = Math.Log(effF2 / spec.F1);

        int lenY = Math.Min(captured.Length, sampleRate * 40); // garde-fou mémoire
        int lenX = reference.Length;
        int fftN = Fft.NextPow2(lenY + lenX);                   // déconvolution linéaire sans repliement

        var X = new Complex[fftN];
        var Y = new Complex[fftN];
        for (int i = 0; i < lenX; i++)
            X[i] = new Complex(reference[i], 0.0);
        for (int i = 0; i < lenY; i++)
            Y[i] = new Complex(captured[i], 0.0);
        Fft.Forward(X);
        Fft.Forward(Y);

        // 2) H = Y·conj(X)/|X|²  (inverse régularisé hors bande du balayage)
        double maxX = 0;
        for (int k = 0; k < fftN; k++)
            maxX = Math.Max(maxX, X[k].Magnitude);
        double floor = Math.Max(maxX * maxX * 1e-8, 1e-24);
        for (int k = 0; k < fftN; k++)
        {
            double p = X[k].Magnitude * X[k].Magnitude;
            double scale = 1.0 / Math.Max(p, floor);
            Y[k] = new Complex(Y[k].Real * X[k].Real + Y[k].Imaginary * X[k].Imaginary,
                               Y[k].Imaginary * X[k].Real - Y[k].Real * X[k].Imaginary) * scale;
        }
        Fft.Inverse(Y);

        // 3) pic de la réponse impulsionnelle (retard positif ou légèrement négatif → replié)
        int searchEnd = Math.Min(fftN, (int)(1.2 * sampleRate));
        int searchStart = fftN - (int)(0.5 * sampleRate);
        int peak = 0;
        double peakMag = 0;
        for (int i = 0; i < fftN; i++)
        {
            bool inRange = i < searchEnd || i >= searchStart;
            if (!inRange)
                continue;
            double m = Y[i].Magnitude;
            if (m > peakMag)
            {
                peakMag = m;
                peak = i;
            }
        }

        // plancher de bruit : médiane des magnitudes de la zone de recherche
        var mags = new List<double>(Math.Max(1, searchEnd / 16));
        for (int i = 0; i < searchEnd; i += 16)
            mags.Add(Y[i].Magnitude);
        mags.Sort();
        double median = mags[mags.Count / 2];
        if (peakMag < Math.Max(median * DetectionSnr, 1e-9))
        {
            double snr = median > 0 ? peakMag / median : 0;
            throw new InvalidOperationException(
                $"Balayage non détecté dans la capture (rapport pic/bruit {snr:0.0} < {(int)DetectionSnr}, " +
                $"retard du meilleur candidat {peak / (double)sampleRate:0.000} s) : vérifiez le niveau de test, " +
                "le micro sélectionné (évitez les cartes virtuelles type « RoomEq Output ») et le routage.");
        }

        int peakWrapped = peak >= searchStart ? peak - fftN : peak; // retard signé (échantillons)

        // 4) fenêtre : son direct + réverbération courte ; les harmoniques de l'ESS
        //    arrivent après T·ln(k)/ln(F2/F1) — la fenêtre les évite.
        int pre = (int)(0.005 * sampleRate);
        double harmonic2 = spec.DurationSeconds * Math.Log(2.0) / lnRatio;
        double tailSec = Math.Min(0.40, Math.Max(0.10, 0.85 * harmonic2));
        int tail = (int)(tailSec * sampleRate);
        int winLen = pre + tail;

        int irFftN = Fft.NextPow2(winLen * 4);
        int peakPos = irFftN / 4;                   // le pic est posé au quart, gain de fenêtre = 1

        var ir = new Complex[irFftN];
        var win = BuildWindow(irFftN, peakPos, pre, tail);
        for (int i = 0; i < winLen; i++)
        {
            int src = (peakWrapped - pre + i) % fftN;               // extraction circulaire
            if (src < 0)
                src += fftN;
            int dst = peakPos - pre + i;
            ir[dst] = new Complex(Y[src].Real * win[dst], 0.0);      // fenêtre indexée en absolu
        }
        Fft.Forward(ir);

        // 5) spectre → niveaux de bandes 1/6 d'octave (gain de transfert)
        int bins = irFftN / 2 + 1;
        var binDb = new double[bins];
        var binFreqs = new double[bins];
        double binHz = (double)sampleRate / irFftN;
        for (int k = 0; k < bins; k++)
        {
            binFreqs[k] = k * binHz;
            binDb[k] = 20.0 * Math.Log10(ir[k].Magnitude + 1e-12);
        }

        SpectrumTools.OctaveLevels(binFreqs, binDb,
            spec.F1 * 1.05, effF2 * 0.97, FractionOctave,
            out var centers, out var levels);

        if (centers.Length < 8)
            throw new InvalidOperationException(
                "Balayage analysé mais trop peu de bandes exploitables. Augmentez la durée du balayage.");

        return new SweepAnalysis
        {
            BandFreqs = centers,
            BandLevelsDb = levels,
            PeakDelaySeconds = peakWrapped / (double)sampleRate,
            PeakMagnitude = peakMag,
            IrWindowSeconds = winLen / (double)sampleRate
        };
    }

    /// <summary>
    /// Fenêtre d'analyse de la réponse impulsionnelle : montée en demi-Hann de 5 ms
    /// avant le pic (gain 1 au pic), corps plat, descente en cosinus sur le dernier
    /// quart de la réverbération conservée.
    /// </summary>
    private static double[] BuildWindow(int n, int peakPos, int pre, int tail)
    {
        var w = new double[n];
        int start = peakPos - pre;
        int end = Math.Min(n - 1, peakPos + tail);
        int fallStart = peakPos + tail - Math.Max(1, tail / 4);

        for (int i = start; i <= end; i++)
        {
            if (i < 0)
                continue;
            if (i < peakPos)
            {
                double t = (i - start) / (double)Math.Max(1, pre);      // 0..1
                w[i] = 0.5 - 0.5 * Math.Cos(Math.PI * t);
            }
            else if (i <= fallStart)
            {
                w[i] = 1.0;
            }
            else
            {
                double t = (i - fallStart) / (double)Math.Max(1, end - fallStart); // 0..1
                w[i] = 0.5 + 0.5 * Math.Cos(Math.PI * t);
            }
        }
        return w;
    }
}
