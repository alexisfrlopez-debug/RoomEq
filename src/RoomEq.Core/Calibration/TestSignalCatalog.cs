using RoomEq.Core.Dsp;

namespace RoomEq.Core.Calibration;

/// <summary>Signaux de test disponibles pour la mesure de pièce — spec I-CAL-08.</summary>
public enum MeasurementSignalKind
{
    /// <summary>Bruit blanc : PSD plate, révélateur des aigus et des résonances.</summary>
    WhiteNoise,

    /// <summary>Bruit rose : énergie constante par octave, répartition naturelle type écoute.</summary>
    PinkNoise,

    /// <summary>Bruit brun : énergie décalée vers les graves, précision maximale sous 200 Hz.</summary>
    BrownNoise,

    /// <summary>Bruit violet : énergie décalée vers les aigus, précision maximale au-dessus de 5 kHz.</summary>
    VioletNoise,

    /// <summary>
    /// Balayage sinusoïdal progressif (logarithmique, 20 Hz → 20 kHz) : étalonne
    /// TOUT le spectre ; l'analyse par déconvolution (Farina) donne une réponse
    /// narrowband précise à chaque fréquence.
    /// </summary>
    FrequencySweep
}

/// <summary>
/// Catalogue des signaux de mesure (spec I-CAL-08) : noms affichés,
/// descriptions, pente spectrale théorique attendue par octave (niveaux de
/// bandes) et plan de mesure.
/// </summary>
public static class MeasurementSignalCatalog
{
    /// <summary>
    /// Pente théorique des NIVEAUX DE BANDES calculés par OctaveLevels (moyenne
    /// par bin, donc pente = pente de densité spectrale de puissance) : blanc 0,
    /// rose −3,01, brun −6,02, violet +6,02 dB/octave. Le balayage (amplitude
    /// constante par ton) est plat par définition, comme des sinus discrets.
    /// </summary>
    public static double ExpectedBandSlopeDbPerOct(MeasurementSignalKind kind) => kind switch
    {
        MeasurementSignalKind.WhiteNoise => 0.0,
        MeasurementSignalKind.PinkNoise => -3.0103,
        MeasurementSignalKind.BrownNoise => -6.0206,
        MeasurementSignalKind.VioletNoise => 6.0206,
        _ => 0.0
    };

    /// <summary>Vrai pour les signaux large bande (bruits) : un seul segment de mesure.</summary>
    public static bool IsBroadband(MeasurementSignalKind kind)
        => kind != MeasurementSignalKind.FrequencySweep;

    public static string DisplayName(MeasurementSignalKind kind) => kind switch
    {
        MeasurementSignalKind.WhiteNoise => "Bruit blanc",
        MeasurementSignalKind.PinkNoise => "Bruit rose",
        MeasurementSignalKind.BrownNoise => "Bruit brun",
        MeasurementSignalKind.VioletNoise => "Bruit violet",
        MeasurementSignalKind.FrequencySweep => "Balayage sinusoïdal",
        _ => kind.ToString()
    };

    public static string Description(MeasurementSignalKind kind) => kind switch
    {
        MeasurementSignalKind.WhiteNoise => "Spectre plat — bon compromis global, sensible au bruit de fond dans les graves.",
        MeasurementSignalKind.PinkNoise => "Énergie constante par octave — la référence des mesures acoustiques.",
        MeasurementSignalKind.BrownNoise => "Graves renforcés — mesure fine des modes de pièce sous 200 Hz.",
        MeasurementSignalKind.VioletNoise => "Aigus renforcés — mesure fine au-dessus de 5 kHz où les bruits s'affaiblissent.",
        MeasurementSignalKind.FrequencySweep => "Balayage progressif 20 Hz → 20 kHz (logarithmique) — étalonne tout le spectre en continu, précision narrowband sur chaque fréquence (déconvolution de Farina).",
        _ => ""
    };

    /// <summary>Fréquences des sinus d'octave ISO 266 (31,5 Hz → 16 kHz) — points de
    /// contrôle manuels éventuels ; la mesure automatique utilise le balayage continu.</summary>
    public static IReadOnlyList<double> DefaultSineSteps { get; } = new[]
    {
        31.5, 63.0, 125.0, 250.0, 500.0, 1000.0, 2000.0, 4000.0, 8000.0, 16000.0
    };

    /// <summary>Spécification du balayage progressif utilisé par la calibration.</summary>
    public static LogSweepSpec DefaultSweep(double durationSeconds)
        => new(LogSweepSpec.Default.F1, LogSweepSpec.Default.F2, durationSeconds);

    /// <summary>Nombre total de segments de mesure pour une sélection de signaux.</summary>
    public static int SegmentCount(IEnumerable<MeasurementSignalKind> kinds)
        => kinds.Count();

    /// <summary>Description lisible d'un plan de mesure multi-signaux.</summary>
    public static string PlanSummary(IEnumerable<MeasurementSignalKind> kinds, double secondsPerSignal, double sweepSeconds)
    {
        ArgumentNullException.ThrowIfNull(kinds);
        var list = kinds.ToList();
        int sweeps = list.Count(k => k == MeasurementSignalKind.FrequencySweep);
        double total = (list.Count - sweeps) * secondsPerSignal + sweeps * sweepSeconds;
        return $"{list.Count} signal(s) · durée totale ≈ {total:0} s";
    }
}
