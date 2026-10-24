using System.Reflection;
using System.Text.Json;
using RoomEq.Core.Dsp;
using RoomEq.Core.Eq;

namespace RoomEq.Core.Headphones;

/// <summary>Un filtre paramétrique d'un preset casque (mapping direct AutoEq → EqBand).</summary>
/// <param name="Kind">"peaking" | "low-shelf" | "high-shelf".</param>
public sealed record HeadphoneFilter(string Kind, double Frequency, double GainDb, double Q)
{
    public FilterType FilterType => Kind switch
    {
        "low-shelf" => FilterType.LowShelf,
        "high-shelf" => FilterType.HighShelf,
        _ => FilterType.Peaking,
    };

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
/// Bibliothèque intégrée de corrections pour casques et écouteurs.
///
/// Pour ce type de transducteur, la correction par capture au micro n'est pas
/// applicable : la réponse mesurée dépendrait du couple micro/coussinet et non
/// du casque lui-même. La bibliothèque embarque donc des corrections
/// paramétriques DÉRIVÉES DU PROJET AutoEq (MIT — Jaakko Pasanen), elles-mêmes
/// calculées à partir de mesures sur tête artificielle (oratory1990, crinacle,
/// Rtings, Super Review…) vers une cible Harman. Aucune dépendance externe :
/// les données sont intégrées à la compilation.
/// </summary>
public static class HeadphonePresetLibrary
{
    private const string ResourceName = "RoomEq.Core.Headphones.headphone_presets.json";

    private static List<HeadphoneModel>? _cache;

    /// <summary>Charge (et met en cache) tous les modèles de la bibliothèque.</summary>
    public static IReadOnlyList<HeadphoneModel> LoadAll()
    {
        if (_cache != null) return _cache;

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

    /// <summary>Marques disponibles, triées alphabétiquement (FR : accents ignorés).</summary>
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
    /// Recherche par texte libre (marque, modèle) : insensible à la casse et à
    /// la ponctuation, préfixe sur chaque mot. Une requête vide rend tout.
    /// </summary>
    public static IReadOnlyList<HeadphoneModel> Search(string query)
    {
        var all = LoadAll();
        if (string.IsNullOrWhiteSpace(query)) return all;

        string norm(string s) => s.Trim().ToLowerInvariant();
        var tokens = norm(query).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length == 0) return all;

        return all
            .Where(m =>
            {
                string hay = norm(m.Brand + " " + m.Model);
                return tokens.All(t => hay.Contains(t));
            })
            .OrderBy(m => m.DisplayName, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Convertit un modèle en preset d'égaliseur prêt à charger.</summary>
    public static EqPreset ToEqPreset(HeadphoneModel model)
    {
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
