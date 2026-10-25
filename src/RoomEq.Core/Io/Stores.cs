using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using RoomEq.Core.Calibration;
using RoomEq.Core.Eq;

namespace RoomEq.Core.Io;

/// <summary>Chemin des données utilisateur (presets, profils micro) — spec I-IO-05.</summary>
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
            if (string.IsNullOrEmpty(baseDir))
                baseDir = Directory.GetCurrentDirectory();
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

    /// <summary>
    /// Caractères interdits dans un nom de fichier sur AU MOINS une plateforme
    /// cible (Windows + macOS/Linux) — union fixe, indépendante de l'OS hôte :
    /// un nom sanitizé sous Linux doit rester valide s'il est copié sous Windows
    /// (v2.2.2 : Path.GetInvalidFileNameChars() dépendait de l'OS hôte).
    /// </summary>
    private const string InvalidFileNameChars =
        "<>:\"/\\|?*" +
        "\u0000\u0001\u0002\u0003\u0004\u0005\u0006\u0007" +
        "\u0008\u0009\u000a\u000b\u000c\u000d\u000e\u000f" +
        "\u0010\u0011\u0012\u0013\u0014\u0015\u0016\u0017" +
        "\u0018\u0019\u001a\u001b\u001c\u001d\u001e\u001f";

    /// <summary>Remplace tout caractère interdit (union multi-plateformes) par « _ ».</summary>
    public static string SanitizeFileName(string name)
    {
        foreach (char c in InvalidFileNameChars)
            name = name.Replace(c, '_');
        return name.Trim();
    }
}

/// <summary>Options JSON partagées (énumérations en chaînes, indenté).</summary>
public static class JsonOpts
{
    public static readonly JsonSerializerOptions Default = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

/// <summary>
/// Magasin JSON générique d'éléments nommés (un fichier par élément) :
/// liste, sauvegarde, chargement, suppression et renommage — sémantique
/// partagée par PresetStore et MicProfileStore (spec I-IO-05).
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

    /// <summary>Noms des éléments stockés, triés (casse ignorée).</summary>
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
        if (!File.Exists(file))
            return default;
        return JsonSerializer.Deserialize<T>(File.ReadAllText(file), JsonOpts.Default);
    }

    public void Delete(string name)
    {
        var file = Path.Combine(_dir, AppPaths.SanitizeFileName(name) + ".json");
        if (File.Exists(file))
            File.Delete(file);
    }

    /// <summary>
    /// Renomme un élément : re-écrit sous le nouveau nom et supprime l'ancien.
    /// Refuse un nom vide ou déjà occupé par un AUTRE élément.
    /// </summary>
    public bool Rename(string oldName, string newName)
    {
        if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName))
            return false;
        newName = newName.Trim();

        string oldSan = AppPaths.SanitizeFileName(oldName);
        string newSan = AppPaths.SanitizeFileName(newName);
        if (newSan.Length == 0)
            return false;

        var oldFile = Path.Combine(_dir, oldSan + ".json");
        if (!File.Exists(oldFile))
            return false;

        var item = Load(oldName);
        if (item == null)
            return false;

        var newFile = Path.Combine(_dir, newSan + ".json");
        bool sameFile = string.Equals(oldFile, newFile, StringComparison.OrdinalIgnoreCase);
        if (!sameFile && File.Exists(newFile))
            return false; // nom déjà pris

        File.WriteAllText(newFile, JsonSerializer.Serialize(_renamed(item, newName), JsonOpts.Default));
        if (!sameFile)
            File.Delete(oldFile);
        return true;
    }
}

/// <summary>Presets d'égaliseur (JSON dans %APPDATA%\RoomEq\presets ; répertoire
/// de substitution pour les tests).</summary>
public sealed class PresetStore : JsonNamedStore<EqPreset>
{
    public PresetStore(string? directoryOverride = null)
        : base(directoryOverride ?? AppPaths.PresetsDir,
               p => p.Name,
               (p, name) => { var c = p.Clone(); c.Name = name; return c; })
    {
    }
}

/// <summary>Profils micro (JSON dans %APPDATA%\RoomEq\micprofiles ; répertoire
/// de substitution pour les tests).</summary>
public sealed class MicProfileStore : JsonNamedStore<MicProfile>
{
    public MicProfileStore(string? directoryOverride = null)
        : base(directoryOverride ?? AppPaths.MicProfilesDir,
               p => p.Name,
               (p, name) => { p.Name = name; return p; })
    {
    }
}
