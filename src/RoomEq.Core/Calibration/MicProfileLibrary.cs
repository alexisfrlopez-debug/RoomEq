using System.Reflection;
using System.Text.Json;

namespace RoomEq.Core.Calibration;

/// <summary>
/// Bibliothèque intégrée de profils de correction pour micros du marché
/// (spec I-CAL-07) : les courbes sont des TYPICALITÉS constructeur — excellent
/// point de départ, à affiner avec le fichier .cal individuel du micro.
/// </summary>
public static class MicProfileLibrary
{
    private const string ResourceName = "RoomEq.Core.Calibration.MicLibrary.json";

    private static List<MicProfile>? _cache;

    /// <summary>Charge (et met en cache) tous les profils valides de la bibliothèque.</summary>
    public static IReadOnlyList<MicProfile> LoadAll()
    {
        if (_cache != null)
            return _cache;

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

            if (profile.IsValid)
                list.Add(profile);
        }

        _cache = list;
        return list;
    }

    /// <summary>Filtre la bibliothèque sur le nom, la marque, la technologie ou l'usage.</summary>
    public static List<MicProfile> Search(IReadOnlyList<MicProfile> library, string query)
    {
        ArgumentNullException.ThrowIfNull(library);
        if (string.IsNullOrWhiteSpace(query))
            return new List<MicProfile>(library);
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
        ArgumentNullException.ThrowIfNull(libraryProfile);
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
