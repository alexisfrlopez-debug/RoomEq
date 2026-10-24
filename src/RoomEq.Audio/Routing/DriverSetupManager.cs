using System.Diagnostics;

namespace RoomEq.Audio.Routing;

/// <summary>
/// Gestion du pilote de la carte virtuelle RoomEq (roomeqvad) : détection du
/// paquet livré avec l'application, installation/désinstallation via pnputil
/// (Windows 10/11 intégré), et suivi de l'arrivée des périphériques.
/// Aucun téléchargement : l'installateur MSI dépose le paquet pilote
/// (roomeqvad.inf / .sys / .cat) dans le sous-répertoire « driver » de
/// l'application.
/// </summary>
public sealed class DriverSetupManager(IProgress<InstallProgress>? progress = null)
{
    /// <summary>Durée maximale d'attente d'arrivée des périphériques après installation.</summary>
    public static readonly TimeSpan ArrivalTimeout = TimeSpan.FromMinutes(5);

    private readonly IProgress<InstallProgress>? _progress = progress;

    // ---------------------------------------------------------------- paquet

    /// <summary>Cherche le paquet pilote à partir d'un dossier candidat.</summary>
    public static string? FindInfFile(string appDirectory, VirtualInstallerSpec spec)
    {
        if (string.IsNullOrEmpty(appDirectory) || string.IsNullOrEmpty(spec.InfFileName))
            return null;
        try
        {
            string direct = Path.Combine(appDirectory, spec.DriverFolder, spec.InfFileName);
            if (File.Exists(direct)) return direct;
            // repli : à côté de l'exe (zip portable)
            string beside = Path.Combine(appDirectory, spec.InfFileName);
            if (File.Exists(beside)) return beside;
            // repli : tout sous-dossier « driver* »
            foreach (var dir in Directory.EnumerateDirectories(appDirectory, "driver*", SearchOption.TopDirectoryOnly))
            {
                string candidate = Path.Combine(dir, spec.InfFileName);
                if (File.Exists(candidate)) return candidate;
            }
        }
        catch
        {
            // répertoire illisible : pas de paquet
        }
        return null;
    }

    /// <summary>Le paquet pilote est-il présent à côté de l'application ?</summary>
    public static bool PackagePresent(string appDirectory, VirtualInstallerSpec spec)
        => FindInfFile(appDirectory, spec) != null;

    /// <summary>Le service pilote est-il déjà installé sur la machine ?</summary>
    public static bool ServiceInstalled(VirtualInstallerSpec spec)
    {
        try
        {
            return File.Exists(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.System), "drivers",
                spec.ServiceName + ".sys"))
                || Environment.OSVersion.Platform != PlatformID.Win32NT;
        }
        catch
        {
            return false;
        }
    }

    // ---------------------------------------------------------------- ligne de commande (pur, testé)

    /// <summary>Arguments pnputil pour installer le paquet pilote ( élévation requise).</summary>
    public static string BuildInstallArguments(string infPath)
        => "/add-driver " + Quote(infPath) + " /install";

    /// <summary>
    /// Extrait l'entrée « oemNN.inf » correspondant au pilote RoomEq dans la
    /// sortie texte de <c>pnputil /enum-drivers</c> (anglais et français).
    /// </summary>
    public static string? ParseDriverStoreName(string enumOutput, string originalInfName)
    {
        if (string.IsNullOrEmpty(enumOutput) || string.IsNullOrEmpty(originalInfName))
            return null;
        // Blocs : "Nom du fichier publié: oem12.inf" / "Published Name: oem12.inf"
        // suivis de "Nom du fichier INF d'origine: roomeqvad.inf" / "Original Name: roomeqvad.inf"
        string[] lines = enumOutput.Split('\n');
        string? published = null;
        foreach (string raw in lines)
        {
            string line = raw.Trim().TrimEnd('\r');
            if (line.Length == 0) { published = null; continue; }
            int i = line.IndexOf(':');
            if (i <= 0) continue;
            string key = line[..i].Trim();
            string value = line[(i + 1)..].Trim();

            if (key.EndsWith("published name", StringComparison.OrdinalIgnoreCase)
                || key.EndsWith("nom du fichier publié", StringComparison.OrdinalIgnoreCase)
                || key.EndsWith("nom du fichier publie", StringComparison.OrdinalIgnoreCase))
            {
                published = value;   // toujours suivre le dernier nom publié vu
            }
            else if (key.EndsWith("original name", StringComparison.OrdinalIgnoreCase)
                     || key.EndsWith("nom inf d'origine", StringComparison.OrdinalIgnoreCase)
                     || key.EndsWith("nom du fichier inf d'origine", StringComparison.OrdinalIgnoreCase))
            {
                if (published != null
                    && string.Equals(value, originalInfName, StringComparison.OrdinalIgnoreCase))
                    return published;
            }
        }
        return null;
    }

    /// <summary>Arguments pnputil pour désinstaller complètement le pilote.</summary>
    public static string BuildUninstallArguments(string driverStoreInf)
        => "/delete-driver " + driverStoreInf + " /uninstall /force";

    private static string Quote(string p) => "\"" + p.Trim('"') + "\"";

    // ---------------------------------------------------------------- exécution

    /// <summary>
    /// Installe le pilote RoomEq (processus élevé) puis attend l'arrivée des
    /// périphériques. Retourne un statut lisible ; jamais d'exception.
    /// </summary>
    public async Task<InstallResult> InstallAsync(VirtualInstallerSpec spec,
                                                  string appDirectory,
                                                  Func<bool> probeInstalled,
                                                  CancellationToken ct)
    {
        string? inf = FindInfFile(appDirectory, spec);
        if (inf == null)
            return Fail("Paquet pilote introuvable : réinstallez RoomEq (l'installateur copie le dossier « driver »).");

        Report("Installation du pilote RoomEq… (acceptez l'invite administrateur)");
        var psi = new ProcessStartInfo
        {
            FileName = "pnputil.exe",
            Arguments = BuildInstallArguments(inf),
            Verb = "runas",              // élévation UAC
            UseShellExecute = true,
        };
        try
        {
            using var p = Process.Start(psi);
            if (p == null)
                return Fail("Impossible de lancer pnputil.");
            await p.WaitForExitAsync(ct);
            if (p.ExitCode != 0)
                return Fail($"pnputil a échoué (code {p.ExitCode}).");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return Fail("Installation annulée : l'invite administrateur a été refusée.");
        }
        catch (Exception ex)
        {
            return Fail("Lancement impossible : " + ex.Message);
        }

        // sondage d'arrivée des périphériques (jusqu'à 5 minutes)
        Report("Pilote installé — attente de l'apparition de « RoomEq Input / RoomEq Output »…");
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < ArrivalTimeout)
        {
            if (probeInstalled())
                return new InstallResult(true,
                    "Carte son virtuelle RoomEq installée et détectée.");
            try { await Task.Delay(2000, ct); }
            catch (OperationCanceledException) { break; }
        }
        return new InstallResult(false,
            "Le pilote est installé mais les périphériques n'apparaissent pas — "
            + "redémarrez Windows puis cliquez « ⟳ ».");
    }

    /// <summary>Désinstalle le pilote RoomEq (processus élevé). Jamais d'exception.</summary>
    public async Task<InstallResult> UninstallAsync(VirtualInstallerSpec spec, CancellationToken ct)
    {
        Report("Recherche du pilote dans le magasin Windows…");
        string? oemInf = null;
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "pnputil.exe",
                Arguments = "/enum-drivers",
                UseShellExecute = false,
                RedirectStandardOutput = true,
                CreateNoWindow = true,
            };
            using var p = Process.Start(psi);
            if (p != null)
            {
                string output = await p.StandardOutput.ReadToEndAsync(ct);
                await p.WaitForExitAsync(ct);
                oemInf = ParseDriverStoreName(output, spec.InfFileName);
            }
        }
        catch
        {
            // énumération impossible : on tente quand même le service
        }

        Report("Désinstallation du pilote RoomEq… (acceptez l'invite administrateur)");
        var psi2 = new ProcessStartInfo
        {
            FileName = "pnputil.exe",
            Arguments = oemInf != null
                ? BuildUninstallArguments(oemInf)
                : "/delete-driver " + spec.InfFileName + " /uninstall /force",
            Verb = "runas",
            UseShellExecute = true,
        };
        try
        {
            using var p2 = Process.Start(psi2);
            if (p2 == null) return Fail("Impossible de lancer pnputil.");
            await p2.WaitForExitAsync(ct);
            return new InstallResult(p2.ExitCode == 0,
                p2.ExitCode == 0
                    ? "Pilote RoomEq supprimé."
                    : $"pnputil a échoué (code {p2.ExitCode}).");
        }
        catch (System.ComponentModel.Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            return Fail("Désinstallation annulée : l'invite administrateur a été refusée.");
        }
        catch (Exception ex)
        {
            return Fail("Lancement impossible : " + ex.Message);
        }
    }

    private void Report(string message) => _progress?.Report(new InstallProgress(message, null));

    private static InstallResult Fail(string message) => new(false, message);
}

/// <summary>Résultat d'une opération pilote (message affichable, jamais d'exception).</summary>
public sealed record InstallResult(bool Success, string Message);

/// <summary>Progression d'installation (texte + pourcentage optionnel).</summary>
public sealed record InstallProgress(string Message, double? Percent);
