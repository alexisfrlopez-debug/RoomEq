using RoomEq.Core.Dsp;

namespace RoomEq.Core.Calibration;

/// <summary>Courbe mesurée pour UN signal de test (spectre Welch complet) — spec I-CAL-04.</summary>
public sealed class SignalCurve
{
    public string Name { get; set; } = "";
    public MeasurementSignalKind Kind { get; set; }
    public double[] Freqs { get; set; } = Array.Empty<double>();
    public double[] Db { get; set; } = Array.Empty<double>();
}

/// <summary>Niveau mesuré pour UN point sinusoïdal à une fréquence connue.</summary>
public sealed class SinePoint
{
    public double Freq { get; set; }
    public double LevelDb { get; set; }

    public SinePoint() { }

    public SinePoint(double freq, double levelDb)
    {
        Freq = freq;
        LevelDb = levelDb;
    }
}

/// <summary>Déviation alignée d'un signal (affichage des courbes individuelles).</summary>
public sealed class SignalDeviation
{
    public string Name { get; set; } = "";
    public MeasurementSignalKind Kind { get; set; }
    public List<CurvePoint> Points { get; set; } = new();
}

/// <summary>Résultat du croisement des mesures multi-signaux.</summary>
public sealed class AggregateResult
{
    /// <summary>Grille de fréquences (bandes 1/6 d'octave, croissante).</summary>
    public double[] Freqs { get; set; } = Array.Empty<double>();

    /// <summary>Réponse moyenne (médiane inter-signaux) alignée sur la moyenne de la cible.</summary>
    public double[] AlignedDb { get; set; } = Array.Empty<double>();

    /// <summary>Dispersion inter-signaux par bande (dB) — fiabilité de la donnée.</summary>
    public double[] StdDb { get; set; } = Array.Empty<double>();

    /// <summary>Courbes individuelles alignées (affichage).</summary>
    public List<SignalDeviation> PerSignal { get; set; } = new();

    public List<CurvePoint> ToCurve()
    {
        var pts = new List<CurvePoint>(Freqs.Length);
        for (int i = 0; i < Freqs.Length; i++)
            pts.Add(new CurvePoint(Freqs[i], AlignedDb[i]));
        return pts;
    }
}

/// <summary>
/// Croisement des mesures multi-signaux (spec I-CAL-04) : chaque signal donne
/// une estimation indépendante de la réponse de la pièce ; après retrait des
/// pentes théoriques et alignement de niveau, la MÉDIANE inter-signaux fournit
/// une réponse robuste (un signal aberrant n'influence pas le résultat) et
/// l'écart-type quantifie la fiabilité de chaque bande.
/// </summary>
public static class MultiSignalAnalysis
{
    /// <summary>
    /// Niveau (dB) d'un sinus pur dans un fragment capturé : maximum du spectre
    /// Welch dans une fenêtre de ±3 % autour de la fréquence attendue.
    /// </summary>
    /// <exception cref="ArgumentException">Moins de 8192 échantillons.</exception>
    public static (double Freq, double LevelDb) SineLevelDb(float[] samples, int sampleRate, double freq)
    {
        ArgumentNullException.ThrowIfNull(samples);
        if (samples.Length < 8192)
            throw new ArgumentException("Fragment trop court pour l'analyse du sinus.");
        var db = SpectrumTools.WelchDb(samples, sampleRate, 8192, out var bins);
        double lo = freq * 0.97, hi = freq * 1.03;
        double best = double.NegativeInfinity;
        double bestF = freq;
        for (int k = 0; k < bins.Length; k++)
        {
            if (bins[k] < lo || bins[k] > hi)
                continue;
            if (db[k] > best)
            {
                best = db[k];
                bestF = bins[k];
            }
        }
        return (bestF, double.IsNegativeInfinity(best) ? -120.0 : best);
    }

    /// <summary>
    /// Croise toutes les mesures et produit la réponse agrégée (médiane) avec sa
    /// dispersion ; les sinus fournissent des points supplémentaires.
    /// </summary>
    /// <exception cref="ArgumentException">Aucune mesure à croiser.</exception>
    /// <exception cref="InvalidOperationException">Pas assez de bandes couvertes.</exception>
    public static AggregateResult Aggregate(
        IReadOnlyList<SignalCurve> curves,
        IReadOnlyList<SinePoint> sines,
        CorrectionOptions opt,
        MicProfile? micProfile,
        double fractionOctave = 1.0 / 6.0)
    {
        ArgumentNullException.ThrowIfNull(curves);
        ArgumentNullException.ThrowIfNull(sines);
        ArgumentNullException.ThrowIfNull(opt);
        if (curves.Count == 0 && sines.Count == 0)
            throw new ArgumentException("Aucune mesure à croiser.");

        // 1) grille commune de bandes de 20 Hz à 20 kHz
        var centers = new List<double>();
        for (double f = 20.0; f <= 20000.0 * 1.0001; f *= Math.Pow(2.0, fractionOctave))
            centers.Add(f);
        int nBands = centers.Count;

        var estimates = new List<double>[nBands];
        for (int i = 0; i < nBands; i++)
            estimates[i] = new List<double>();
        var perSignal = new List<SignalDeviation>();

        // 2) courbes de bruit : correction micro → bandes → retrait de pente → alignement
        foreach (var c in curves)
        {
            if (c.Freqs.Length < 32)
                continue;

            var corrected = new double[c.Freqs.Length];
            for (int i = 0; i < c.Freqs.Length; i++)
            {
                double mic = micProfile is { Enabled: true, IsValid: true } ? micProfile.GainAt(c.Freqs[i]) : 0.0;
                corrected[i] = c.Db[i] - mic;
            }

            SpectrumTools.OctaveLevels(c.Freqs, corrected, 20.0, 20000.0, fractionOctave,
                out var bandCenters, out var bandLevels);
            if (bandCenters.Length == 0)
                continue;

            double slope = MeasurementSignalCatalog.ExpectedBandSlopeDbPerOct(c.Kind);
            var dev = new double[bandCenters.Length];
            for (int i = 0; i < bandCenters.Length; i++)
                dev[i] = bandLevels[i] - slope * Math.Log2(bandCenters[i] / 1000.0);

            double offset = SpectrumTools.MeanDb(bandCenters, dev, opt.AlignMinHz, opt.AlignMaxHz);
            var pts = new List<CurvePoint>(bandCenters.Length);
            for (int i = 0; i < bandCenters.Length; i++)
            {
                dev[i] -= offset;
                pts.Add(new CurvePoint(bandCenters[i], dev[i]));
                AddToBand(estimates, centers, fractionOctave, bandCenters[i], dev[i]);
            }

            perSignal.Add(new SignalDeviation { Name = c.Name, Kind = c.Kind, Points = pts });
        }

        // 3) sinus : déviations = niveau − moyenne sur la plage d'alignement
        if (sines.Count > 0)
        {
            var inRange = sines.Where(s => s.Freq >= opt.AlignMinHz && s.Freq <= opt.AlignMaxHz).ToList();
            if (inRange.Count >= 2)
            {
                double mean = inRange.Average(s => s.LevelDb);
                var pts = new List<CurvePoint>(sines.Count);
                foreach (var s in sines)
                {
                    double d = s.LevelDb - mean;
                    pts.Add(new CurvePoint(s.Freq, d));
                    AddToBand(estimates, centers, fractionOctave, s.Freq, d);
                }
                perSignal.Add(new SignalDeviation
                {
                    Name = MeasurementSignalCatalog.DisplayName(MeasurementSignalKind.FrequencySweep),
                    Kind = MeasurementSignalKind.FrequencySweep,
                    Points = pts
                });
            }
        }

        // 4) médiane + dispersion par bande (bandes sans donnée écartées)
        var fOut = new List<double>();
        var aggOut = new List<double>();
        var stdOut = new List<double>();
        for (int i = 0; i < nBands; i++)
        {
            if (estimates[i].Count == 0)
                continue;
            fOut.Add(centers[i]);
            aggOut.Add(Median(estimates[i]));
            stdOut.Add(StdDev(estimates[i]));
        }

        if (fOut.Count < 8)
            throw new InvalidOperationException(
                "Croisement impossible : pas assez de bandes couvertes. Vérifiez la durée des mesures et le niveau du micro.");

        var freqs = fOut.ToArray();

        // 5) alignement final sur la moyenne de la cible (comme une mesure unique)
        var target = new double[freqs.Length];
        for (int i = 0; i < freqs.Length; i++)
            target[i] = TargetCurve.EvaluateDb(freqs[i], opt.Target, opt.ShelfDb, opt.TiltDbPerOct);

        double meanAgg = SpectrumTools.MeanDb(freqs, aggOut, opt.AlignMinHz, opt.AlignMaxHz);
        double meanTarget = SpectrumTools.MeanDb(freqs, target, opt.AlignMinHz, opt.AlignMaxHz);
        double shift = meanAgg - meanTarget;

        var aligned = new double[freqs.Length];
        var std = new double[freqs.Length];
        for (int i = 0; i < freqs.Length; i++)
        {
            aligned[i] = aggOut[i] - shift;
            std[i] = stdOut[i];
        }

        return new AggregateResult
        {
            Freqs = freqs,
            AlignedDb = aligned,
            StdDb = std,
            PerSignal = perSignal
        };
    }

    /// <summary>Poids de confiance (0,35..1) dérivé de la dispersion inter-signaux :
    /// les bandes où les signaux divergent fortement reçoivent une correction dosée
    /// prudemment.</summary>
    public static Func<double, double> ConfidenceWeight(double[] freqs, double[] stdDb)
    {
        ArgumentNullException.ThrowIfNull(freqs);
        ArgumentNullException.ThrowIfNull(stdDb);
        var std = (double[])stdDb.Clone();
        return f =>
        {
            double s = SpectrumTools.InterpLog(freqs, std, f);
            double t = (s - 1.5) / 4.5;             // 0 à 1,5 dB d'accord ; 1 à 6 dB de désaccord
            t = Math.Clamp(t, 0.0, 1.0);
            return 1.0 - 0.65 * t;                  // 1,0 → 0,35
        };
    }

    private static void AddToBand(List<double>[] estimates, List<double> centers, double fractionOctave, double freq, double dev)
    {
        double halfRatio = Math.Pow(2.0, fractionOctave / 2.0);
        for (int i = 0; i < centers.Count; i++)
        {
            if (freq >= centers[i] / halfRatio && freq <= centers[i] * halfRatio)
            {
                estimates[i].Add(dev);
                return;
            }
        }
    }

    internal static double Median(IReadOnlyList<double> values)
    {
        var sorted = values.OrderBy(v => v).ToList();
        int n = sorted.Count;
        return n == 0 ? 0.0 : (n % 2 == 1 ? sorted[n / 2] : 0.5 * (sorted[n / 2 - 1] + sorted[n / 2]));
    }

    internal static double StdDev(IReadOnlyList<double> values)
    {
        if (values.Count < 2)
            return 0.0;
        double mean = 0;
        foreach (var v in values)
            mean += v;
        mean /= values.Count;
        double acc = 0;
        foreach (var v in values)
            acc += (v - mean) * (v - mean);
        return Math.Sqrt(acc / (values.Count - 1));
    }
}
