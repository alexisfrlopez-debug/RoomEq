using System.Diagnostics;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.RegularExpressions;

namespace RoomEq.Audio.Routing;

/// <summary>Progression de l'installation d'un pilote de carte virtuelle.</summary>
public sealed record InstallProgress(string Phase, double? Percent, string Message);

/// <summary>Résultat d'une installation automatique.</summary>
public sealed record InstallResult(
    bool Success,
    string Message,
    string? SetupPath = null,
    string? LogPath = null);

/// <summary>
/// Noyau pur (sans réseau ni processus) de l'installateur : validation des URL
/// (domaines officiels verrouillés), sécurité des archives (anti ZipSlip),
/// recherche de l'exécutable d'installation et vérification du format PE.
/// Toutes les méthodes sont testables hors Windows.
/// </summary>
public static class InstallerCore
{
    /// <summary>Domaines depuis lesquels un téléchargement est autorisé.</summary>
    public static readonly IReadOnlyList<string> AllowedHosts = new[]
    {
        "download.vb-audio.com",
        "www.download.vb-audio.com"
    };

    /// <summary>Domaines autorisés pour les pages officielles de découverte de version.</summary>
    public static readonly IReadOnlyList<string> AllowedPageHosts = new[]
    {
        "vb-audio.com",
        "www.vb-audio.com",
        "voicemeeter.com",
        "www.voicemeeter.com"
    };

    /// <summary>Refuse toute URL de page hors HTTPS et hors domaines officiels.</summary>
    public static void ValidatePageUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("URL de page officielle refusée (HTTPS requis) : " + url);
        if (!AllowedPageHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Domaine de page non autorisé : " + uri.Host);
    }

    /// <summary>
    /// Extrait d'une page officielle les URL de téléchargement correspondant au motif,
    /// absolutise les chemins relatifs (https://download.vb-audio.com/), ne garde que
    /// les domaines verrouillés (<see cref="ValidateUrl"/>) et retourne la version la
    /// plus récente (vNNN max). Null si aucune URL valide n'est trouvée.
    /// </summary>
    public static string? ExtractLatestDownloadUrl(string html, string pattern)
    {
        if (string.IsNullOrEmpty(html) || string.IsNullOrEmpty(pattern)) return null;

        var found = new List<(string Url, int Version)>();
        foreach (Match m in Regex.Matches(html, pattern, RegexOptions.IgnoreCase))
        {
            string raw = m.Value;
            string url = raw.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? raw
                : "https://download.vb-audio.com/" + raw.TrimStart('/');
            try { ValidateUrl(url); }
            catch { continue; } // domaine non officiel : lien ignoré

            var vm = Regex.Match(url, "_v([0-9]+)", RegexOptions.IgnoreCase);
            found.Add((url, vm.Success ? int.Parse(vm.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : 0));
        }

        if (found.Count == 0) return null;
        found.Sort((a, b) => b.Version.CompareTo(a.Version));
        return found[0].Url;
    }

    /// <summary>Refuse toute URL hors HTTPS et hors domaines officiels.</summary>
    public static void ValidateUrl(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || uri.Scheme != Uri.UriSchemeHttps)
            throw new InvalidOperationException("URL de téléchargement refusée (HTTPS requis) : " + url);
        if (!AllowedHosts.Contains(uri.Host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"Domaine non autorisé pour le téléchargement officiel : {uri.Host}");
    }

    /// <summary>
    /// Vérifie qu'une entrée d'archive ne tente pas de sortir du dossier
    /// d'extraction (chemins absolus, « .. », lettres de lecteur).
    /// </summary>
    public static void ValidateEntryName(string entryName)
    {
        if (string.IsNullOrEmpty(entryName))
            throw new InvalidOperationException("Archive corrompue : entrée vide.");
        string name = entryName.Replace('\\', '/');
        if (name.StartsWith('/') || name.Contains(':'))
            throw new InvalidOperationException(
                "Archive refusée : chemin absolu détecté (" + entryName + ").");
        foreach (var seg in name.Split('/', StringSplitOptions.RemoveEmptyEntries))
            if (seg == "..")
                throw new InvalidOperationException(
                    "Archive refusée : traversée de répertoire détectée (" + entryName + ").");
    }

    /// <summary>
    /// Recherche l'exécutable d'installation dans une liste de noms d'entrées
    /// (préférence pour la variante X64), null si absent.
    /// </summary>
    public static string? FindSetupExecutable(
        IEnumerable<string> entryNames, IReadOnlyList<string> candidates)
    {
        string? best = null;
        int bestScore = -1;
        foreach (var entry in entryNames)
        {
            string leaf = Path.GetFileName(entry.Replace('\\', '/'));
            for (int i = 0; i < candidates.Count; i++)
            {
                if (leaf.Equals(candidates[i], StringComparison.OrdinalIgnoreCase))
                {
                    // index bas = prioritaire ; les noms contenant x64 valent un bonus
                    int score = candidates.Count - i
                                + (leaf.Contains("x64", StringComparison.OrdinalIgnoreCase) ? 10 : 0);
                    if (score > bestScore) { bestScore = score; best = entry; }
                }
            }
        }
        return best;
    }

    /// <summary>Vérifie la signature PE (« MZ ») d'un exécutable Windows téléchargé.</summary>
    public static void ValidateExecutable(Stream content)
    {
        Span<byte> header = stackalloc byte[2];
        int read = content.Read(header);
        content.Position = 0;
        if (read < 2 || header[0] != 0x4D || header[1] != 0x5A)
            throw new InvalidOperationException(
                "Fichier d'installation invalide (signature PE absente) — téléchargement supprimé.");
    }
}

/// <summary>
/// Installateur automatique d'un pilote de carte son virtuelle : téléchargement
/// depuis les domaines officiels, validation (taille, structure, PE), extraction
/// sécurisée, lancement en élévation (UAC) puis attente de l'apparition des
/// périphériques. Réservé à Windows.
/// </summary>
public sealed class VirtualAudioInstaller
{
    private static readonly HttpClient Http = CreateClient();
    private readonly IProgress<InstallProgress>? _progress;

    /// <summary>Répertoire de travail des téléchargements.</summary>
    public string WorkDirectory { get; }

    public VirtualAudioInstaller(IProgress<InstallProgress>? progress = null, string? workDir = null)
    {
        _progress = progress;
        WorkDirectory = workDir ?? Path.Combine(Path.GetTempPath(), "RoomEq-install");
    }

    private static HttpClient CreateClient()
    {
        var c = new HttpClient(new SocketsHttpHandler
        {
            AutomaticDecompression = System.Net.DecompressionMethods.None
        })
        {
            Timeout = TimeSpan.FromMinutes(6)
        };
        string version = typeof(VirtualAudioInstaller).Assembly.GetName().Version?.ToString(3) ?? "1.0";
        c.DefaultRequestHeaders.UserAgent.ParseAdd($"RoomEq/{version} (virtual-audio-installer)");
        return c;
    }

    private void Report(string phase, double? percent, string message)
        => _progress?.Report(new InstallProgress(phase, percent, message));

    /// <summary>
    /// Télécharge, valide et lance l'installateur du fournisseur, puis attend
    /// l'apparition des périphériques via <paramref name="probeInstalled"/>.
    /// </summary>
    /// <param name="provider">Fournisseur dont on installe le pilote.</param>
    /// <param name="ct">Jeton d'annulation (fenêtre fermée…).</param>
    /// <param name="probeInstalled">Sonde de détection (retourne vrai quand installé).</param>
    /// <param name="arrivalTimeout">Durée maximale d'attente après lancement.</param>
    public async Task<InstallResult> InstallAsync(
        IVirtualAudioProvider provider,
        CancellationToken ct = default,
        Func<bool>? probeInstalled = null,
        TimeSpan? arrivalTimeout = null)
    {
        var spec = provider.Installer;
        string log = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RoomEq", "logs");
        string? logPath = null;

        try
        {
            if (!OperatingSystem.IsWindows())
                return new InstallResult(false,
                    "L'installation automatique d'un pilote est réservée à Windows.");

            if (provider.IsPreinstalled)
                return new InstallResult(true,
                    $"« {provider.DisplayName} » ne nécessite aucune installation.");
            if (!spec.Supported)
                return new InstallResult(false,
                    $"« {provider.DisplayName} » ne propose pas d'installation automatique : " +
                    "passez par la page du produit.");

            Directory.CreateDirectory(WorkDirectory);
            Directory.CreateDirectory(log);

            // 1) Téléchargement (première URL candidates qui répond)
            Report("download", 0, "Téléchargement depuis le site officiel…");
            var (payload, usedUrl) = await DownloadAsync(spec, ct).ConfigureAwait(false);
            string fileName = Path.GetFileName(new Uri(usedUrl).LocalPath);
            if (fileName.Length == 0) fileName = provider.Id + ".download";
            string localPath = Path.Combine(WorkDirectory, fileName);
            await File.WriteAllBytesAsync(localPath, payload, ct).ConfigureAwait(false);
            logPath = Path.Combine(log, $"install-{provider.Id}-{DateTime.Now:yyyyMMdd-HHmmss}.log");
            await File.WriteAllTextAsync(logPath,
                $"[{DateTime.Now:O}] {provider.Id} ← {fileName} ({payload.Length} octets)\n", ct)
                .ConfigureAwait(false);

            // 2) Validation + préparation de l'exécutable
            Report("validate", null, "Vérification de l'archive…");
            string setupPath = PrepareSetup(provider, payload, localPath);

            // 3) Lancement en élévation (l'utilisateur voit l'invite UAC)
            bool alreadyElevated = IsCurrentProcessElevated();
            Report("launch", null, alreadyElevated
                ? "Lancement de l'installateur (RoomEq a déjà les droits administrateur : aucune invite UAC n'apparaîtra)…"
                : "Lancement de l'installateur — acceptez l'invite Windows (UAC)…");
            Process? proc = LaunchElevated(setupPath, Path.GetDirectoryName(setupPath));
            if (proc == null)
                return new InstallResult(false,
                    "Invite UAC refusée ou fermée — relancez l'installation et cliquez « Oui », " +
                    "ou lancez manuellement : " + setupPath,
                    setupPath, logPath);

            if (!alreadyElevated)
                Report("launch", null,
                    "Installateur lancé. Si aucune invite n'est apparue, l'UAC est en mode " +
                    "« ne jamais notifier » : l'installation continue normalement.");

            // 4) Attente de l'apparition des périphériques
            var timeout = arrivalTimeout ?? TimeSpan.FromSeconds(300);
            Report("detect", null,
                $"{spec.PostLaunchHint} Détection des périphériques « {provider.DisplayName} » (jusqu'à {timeout.TotalSeconds:N0} s)…");
            bool arrived = await WaitArrivalAsync(probeInstalled, proc, timeout, ct).ConfigureAwait(false);

            Report("done", 100, arrived ? "Périphériques détectés." : "Installation terminée (à vérifier).");
            await File.AppendAllTextAsync(logPath,
                $"[{DateTime.Now:O}] arrivée={arrived}\n", ct).ConfigureAwait(false);

            return arrived
                ? new InstallResult(true,
                    $"Périphériques « {provider.DisplayName} » détectés : cliquez « ⟳ » puis « Définir par défaut ».",
                    setupPath, logPath)
                : new InstallResult(true,
                    $"Installateur terminé. Si « {provider.DisplayName} » n'apparaît pas après « ⟳ », redémarrez Windows (finalisation du pilote).",
                    setupPath, logPath);
        }
        catch (OperationCanceledException)
        {
            return new InstallResult(false, "Installation annulée.");
        }
        catch (Exception ex)
        {
            Report("error", null, "Échec : " + ex.Message);
            try
            {
                if (logPath != null)
                    await File.AppendAllTextAsync(logPath,
                        $"[{DateTime.Now:O}] ERREUR {ex.GetType().Name}: {ex.Message}\n", ct)
                        .ConfigureAwait(false);
            }
            catch { /* le journal ne doit jamais masquer l'erreur */ }
            return new InstallResult(false,
                "Installation impossible : " + ex.Message +
                (spec.ProductPageUrl.Length > 0
                    ? " — téléchargement manuel possible depuis la page du produit."
                    : ""), null, logPath);
        }
    }

    private async Task<(byte[] Payload, string Url)> DownloadAsync(VirtualInstallerSpec spec, CancellationToken ct)
    {
        var candidates = new List<string>(spec.DownloadUrls);

        // 0) Découverte de la version courante sur la page officielle (meilleur effort).
        // Les éditeurs renomment leurs archives à chaque version : la page produit
        // fait foi, les URL statiques servent de repli.
        if (spec.DiscoveryPageUrl.Length > 0 && spec.DiscoveryPattern.Length > 0)
        {
            try
            {
                InstallerCore.ValidatePageUrl(spec.DiscoveryPageUrl);
                string html = await Http.GetStringAsync(spec.DiscoveryPageUrl, ct).ConfigureAwait(false);
                string? latest = InstallerCore.ExtractLatestDownloadUrl(html, spec.DiscoveryPattern);
                if (latest != null
                    && (candidates.Count == 0
                        || !string.Equals(candidates[0], latest, StringComparison.OrdinalIgnoreCase)))
                {
                    candidates.Insert(0, latest);
                    Report("download", 0, "Version courante détectée sur le site officiel : "
                        + Path.GetFileName(new Uri(latest).LocalPath));
                }
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch { /* découverte indisponible : on garde les URL statiques */ }
        }

        if (candidates.Count == 0)
            throw new InvalidOperationException("Aucune source officielle disponible pour ce protocole.");

        Exception? last = null;
        foreach (var url in candidates)
        {
            try
            {
                InstallerCore.ValidateUrl(url);
                using var resp = await Http.GetAsync(url,
                    HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
                resp.EnsureSuccessStatusCode();

                long? total = resp.Content.Headers.ContentLength;
                byte[] buffer = new byte[81920];
                using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                using var ms = new MemoryStream();
                long read = 0;
                int n;
                while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
                {
                    await ms.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                    read += n;
                    if (total is > 0)
                        Report("download", Math.Min(99.0, read * 100.0 / total.Value),
                            $"Téléchargement… {read / 1e6:0.0} / {total.Value / 1e6:0.0} Mo");
                }

                if (ms.Length < spec.MinBytes)
                    throw new InvalidOperationException(
                        $"Archive trop petite ({ms.Length} octets) — téléchargement suspect refusé.");
                return (ms.ToArray(), url);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (TaskCanceledException) when (!ct.IsCancellationRequested) { last = new TimeoutException(url); }
            // un candidat défaillant (404, taille suspecte, coupure réseau…) ne doit
            // pas compromettre les suivants
            catch (Exception ex) when (ex is HttpRequestException or IOException or InvalidOperationException or TimeoutException)
            { last = ex; }
        }
        throw new InvalidOperationException(
            "Aucune URL officielle n'a répondu" + (last != null ? " (" + last.Message + ")" : "") + ".");
    }

    /// <summary>Valide le conteneur téléchargé et retourne le chemin de l'installateur prêt à lancer.</summary>
    private string PrepareSetup(IVirtualAudioProvider provider, byte[] payload, string localPath)
    {
        var spec = provider.Installer;
        string containerDir = Path.Combine(WorkDirectory, provider.Id);

        if (string.Equals(spec.Container, "exe", StringComparison.OrdinalIgnoreCase))
        {
            // Installateur .exe direct (ex. VoiceMeeter) : signature PE + taille
            using var ms = new MemoryStream(payload);
            InstallerCore.ValidateExecutable(ms);
            Directory.CreateDirectory(containerDir);
            string target = Path.Combine(containerDir, Path.GetFileName(
                spec.SetupExecutableNames[0].Equals(Path.GetFileName(localPath), StringComparison.OrdinalIgnoreCase)
                    ? Path.GetFileName(localPath)
                    : spec.SetupExecutableNames[0]));
            File.WriteAllBytes(target, payload);
            return target;
        }

        // Archive zip : validation anti-ZipSlip puis extraction ciblée de l'installateur
        string extractDir = Path.Combine(containerDir, "extracted");
        Directory.CreateDirectory(extractDir);
        using (var ms = new MemoryStream(payload))
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Read))
        {
            string? setupEntry = InstallerCore.FindSetupExecutable(
                zip.Entries.Select(e => e.FullName), spec.SetupExecutableNames);
            if (setupEntry == null)
                throw new InvalidOperationException(
                    "Archive officielle inattendue : " + string.Join(", ", spec.SetupExecutableNames) +
                    " introuvable.");

            foreach (var e in zip.Entries)
                InstallerCore.ValidateEntryName(e.FullName);

            string dest = Path.GetFullPath(Path.Combine(extractDir,
                setupEntry.Replace('\\', '/').TrimStart('/')));
            if (!dest.StartsWith(Path.GetFullPath(extractDir) + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Chemin d'extraction hors dossier cible.");

            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            using var es = zip.Entries.First(e => e.FullName == setupEntry).Open();
            using var ef = File.Create(dest);
            es.CopyTo(ef);
            return dest;
        }
    }

    /// <summary>Le processus courant tourne-t-il déjà avec les droits administrateur ?</summary>
    private static bool IsCurrentProcessElevated()
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// Lance l'installateur avec élévation. Null si l'utilisateur refuse l'UAC.
    /// Si RoomEq tourne déjà en administrateur, l'installateur hérite des droits
    /// sans invite (comportement Windows attendu).
    /// </summary>
    private Process? LaunchElevated(string setupPath, string? workingDir)
    {
        var psi = new ProcessStartInfo
        {
            FileName = setupPath,
            WorkingDirectory = workingDir ?? Path.GetDirectoryName(setupPath) ?? "",
            UseShellExecute = true
        };
        if (!IsCurrentProcessElevated())
            psi.Verb = "runas"; // déclenche l'invite UAC
        try
        {
            return Process.Start(psi);
        }
        catch (System.ComponentModel.Win32Exception w) when (w.NativeErrorCode == 1223)
        {
            return null; // UAC refusé par l'utilisateur
        }
        catch (System.ComponentModel.Win32Exception w) when (w.NativeErrorCode == 740)
        {
            // élévation requise : réessayer explicitement via le verbe « runas »
            try
            {
                psi.Verb = "runas";
                return Process.Start(psi);
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return null; // UAC refusé ou indisponible
            }
        }
    }

    private static async Task<bool> WaitArrivalAsync(
        Func<bool>? probe, Process? proc, TimeSpan timeout, CancellationToken ct)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            ct.ThrowIfCancellationRequested();
            if (probe != null)
            {
                try { if (probe()) return true; }
                catch { // sous-système audio momentanément indisponible : réessayer
                }
            }
            if (proc != null && proc.HasExited && probe == null) return true;
            await Task.Delay(1500, ct).ConfigureAwait(false);
        }
        return false;
    }
}
