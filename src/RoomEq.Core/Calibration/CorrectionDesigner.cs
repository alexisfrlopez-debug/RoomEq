using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;

namespace RoomEq.Core.Calibration;

/// <summary>Options de conception de la correction acoustique.</summary>
public sealed class CorrectionOptions
{
    /// <summary>Limite basse de correction (en dessous, trop dépendant du micro et du local).</summary>
    public double FreqMin { get; set; } = 40.0;

    /// <summary>Limite haute de correction.</summary>
    public double FreqMax { get; set; } = 16000.0;

    /// <summary>Boost maximal par bande (sécurité des haut-parleurs et de la marge dynamique).</summary>
    public double MaxBoostDb { get; set; } = 6.0;

    /// <summary>Coupe maximale par bande (une coupe est toujours plus sûre qu'un boost).</summary>
    public double MaxCutDb { get; set; } = 12.0;

    /// <summary>Corrections plus faibles que ce seuil sont ignorées (évite les filtres inutiles).</summary>
    public double ThresholdDb { get; set; } = 0.75;

    /// <summary>Résolution des bandes d'analyse / correction.</summary>
    public double FractionOctave { get; set; } = 1.0 / 3.0;

    public TargetType Target { get; set; } = TargetType.Flat;
    public double ShelfDb { get; set; } = TargetCurve.DefaultShelfDb;
    public double TiltDbPerOct { get; set; } = TargetCurve.DefaultTiltDbPerOct;

    /// <summary>Plage utilisée pour aligner (normaliser) la mesure avec la cible.</summary>
    public double AlignMinHz { get; set; } = 200.0;
    public double AlignMaxHz { get; set; } = 8000.0;
}

/// <summary>Résultat complet d'une calibration : courbes mesurées et preset généré.</summary>
public sealed class CorrectionResult
{
    public CorrectionOptions Options { get; set; } = new();
    public int SampleRate { get; set; }
    public double[] BandCenters { get; set; } = Array.Empty<double>();
    public double[] CorrectionDb { get; set; } = Array.Empty<double>();
    public List<CurvePoint> MeasuredCurve { get; set; } = new();
    public List<CurvePoint> TargetCurvePts { get; set; } = new();
    public List<CurvePoint> PredictedCurve { get; set; } = new();
    public EqPreset Preset { get; set; } = new();

    /// <summary>Erreur résiduelle max (|prédit - cible|) dans la plage de correction.</summary>
    public double ResidualMaxDb()
    {
        double max = 0;
        foreach (var p in PredictedCurve)
        {
            if (p.Freq < Options.FreqMin || p.Freq > Options.FreqMax) continue;
            double target = TargetCurve.EvaluateDb(p.Freq, Options.Target, Options.ShelfDb, Options.TiltDbPerOct);
            max = Math.Max(max, Math.Abs(p.Db - target));
        }
        return max;
    }

    /// <summary>Erreur résiduelle RMS (|prédit - cible|) dans la plage de correction.</summary>
    public double ResidualRmsDb()
    {
        double sum = 0;
        int n = 0;
        foreach (var p in PredictedCurve)
        {
            if (p.Freq < Options.FreqMin || p.Freq > Options.FreqMax) continue;
            double target = TargetCurve.EvaluateDb(p.Freq, Options.Target, Options.ShelfDb, Options.TiltDbPerOct);
            sum += (p.Db - target) * (p.Db - target);
            n++;
        }
        return n > 0 ? Math.Sqrt(sum / n) : 0;
    }
}

/// <summary>
/// Concepteur de correction automatique :
/// mesure spectrale -> correction par bandes 1/3 d'octave -> preset de filtres peaking.
/// </summary>
public static class CorrectionDesigner
{
    public static CorrectionResult Design(
        double[] freqs, double[] rawMeasuredDb,
        CorrectionOptions opt, int sampleRate,
        string presetName, MicProfile? micProfile)
    {
        if (freqs.Length != rawMeasuredDb.Length || freqs.Length < 32)
            throw new ArgumentException("Spectre mesuré invalide.");

        // 1) Lissage fin du spectre brut (1/6 octave) pour stabiliser l'analyse
        var smooth = SpectrumTools.FractionalOctaveSmooth(freqs, rawMeasuredDb, Math.Max(opt.FractionOctave / 2.0, 1.0 / 12.0));

        // 2) Correction micro : spectre_corrigé = spectre_mesuré - gain_micro(f)
        var corrected = new double[freqs.Length];
        for (int i = 0; i < freqs.Length; i++)
        {
            double mic = (micProfile is { Enabled: true, IsValid: true }) ? micProfile.GainAt(freqs[i]) : 0.0;
            corrected[i] = smooth[i] - mic;
        }

        // 3) Alignement de niveau : la mesure absolue dépend du gain micro/ampli, seule la FORME compte.
        //    On aligne la moyenne mesurée sur la moyenne cible dans la bande d'alignement.
        var targetAtPoints = new double[freqs.Length];
        for (int i = 0; i < freqs.Length; i++)
            targetAtPoints[i] = TargetCurve.EvaluateDb(freqs[i], opt.Target, opt.ShelfDb, opt.TiltDbPerOct);

        double meanMeas = SpectrumTools.MeanDb(freqs, corrected, opt.AlignMinHz, opt.AlignMaxHz);
        double meanTarget = SpectrumTools.MeanDb(freqs, targetAtPoints, opt.AlignMinHz, opt.AlignMaxHz);
        double offset = meanMeas - meanTarget;

        var aligned = new double[freqs.Length];
        for (int i = 0; i < freqs.Length; i++)
            aligned[i] = corrected[i] - offset;

        // 4) Niveaux par bandes 1/3 d'octave sur la plage utile
        SpectrumTools.OctaveLevels(freqs, aligned, opt.FreqMin, opt.FreqMax, opt.FractionOctave,
            out var centers, out var measuredBand);

        var targetBand = new double[centers.Length];
        for (int i = 0; i < centers.Length; i++)
            targetBand[i] = TargetCurve.EvaluateDb(centers[i], opt.Target, opt.ShelfDb, opt.TiltDbPerOct);

        // 5) Correction par bande = cible - mesure, bornée
        double q = BandQ(opt.FractionOctave);
        var corrections = new double[centers.Length];
        for (int i = 0; i < centers.Length; i++)
        {
            double c = targetBand[i] - measuredBand[i];
            c = Math.Clamp(c, -opt.MaxCutDb, opt.MaxBoostDb);
            if (Math.Abs(c) < opt.ThresholdDb) c = 0.0;
            corrections[i] = c;
        }

        // 6) Construction du preset : un filtre peaking par bande corrigée
        var preset = new EqPreset(presetName)
        {
            Description = $"Correction auto RoomEq — cible {opt.Target}, bandes 1/{Math.Round(1.0 / opt.FractionOctave)} oct " +
                          $"({opt.FreqMin:0}-{opt.FreqMax:0} Hz)"
        };

        double maxBoost = 0;
        for (int i = 0; i < centers.Length; i++)
        {
            if (Math.Abs(corrections[i]) < 1e-6) continue;
            preset.Bands.Add(new EqBand(FilterType.Peaking, centers[i], corrections[i], q));
            if (corrections[i] > 0) maxBoost = Math.Max(maxBoost, corrections[i]);
        }
        // Préampli de sécurité : libère la marge nécessaire aux boosts pour éviter l'écrêtage
        preset.PreampDb = -maxBoost;

        // 7) Prédiction : mesure + réponse réelle des filtres générés
        var tempChain = new BiquadFilter[preset.Bands.Count];
        for (int i = 0; i < preset.Bands.Count; i++)
        {
            var b = preset.Bands[i];
            tempChain[i] = new BiquadFilter(b.Type, sampleRate, b.Frequency, b.GainDb, b.Q);
        }

        var predicted = new List<CurvePoint>(freqs.Length);
        for (int i = 0; i < freqs.Length; i++)
        {
            double eq = 0;
            foreach (var f in tempChain) eq += f.ResponseDb(freqs[i], sampleRate);
            predicted.Add(new CurvePoint(freqs[i], aligned[i] + eq));
        }

        var measuredCurve = new List<CurvePoint>(freqs.Length);
        var targetCurvePts = new List<CurvePoint>(freqs.Length);
        for (int i = 0; i < freqs.Length; i++)
        {
            measuredCurve.Add(new CurvePoint(freqs[i], aligned[i]));
            targetCurvePts.Add(new CurvePoint(freqs[i], targetAtPoints[i]));
        }

        return new CorrectionResult
        {
            Options = opt,
            SampleRate = sampleRate,
            BandCenters = centers,
            CorrectionDb = corrections,
            MeasuredCurve = measuredCurve,
            TargetCurvePts = targetCurvePts,
            PredictedCurve = predicted,
            Preset = preset
        };
    }

    /// <summary>
    /// Conception d'une correction depuis une courbe DÉJÀ alignée sur la cible
    /// (issue du croisement multi-signaux). Le placement des filtres est entièrement
    /// automatique (AutoBandPlanner) : nombre de bandes, fréquences et largeurs (Q)
    /// optimisés pour la courbe mesurée. Un poids de confiance (dispersion inter-signaux)
    /// peut doser prudemment les corrections aux fréquences peu fiables.
    /// </summary>
    public static CorrectionResult DesignFromAlignedCurve(
        double[] freqs, double[] alignedDb, CorrectionOptions opt, int sampleRate,
        string presetName, MicProfile? micProfile,
        AutoBandOptions? plannerOptions = null,
        Func<double, double>? confidenceWeight = null)
    {
        if (freqs.Length != alignedDb.Length || freqs.Length < 8)
            throw new ArgumentException("Courbe alignée invalide.");

        // 1) déviation = cible - mesuré (ce que l'égaliseur doit produire)
        var deviation = new double[freqs.Length];
        for (int i = 0; i < freqs.Length; i++)
        {
            double target = TargetCurve.EvaluateDb(freqs[i], opt.Target, opt.ShelfDb, opt.TiltDbPerOct);
            deviation[i] = target - alignedDb[i];
        }

        // 2) placement automatique des filtres
        var auto = AutoBandPlanner.Plan(
            freqs, deviation,
            plannerOptions ?? AutoBandOptions.From(opt, sampleRate),
            confidenceWeight);
        Io.DiagnosticsLog.Info(
            $"Plan auto : {auto.Filters.Count} filtre(s) — arrêt : {auto.StopReason}");

        // 3) preset : un peaking par filtre placé + préampli de sécurité
        var preset = new EqPreset(presetName)
        {
            Description = $"Correction auto RoomEq (multi-signaux) — {auto.Filters.Count} bandes optimisées " +
                          $"({opt.FreqMin:0}-{opt.FreqMax:0} Hz)"
        };

        double maxBoost = 0;
        foreach (var f in auto.Filters)
        {
            preset.Bands.Add(new EqBand(FilterType.Peaking, f.Frequency, f.GainDb, f.Q));
            if (f.GainDb > 0) maxBoost = Math.Max(maxBoost, f.GainDb);
        }
        preset.PreampDb = -maxBoost;

        // 4) courbes de résultat
        var predicted = new List<CurvePoint>(freqs.Length);
        for (int i = 0; i < freqs.Length; i++)
            predicted.Add(new CurvePoint(freqs[i], alignedDb[i] + auto.PredictedDb[i]));

        var measuredCurve = new List<CurvePoint>(freqs.Length);
        var targetCurvePts = new List<CurvePoint>(freqs.Length);
        for (int i = 0; i < freqs.Length; i++)
        {
            measuredCurve.Add(new CurvePoint(freqs[i], alignedDb[i]));
            targetCurvePts.Add(new CurvePoint(freqs[i],
                TargetCurve.EvaluateDb(freqs[i], opt.Target, opt.ShelfDb, opt.TiltDbPerOct)));
        }

        var centers = auto.Filters.Select(f => f.Frequency).ToArray();
        var corrBand = auto.Filters.Select(f => f.GainDb).ToArray();

        return new CorrectionResult
        {
            Options = opt,
            SampleRate = sampleRate,
            BandCenters = centers,
            CorrectionDb = corrBand,
            MeasuredCurve = measuredCurve,
            TargetCurvePts = targetCurvePts,
            PredictedCurve = predicted,
            Preset = preset
        };
    }

    /// <summary>
    /// Ré-import de la courbe de calibration affichée dans l'onglet Calibration :
    /// régénère le preset à partir de la courbe mesurée (déjà alignée sur la cible)
    /// avec le niveau de précision choisi. La précision détermine le nombre de bandes,
    /// leur espacement minimal et leurs largeurs (Q) ; les fréquences et les gains
    /// restent optimisés automatiquement par AutoBandPlanner.
    /// Le profil micro n'est PAS réappliqué : la courbe source est déjà corrigée.
    /// </summary>
    public static CorrectionResult DesignFromCalibrationCurve(
        CorrectionResult source, CorrectionPrecision precision, string presetName)
    {
        if (source.MeasuredCurve.Count < 8)
            throw new ArgumentException(
                "La courbe de calibration est vide ou trop courte. Lancez d'abord une mesure dans l'onglet Calibration.");

        double[] freqs = source.MeasuredCurve.Select(p => p.Freq).ToArray();
        double[] db = source.MeasuredCurve.Select(p => p.Db).ToArray();

        int sampleRate = source.SampleRate > 0 ? source.SampleRate : 48000;
        var planner = CorrectionPrecisionSettings.ToOptions(precision, source.Options, sampleRate);

        var result = DesignFromAlignedCurve(freqs, db, source.Options, sampleRate, presetName, null, planner);
        result.Preset.Description =
            $"Import de la courbe de calibration — précision {CorrectionPrecisionCatalog.Option(precision).Name}, " +
            $"{result.Preset.Bands.Count} bandes ({source.Options.FreqMin:0}-{source.Options.FreqMax:0} Hz)";
        return result;
    }

    /// <summary>Q du filtre peaking couvrant exactement une bande d'octave fractionnaire.</summary>
    public static double BandQ(double fractionOctave)
    {
        double bw = Math.Pow(2.0, fractionOctave);          // largeur relative
        return Math.Sqrt(bw) / (bw - 1.0);                  // 1/3 oct -> Q ≈ 4,32
    }
}
