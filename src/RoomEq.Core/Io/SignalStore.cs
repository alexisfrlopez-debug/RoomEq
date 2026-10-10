using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace RoomEq.Core.Io;

/// <summary>Un fichier de signal présent dans la bibliothèque locale de l'application.</summary>
public sealed record DownloadedSignalFile(string FileName, string DisplayName, long Bytes);

/// <summary>
/// Bibliothèque locale des signaux de test téléchargeables
/// (%APPDATA%\RoomEq\signaux). Garde-fous : seuls des noms de fichiers sûrs
/// (validés par le manifeste) peuvent être écrits, l'empreinte SHA-256 attendue
/// est vérifiée avant enregistrement, et un index JSON mémorise les noms
/// conviviaux pour l'affichage dans le menu déroulant.
/// </summary>
public sealed class SignalStore
{
    private const long MaxBytes = 25_000_000;

    private readonly string _dir;
    private readonly string _indexPath;

    private sealed class IndexFile
    {
        [JsonPropertyName("files")]
        public List<IndexEntry> Files { get; set; } = new();
    }

    private sealed class IndexEntry
    {
        [JsonPropertyName("fileName")] public string FileName { get; set; } = "";
        [JsonPropertyName("displayName")] public string DisplayName { get; set; } = "";
    }

    /// <summary>Dossier réel de la bibliothèque (tests : répertoire temporaire injecté).</summary>
    public string DirectoryPath => _dir;

    public SignalStore(string? overrideDir = null)
    {
        string baseDir = overrideDir ?? AppPaths.DataDir;
        _dir = Path.Combine(baseDir, "signaux");
        _indexPath = Path.Combine(_dir, "index.json");
        Directory.CreateDirectory(_dir);
    }

    /// <summary>Liste les signaux téléchargés (nom de fichier + nom convivial si connu).</summary>
    public IReadOnlyList<DownloadedSignalFile> List()
    {
        var names = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(_indexPath))
            {
                var index = JsonSerializer.Deserialize<IndexFile>(File.ReadAllText(_indexPath));
                foreach (var e in index?.Files ?? new List<IndexEntry>())
                    if (!string.IsNullOrWhiteSpace(e.FileName))
                        names[e.FileName] = e.DisplayName;
            }
        }
        catch
        {
            // un index corrompu ne doit jamais masquer les fichiers présents
        }

        var result = new List<DownloadedSignalFile>();
        try
        {
            foreach (var f in Directory.EnumerateFiles(_dir, "*.wav").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
            {
                var fi = new FileInfo(f);
                string fileName = fi.Name;
                result.Add(new DownloadedSignalFile(
                    fileName,
                    names.TryGetValue(fileName, out var dn) && dn.Length > 0 ? dn : Path.GetFileNameWithoutExtension(fileName),
                    fi.Length));
            }
        }
        catch
        {
            // lecture de dossier impossible : bibliothèque vide
        }
        return result;
    }

    /// <summary>Chemin complet d'un fichier de la bibliothèque (nom déjà validé).</summary>
    public string PathFor(string fileName) => Path.Combine(_dir, fileName);

    /// <summary>Le fichier est-il déjà présent (taille cohérente) ?</summary>
    public bool Contains(string fileName, long expectedBytes)
    {
        try
        {
            var fi = new FileInfo(PathFor(fileName));
            return fi.Exists && fi.Length == expectedBytes;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Enregistre un signal téléchargé : vérifie le SHA-256 attendu, refuse tout
    /// nom de fichier non sûr et toute taille excessive, puis met à jour l'index.
    /// Renvoi vrai si le fichier a été écrit (ou était déjà identique).
    /// </summary>
    public bool Save(string fileName, byte[] content, string expectedSha256, string displayName)
    {
        if (fileName != AppPaths.SanitizeFileName(fileName)
            || fileName.Contains('/')
            || fileName.Contains('\\')
            || fileName.StartsWith('.')
            || !fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Nom de fichier de signal refusé : " + fileName);

        if (content.Length == 0 || content.Length > MaxBytes)
            throw new ArgumentException("Taille de signal hors bornes.");

        string actual = Convert.ToHexString(SHA256.HashData(content)).ToLowerInvariant();
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Empreinte SHA-256 incorrecte pour " + fileName + " — téléchargement rejeté.");

        string target = PathFor(fileName);
        try
        {
            if (!Contains(fileName, content.Length))
                File.WriteAllBytes(target, content);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            throw new IOException("Écriture impossible dans la bibliothèque de signaux : " + ex.Message, ex);
        }

        AddToIndex(fileName, displayName);
        return true;
    }

    private void AddToIndex(string fileName, string displayName)
    {
        var index = new IndexFile();
        try
        {
            if (File.Exists(_indexPath))
            {
                var loaded = JsonSerializer.Deserialize<IndexFile>(File.ReadAllText(_indexPath));
                if (loaded != null) index = loaded;
            }
        }
        catch
        {
            // index illisible : on repart d'un index neuf
        }

        index.Files.RemoveAll(f => string.Equals(f.FileName, fileName, StringComparison.OrdinalIgnoreCase));
        index.Files.Add(new IndexEntry { FileName = fileName, DisplayName = displayName });

        try
        {
            File.WriteAllText(_indexPath, JsonSerializer.Serialize(index, JsonOpts.Default));
        }
        catch
        {
            // l'échec d'index n'invalide pas le fichier déjà écrit
        }
    }
}
