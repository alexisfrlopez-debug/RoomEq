using System.Globalization;
using RoomEq.Core.Dsp;

namespace RoomEq.Core.Calibration;

/// <summary>
/// Profil de correction d'un micro de mesure (spec I-CAL-07) : courbe gain (dB)
/// en fonction de la fréquence, importée depuis un fichier constructeur (.cal
/// REW, CSV, TXT) ou issue de la bibliothèque intégrée. La correction s'applique
/// AVANT l'analyse : spectre_corrigé = spectre_mesuré − gain_micro.
/// </summary>
public sealed class MicProfile
{
    public string Name { get; set; } = "Micro";
    public string SourceFile { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public List<double> Frequencies { get; set; } = new();
    public List<double> GainsDb { get; set; } = new();
    public DateTime CreatedAt { get; set; } = DateTime.Now;

    /// <summary>Marque / fabricant (profils de la bibliothèque intégrée).</summary>
    public string Brand { get; set; } = "";

    /// <summary>Technologie de transduction (électret, condensateur vrai, RF, dynamique, ruban, USB…).</summary>
    public string Technology { get; set; } = "";

    /// <summary>Notes d'usage (mesure, voix, salle de concert, typicité de la courbe…).</summary>
    public string Notes { get; set; } = "";

    public bool IsValid => Frequencies.Count >= 5;
    public int PointCount => Frequencies.Count;

    public double FreqMin => Frequencies.Count > 0 ? Frequencies[0] : 0;
    public double FreqMax => Frequencies.Count > 0 ? Frequencies[^1] : 0;

    /// <summary>Gain du micro (dB) à la fréquence donnée (interpolation log-linéaire,
    /// bornes écrêtées ; profil vide → 0 sans exception).</summary>
    public double GainAt(double freq)
    {
        if (Frequencies.Count == 0)
            return 0.0;
        return SpectrumTools.InterpLog(Frequencies, GainsDb, freq);
    }

    public List<CurvePoint> ToCurve()
    {
        var pts = new List<CurvePoint>(Frequencies.Count);
        for (int i = 0; i < Frequencies.Count; i++)
            pts.Add(new CurvePoint(Frequencies[i], GainsDb[i]));
        return pts;
    }

    /// <summary>
    /// Parseur tolérant : accepte les .cal REW (« freq,gain » décimales point,
    /// en-têtes texte, colonnes supplémentaires), CSV séparateur , ou ;,
    /// tabulations, espaces, décimales . ou , (locale française).
    /// </summary>
    public static MicProfile ParseText(string text, string name, string sourceFile = "")
    {
        ArgumentNullException.ThrowIfNull(text);
        var freqs = new List<double>();
        var gains = new List<double>();

        foreach (var rawLine in text.Split('\n'))
        {
            var line = rawLine.Trim().TrimEnd('\r').Replace("\"", "");
            if (line.Length == 0)
                continue;

            var tokens = SplitNumericTokens(line);
            if (tokens.Count < 2)
                continue;

            if (!TryParseDouble(tokens[0], out double f))
                continue;
            if (!TryParseDouble(tokens[1], out double g))
                continue;
            if (f <= 0.0 || f > 1e6 || Math.Abs(g) > 40.0)
                continue; // bornes plausibles

            freqs.Add(f);
            gains.Add(g);
        }

        // tri par fréquence croissante + déduplication (le dernier gagne)
        var pairs = freqs.Zip(gains, (f, g) => (f, g))
                         .OrderBy(p => p.f)
                         .ToList();
        var pf = new List<double>(pairs.Count);
        var pg = new List<double>(pairs.Count);
        foreach (var (f, g) in pairs)
        {
            if (pf.Count > 0 && Math.Abs(f - pf[^1]) < 1e-9)
            {
                pg[^1] = g;
                continue;
            }
            pf.Add(f);
            pg.Add(g);
        }

        return new MicProfile
        {
            Name = name,
            SourceFile = sourceFile,
            Frequencies = pf,
            GainsDb = pg
        };
    }

    /// <summary>
    /// Découpe une ligne en jetons numériques en choisissant le bon séparateur :
    /// ';' (CSV FR), tabulation, ',' (séparateur .cal), espace.
    /// </summary>
    private static List<string> SplitNumericTokens(string line)
    {
        StringSplitOptions opts = StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries;

        string[] parts = line.Contains(';') ? line.Split(';', opts)
                       : line.Contains('\t') ? line.Split('\t', opts)
                       : line.Contains(',') ? line.Split(',', opts)
                       : line.Split(' ', opts);

        var result = new List<string>();
        foreach (var p in parts)
            if (IsNumericLike(p))
                result.Add(p);
        return result;
    }

    private static bool IsNumericLike(string s)
    {
        s = s.Trim();
        if (s.Length == 0)
            return false;
        string norm = s.Contains(',') && !s.Contains('.') ? s.Replace(',', '.') : s;
        return double.TryParse(norm, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private static bool TryParseDouble(string s, out double value)
    {
        s = s.Trim();
        // jeton avec virgule ET sans point -> décimale française
        if (s.Contains(',') && !s.Contains('.'))
            s = s.Replace(',', '.');
        return double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }
}
