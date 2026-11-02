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

        // v3.9.8 — PRÉ-VOL : avant de toucher au registre, la DLL doit passer
        // l'AUTO-TEST COM complet (chargement, CoCreateInstance via
        // l'inscription réelle, quatre interfaces, enregistrement,
        // LockForProcess, APOProcess sur un signal témoin, UnlockForProcess).
        // Retour terrain v3.9.7 : « l'activation coupe la sortie » — un échec
        // ici est désormais bloquant AVANT toute écriture : la sortie ne peut
        // PLUS être mise en danger par une DLL que notre propre processus
        // n'arrive pas à instancier, et le verdict nomme l'étape exacte.
        string dll = Path.Combine(ApoConstants.DeployedBinDir, ApoConstants.DeployedDllName);
        var selfTest = ApoDllSelfTest.Run(dll);
        if (!selfTest.Passed)
            return ApoInstallResult.Fail(ApoFailureDiagnostics.SelfTestBlocked(selfTest, dll));

        var registrar = Registrar();
        var result = registrar.Enable(endpointKey);
        if (!result.Success)
            return result;

        // v3.9.8 — inventaire des positions AVANT tout rollback : l'état
        // FxProperties au moment de l'activation fait partie du diagnostic
        // (variantes « multi » = Windows ignore les CLSID simples ; CLSID
        // constructeur en SFX/MFX = chaîne non vide à contourner).
        var inventory = CaptureSlotInventory(endpointKey);
        string slotChosen = ApoSlotPolicy.SlotName(
            registrar.EnabledSlot(endpointKey) ?? ApoFxSlot.Efx) ?? "EFX";

        RestartAudioService();

        // v3.9.7 — LE SON PASSE AVANT L'ÉGALISEUR : après le redémarrage du
        // service, on VÉRIFIE que la sortie répond toujours au rendu WASAPI.
        // Un endpoint dont le moteur audio n'a pas pu charger l'APO ne
        // construit PAS son graphe : plus de son du tout (« Failed to play
        // test tone », mixeur vide — retour terrain v3.9.6/v3.9.7). Sortie
        // morte → RESTAURATION AUTOMATIQUE immédiate de l'origine + nouveau
        // redémarrage : l'utilisateur garde toujours du son, et le verdict
        // EMBARQUE LE DIAGNOSTIC DIFFÉRENTIEL COMPLET (v3.9.8).
        var (health, probeError, logShowsLoad) = VerifyEndpointHealth(endpointKey);
        if (health == ApoActivationHealth.DeadEndpoint)
        {
            string diag = ApoFailureDiagnostics.ComposeDeadEndpoint(new ApoDeadEndpointContext(
                Engine: ReadEnginePrerequisites(),
                LogShowsLoad: logShowsLoad,
                LogTail: ReadLogTailLines(3),
                ProbeError: probeError,
                SelfTest: selfTest,
                Inventory: inventory,
                SlotChosen: slotChosen,
                OsVersion: Environment.OSVersion.VersionString));
            Registrar().Disable(endpointKey);          // restaure l'origine
            RestartAudioService();                    // le graphe d'origine revient
            return ApoInstallResult.Fail(diag);
        }

        return ApoInstallResult.Ok(result.Message +
            " Le service audio a été redémarré : le son s'interrompt environ une seconde." +
            " Le moteur audio accepte désormais les DLL non signées (réglage réversible d'Equalizer APO) : " +
            "les rares applications exigeant un chemin audio protégé peuvent refuser de jouer. " +
            " Auto-test DLL : " + selfTest.Summary() + "." +
            (health == ApoActivationHealth.Healthy
                ? " Vérifié : la sortie répond et l'APO est chargé (journal à l'heure)."
                : " Vérifié : la sortie répond au rendu (journal de l'APO muet — permissions « %ProgramData% » ; sans conséquence sur le son)."));
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

        // v3.9.8 : même pré-vol que l'activation — réparer ne doit jamais
        // exposer une sortie à une DLL que notre propre processus
        // n'instancie pas.
        string dll = Path.Combine(ApoConstants.DeployedBinDir, ApoConstants.DeployedDllName);
        var selfTest = ApoDllSelfTest.Run(dll);
        if (!selfTest.Passed)
            return ApoInstallResult.Fail(ApoFailureDiagnostics.SelfTestBlocked(selfTest, dll));

        int repaired = Registrar().RepairEnabled();
        if (repaired > 0 || fixedPrerequisites)
        {
            RestartAudioService();
            var parts = new List<string>();
            if (repaired > 0)
                parts.Add(repaired + " sortie(s) réparée(s) (mise à jour de pilote qui a effacé l'inscription)");
            if (fixedPrerequisites)
                parts.Add("prérequis du moteur audio réparés (inscription APO, acceptation des DLL non signées)");

            // v3.9.7 — même protection que l'activation : toute sortie réparée
            // doit rester VIVANTE ; celle qui ne répond plus est restaurée.
            int rolledBack = 0;
            foreach (string key in RegistrarBackedKeys())
            {
                if (VerifyEndpointHealth(key).Health == ApoActivationHealth.DeadEndpoint)
                {
                    Registrar().Disable(key);
                    rolledBack++;
                }
            }
            if (rolledBack > 0)
            {
                RestartAudioService();
                return ApoInstallResult.Fail(
                    "LE SON A ÉTÉ PRÉSERVÉ : " + rolledBack + " sortie(s) restaurée(s) " +
                    "automatiquement (le moteur audio n'a pas réussi à charger l'égaliseur " +
                    "dessus). Auto-test DLL : " + selfTest.Summary() + " · Prérequis moteur : " +
                    ApoFailureDiagnostics.DescribeEngine(ReadEnginePrerequisites()) +
                    ". Transmettez ce message — il contient le diagnostic ; un " +
                    "redémarrage de Windows peut aussi débloquer le chargement.");
            }
            return ApoInstallResult.Ok(string.Join(" · ", parts) + ".");
        }
        return ApoInstallResult.Ok("Rien à réparer : toutes les sorties activées sont conformes.");
    }

    /// <summary>Clés des endpoints connus du magasin de sauvegardes (élevé).</summary>
    private static IReadOnlyList<string> RegistrarBackedKeys()
    {
        try
        {
            return new ApoBackupFileStore(ApoConstants.BackupPath).Keys();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    // ---------------------------------------------------------------- santé sortie

    /// <summary>
    /// v3.9.7 — attend que le moteur audio reconstruise le graphe de la
    /// sortie, puis croise la sonde WASAPI (silencieuse) et la trace de
    /// chargement de l'APO (apo.log, y compris le journal de repli du profil
    /// service). Jamais d'exception ; verdict piloté par ApoActivationPolicy.
    /// v3.9.8 : remonte AUSSI l'erreur exacte de la dernière tentative de
    /// sonde et l'état « journal » — éléments du diagnostic différentiel.
    /// </summary>
    private static (ApoActivationHealth Health, string? ProbeError, bool LogShowsLoad) VerifyEndpointHealth(
        string endpointKey)
    {
        const int maxAttempts = 4;
        var probe = ApoEndpointProbe.ProbeDetailed(endpointKey);
        for (int attempt = 1; attempt <= maxAttempts && probe.Result == ApoProbeResult.DeadGraph; attempt++)
        {
            Thread.Sleep(attempt == 1 ? 800 : 1500);
            probe = ApoEndpointProbe.ProbeDetailed(endpointKey);
            if (probe.Result != ApoProbeResult.DeadGraph)
                break;
            bool witness = ApoEndpointProbe.AnyOtherAlive(endpointKey);
            if (!ApoActivationPolicy.ShouldRetryProbe(attempt, maxAttempts, witness))
                break; // témoin vivant et elle morte : verdict immédiat
        }
        bool loaded = LogShowsLoad();
        return (ApoActivationPolicy.Evaluate(probe.Result, loaded), probe.LastError, loaded);
    }

    /// <summary>
    /// L'APO a-t-elle laissé une trace de chargement ? Journal principal
    /// (%ProgramData%\RoomEq\apo\apo.log) OU journal de repli (profil du
    /// service — écrit quand %ProgramData% refuse l'écriture à audiodg).
    /// Lecture tolérante, jamais d'exception.
    /// </summary>
    private static bool LogShowsLoad()
    {
        foreach (string path in LogCandidatePaths())
        {
            try
            {
                if (!File.Exists(path))
                    continue;
                string text = File.ReadAllText(path);
                if (text.Contains("APO instancie", StringComparison.Ordinal)
                    || text.Contains("LockForProcess", StringComparison.Ordinal)
                    || text.Contains("surveillant actif", StringComparison.Ordinal))
                    return true;
            }
            catch
            {
                // journal occupé/illisible : ce n'est pas un verdict de mort
            }
        }
        return false;
    }

    private static IEnumerable<string> LogCandidatePaths()
    {
        yield return ApoConstants.ApoLogPath;
        yield return Path.Combine(
            Environment.GetEnvironmentVariable("SystemRoot") ?? "C:\\Windows",
            "ServiceProfiles", "LocalService", "AppData", "Local", "Temp", "RoomEqApo.log");
    }

    // ------------------------------------------- diagnostics v3.9.8 (élevé)

    /// <summary>
    /// Prérequis machine du chargement APO, lus APRÈS l'échec (miroir du
    /// panneau) : chaque maillon faux est nommé dans le verdict.
    /// </summary>
    private static ApoEnginePrerequisites ReadEnginePrerequisites()
    {
        try
        {
            return new ApoEnginePrerequisites(
                ApoEnginePolicy.IsDllLoadAllowed(WindowsApoEndpointStore.GetProtectedAudioDg()),
                WindowsApoEndpointStore.IsEngineRegistered(),
                WindowsApoEndpointStore.ComRegistered(
                    Path.Combine(ApoConstants.DeployedBinDir, ApoConstants.DeployedDllName)));
        }
        catch
        {
            return new ApoEnginePrerequisites(false, false, false); // registre illisible : tout signaler
        }
    }

    /// <summary>
    /// Inventaire des positions FxProperties de l'endpoint (SFX/MFX/EFX et
    /// variantes « multi ») — capturé AVANT tout rollback pour le diagnostic.
    /// Jamais d'exception, inventaire vide sinon.
    /// </summary>
    private static ApoSlotInventory? CaptureSlotInventory(string endpointKey)
    {
        try
        {
            var store = new WindowsApoEndpointStore();
            return new ApoSlotInventory(
                store.GetString(endpointKey, ApoConstants.FxStreamEffectClsid),
                store.GetString(endpointKey, ApoConstants.FxModeEffectClsid),
                store.GetString(endpointKey, ApoConstants.FxEndpointEffectClsid),
                store.GetStrings(endpointKey, ApoConstants.FxMultiStreamEffectClsid) != null,
                store.GetStrings(endpointKey, ApoConstants.FxMultiModeEffectClsid) != null,
                store.GetStrings(endpointKey, ApoConstants.FxMultiEndpointEffectClsid) != null);
        }
        catch
        {
            return null; // registre indisponible : pas d'inventaire, pas d'exception
        }
    }

    /// <summary>
    /// Dernières lignes du journal de l'APO (principal ou repli), pour le
    /// diagnostic — liste vide si aucun n'est lisible.
    /// </summary>
    private static IReadOnlyList<string> ReadLogTailLines(int maxLines)
    {
        foreach (string path in LogCandidatePaths())
        {
            try
            {
                if (!File.Exists(path))
                    continue;
                var lines = File.ReadAllLines(path)
                    .Where(l => l.Trim().Length > 0)
                    .TakeLast(maxLines)
                    .ToList();
                if (lines.Count > 0)
                    return lines;
            }
            catch
            {
                // journal occupé : essayer le suivant
            }
        }
        return Array.Empty<string>();
    }

    // ---------------------------------------------------------------- désinstallation

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

            var (acl, aclFixed) = ApplyAcls(bin);
            if (!acl.Success)
                return (acl, false);

            // v3.9.5 — VERROU : icacls échouait en silence (« meilleur effort ») et
            // laissait le dossier sans l'entrée Utilisateurs:Modifier — l'app ne
            // pouvait plus écrire la courbe, l'EQ restait muet sans explication.
            // Désormais : vérification .NET réelle, et échec EXPLICITE si absent.
            if (!ApoAclVerifier.UsersCanModify(ApoConstants.ProgramDataDir))
                return (ApoInstallResult.Fail(
                    "Les permissions du dossier " + ApoConstants.ProgramDataDir
                    + " n'ont pas pu être établies (icacls a réussi mais l'entrée « Utilisateurs : Modifier » est absente) : l'application ne "
                    + "pourrait pas envoyer la courbe à l'égaliseur système. Réessayez "
                    + "l'activation ; si l'échec persiste, créez le dossier manuellement et "
                    + "accordez « Modifier » aux Utilisateurs."), false);

            // v3.9.7 — VERROU bin : le chargement de la DLL par audiodg ne doit
            // pouvoir être refusé à AUCUNE identité de service — « Tout le monde :
            // lecture + exécution » (comme Program Files), vérifié en relisant
            // la DACL. Une entrée manquante ici est le mécanisme EXACT du
            // retour terrain v3.9.6 « plus de son sur cette sortie ».
            if (!ApoAclVerifier.EveryoneCanReadAndExecute(bin))
                return (ApoInstallResult.Fail(
                    "Les permissions du dossier " + bin
                    + " n'ont pas pu être établies (l'entrée « Tout le monde : lecture "
                    + "et exécution » est absente) : le moteur audio de Windows ne "
                    + "pourrait pas charger l'égaliseur et la sortie deviendrait muette. "
                    + "Réessayez l'activation ; si l'échec persiste, un logiciel de "
                    + "sécurité bloque la modification des permissions."), false);

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
            if (aclFixed)
                whatChanged.Add("permissions des dossiers partagés réappliquées");
            if (fixedEngineRegistration)
                whatChanged.Add("inscription moteur audio restaurée");
            if (fixedPolicy)
                whatChanged.Add("chargement des DLL non signées autorisé (réglage Equalizer APO, original sauvegardé)");

            return (ApoInstallResult.Ok(whatChanged.Count == 0
                ? "Déploiement APO à jour."
                : "Déploiement APO réparé : " + string.Join(" · ", whatChanged) + "."),
                fixedEngineRegistration || fixedPolicy || aclFixed);
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
    /// ACL des dossiers partagés (v3.9.7 — cf. <see cref="ApoAclArgs"/> pour
    /// le pourquoi de chaque SID) : construites par <see cref="ApoAclArgs"/>
    /// (pures, verrouillées par tests) puis APPLIQUÉES avec contrôle du code
    /// de retour icacls et VÉRIFIÉES par lecture .NET de la DACL — un octroi
    /// silencieusement échoué (retour terrain v3.9.6) ne peut plus passer
    /// inaperçu : le déploiement échoue avec un message actionnable.
    /// · bin : SYSTEM/Admins tout, TOUT LE MONDE lecture + exécution — le
    ///   chargement par audiodg ne peut plus être refusé, l'écriture reste
    ///   aux administrateurs ;
    /// · racine apo : SYSTEM/Admins tout, Utilisateurs + service audio
    ///   (nom ET LocalService par SID) MODIFIENT — courbe et journal
    ///   toujours inscriptibles.
    /// </summary>
    private static (ApoInstallResult Result, bool FixedSomething) ApplyAcls(string bin)
    {
        // bin d'abord (protégé contre l'héritage), puis la racine dont les
        // ACE se propagent aux fichiers existants (active.json…) mais
        // s'arrêtent à bin.
        if (!RunChecked("icacls", ApoAclArgs.ForBin(bin)))
            return (ApoInstallResult.Fail(
                "icacls a échoué sur " + bin + " : les permissions du dossier des DLL " +
                "n'ont pas pu être établies (le moteur audio ne pourrait pas charger " +
                "l'égaliseur). Relancez l'activation ; si l'échec persiste, un logiciel " +
                "de sécurité bloque la modification des permissions."), false);
        if (!RunChecked("icacls", ApoAclArgs.ForRoot(ApoConstants.ProgramDataDir)))
            return (ApoInstallResult.Fail(
                "icacls a échoué sur " + ApoConstants.ProgramDataDir + " : les permissions " +
                "du dossier de configuration n'ont pas pu être établies (l'application ne " +
                "pourrait pas envoyer la courbe, l'APO ne pourrait pas écrire son journal). " +
                "Relancez l'activation ; si l'échec persiste, un logiciel de sécurité " +
                "bloque la modification des permissions."), false);
        return (ApoInstallResult.Ok("ACL établies."), true);
    }

    private static void RestartAudioService()
    {
        // net.exe accepte /y pour les services dépendants ; les codes de
        // retour sont ignorés (certaines politiques interdisent l'arrêt).
        Run("net", "stop audiosrv /y", waitForExit: true);
        Run("net", "start audiosrv", waitForExit: true);
        // v3.9.8 — le verdict de la sonde n'a de valeur que si le moteur a
        // VRAIMENT relu les FxProperties : on attend l'état RUNNING (sc query
        // — code d'état numérique 4, indépendant de la langue) au lieu d'un
        // simple Sleep, puis on laisse audiodg se relancer.
        for (int i = 0; i < 20 && !AudioServiceRunning(); i++)
            Thread.Sleep(500);
        Thread.Sleep(400); // laisse audiodg se relancer et relire FxProperties
    }

    /// <summary>Le service audio est-il RUNNING ? (sc query, code numérique
    /// — la formulation du texte d'état est LOCALISÉE, pas le code).</summary>
    private static bool AudioServiceRunning()
    {
        try
        {
            string? output = RunCapture("sc.exe", "query audiosrv");
            if (output == null)
                return false;
            foreach (string line in output.Split('\n'))
            {
                int colon = line.IndexOf(':');
                if (colon <= 0)
                    continue;
                string value = line[(colon + 1)..].Trim();
                if (value.StartsWith("4 ") || value == "4")
                    return true; // SERVICE_RUNNING
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Exécute et capture la sortie standard (null si échec).</summary>
    private static string? RunCapture(string fileName, string arguments, int timeoutMs = 10000)
    {
        try
        {
            using var p = Process.Start(new ProcessStartInfo
            {
                FileName = fileName,
                Arguments = arguments,
                CreateNoWindow = true,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                StandardOutputEncoding = System.Text.Encoding.UTF8,
            });
            if (p == null)
                return null;
            string output = p.StandardOutput.ReadToEnd();
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(); } catch { /* déjà parti */ }
                return null;
            }
            return output;
        }
        catch
        {
            return null;
        }
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

    /// <summary>Exécute et exige un code de retour nul (v3.9.7 — plus jamais
    /// d'octroi ACL silencieusement raté : le verdict le nomme).</summary>
    private static bool RunChecked(string fileName, string arguments, int timeoutMs = 20000)
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
            if (p == null)
                return false;
            if (!p.WaitForExit(timeoutMs))
            {
                try { p.Kill(); } catch { /* déjà parti */ }
                return false;
            }
            return p.ExitCode == 0;
        }
        catch
        {
            return false;
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
