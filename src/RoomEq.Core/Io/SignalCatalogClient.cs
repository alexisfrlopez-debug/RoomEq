using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace RoomEq.Core.Io;

/// <summary>
/// Téléchargement du catalogue de signaux publié avec le projet : manifeste
/// JSON puis fichiers WAV, depuis le dossier officiel du dépôt GitHub
/// (domaine verrouillé, voir SignalUrls). Garde-fous : limite de taille
/// stricte appliquée pendant la lecture, URL revalidée avant chaque requête,
/// aucune exception ne remonte à l'interface (messages d'erreur en clair).
/// </summary>
public sealed class SignalCatalogClient
{
    /// <summary>Taille maximale acceptée pour le manifeste (généreux : quelques Ko).</summary>
    public const long MaxManifestBytes = 512_000;

    private static readonly HttpClient Http = CreateClient();

    private static HttpClient CreateClient()
    {
        var c = new HttpClient(new HttpClientHandler { AllowAutoRedirect = false })
        {
            Timeout = TimeSpan.FromSeconds(30)
        };
        c.DefaultRequestHeaders.UserAgent.ParseAdd("RoomEq/1.9 (signaux de test)");
        return c;
    }

    /// <summary>
    /// Télécharge et analyse le manifeste. Renvoi la liste validée, ou une erreur
    /// en clair (réseau, JSON invalide, règle de sécurité violée).
    /// </summary>
    public async Task<(List<DownloadableSignalItem>? Signals, string? Error)> FetchManifestAsync(
        CancellationToken ct = default)
    {
        try
        {
            byte[] data = await DownloadCappedAsync(SignalUrls.ManifestUrl, MaxManifestBytes, null, ct)
                          .ConfigureAwait(false);
            string json = System.Text.Encoding.UTF8.GetString(data);
            if (SignalManifest.TryParse(json, out var signals, out var error))
                return (signals, null);
            return (null, error);
        }
        catch (OperationCanceledException)
        {
            return (null, "Téléchargement du catalogue annulé.");
        }
        catch (Exception ex)
        {
            return (null, "Catalogue inaccessible : " + ex.Message);
        }
    }

    /// <summary>
    /// Télécharge un fichier de signal validé par le manifeste (URL revalidée,
    /// taille attendue contrôlée). Renvoi le contenu ou une erreur en clair.
    /// </summary>
    public async Task<(byte[]? Content, string? Error)> DownloadFileAsync(
        DownloadableSignalItem item, IProgress<double>? progress, CancellationToken ct = default)
    {
        string url = SignalUrls.FileUrl(item.FileName);
        if (!SignalUrls.IsAllowedFileUrl(url))
            return (null, "URL non autorisée : " + url);

        try
        {
            byte[] data = await DownloadCappedAsync(url, item.Bytes, progress, ct).ConfigureAwait(false);
            if (data.Length != item.Bytes)
                return (null, $"Taille inattendue pour {item.FileName} : {data.Length} octets (attendu {item.Bytes}).");
            return (data, null);
        }
        catch (OperationCanceledException)
        {
            return (null, "Téléchargement annulé.");
        }
        catch (Exception ex)
        {
            return (null, $"Téléchargement impossible ({item.FileName}) : {ex.Message}");
        }
    }

    /// <summary>Lecture bornée d'une URL : la taille maximale est imposée au fil de l'eau.</summary>
    private static async Task<byte[]> DownloadCappedAsync(
        string url, long maxBytes, IProgress<double>? progress, CancellationToken ct)
    {
        using var resp = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new IOException("le serveur a répondu " + (int)resp.StatusCode);

        if (resp.Content.Headers.ContentLength is long declared && declared > maxBytes)
            throw new IOException($"fichier trop volumineux ({declared} octets).");

        long? total = resp.Content.Headers.ContentLength;
        await using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();

        byte[] chunk = new byte[81920];
        long readTotal = 0;
        while (true)
        {
            int read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false);
            if (read == 0) break;
            readTotal += read;
            if (readTotal > maxBytes)
                throw new IOException("fichier plus volumineux qu'annoncé — téléchargement interrompu.");
            buffer.Write(chunk, 0, read);
            if (progress != null && total is > 0)
                progress.Report(Math.Min(1.0, readTotal / (double)total.Value));
        }
        return buffer.ToArray();
    }
}
