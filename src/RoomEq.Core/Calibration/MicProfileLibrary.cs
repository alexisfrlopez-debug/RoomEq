using System.Reflection;
using System.Text.Json;

namespace RoomEq.Core.Calibration;

/// <summary>
/// Bibliothèque intégrée de profils de correction pour les micros du marché
/// (mesure, studio, scène — électret, condensateur vrai, RF, dynamique, ruban, USB).
/// Les courbes sont des TYPICALITÉS constructeur : le profil fournit un excellent
/// point de départ, à affiner dans l'éditeur ou avec le fichier de calibration
/// individuel du micro (.cal REW / CSV).
/// </summary>
public static class MicProfileLibrary
{
    private const string ResourceName = "RoomEq.Core.Calibration.MicLibrary.json";

    private static List<MicProfile>? _cache;

    /// <summary>Charge (et met en cache) tous les profils de la bibliothèque.</summary>
    public static IReadOnlyList<MicProfile> LoadAll()
    {
        if (_cache != null) return _cache;

        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"Ressource intégrée introuvable : {ResourceName}");

        using var doc = JsonDocument.Parse(stream);
        var root = doc.RootElement;

        var list = new List<MicProfile>();
        foreach (var el in root.GetProperty("profiles").EnumerateArray())
        {
            var profile = new MicProfile
            {
                Name = el.GetProperty("name").GetString() ?? "Micro",
                Brand = el.GetProperty("brand").GetString() ?? "",
                Technology = el.GetProperty("technology").GetString() ?? "",
                Notes = el.GetProperty("notes").GetString() ?? "",
                SourceFile = "bibliothèque intégrée"
            };

            foreach (var pt in el.GetProperty("points").EnumerateArray())
            {
                var arr = pt.EnumerateArray().ToArray();
                profile.Frequencies.Add(arr[0].GetDouble());
                profile.GainsDb.Add(arr[1].GetDouble());
            }

            if (profile.IsValid) list.Add(profile);
        }

        _cache = list;
        return list;
    }

    /// <summary>Filtre la bibliothèque sur le nom, la marque, la technologie ou l'usage.</summary>
    public static List<MicProfile> Search(IReadOnlyList<MicProfile> library, string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return new List<MicProfile>(library);
        string q = query.Trim();
        return library.Where(p =>
            p.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            p.Brand.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            p.Technology.Contains(q, StringComparison.OrdinalIgnoreCase) ||
            p.Notes.Contains(q, StringComparison.OrdinalIgnoreCase))
            .ToList();
    }

    /// <summary>Copie modifiable d'un profil de bibliothèque (prêt à être enregistré).</summary>
    public static MicProfile ToEditableCopy(MicProfile libraryProfile, string? newName = null)
    {
        return new MicProfile
        {
            Name = newName ?? libraryProfile.Name,
            Brand = libraryProfile.Brand,
            Technology = libraryProfile.Technology,
            Notes = libraryProfile.Notes,
            SourceFile = "bibliothèque (" + libraryProfile.Name + ")",
            Frequencies = new List<double>(libraryProfile.Frequencies),
            GainsDb = new List<double>(libraryProfile.GainsDb)
        };
    }
}
