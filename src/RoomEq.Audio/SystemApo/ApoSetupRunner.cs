using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;

namespace RoomEq.Audio.SystemApo;

/// <summary>
/// Lance une commande APO en ÉLÉVATION (invite UAC unique, spec I-AUD-09
/// invariant 4) : relance l'exécutable courant avec « --apo-setup », verbe
/// « runas », attend la fin et lit le verdict écrit par le processus élevé
/// dans setup-result.json (pas de canal de sortie direct entre les deux
/// niveaux d'intégrité). Le refus de l'invite = succès-faux lisible, rien
/// modifié, jamais d'exception.
/// </summary>
public sealed class ApoSetupRunner
{
    /// <summary>Exécute la commande élevée et attend son verdict.</summary>
    public async Task<ApoInstallResult> RunAsync(
        string verb, IReadOnlyList<string> args, CancellationToken ct = default)
    {
        if (!OperatingSystem.IsWindows())
            return ApoInstallResult.Fail("L'EQ système est une fonctionnalité Windows.");

        string? exe = Environment.ProcessPath;
        if (exe == null || !File.Exists(exe))
            return ApoInstallResult.Fail("Exécutable introuvable pour l'élévation.");

        var arguments = new List<string> { "--apo-setup", verb };
        arguments.AddRange(args);

        try
        {
            using var process = Process.Start(new ProcessStartInfo
            {
                FileName = exe,
                Arguments = string.Join(" ", arguments.Select(Quote)),
                Verb = "runas",               // UAC
                UseShellExecute = true,
                CreateNoWindow = true,
            });
            if (process == null)
                return ApoInstallResult.Fail("Le processus d'installation n'a pas pu être lancé.");

            await process.WaitForExitAsync(ct).ConfigureAwait(false);

            return ReadResult() ?? ApoInstallResult.Fail(
                "La commande élevée s'est terminée sans verdict (code " + process.ExitCode + ").");
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223) // ERROR_CANCELLED
        {
            return ApoInstallResult.Fail(
                "Élévation refusée : aucune modification n'a été effectuée.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return ApoInstallResult.Fail("Lancement de l'installation impossible : " + ex.Message);
        }
    }

    /// <summary>Lit (puis supprime) le verdict du processus élevé.</summary>
    private static ApoInstallResult? ReadResult()
    {
        try
        {
            string path = ApoConstants.SetupResultPath;
            if (!File.Exists(path))
                return null;
            var result = JsonSerializer.Deserialize<ApoInstallResult>(File.ReadAllText(path));
            File.Delete(path);
            return result;
        }
        catch
        {
            return null;
        }
    }

    private static string Quote(string arg)
        => arg.Contains(' ') ? "\"" + arg + "\"" : arg;
}
