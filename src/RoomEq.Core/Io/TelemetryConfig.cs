using System.IO;
using System.Text.Json;

namespace RoomEq.Core.Io;

/// <summary>
/// Configuration de la télémétrie de la version de test : identifiants OAuth du
/// compte de développement et dossier Drive de destination des journaux.
/// Lue dans « telemetrie.json » à côté de l'exécutable (sinon dans
/// %LOCALAPPDATA%\RoomEq). Fichier absent ou désactivé = télémétrie coupée,
/// l'application fonctionne exactement comme la version normale.
/// </summary>
public sealed class TelemetryConfig
{
    public bool Enabled { get; init; }
    public string ClientId { get; init; } = "";
    public string ClientSecret { get; init; } = "";
    public string RefreshToken { get; init; } = "";
    public string TokenUri { get; init; } = "https://oauth2.googleapis.com/token";
    /// <summary>ID du dossier Drive « journaux-app » recevant les fichiers.</summary>
    public string FolderId { get; init; } = "";

    private const string FileName = "telemetrie.json";

    // le fichier de configuration est écrit par les outils du projet avec des clés
    // snake_case (« client_id », « folder_id »…) : liaison explicite requise
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Charge la configuration (jamais d'exception : fichier absent/illégal = désactivée).</summary>
    public static TelemetryConfig Load()
    {
        foreach (string dir in CandidateDirs())
        {
            var cfg = LoadIn(dir);
            if (cfg != null) return cfg;
        }
        return new TelemetryConfig { Enabled = false };
    }

    /// <summary>Tente de charger la configuration depuis un répertoire précis (null si absente/illégale).</summary>
    public static TelemetryConfig? LoadIn(string dir)
    {
        try
        {
            string path = Path.Combine(dir, FileName);
            if (!File.Exists(path)) return null;
            var cfg = JsonSerializer.Deserialize<TelemetryConfig>(File.ReadAllText(path), JsonOpts);
            if (cfg == null) return null;
            bool complete = cfg.Enabled
                            && cfg.ClientId.Length > 0 && cfg.ClientSecret.Length > 0
                            && cfg.RefreshToken.Length > 0 && cfg.FolderId.Length > 0;
            return new TelemetryConfig
            {
                Enabled = complete,
                ClientId = cfg.ClientId,
                ClientSecret = cfg.ClientSecret,
                RefreshToken = cfg.RefreshToken,
                TokenUri = cfg.TokenUri,
                FolderId = cfg.FolderId
            };
        }
        catch
        {
            // configuration illisible : télémétrie désactivée
            return null;
        }
    }

    private static System.Collections.Generic.IEnumerable<string> CandidateDirs()
    {
        yield return AppContext.BaseDirectory;
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local))
            yield return Path.Combine(local, "RoomEq");
    }
}
