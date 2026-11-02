using System.Diagnostics;
using System.Runtime.Versioning;

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
[SupportedOSPlatform("windows")]
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
        var (deploy, _) = Deploy(appBase);
        if (!deploy.Success)
            return deploy;

        var registrar = Registrar();
        var result = registrar.Enable(endpointKey);
        if (!result.Success)
            return result;

        RestartAudioService();
        return ApoInstallResult.Ok(result.Message +
            " Le service audio a été redémarré : le son s'interrompt environ une seconde." +
            " Le moteur audio accepte désormais les DLL non signées (réglage réversible d'Equalizer APO) : " +
            "les rares applications exigeant un chemin audio protégé peuvent refuser de jouer.");
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
        var (deploy, fixedPrerequisites) = Deploy(appBase); // ré-installe aussi les DLL/CLSID manquants
        if (!deploy.Success)
            return deploy;

        int repaired = Registrar().RepairEnabled();
        if (repaired > 0 || fixedPrerequisites)
        {
            RestartAudioService();
            var parts = new List<string>();
            if (repaired > 0)
                parts.Add(repaired + " sortie(s) réparée(s) (mise à jour de pilote qui a effacé l'inscription)");
            if (fixedPrerequisites)
                parts.Add("prérequis du moteur audio réparés (inscription APO, acceptation des DLL non signées)");
            return ApoInstallResult.Ok(string.Join(" · ", parts) + ".");
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
        // v3.9.6 : inscription moteur audio retirée + politique « audio
        // protégé » restituée à son état d'origine (uniquement si NOUS l'avons
        // modifiée — un Equalizer APO installé ne doit pas être désactivé).
        WindowsApoEndpointStore.UnregisterEngineApo();
        RestoreProtectedAudioDg();
        RestartAudioService();

        // fichiers partagés : DLL déployées + sauvegardes + courbe + journal.
        // On les retire APRÈS la restauration des endpoints et le redémarrage
        // du service (audiodg a relâché les DLL) — invariant 11.
        try
        {
            if (Directory.Exists(ApoConstants.DeployedBinDir))
                Directory.Delete(ApoConstants.DeployedBinDir, recursive: true);
            foreach (var f in new[] { ApoConstants.BackupPath, ApoConstants.ActiveCurvePath,
                                      ApoConstants.ApoLogPath, ApoConstants.SetupResultPath,
                                      ApoConstants.AudioPolicyBackupPath })
                if (File.Exists(f))
                    File.Delete(f);
        }
        catch
        {
            // des fichiers occupés ne doivent pas faire échouer la restauration
        }
        return ApoInstallResult.Ok(result.Message +
            " Inscription COM et inscription moteur audio retirées, politique audio d'origine restaurée, fichiers partagés supprimés.");
    }

    /// <summary>Restitue la politique « audio protégé » si nous l'avions
    /// modifiée (sauvegarde présente) : remet la valeur d'origine — ou la
    /// supprime si elle n'existait pas. Sans sauvegarde : ne rien toucher.</summary>
    private static void RestoreProtectedAudioDg()
    {
        var backup = ApoEnginePolicy.LoadBackup(ApoConstants.AudioPolicyBackupPath);
        if (backup == null)
            return; // nous n'avons jamais changé cette politique
        WindowsApoEndpointStore.SetProtectedAudioDg(backup.Original);
        ApoEnginePolicy.DeleteBackup(ApoConstants.AudioPolicyBackupPath);
    }

    // ---------------------------------------------------------------- déploiement

    /// <summary>
    /// Copie les DLL APO de l'application vers %ProgramData%\RoomEq\apo\bin,
    /// (re)fait l'inscription COM de la DLL, l'INSCRIPTION AUPRÈS DU MOTEUR
    /// AUDIO (v3.9.6 — clé AudioEngine\AudioProcessingObjects, équivalent
    /// RegisterAPO/regsvr32) et ouvre la politique « audio protégé »
    /// (DisableProtectedAudioDG=1 — sans quoi audiodg refuse SILENCIEUSEMENT
    /// de charger une DLL non signée ; réglage Equalizer APO 10+ ans, valeur
    /// d'origine sauvegardée et restituée à la désinstallation). Idempotent.
    /// </summary>
    private static (ApoInstallResult Result, bool FixedSomething) Deploy(string appBase)
    {
        try
        {
            string src = Path.Combine(appBase, ApoConstants.AppApoSubdir);
            if (!Directory.Exists(src))
                return (ApoInstallResult.Fail(
                    "DLL APO introuvables (dossier « apo » absent à côté de l'application) : " +
                    "réinstallez RoomEq avec le dernier installateur."), false);

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

            string dll = Path.Combine(bin, ApoConstants.DeployedDllName);
            if (!File.Exists(dll))
                return (ApoInstallResult.Fail(
                    "DLL APO " + ApoConstants.DeployedDllName + " absente du dossier « apo » de l'application."), false);

            ApplyAcls(bin);

            // v3.9.5 — VERROU : icacls échouait en silence (« meilleur effort ») et
            // laissait le dossier sans l'entrée Utilisateurs:Modifier — l'app ne
            // pouvait plus écrire la courbe, l'EQ restait muet sans explication.
            // Désormais : vérification .NET réelle, et échec EXPLICITE si absent.
            if (!ApoAclVerifier.UsersCanModify(ApoConstants.ProgramDataDir))
                return (ApoInstallResult.Fail(
                    "Les permissions du dossier " + ApoConstants.ProgramDataDir
                    + " n'ont pas pu être établies (icacls a échoué) : l'application ne "
                    + "pourrait pas envoyer la courbe à l'égaliseur système. Réessayez "
                    + "l'activation ; si l'échec persiste, créez le dossier manuellement et "
                    + "accordez « Modifier » aux Utilisateurs."), false);

            // v3.9.5 — courbe initiale : garantit qu'active.json existe, appartient
            // au dossier et est valide DÈS l'activation (l'app l'écrase par la
            // courbe réelle ~immédiatement après ; le fichier prouve au passage
            // que le dossier est inscriptible par un processus élevé).
            try
            {
                if (!File.Exists(ApoConstants.ActiveCurvePath))
                {
                    var initial = new RoomEq.Core.Eq.EqPreset("Démarrage") { PreampDb = 0.0 };
                    new ApoConfigStore().WriteCurve(initial, bypass: false);
                }
            }
            catch
            {
                // non bloquant : l'application écrira sa courbe juste après —
                // le verrou d'ACL ci-dessus a déjà couvert le cas d'échec réel
            }

            if (!WindowsApoEndpointStore.ComRegistered(dll))
                WindowsApoEndpointStore.RegisterCom(dll);

            // v3.9.6 — les DEUX prérequis qui manquaient (retour terrain
            // v3.9.5 : « journal APO absent, DLL jamais chargée par le moteur
            // audio ») : l'inscription auprès du moteur audio (RegisterAPO)
            // et l'acceptation des DLL non signées (DisableProtectedAudioDG,
            // mécanisme Equalizer APO — sans lui, audiodg en mode protégé
            // refuse notre DLL sans le moindre message).
            bool fixedEngineRegistration = WindowsApoEndpointStore.RegisterEngineApo();
            bool fixedPolicy = EnsureProtectedAudioDgAllowsUnsignedApos();

            var whatChanged = new List<string>();
            if (fixedEngineRegistration)
                whatChanged.Add("inscription moteur audio restaurée");
            if (fixedPolicy)
                whatChanged.Add("chargement des DLL non signées autorisé (réglage Equalizer APO, original sauvegardé)");

            return (ApoInstallResult.Ok(whatChanged.Count == 0
                ? "Déploiement APO à jour."
                : "Déploiement APO réparé : " + string.Join(" · ", whatChanged) + "."),
                fixedEngineRegistration || fixedPolicy);
        }
        catch (Exception ex)
        {
            return (ApoInstallResult.Fail("Déploiement impossible : " + ex.Message), false);
        }
    }

    /// <summary>
    /// Ouvre la politique « audio protégé » si nécessaire (processus élevé) :
    /// DisableProtectedAudioDG=1 sous HKLM\…\CurrentVersion\Audio. La valeur
    /// d'ORIGINE est capturée UNE fois (audio-policy.json) — uniquement si
    /// nous sommes celui qui change la politique : si elle est déjà à 1
    /// (Equalizer APO installé, ou activation RoomEq précédente), on ne
    /// touche à rien et la désinstallation n'y touchera pas non plus.
    /// </summary>
    /// <returns>Vrai si la politique a été modifiée par NOUS.</returns>
    private static bool EnsureProtectedAudioDgAllowsUnsignedApos()
    {
        int? current = WindowsApoEndpointStore.GetProtectedAudioDg();
        if (ApoEnginePolicy.IsDllLoadAllowed(current))
            return false; // déjà ouverte (nous ou un tiers) : ne rien toucher

        // capture l'origine UNE fois — une ré-activation ne doit JAMAIS
        // écraser la VRAIE valeur d'origine par une valeur intermédiaire
        if (ApoEnginePolicy.LoadBackup(ApoConstants.AudioPolicyBackupPath) == null)
            ApoEnginePolicy.SaveBackup(ApoConstants.AudioPolicyBackupPath, current);

        WindowsApoEndpointStore.SetProtectedAudioDg(1);
        return true;
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
