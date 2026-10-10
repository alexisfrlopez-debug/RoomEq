using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoomEq.Core.Calibration;
using RoomEq.Core.Eq;

namespace RoomEq.Core.Io;

/// <summary>Chemin des données utilisateur (presets, profils micro).</summary>
public static class AppPaths
{
    public static string DataDir { get; }
    public static string PresetsDir { get; }
    public static string MicProfilesDir { get; }

    static AppPaths()
    {
        string baseDir;
        try
        {
            baseDir = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(baseDir)) baseDir = Directory.GetCurrentDirectory();
        }
        catch
        {
            baseDir = Directory.GetCurrentDirectory();
        }

        DataDir = Path.Combine(baseDir, "RoomEq");
        PresetsDir = Path.Combine(DataDir, "presets");
        MicProfilesDir = Path.Combine(DataDir, "micprofiles");

        Directory.CreateDirectory(PresetsDir);
        Directory.CreateDirectory(MicProfilesDir);
    }

    public static string SanitizeFileName(string name)
    {
        foreach (char c in Path.GetInvalidFileNameChars())
            name = name.Replace(c, '_');
        return name.Trim();
    }
}

/// <summary>Options JSON partagées (énum en chaînes, indented).</summary>
public static class JsonOpts
{
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>
/// Magasin JSON générique d'éléments nommés (un fichier par élément dans un
/// répertoire) : liste, sauvegarde, chargement, suppression et renommage.
/// PresetStore et MicProfileStore partagent exactement cette sémantique.
/// </summary>
public abstract class JsonNamedStore<T>
{
    private readonly string _dir;
    private readonly Func<T, string> _nameOf;
    private readonly Func<T, string, T> _renamed;

    protected JsonNamedStore(string dir, Func<T, string> nameOf, Func<T, string, T> renamed)
    {
        _dir = dir;
        _nameOf = nameOf;
        _renamed = renamed;
        Directory.CreateDirectory(_dir);
    }

    public IReadOnlyList<string> List()
    {
        var list = new List<string>();
        foreach (var f in Directory.GetFiles(_dir, "*.json"))
            list.Add(Path.GetFileNameWithoutExtension(f));
        list.Sort(StringComparer.OrdinalIgnoreCase);
        return list;
    }

    public void Save(T item)
    {
        var file = Path.Combine(_dir, AppPaths.SanitizeFileName(_nameOf(item)) + ".json");
        File.WriteAllText(file, JsonSerializer.Serialize(item, JsonOpts.Default));
    }

    public T? Load(string name)
    {
        var file = Path.Combine(_dir, AppPaths.SanitizeFileName(name) + ".json");
        if (!File.Exists(file)) return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(file), JsonOpts.Default);
    }

    public void Delete(string name)
    {
        var file = Path.Combine(_dir, AppPaths.SanitizeFileName(name) + ".json");
        if (File.Exists(file)) File.Delete(file);
    }

    /// <summary>
    /// Renomme un élément : re-écrit sous le nouveau nom et supprime l'ancien.
    /// Refuse un nom vide ou déjà occupé par un AUTRE élément. Renvoi vrai si succès.
    /// </summary>
    public bool Rename(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return false;
        newName = newName.Trim();

        string oldSan = AppPaths.SanitizeFileName(oldName);
        string newSan = AppPaths.SanitizeFileName(newName);
        if (newSan.Length == 0) return false;

        var oldFile = Path.Combine(_dir, oldSan + ".json");
        if (!File.Exists(oldFile)) return false;

        var item = Load(oldName);
        if (item == null) return false;

        var newFile = Path.Combine(_dir, newSan + ".json");
        bool sameFile = string.Equals(oldFile, newFile, StringComparison.OrdinalIgnoreCase);
        if (!sameFile && File.Exists(newFile)) return false; // nom déjà pris

        File.WriteAllText(newFile, JsonSerializer.Serialize(_renamed(item, newName), JsonOpts.Default));
        if (!sameFile) File.Delete(oldFile);
        return true;
    }
}

/// <summary>Enregistrement et chargement des presets d'égaliseur (JSON dans %APPDATA%\RoomEq\presets).
/// Un répertoire de substitution peut être fourni (tests).</summary>
public sealed class PresetStore : JsonNamedStore<EqPreset>
{
    public PresetStore(string? directoryOverride = null)
        : base(directoryOverride ?? AppPaths.PresetsDir,
               p => p.Name,
               (p, name) => { var c = p.Clone(); c.Name = name; return c; })
    {
    }
}

/// <summary>Enregistrement et chargement des profils micro (JSON dans %APPDATA%\RoomEq\micprofiles).
/// Un répertoire de substitution peut être fourni (tests).</summary>
public sealed class MicProfileStore : JsonNamedStore<MicProfile>
{
    public MicProfileStore(string? directoryOverride = null)
        : base(directoryOverride ?? AppPaths.MicProfilesDir,
               p => p.Name,
               (p, name) => { p.Name = name; return p; })
    {
    }
}
