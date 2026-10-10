using RoomEq.Core.Dsp;

namespace RoomEq.Core.Calibration;

/// <summary>Options du planificateur automatique de bandes.</summary>
public sealed class AutoBandOptions
{
    public double FreqMin { get; set; } = 40.0;
    public double FreqMax { get; set; } = 16000.0;

    /// <summary>Boost maximal par filtre (sécurité des haut-parleurs).</summary>
    public double MaxBoostDb { get; set; } = 6.0;

    /// <summary>Coupe maximale par filtre.</summary>
    public double MaxCutDb { get; set; } = 12.0;

    /// <summary>Correcton plus faible que ce seuil : filtre non créé.</summary>
    public double ThresholdDb { get; set; } = 0.75;

    /// <summary>Nombre maximal de filtres placés.</summary>
    public int MaxFilters { get; set; } = 20;

    /// <summary>Arrêt dès que le résidu RMS passe sous ce niveau (dB).</summary>
    public double ResidualTargetRmsDb { get; set; } = 1.0;

    public double MinQ { get; set; } = 0.7;
    public double MaxQ { get; set; } = 8.0;
    public int SampleRate { get; set; } = 48000;

    /// <summary>
    /// Espacement minimal absolu entre deux centres de bandes (en octaves).
    /// Garantit l'absence de doublons : aucun filtre n'est placé dans la zone
    /// déjà occupée par un filtre existant.
    /// </summary>
    public double MinSpacingOctaves { get; set; } = 1.0 / 12.0;

    /// <summary>
    /// Fraction de la largeur de bande d'un filtre existant déclarée « occupée »
    /// (0,6 = deux filtres de même Q doivent être séparés d'au moins 60 % de leur
    /// largeur — leurs zones utiles ne se recouvrent pas).
    /// </summary>
    public double SpacingFactor { get; set; } = 0.6;

    /// <summary>Dérive les options depuis les options de correction générales.</summary>
    public static AutoBandOptions From(CorrectionOptions opt, int sampleRate) => new()
    {
        FreqMin = opt.FreqMin,
        FreqMax = opt.FreqMax,
        MaxBoostDb = opt.MaxBoostDb,
        MaxCutDb = opt.MaxCutDb,
        ThresholdDb = opt.ThresholdDb,
        SampleRate = sampleRate
    };
}

/// <summary>Un filtre placé par le planificateur.</summary>
public sealed class PlannedFilter
{
    public double Frequency { get; set; }
    public double GainDb { get; set; }
    public double Q { get; set; }
}

/// <summary>Résultat du placement automatique.</summary>
public sealed class AutoBandResult
{
    public List<PlannedFilter> Filters { get; } = new();

    /// <summary>Nombre de filtres retirés par la passe finale de déduplication (diagnostic).</summary>
    public int DuplicatesRemoved { get; set; }
    /// <summary>Réponse cumulée des filtres sur la grille d'entrée.</summary>
    public double[] PredictedDb { get; set; } = Array.Empty<double>();
    /// <summary>Résidu (déviation - prédiction) sur la grille d'entrée.</summary>
    public double[] ResidualDb { get; set; } = Array.Empty<double>();
    public double ResidualRmsDb { get; set; }
    public double ResidualMaxDb { get; set; }
    /// <summary>Raison de l'arrêt du placement (diagnostic).</summary>
    public string StopReason { get; set; } = "";
}

/// <summary>
/// Placement automatique des filtres peaking : algorithme glouton — à chaque itération,
/// le plus grand écart résiduel (cible - mesuré) est annulé par un filtre peaking dont
/// la fréquence, le gain ET la largeur (Q) sont déduits de la largeur mesurée de
/// l'anomalie. La simulation utilise la VRAIE réponse des biquads : le résidu converge
/// vers la courbe cible avec un nombre de bandes minimal. Le nombre de bandes, leurs
/// fréquences et leurs largeurs sont donc choisis automatiquement pour la courbe
/// optimale — aucun réglage manuel.
/// ANTI-DOUBLONS : après chaque placement, une zone d'exclusion (déduite de la largeur
/// de bande du filtre, bornée par MinSpacingOctaves) est marquée autour de son centre ;
/// les itérations suivantes cherchent le pic résiduel HORS des zones occupées, si bien
/// qu'aucune bande ne peut être créée deux fois à la même fréquence. Une passe finale
/// de déduplication garantit l'invariant quel que soit le déroulement.
/// </summary>
public static class AutoBandPlanner
{
    /// <summary>Largeur de bande d'un filtre peaking (en octaves) pour un Q donné.</summary>
    public static double BandwidthOctaves(double q)
    {
        q = Math.Max(q, 0.5);
        return Math.Log2((2.0 * q + 1.0) / (2.0 * q - 1.0));
    }

    /// <summary>Demi-zone d'exclusion (octaves) autour du centre d'un filtre placé.</summary>
    internal static double ExclusionHalfOctaves(double q, AutoBandOptions opt)
        => Math.Max(opt.MinSpacingOctaves, opt.SpacingFactor * BandwidthOctaves(q)) / 2.0;

    public static AutoBandResult Plan(
        double[] freqs, double[] deviationDb, AutoBandOptions opt,
        Func<double, double>? confidenceWeight = null)
    {
        if (freqs.Length != deviationDb.Length || freqs.Length < 8)
            throw new ArgumentException("Courbe de déviation invalide.");
        if (opt.FreqMin >= opt.FreqMax)
            throw new ArgumentException("Plage de correction invalide.");

        int n = freqs.Length;
        var predicted = new double[n];
        var residual = (double[])deviationDb.Clone();
        var placed = new List<PlannedFilter>();
        var result = new AutoBandResult();
        var occupied = new bool[n]; // zones d'exclusion anti-doublons

        int lo = LowerIndex(freqs, opt.FreqMin);
        int hi = UpperIndex(freqs, opt.FreqMax);
        if (hi <= lo)
            throw new ArgumentException("Plage de correction hors de la courbe fournie.");

        double Rms()
        {
            double s = 0;
            for (int i = lo; i <= hi; i++) s += residual[i] * residual[i];
            return Math.Sqrt(s / (hi - lo + 1));
        }

        int placedCount = 0;
        int maskBudget = n; // chaque masquage consomme une itération (terminaison garantie)
        while (placedCount < opt.MaxFilters && maskBudget-- > 0)
        {
            // 1) plus grand écart résiduel HORS des zones déjà occupées par un filtre
            //    placé (anti-doublons : jamais deux bandes à la même fréquence)
            int peak = -1;
            double peakVal = 0;
            for (int i = lo; i <= hi; i++)
            {
                if (occupied[i]) continue;
                if (Math.Abs(residual[i]) > Math.Abs(peakVal))
                {
                    peakVal = residual[i];
                    peak = i;
                }
            }

            if (peak < 0)
            {
                result.StopReason = "Toutes les anomalies restantes sont couvertes par des bandes existantes.";
                break;
            }
            if (Math.Abs(peakVal) < opt.ThresholdDb)
            {
                result.StopReason = "Résidu sous le seuil de correction.";
                break;
            }

            // 2) largeur de l'anomalie : croisement à mi-hauteur → Q
            double half = peakVal / 2.0;
            int li = peak, ri = peak;
            int maxSpan = 0;
            while (li > lo && SameSignOrPastHalf(residual[li - 1], half, peakVal) && maxSpan++ < 8 * n) li--;
            while (ri < hi - 1 && SameSignOrPastHalf(residual[ri + 1], half, peakVal) && maxSpan++ < 8 * n) ri++;

            double f0 = freqs[peak];
            double fLo = freqs[li], fHi = freqs[ri];
            double q = (fHi > fLo * 1.0001)
                ? Math.Sqrt(fLo * fHi) / (fHi - fLo)
                : 2.0;
            q = Math.Clamp(q, opt.MinQ, opt.MaxQ);

            // 3) gain = écart résiduel, borné, dosé par la confiance inter-signaux
            double gain = Math.Clamp(peakVal, -opt.MaxCutDb, opt.MaxBoostDb);
            if (confidenceWeight != null)
                gain = Math.Clamp(gain * confidenceWeight(f0), -opt.MaxCutDb, opt.MaxBoostDb);
            if (Math.Abs(gain) < opt.ThresholdDb)
            {
                result.StopReason = "Gain résiduel sous le seuil après dosage.";
                break;
            }

            // 3bis) règle anti-doublon COMPLÈTE : les demi-zones d'exclusion des deux
            //    filtres ne doivent pas se chevaucher. Si le nouveau filtre (dont le Q
            //    vient d'être déduit) violerait l'espacement, son pic est masqué et la
            //    recherche reprend ailleurs — aucun doublon ne peut être créé.
            if (ConflictsWithExisting(f0, q, placed, opt))
            {
                occupied[peak] = true;
                continue;
            }

            // 4) placement + zone d'exclusion + mise à jour du résidu avec la vraie réponse du biquad
            var planned = new PlannedFilter { Frequency = f0, GainDb = gain, Q = q };
            placed.Add(planned);
            placedCount++;
            MarkOccupied(occupied, freqs, planned, opt);

            var biq = new BiquadFilter(FilterType.Peaking, opt.SampleRate, f0, gain, q);
            for (int i = 0; i < n; i++)
            {
                predicted[i] += biq.ResponseDb(freqs[i], opt.SampleRate);
                residual[i] = deviationDb[i] - predicted[i];
            }

            result.ResidualRmsDb = Rms();
            if (result.ResidualRmsDb <= opt.ResidualTargetRmsDb)
            {
                // attention : un RMS global faible peut masquer une anomalie étroite
                // encore importante — on ne s'arrête que si le résidu MAX est aussi
                // retombé près du seuil
                double maxAbs = 0;
                for (int i = lo; i <= hi; i++) maxAbs = Math.Max(maxAbs, Math.Abs(residual[i]));
                if (maxAbs < 2.0)
                {
                    result.StopReason = "Résidu RMS cible atteint.";
                    break;
                }
            }
        }

        if (placedCount >= opt.MaxFilters && result.ResidualRmsDb > opt.ResidualTargetRmsDb)
            result.StopReason = $"Limite de {opt.MaxFilters} filtres atteinte.";

        // 5) passe finale de déduplication (ceinture et bretelles) : garantie
        //    d'absence de doublons. Les filtres conservés sont re-simulés.
        result.DuplicatesRemoved = RemoveDuplicates(placed, opt);
        if (result.DuplicatesRemoved > 0)
            Recompute(placed, freqs, deviationDb, opt, predicted, residual);
        placed.Sort((a, b) => a.Frequency.CompareTo(b.Frequency));

        result.PredictedDb = predicted;
        result.ResidualDb = residual;
        result.ResidualRmsDb = Rms();
        double maxRes = 0;
        for (int i = lo; i <= hi; i++) maxRes = Math.Max(maxRes, Math.Abs(residual[i]));
        result.ResidualMaxDb = maxRes;
        foreach (var f in placed)
            result.Filters.Add(new PlannedFilter { Frequency = f.Frequency, GainDb = f.GainDb, Q = f.Q });

        return result;
    }

    /// <summary>Marque occupés les points de la grille situés dans la zone d'exclusion du filtre.</summary>
    private static void MarkOccupied(bool[] occupied, double[] freqs, PlannedFilter filter, AutoBandOptions opt)
    {
        double halfOct = ExclusionHalfOctaves(filter.Q, opt);
        double loRatio = Math.Pow(2.0, -halfOct), hiRatio = Math.Pow(2.0, halfOct);
        for (int i = 0; i < freqs.Length; i++)
        {
            if (occupied[i]) continue;
            if (freqs[i] >= filter.Frequency * loRatio && freqs[i] <= filter.Frequency * hiRatio)
                occupied[i] = true;
        }
    }

    /// <summary>
    /// Vrai si un nouveau filtre (f0, q) violerait l'espacement minimal avec un filtre
    /// déjà placé : la distance (en octaves) doit dépasser la somme des demi-zones
    /// d'exclusion des deux filtres — les bandes ne se recouvrent jamais.
    /// </summary>
    private static bool ConflictsWithExisting(double f0, double qNew, List<PlannedFilter> placed, AutoBandOptions opt)
    {
        double halfNew = ExclusionHalfOctaves(qNew, opt);
        foreach (var p in placed)
        {
            double distance = Math.Abs(Math.Log2(f0 / p.Frequency));
            if (distance < ExclusionHalfOctaves(p.Q, opt) + halfNew - 1e-12)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Retire les doublons (deux filtres dont les centres violent l'espacement minimal) :
    /// sur chaque paire trop proche, le filtre au gain le plus faible est supprimé.
    /// Renvoie le nombre de filtres retirés.
    /// </summary>
    private static int RemoveDuplicates(List<PlannedFilter> filters, AutoBandOptions opt)
    {
        if (filters.Count < 2) return 0;
        var sorted = filters.OrderBy(f => f.Frequency).ToList();
        var removed = new HashSet<PlannedFilter>();
        for (int i = 1; i < sorted.Count; i++)
        {
            var prev = sorted[i - 1];
            var cur = sorted[i];
            if (removed.Contains(prev) || removed.Contains(cur)) continue;
            double distance = Math.Log2(cur.Frequency / prev.Frequency);
            double minDistance = ExclusionHalfOctaves(prev.Q, opt) + ExclusionHalfOctaves(cur.Q, opt);
            if (distance < minDistance)
                removed.Add(Math.Abs(cur.GainDb) <= Math.Abs(prev.GainDb) ? cur : prev);
        }
        foreach (var f in removed) filters.Remove(f);
        return removed.Count;
    }

    /// <summary>Re-simule la prédiction et le résidu après retrait de filtres.</summary>
    private static void Recompute(
        List<PlannedFilter> filters, double[] freqs, double[] deviationDb,
        AutoBandOptions opt, double[] predicted, double[] residual)
    {
        Array.Clear(predicted);
        var biqs = filters.Select(f => new BiquadFilter(FilterType.Peaking, opt.SampleRate, f.Frequency, f.GainDb, f.Q)).ToList();
        for (int i = 0; i < freqs.Length; i++)
        {
            double sum = 0;
            foreach (var b in biqs) sum += b.ResponseDb(freqs[i], opt.SampleRate);
            predicted[i] = sum;
            residual[i] = deviationDb[i] - sum;
        }
    }

    /// <summary>Vrai tant qu'on reste « au-delà » du seuil à mi-hauteur (même signe dominant).</summary>
    private static bool SameSignOrPastHalf(double value, double half, double peak)
    {
        // peak > 0 : on continue tant que la valeur reste > half (descente vers 0)
        // peak < 0 : on continue tant que la valeur reste < half (remontée vers 0)
        return peak >= 0 ? value > half : value < half;
    }

    private static int LowerIndex(double[] freqs, double fMin)
    {
        int i = 0;
        while (i < freqs.Length && freqs[i] < fMin) i++;
        return Math.Min(i, freqs.Length - 1);
    }

    private static int UpperIndex(double[] freqs, double fMax)
    {
        int i = freqs.Length - 1;
        while (i > 0 && freqs[i] > fMax) i--;
        return i;
    }
}
