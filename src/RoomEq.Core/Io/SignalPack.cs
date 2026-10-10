using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace RoomEq.Core.Io;

/// <summary>
/// Un signal de test TÉLÉCHARGEABLE décrit par le manifeste publié avec le
/// dépôt GitHub du projet (dossier « signaux/ »). Contrairement aux signaux
/// générés à la volée par le moteur, ceux-ci sont des fichiers WAV prêts à
/// l'écoute (impulsions, bruits colorés, multi-tones, balayages longs…).
/// </summary>
public sealed record DownloadableSignalItem(
    string Id,
    string DisplayName,
    string Description,
    string FileName,
    long Bytes,
    string Sha256);

/// <summary>
/// Analyse et valide le manifeste JSON des signaux téléchargeables. Toutes les
/// règles de sécurité sont vérifiées AVANT tout contact réseau sur un fichier :
/// nom de fichier sûr (pas de traversal), taille bornée, empreinte SHA-256
/// bien formée, identifiants uniques.
/// </summary>
public static partial class SignalManifest
{
    private const int MaxSignals = 50;
    private const long MinBytes = 1;
    private const long MaxBytes = 25_000_000; // 25 Mo par fichier, très au-dessus du besoin

    [GeneratedRegex("^[a-z0-9][a-z0-9-]{0,63}$")]
    private static partial Regex IdPattern();

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,127}$")]
    private static partial Regex FileNamePattern();

    [GeneratedRegex("^[0-9a-f]{64}$")]
    private static partial Regex Sha256Pattern();

    /// <summary>Analyse le JSON du manifeste. Renvoi la liste validée ou une erreur explicite.</summary>
    public static bool TryParse(string json, out List<DownloadableSignalItem> signals, out string error)
    {
        signals = new List<DownloadableSignalItem>();
        error = "";

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(json);
        }
        catch (JsonException ex)
        {
            error = "Manifeste illisible (JSON invalide) : " + ex.Message;
            return false;
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("signals", out var arr) || arr.ValueKind != JsonValueKind.Array)
            {
                error = "Manifeste invalide : tableau « signals » absent.";
                return false;
            }
            if (arr.GetArrayLength() == 0)
            {
                error = "Manifeste invalide : aucun signal proposé.";
                return false;
            }
            if (arr.GetArrayLength() > MaxSignals)
            {
                error = $"Manifeste invalide : plus de {MaxSignals} signaux.";
                return false;
            }

            var seenIds = new HashSet<string>(StringComparer.Ordinal);
            var seenFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var el in arr.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.Object)
                {
                    error = "Manifeste invalide : entrée non-objet dans « signals ».";
                    return false;
                }

                string id = GetString(el, "id");
                string displayName = GetString(el, "displayName");
                string description = GetString(el, "description");
                string fileName = GetString(el, "fileName");
                string sha = GetString(el, "sha256");
                long bytes = el.TryGetProperty("bytes", out var b) && b.TryGetInt64(out var v) ? v : -1;

                if (!IdPattern().IsMatch(id))
                {
                    error = $"Manifeste invalide : identifiant incorrect « {id} ».";
                    return false;
                }
                if (displayName.Length == 0 || displayName.Length > 80)
                {
                    error = $"Manifeste invalide : nom d'affichage incorrect pour « {id} ».";
                    return false;
                }
                if (description.Length > 300)
                {
                    error = $"Manifeste invalide : description trop longue pour « {id} ».";
                    return false;
                }
                if (!FileNamePattern().IsMatch(fileName) || !fileName.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
                {
                    error = $"Manifeste invalide : nom de fichier refusé pour « {id} ».";
                    return false;
                }
                if (bytes < MinBytes || bytes > MaxBytes)
                {
                    error = $"Manifeste invalide : taille hors bornes pour « {id} ».";
                    return false;
                }
                if (!Sha256Pattern().IsMatch(sha))
                {
                    error = $"Manifeste invalide : empreinte SHA-256 incorrecte pour « {id} ».";
                    return false;
                }
                if (!seenIds.Add(id))
                {
                    error = $"Manifeste invalide : identifiant en doublon « {id} ».";
                    return false;
                }
                if (!seenFiles.Add(fileName))
                {
                    error = $"Manifeste invalide : fichier en doublon « {fileName} ».";
                    return false;
                }

                signals.Add(new DownloadableSignalItem(id, displayName, description, fileName, bytes, sha));
            }
        }

        return true;
    }

    private static string GetString(JsonElement el, string name)
        => el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String
            ? v.GetString() ?? ""
            : "";
}

/// <summary>
/// URLs du catalogue de signaux, hébergé à côté des sources du projet sur GitHub
/// (dépôt public, branche main). Domaine et chemin VERROUILLÉS : seuls les
/// fichiers listés par le manifeste, situés dans ce dossier exact, sont
/// téléchargeables — aucune autre source n'est acceptée.
/// </summary>
public static class SignalUrls
{
    public const string BaseDir = "https://raw.githubusercontent.com/alexisfrlopez-debug/RoomEq/main/signaux/";
    public const string ManifestUrl = BaseDir + "manifest.json";

    /// <summary>URL de téléchargement d'un fichier validé par le manifeste.</summary>
    public static string FileUrl(string fileName) => BaseDir + Uri.EscapeDataString(fileName);

    /// <summary>L'URL appartient-elle au dossier officiel des signaux ? (https, domaine exact, chemin exact)</summary>
    public static bool IsAllowedFileUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri)) return false;
        if (uri.Scheme != Uri.UriSchemeHttps) return false;
        if (!string.Equals(uri.Host, "raw.githubusercontent.com", StringComparison.OrdinalIgnoreCase)) return false;
        if (!string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment)) return false;
        if (uri.AbsolutePath.Contains("..", StringComparison.Ordinal)) return false;
        return uri.AbsolutePath.StartsWith("/alexisfrlopez-debug/RoomEq/main/signaux/", StringComparison.Ordinal);
    }
}
