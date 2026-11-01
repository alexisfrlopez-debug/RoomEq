using System.Diagnostics;

namespace RoomEq.Audio.SystemApo;

/// <summary>
/// Commandes d'installation APO exécutées DANS le processus élevé
/// (RoomEq.exe --apo-setup …, lancé par <see cref="ApoSetupRunner"/>).
/// Tout y est réversible et idempotent : déploiement des DLL vers
/// %ProgramData% (lecture seule pour les utilisateurs standard — une DLL
/// charge par audiodg doit rester inscriptible aux seuls administrateurs),
/// inscription COM, opérations du registrar (sauvegarde AVANT écriture),
/// redémarrage du service audio pour que le moteur relise les FxProperties.
/// </summary>
public static class ApoSetupCommands
{
    /// <summary>
    /// Exécute une commande. Retourne (code de sortie, résultat) ; le résultat
    /// est écrit dans <c>setup-result.json</c> pour l'application non élevée.
    /// </summary>
    public static int Run(string verb, IReadOnlyList<string> args, string appBaseDirectory)
    {
        var result = verb switch
        {
            "enable" when args.Count == 1 => Enable(args[0], appBaseDirectory),
            "disable" when args.Count == 1 => Disable(args[0]),
            "repair" => Repair(appBaseDirectory),
            "uninstall" => Uninstall(appBaseDirectory),
            _ => ApoInstallResult.Fail("Commande inconnue : " + verb),
        };
        WriteResult(result);
        return result.Success ? 0 : 1;
    }

    // ------------------------------------------------------------------ activer

    private static ApoInstallResult Enable(string endpointKey, string appBase)
    {
        var deploy = Deploy(appBase);
        if (!deploy.Success)
            return deploy;

        var registrar = Registrar();
        var result = registrar.Enable(endpointKey);
        if (!result.Success)
            return result;

        RestartAudioService();
        return ApoInstallResult.Ok(result.Message +
            " Le service audio a été redémarré : le son s'interrompt environ une seconde.");
    }

    // ---------------------------------------------------------------- désactiver

    private static ApoInstallResult Disable(string endpointKey)
    {
        var result = Registrar().Disable(endpointKey);
        if (result.Success)
            RestartAudioService();
        return result;
    }

    // ---------------------------------------------------------------- réparation

    private static ApoInstallResult Repair(string appBase)
    {
        var deploy = Deploy(appBase); // ré-installe aussi les DLL/CLSID manquants
        if (!deploy.Success)
            return deploy;

        int repaired = Registrar().RepairEnabled();
        if (repaired > 0)
        {
            RestartAudioService();
            return ApoInstallResult.Ok(repaired + " sortie(s) réparée(s) (mise à jour de pilote had effacé l'inscription).");
        }
        return ApoInstallResult.Ok("Rien à réparer : toutes les sorties activées sont conformes.");
    }

    // ------------------------------------------------------------ désinstallation

    private static ApoInstallResult Uninstall(string appBase)
    {
        var registrar = Registrar();
        var result = registrar.UninstallAll();
        if (!result.Success)
            return result;

        WindowsApoEndpointStore.UnregisterCom();
        RestartAudioService();

        // fichiers partagés : DLL déployées + sauvegardes + courbe + journal.
        // On les retire APRÈS la restauration des endpoints et le redémarrage
        // du service (audiodg a relâché les DLL) — invariant 11.
        try
        {
            if (Directory.Exists(ApoConstants.DeployedBinDir))
                Directory.Delete(ApoConstants.DeployedBinDir, recursive: true);
            foreach (var f in new[] { ApoConstants.BackupPath, ApoConstants.ActiveCurvePath,
                                      ApoConstants.ApoLogPath, ApoConstants.SetupResultPath })
                if (File.Exists(f))
                    File.Delete(f);
        }
        catch
        {
            // des fichiers occupés ne doivent pas faire échouer la restauration
        }
        return ApoInstallResult.Ok(result.Message + " Inscription COM retirée, fichiers partagés supprimés.");
    }

    // ---------------------------------------------------------------- déploiement

    /// <summary>
    /// Copie les DLL APO de l'application vers %ProgramData%\RoomEq\apo\bin
    /// et (re)fait l'inscription COM vers la DLL de l'architecture de la
    /// machine. Idempotent.
    /// </summary>
    private static ApoInstallResult Deploy(string appBase)
    {
        try
        {
            string src = Path.Combine(appBase, ApoConstants.AppApoSubdir);
            if (!Directory.Exists(src))
                return ApoInstallResult.Fail(
                    "DLL APO introuvables (dossier « apo » absent à côté de l'application) : " +
                    "réinstallez RoomEq avec le dernier installateur.");

            string bin = ApoConstants.DeployedBinDir;
            Directory.CreateDirectory(bin);
            Directory.CreateDirectory(ApoConstants.ProgramDataDir);

            foreach (var name in new[] { "RoomEqApo_x64.dll", "RoomEqApo_arm64.dll" })
            {
                var from = Path.Combine(src, name);
                if (!File.Exists(from))
                    continue; // l'autre architecture peut manquer, la nôtre suffit
                var to = Path.Combine(bin, name);
                File.Copy(from, to, overwrite: true);
            }

            string arch = Environment.Is64BitOperatingSystem
                && Environment.GetEnvironmentVariable("PROCESSOR_ARCHITECTURE")?.StartsWith("ARM", StringComparison.OrdinalIgnoreCase) == true
                    ? "RoomEqApo_arm64.dll"
                    : "RoomEqApo_x64.dll";
            string dll = Path.Combine(bin, arch);
            if (!File.Exists(dll))
                return ApoInstallResult.Fail("DLL APO " + arch + " absente du dossier « apo » de l'application.");

            ApplyAcls(bin);

            if (!WindowsApoEndpointStore.ComRegistered(dll))
                WindowsApoEndpointStore.RegisterCom(dll);

            return ApoInstallResult.Ok("Déploiement APO à jour.");
        }
        catch (Exception ex)
        {
            return ApoInstallResult.Fail("Déploiement impossible : " + ex.Message);
        }
    }

    /// <summary>
    /// ACL des dossiers partagés — par SID (indépendant de la langue de
    /// Windows) via icacls.exe :
    /// · racine apo : SYSTEM/Admins tout, Utilisateurs + service audio
    ///   MODIFIENT (l'application non élevée écrit active.json, audiodg
    ///   écrit son journal) ;
    /// · bin : SYSTEM/Admins tout, les autres LECTURE/EXÉCUTION seule — une
    ///   DLL chargée par audiodg ne doit jamais être remplaçable par un
    ///   utilisateur standard (élévation de privilège).
    /// </summary>
    private static void ApplyAcls(string bin)
    {
        // bin d'abord (protégé contre l'héritage), puis la racine dont les
        // ACE se propagent aux fichiers existants (active.json…) mais
        // s'arrêtent à bin.
        Run("icacls", $"\"{bin}\" /inheritance:r " +
            $"/grant *S-1-5-18:(OI)(CI)F /grant *S-1-5-32-544:(OI)(CI)F " +
            $"/grant *S-1-5-32-545:(OI)(CI)RX /grant \"NT SERVICE\\Audiosrv:(OI)(CI)RX\"");
        Run("icacls", $"\"{ApoConstants.ProgramDataDir}\" /inheritance:r " +
            $"/grant *S-1-5-18:(OI)(CI)F /grant *S-1-5-32-544:(OI)(CI)F " +
            $"/grant *S-1-5-32-545:(OI)(CI)M /grant \"NT SERVICE\\Audiosrv:(OI)(CI)M\"");
    }

    private static void RestartAudioService()
    {
        // net.exe accepte /y pour les services dépendants ; les codes de
        // retour sont ignorés (meilleur effort — certaines politiques
        // interdisent l'arrêt, l'utilisateur sera prévenu par le son lui-même)
        Run("net", "stop audiosrv /y", waitForExit: true);
        Run("net", "start audiosrv", waitForExit: true);
        Thread.Sleep(400); // laisse audiodg se relancer et relire FxProperties
    }

    private static void Run(string fileName, string arguments, bool waitForExit = true)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
            });
            if (waitForExit)
                p?.WaitForExit(15000);
        }
        catch
        {
            // meilleur effort, jamais bloquant
        }
    }

    private static ApoRegistrar Registrar()
        => new(new WindowsApoEndpointStore(), new ApoBackupFileStore(ApoConstants.BackupPath));

    private static void WriteResult(ApoInstallResult result)
    {
        try
        {
            Directory.CreateDirectory(ApoConstants.ProgramDataDir);
            File.WriteAllText(ApoConstants.SetupResultPath,
                System.Text.Json.JsonSerializer.Serialize(result));
        }
        catch
        {
            // le code de sortie reste porteur du verdict
        }
    }
}
