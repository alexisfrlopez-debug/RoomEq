using System.IO;
using System.Text.Json;
using RoomEq.Core.Io;

namespace RoomEq.Core.Eq;

/// <summary>Origine d'une liaison preset ↔ périphérique (spec I-EQ-04).</summary>
public enum DevicePresetKind
{
    /// <summary>
    /// Choix explicite de l'utilisateur pour ce périphérique (prioritaire sur
    /// toute détection automatique).
    /// </summary>
    Manual,

    /// <summary>
    /// Preset utilisateur enregistré à l'issue d'une calibration réalisée sur
    /// ce périphérique (prioritaire sur les presets usine reconnus).
    /// </summary>
    Calibration,

    /// <summary>
    /// Aucun preset pour ce périphérique, même si la reconnaissance
    /// automatique trouverait quelque chose.
    /// </summary>
    None,
}

/// <summary>
/// Référence de preset appliquée à un périphérique de sortie précis.
/// <paramref name="PresetRef"/> désigne soit un preset utilisateur (par nom,
/// magasin PresetStore), soit un modèle de la bibliothèque usine (par
/// identifiant HeadphonePresetLibrary).
/// </summary>
public sealed class DevicePresetBinding
{
    public string DeviceId { get; set; } = "";
    public string DeviceName { get; set; } = "";

    /// <summary>user | factory : catégorie du preset référencé.</summary>
    public string PresetClass { get; set; } = "user";

    public string PresetRef { get; set; } = "";
    public DevicePresetKind Kind { get; set; } = DevicePresetKind.Manual;
    public DateTime BoundAt { get; set; } = DateTime.Now;

    /// <summary>Liaison exploitable (pas « Aucun », référence non vide).</summary>
    public bool IsUsable => Kind != DevicePresetKind.None
                            && !string.IsNullOrWhiteSpace(PresetRef);
}

/// <summary>
/// Magasin JSON des liaisons preset ↔ périphérique de sortie
/// (%APPDATA%\RoomEq\devicepresets.json par défaut) — spec I-EQ-04.
/// </summary>
public sealed class DevicePresetStore
{
    private sealed class Db
    {
        public int Version { get; set; } = 1;
        public List<DevicePresetBinding> Bindings { get; set; } = new();
    }

    public string Directory { get; }
    public string FilePath { get; }

    /// <summary>Magasin par défaut : %APPDATA%\RoomEq\devicepresets.json.</summary>
    public DevicePresetStore()
        : this(Path.Combine(AppPaths.DataDir))
    {
    }

    /// <summary>Magasin dans un répertoire imposé (tests, mode portable…).</summary>
    public DevicePresetStore(string directory)
    {
        Directory = directory;
        FilePath = Path.Combine(directory, "devicepresets.json");
    }

    /// <summary>Toutes les liaisons, indexées par identifiant de périphérique.</summary>
    public IReadOnlyDictionary<string, DevicePresetBinding> LoadAll()
    {
        var map = new Dictionary<string, DevicePresetBinding>(StringComparer.OrdinalIgnoreCase);
        foreach (var b in ReadDb().Bindings)
        {
            if (!string.IsNullOrWhiteSpace(b.DeviceId))
                map[b.DeviceId] = b; // dernier écrit gagne
        }
        return map;
    }

    /// <summary>Liaison d'un périphérique (null si aucune).</summary>
    public DevicePresetBinding? Get(string deviceId)
        => LoadAll().TryGetValue(deviceId, out var b) ? b : null;

    /// <summary>Écrit (ou remplace) la liaison d'un périphérique.</summary>
    public void Set(DevicePresetBinding binding)
    {
        if (string.IsNullOrWhiteSpace(binding.DeviceId))
            throw new ArgumentException("DeviceId requis.", nameof(binding));
        var db = ReadDb();
        db.Bindings.RemoveAll(b => string.Equals(
            b.DeviceId, binding.DeviceId, StringComparison.OrdinalIgnoreCase));
        db.Bindings.Add(binding);
        WriteDb(db);
    }

    /// <summary>
    /// Supprime la liaison d'un périphérique (retour au mode Auto).
    /// Renvoie vrai si une liaison existait.
    /// </summary>
    public bool Remove(string deviceId)
    {
        var db = ReadDb();
        int n = db.Bindings.RemoveAll(b => string.Equals(
            b.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
        if (n > 0) WriteDb(db);
        return n > 0;
    }

    private Db ReadDb()
    {
        try
        {
            if (!File.Exists(FilePath)) return new Db();
            var db = JsonSerializer.Deserialize<Db>(File.ReadAllText(FilePath), JsonOpts.Default);
            return db ?? new Db();
        }
        catch
        {
            // fichier illisible : repartir de zéro plutôt que de bloquer l'audio
            return new Db();
        }
    }

    private void WriteDb(Db db)
    {
        System.IO.Directory.CreateDirectory(Directory);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(db, JsonOpts.Default));
    }
}
