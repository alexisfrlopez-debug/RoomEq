using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace RoomEq.Core.Io;

/// <summary>
/// Envoi automatique des journaux de diagnostic vers le dossier Drive de
/// développement (version EXE de test) : l'application rafraîchit le token OAuth,
/// puis téléverse chaque fichier de journal sous un nom unique par machine —
/// « &lt;machine&gt;-roomeq-AAAA-MM-JJ.log » — afin qu'il puisse être relu à distance
/// pour diagnostiquer et améliorer l'application.
/// Aucune donnée audio n'est envoyée : uniquement le texte du journal.
/// Toutes les opérations sont silencieuses (jamais d'exception vers l'appelant).
/// </summary>
public static class DriveLogUploader
{
    private static readonly HttpClient Http = CreateClient();

    public static TelemetryConfig Config { get; } = LoadConfig();

    /// <summary>
    /// Version publique : AUCUNE télémétrie, même si un telemetrie.json était
    /// présent à côté de l'exécutable. Les autres versions suivent la configuration.
    /// </summary>
    private static TelemetryConfig LoadConfig()
        => AppFlavor.IsPublic
            ? new TelemetryConfig { Enabled = false }
            : TelemetryConfig.Load();

    /// <summary>Dernier résultat d'envoi (texte court pour l'interface).</summary>
    public static string LastResult { get; private set; } = "Aucun envoi effectué.";

    /// <summary>Signalé après chaque tentative d'envoi (thread de travail).</summary>
    public static event Action<string>? UploadFinished;

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private static HttpClient CreateClient()
    {
        var c = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        c.DefaultRequestHeaders.UserAgent.ParseAdd(
            "RoomEq-Telemetry/" + (typeof(DriveLogUploader).Assembly.GetName().Version?.ToString(3) ?? "1.0"));
        return c;
    }

    /// <summary>Nom du fichier sur le Drive pour un fichier de journal local.</summary>
    public static string DriveNameFor(string machine, string localFileName)
        => Sanitize(machine) + "-" + Path.GetFileName(localFileName).ToLowerInvariant();

    /// <summary>
    /// Collecte les journaux à envoyer : ceux d'aujourd'hui et d'hier dans tous les
    /// répertoires de journal possibles, plus la copie mémoire si le disque était
    /// inaccessible. Renvoie (chemin local, nom sur le Drive).
    /// </summary>
    public static List<(string LocalPath, string DriveName)> CollectLogFiles(string machine)
        => CollectLogFiles(machine, LogDirectories());

    /// <summary>Variant à répertoires explicites (tests) ; le repli mémoire peut être coupé.</summary>
    public static List<(string LocalPath, string DriveName)> CollectLogFiles(
        string machine, System.Collections.Generic.IEnumerable<string> dirs,
        bool includeMemoryFallback = true)
    {
        var result = new List<(string, string)>();
        string machineName = Sanitize(machine);
        var wanted = new HashSet<string> { Today(), Yesterday() };

        foreach (string dir in dirs)
        {
            try
            {
                if (!Directory.Exists(dir)) continue;
                foreach (string file in Directory.GetFiles(dir, "roomeq-*.log"))
                {
                    string day = DayFromName(Path.GetFileName(file));
                    if (!wanted.Contains(day)) continue;
                    string key = DriveNameFor(machineName, file);
                    if (!result.Any(r => r.Item2 == key))
                        result.Add((file, key));
                }
            }
            catch
            {
                // répertoire illisible : on tente les suivants
            }
        }

        // copie mémoire (journal non écrit sur disque) — envoyée si non vide
        string dump = includeMemoryFallback ? DiagnosticsLog.MemoryDump() : "";
        if (dump.Length > 0)
        {
            try
            {
                string tmp = Path.Combine(Path.GetTempPath(),
                    $"roomeq-memoire-{Today()}.log");
                File.WriteAllText(tmp, dump, Encoding.UTF8);
                result.Add((tmp, machineName + "-roomeq-memoire-" + Today() + ".log"));
            }
            catch
            {
                // impossible d'écrire le tampon temporaire : ignoré
            }
        }
        return result;
    }

    /// <summary>Envoie maintenant les journaux. Renvoie un message court d'état.</summary>
    public static async Task<string> UploadNowAsync()
    {
        if (!Config.Enabled)
        {
            LastResult = "Télémétrie désactivée (telemetrie.json absent).";
            return LastResult;
        }

        try
        {
            string? token = await RefreshAccessTokenAsync().ConfigureAwait(false);
            if (token == null)
            {
                LastResult = "Envoi Drive impossible (authentification refusée).";
            }
            else
            {
                var files = CollectLogFiles(Environment.MachineName);
                int sent = 0;
                foreach (var (path, driveName) in files)
                {
                    try
                    {
                        byte[] content = await File.ReadAllBytesAsync(path).ConfigureAwait(false);
                        if (await UploadOneAsync(token, driveName, content).ConfigureAwait(false))
                            sent++;
                    }
                    catch (Exception ex)
                    {
                        DiagnosticsLog.Warn($"Télémétrie : fichier « {Path.GetFileName(path)} » non envoyé — {ex.Message}");
                    }
                }
                LastResult = sent == 0
                    ? "Aucun journal envoyé (rien de nouveau)."
                    : $"{sent} journal(aux) envoyé(s) vers le Drive.";
                DiagnosticsLog.Info("Télémétrie : " + LastResult);
            }
        }
        catch (Exception ex)
        {
            LastResult = "Envoi Drive impossible : " + ex.Message;
            DiagnosticsLog.Warn("Télémétrie : " + LastResult);
        }

        UploadFinished?.Invoke(LastResult);
        return LastResult;
    }

    // ─────────────────────────── internals ───────────────────────────

    private static async Task<string?> RefreshAccessTokenAsync()
    {
        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["client_id"] = Config.ClientId,
            ["client_secret"] = Config.ClientSecret,
            ["refresh_token"] = Config.RefreshToken,
            ["grant_type"] = "refresh_token"
        });
        using var resp = await Http.PostAsync(Config.TokenUri, form).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts).ConfigureAwait(false);
        return payload.TryGetProperty("access_token", out var tok) ? tok.GetString() : null;
    }

    /// <summary>Crée le fichier s'il n'existe pas, sinon met à jour son contenu (PATCH).</summary>
    private static async Task<bool> UploadOneAsync(string token, string driveName, byte[] content)
    {
        string? existingId = await FindFileIdAsync(token, driveName).ConfigureAwait(false);

        if (existingId != null)
        {
            using var patch = new ByteArrayContent(content);
            patch.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
            using var resp = await Http.PatchAsync(
                $"https://www.googleapis.com/upload/drive/v3/files/{existingId}?uploadType=media",
                patch).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        }

        using var mp = new MultipartContent("related", "==roomq==");
        var meta = new ByteArrayContent(
            Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new { name = driveName, parents = new[] { Config.FolderId } })));
        meta.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        var body = new ByteArrayContent(content);
        body.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        mp.Add(meta);
        mp.Add(body);

        using var resp2 = await Http.PostAsync(
            "https://www.googleapis.com/upload/drive/v3/files?uploadType=multipart&fields=id",
            mp).ConfigureAwait(false);
        return resp2.IsSuccessStatusCode;
    }

    private static async Task<string?> FindFileIdAsync(string token, string driveName)
    {
        string q = $"name = '{driveName.Replace("'", "\\'")}' and " +
                   $"'{Config.FolderId}' in parents and trashed = false";
        using var req = new HttpRequestMessage(HttpMethod.Get,
            "https://www.googleapis.com/drive/v3/files?q=" + Uri.EscapeDataString(q) +
            "&fields=files(id,name)&pageSize=5");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var resp = await Http.SendAsync(req).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) return null;
        var payload = await resp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts).ConfigureAwait(false);
        if (payload.ValueKind != JsonValueKind.Array && payload.TryGetProperty("files", out var files)
            && files.GetArrayLength() > 0)
            return files[0].GetProperty("id").GetString();
        return null;
    }

    private static System.Collections.Generic.IEnumerable<string> LogDirectories()
    {
        yield return Path.Combine(AppContext.BaseDirectory, "logs");
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrEmpty(local))
            yield return Path.Combine(local, "RoomEq", "logs");
    }

    internal static string Sanitize(string machine)
    {
        var sb = new StringBuilder();
        foreach (char c in machine)
            sb.Append(char.IsLetterOrDigit(c) || c is '-' or '_' ? c : '-');
        return sb.Length == 0 ? "machine" : sb.ToString();
    }

    internal static string Today() => DateTime.Now.ToString("yyyy-MM-dd");
    internal static string Yesterday() => DateTime.Now.AddDays(-1).ToString("yyyy-MM-dd");

    /// <summary>Extrait la date du nom « roomeq-AAAA-MM-JJ.log » ("" si non reconnue).</summary>
    internal static string DayFromName(string fileName)
    {
        string name = Path.GetFileName(fileName);
        int i = name.IndexOf("roomeq-", StringComparison.Ordinal);
        if (i < 0) return "";
        string rest = name[(i + "roomeq-".Length)..];
        int dash = rest.IndexOf('-');
        if (dash == 0) rest = rest[1..];                     // préfixe machine : -roomeq-
        dash = rest.IndexOf('-');
        if (dash == 0) rest = rest[1..];                     // cas « memoire-… » sans date directe
        return rest.Length >= 10 && rest[..10].Count(char.IsDigit) == 8 ? rest[..10] : "";
    }
}
