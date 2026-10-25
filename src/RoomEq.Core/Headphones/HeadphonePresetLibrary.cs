using System.Reflection;
using System.Text;
using System.Text.Json;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;

namespace RoomEq.Core.Headphones;

/// <summary>Un filtre paramétrique d'un preset casque — spec I-HPH-01.</summary>
/// <param name="Kind">"peaking" | "low-shelf" | "high-shelf".</param>
public sealed record HeadphoneFilter(string Kind, double Frequency, double GainDb, double Q)
{
    public FilterType FilterType => Kind switch
    {
        "low-shelf" => FilterType.LowShelf,
        "high-shelf" => FilterType.HighShelf,
        _ => FilterType.Peaking,
    };

    /// <summary>Conversion en bande d'égaliseur avec clamps de sécurité.</summary>
    public EqBand ToBand() => new(FilterType,
        Math.Clamp(Frequency, 20.0, 20000.0),
        Math.Clamp(GainDb, -24.0, 24.0),
        Math.Clamp(Q, 0.1, 20.0));
}

/// <summary>
/// Un modèle de casque ou d'écouteur de la bibliothèque intégrée : correction
/// paramétrique complète (préampli + filtres) prête à charger dans l'égaliseur.
/// </summary>
public sealed class HeadphoneModel
{
    public string Id { get; init; } = "";
    public string Brand { get; init; } = "";
    public string Model { get; init; } = "";

    /// <summary>over-ear | in-ear | earbud | gaming.</summary>
    public string Type { get; init; } = "";

    /// <summary>budget | moyenne | premium (indicative).</summary>
    public string Gamme { get; init; } = "";

    /// <summary>Source de mesure AutoEq (oratory1990, crinacle, Rtings…).</summary>
    public string Source { get; init; } = "";

    public double PreampDb { get; init; }
    public List<HeadphoneFilter> Filters { get; } = new();

    public string DisplayName => Brand + " " + Model;

    public string TypeLabel => Type switch
    {
        "over-ear" => "Circum-aural",
        "in-ear" => "Intra-aural",
        "earbud" => "Écouteurs classiques",
        "gaming" => "Casque gaming",
        _ => Type,
    };

    public string GammeLabel => Gamme switch
    {
        "budget" => "Petit prix",
        "moyenne" => "Gamme moyenne",
        "premium" => "Haut de gamme",
        _ => Gamme,
    };
}

/// <summary>
/// Bibliothèque intégrée de corrections pour casques et écouteurs (spec
/// I-HPH-01). Corrections paramétriques DÉRIVÉES DU PROJET AutoEq (MIT —
/// Jaakko Pasanen), calculées à partir de mesures sur tête artificielle vers
/// une cible Harman. Aucune dépendance externe : données intégrées à la
/// compilation.
/// </summary>
public static class HeadphonePresetLibrary
{
    private const string ResourceName = "RoomEq.Core.Headphones.headphone_presets.json";

    private static List<HeadphoneModel>? _cache;

    /// <summary>Charge (et met en cache) tous les modèles de la bibliothèque.</summary>
    public static IReadOnlyList<HeadphoneModel> LoadAll()
    {
        if (_cache != null)
            return _cache;

        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Ressource intégrée introuvable : {ResourceName}");

        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var list = new List<HeadphoneModel>();
        foreach (var el in root.GetProperty("models").EnumerateArray())
        {
            var m = new HeadphoneModel
            {
                Id = el.GetProperty("id").GetString() ?? "",
                Brand = el.GetProperty("brand").GetString() ?? "",
                Model = el.GetProperty("model").GetString() ?? "",
                Type = el.GetProperty("type").GetString() ?? "over-ear",
                Gamme = el.GetProperty("gamme").GetString() ?? "moyenne",
                Source = el.GetProperty("source").GetString() ?? "",
                PreampDb = el.GetProperty("preampDb").GetDouble(),
            };
            foreach (var f in el.GetProperty("filters").EnumerateArray())
            {
                m.Filters.Add(new HeadphoneFilter(
                    f.GetProperty("type").GetString() ?? "peaking",
                    f.GetProperty("f").GetDouble(),
                    f.GetProperty("g").GetDouble(),
                    f.GetProperty("q").GetDouble()));
            }
            if (m.Filters.Count > 0 && m.Brand.Length > 0)
                list.Add(m);
        }

        _cache = list;
        return list;
    }

    /// <summary>Marques disponibles, triées alphabétiquement (casse ignorée).</summary>
    public static IReadOnlyList<string> Brands() =>
        LoadAll().Select(m => m.Brand)
                  .Distinct(StringComparer.OrdinalIgnoreCase)
                  .OrderBy(b => b, StringComparer.OrdinalIgnoreCase)
                  .ToList();

    /// <summary>Modèles d'une marque, triés par nom.</summary>
    public static IReadOnlyList<HeadphoneModel> ByBrand(string brand) =>
        LoadAll()
            .Where(m => string.Equals(m.Brand, brand, StringComparison.OrdinalIgnoreCase))
            .OrderBy(m => m.Model, StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>Retrouve un modèle par son identifiant.</summary>
    public static HeadphoneModel? Find(string id) =>
        LoadAll().FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// Recherche par texte libre (marque, modèle) : insensible à la casse, tous
    /// les jetons doivent apparaître dans « marque modèle » ; requête vide → tout.
    /// </summary>
    public static IReadOnlyList<HeadphoneModel> Search(string query)
    {
        var all = LoadAll();
        if (string.IsNullOrWhiteSpace(query))
            return all;

        string norm(string s) => s.Trim().ToLowerInvariant();
        var tokens = norm(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0)
            return all;

        return all
            .Where(m =>
            {
                string hay = norm(m.Brand + " " + m.Model);
                return tokens.All(t => hay.Contains(t));
            })
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Reconnaît un casque / écouteur de la bibliothèque depuis le nom d'un
    /// périphérique (« Casque (JBL Tune Flex 2 Stereo) », « Écouteurs (Beats
    /// Studio Pro) », « Headphones (WH-1000XM4) »…). Normalisation (minuscules,
    /// accents décomposés, alphanumériques uniquement) puis recherche de
    /// « marque+modèle » ; à défaut le modèle seul s'il est assez long
    /// (≥ 8 caractères normalisés). Le candidat le plus SPÉCIFIQUE gagne.
    /// </summary>
    /// <returns>Le modèle reconnu, ou null si aucun ne correspond.</returns>
    public static HeadphoneModel? MatchDeviceName(string? deviceFriendlyName)
    {
        if (string.IsNullOrWhiteSpace(deviceFriendlyName))
            return null;
        string hay = NormalizeDeviceName(deviceFriendlyName);
        if (hay.Length < 4)
            return null;

        HeadphoneModel? best = null;
        int bestScore = int.MaxValue;
        int bestLen = -1;

        foreach (var m in LoadAll())
        {
            string full = NormalizeDeviceName(m.Brand + " " + m.Model);
            int score;
            int len;

            if (full.Length >= 4 && hay.Contains(full))
            {
                score = 0;                 // marque + modèle : correspondance sûre
                len = full.Length;
            }
            else
            {
                string modelOnly = NormalizeDeviceName(m.Model);
                if (modelOnly.Length < 8 || !hay.Contains(modelOnly))
                    continue;              // modèle seul trop court ou absent
                score = 1;                 // correspondance moins sûre
                len = modelOnly.Length;
            }

            // meilleur candidat : correspondance la plus sûre, puis la plus longue
            if (score < bestScore || (score == bestScore && len > bestLen))
            {
                best = m;
                bestScore = score;
                bestLen = len;
            }
        }
        return best;
    }

    /// <summary>Normalisation commune : minuscules latines + chiffres uniquement
    /// (accents décomposés puis filtrés).</summary>
    private static string NormalizeDeviceName(string s)
    {
        var sb = new StringBuilder(s.Length);
        foreach (char c in s.ToLowerInvariant())
        {
            foreach (char d in c.ToString().Normalize(NormalizationForm.FormD))
                if (d is (>= 'a' and <= 'z') or (>= '0' and <= '9'))
                    sb.Append(d);
        }
        return sb.ToString();
    }

    /// <summary>Convertit un modèle en preset d'égaliseur prêt à charger.</summary>
    public static EqPreset ToEqPreset(HeadphoneModel model)
    {
        ArgumentNullException.ThrowIfNull(model);
        var p = new EqPreset(model.DisplayName)
        {
            Description = $"Correction {model.TypeLabel.ToLower()} — source de mesure : {model.Source} " +
                          "(bibliothèque intégrée, données AutoEq)",
            PreampDb = Math.Clamp(model.PreampDb, -15.0, 0.0),
        };
        foreach (var f in model.Filters)
            p.Bands.Add(f.ToBand());
        return p;
    }

    /// <summary>Nombre de modèles de la bibliothèque (affiché dans l'interface).</summary>
    public static int Count => LoadAll().Count;
}
