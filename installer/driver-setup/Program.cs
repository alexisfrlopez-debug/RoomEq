// ============================================================================
//  RoomEqDriverSetup - installation de la carte son virtuelle RoomEq SANS MSI.
//
//  Distribution '2 executables' (demande utilisateur 2026-10) :
//    - RoomEq-<version>-win-x64.exe : l'application Avalonia, portable,
//      auto-contenue, sans installation ni UAC ;
//    - RoomEqDriverSetup.exe (ce programme) : pilote + ASIO en un seul
//      fichier, les fichiers pilote sont EMBARQUES en ressource.
//
//  Usage (v2.2.0 — interface graphique par defaut) :
//    RoomEqDriverSetup.exe                 FENETRE GRAPHIQUE : etat de la machine,
//                                           boutons Installer / Diagnostic complet /
//                                           Revenir au mode normal / Desinstaller.
//                                           Aucune ligne de commande.
//    RoomEqDriverSetup.exe --gui --install   idem fenetre, installation lancee
//                                           d'office (utilise par le bouton
//                                           « Installer le pilote » de l'application)
//
//  Options console (support / automatisation — comportement inchange) :
//    RoomEqDriverSetup.exe --console       installation en console (defaut historique)
//    RoomEqDriverSetup.exe --status        etat de la machine + VERDICT
//    RoomEqDriverSetup.exe --uninstall     retrait complet (best effort)
//    RoomEqDriverSetup.exe --mode-normal   RETOUR AU MODE NORMAL de Windows
//    RoomEqDriverSetup.exe --install --accept-testmode   (automatisation)
//    RoomEqDriverSetup.exe --install --no-testmode       (annulation propre)
//
//  MODE TEST (v2.1.0) : le pilote actuel est SIGNE-TEST ; Windows ne peut le
//  charger QU'en mode test (bcdedit). Ce programme ne l'active JAMAIS sans
//  accord explicite : a l'installation, la question est posee AVANT toute
//  modification (reponse par defaut : NON). --mode-normal fait le chemin
//  inverse (testsigning off + filigrane disparait au redemarrage).
//
//  Le programme requiert l'elevation (app.manifest) : une seule invite UAC.
//
//  Etapes d'installation (portage console de RoomEq.Installer.CA) :
//    1. extraction du payload embarque vers %TEMP%\RoomEqDriverSetup ;
//    2. certificat de test -> LocalMachine Root + TrustedPublisher ;
//    3. mode testsigning (bcdedit) si inactif - pre-diagnostic Secure Boot ;
//    4. pnputil /add-driver /install (magasin de pilotes) ;
//    5. peripherique ROOT\ROOMEQVAD (recette devcon) + liaison du pilote ;
//    6. DLL ASIO -> C:\Program Files\RoomEq + enregistrement COM (HKLM).
//  Un build sans .cer embarque (pilote signe Microsoft) saute 2, 3 et le
//  redemarrage : tout devient immediat.
// ============================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Win32;

namespace RoomEqDriverSetup
{
    internal static class Program
    {
        private const string InfName = "roomeqvad.inf";
        private const string HwId = "ROOT\\ROOMEQVAD";
        private const string CertResName = "RoomEq-TestSigning.cer";
        private const string CertSubject = "RoomEq Test Signing";
        private const string AsioClsid = "{8F5E3A91-4B2C-4D6E-9A1F-2E7C5B8D9A03}";
        private const string AsioDllName = "RoomEqAsio64.dll";

        /// <summary>Vrai si la fenetre graphique est active (textes adaptes).</summary>
        internal static bool GuiActive;

        [STAThread]
        private static int Main(string[] args)
        {
            // v2.2.0 : double-clic (aucun argument) = FENETRE GRAPHIQUE ;
            // les options console restent pour le support et l'automatisation.
            bool wantGui = args.Length == 0;
            bool acceptTestMode = false, noTestMode = false;
            string mode = null;
            foreach (string a in args)
            {
                string m = a.TrimStart('-').ToLowerInvariant();
                if (m == "gui") { wantGui = true; continue; }
                if (m == "console" || m == "cli" || m == "c") continue;
                if (m == "u") m = "uninstall";
                if (m == "s") m = "status";
                if (m == "n" || m == "normal") m = "mode-normal";
                if (m == "install" || m == "uninstall" || m == "status" || m == "mode-normal")
                {
                    mode = m;
                    continue;
                }
                if (m == "accept-testmode") { acceptTestMode = true; continue; }
                if (m == "no-testmode") { noTestMode = true; continue; }
                if (m == "i") { mode = "install"; continue; }
                if (m.Length > 0)
                {
                    Console.WriteLine("Argument inconnu : " + a);
                    Console.WriteLine("Usage : RoomEqDriverSetup.exe [--gui] | [--install | --status | --uninstall | --mode-normal]");
                    Console.WriteLine("        [--install --accept-testmode | --install --no-testmode]");
                    Console.WriteLine("        (sans argument : fenetre graphique)");
                    return 1;
                }
            }

            if (wantGui)
            {
                GuiActive = true;
                return GuiRunner.Run(mode);   // mode = action auto (null = aucune)
            }

            AttachToConsole();
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            try { Console.Title = "RoomEqDriverSetup"; } catch { }
            Console.WriteLine("RoomEqDriverSetup - carte son virtuelle RoomEq");
            Console.WriteLine("==============================================");
            Console.WriteLine();

            if (!IsElevated())
            {
                Console.WriteLine("[ERREUR] droits administrateur manquants.");
                Console.WriteLine("         Clic droit sur RoomEqDriverSetup.exe -> Executer en tant");
                Console.WriteLine("         qu'administrateur (l'invite UAC est normale : le pilote");
                Console.WriteLine("         touche des zones systeme de Windows).");
                return 1;
            }

            if (mode == null) mode = "install";
            switch (mode)
            {
                case "status": return RunStatus();
                case "uninstall": return RunUninstall();
                case "mode-normal": return RunModeNormal();
                case "install": return RunInstall(acceptTestMode, noTestMode);
                default: return 1;
            }
        }

        // WinExe : aucune console n'est attachee d'office. Pour les options
        // console lancees depuis un terminal, on s'attache a la console
        // parente ; sinon (double-clic avec arguments) on en ouvre une pour
        // que le rapport reste visible.
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AttachConsole(uint dwProcessId);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool AllocConsole();
        private const uint AttachParentProcess = 0xFFFFFFFF;

        private static void AttachToConsole()
        {
            try
            {
                if (!AttachConsole(AttachParentProcess)) AllocConsole();
            }
            catch { }
        }

        // ==================================================== INSTALLATION

        internal static int RunInstall(bool acceptTestMode, bool noTestMode)
        {
            string workDir;
            string cerPath = null;
            bool rebootNeeded = false;

            // -- 0) MODE TEST : accord explicite AVANT toute modification -------
            //     Le pilote distribue est signe-test : Windows ne peut le charger
            //     qu'en mode test (filigrane en bas a droite de l'ecran). Depuis
            //     la v2.1.0, ce programme ne bascule JAMAIS Windows en mode test
            //     sans reponse explicite de l'utilisateur (defaut : NON).
            bool testSignedBuild = HasEmbeddedCert();
            bool testModeNeeded = testSignedBuild && !IsTestSigningOn();

            if (testModeNeeded && !acceptTestMode)
            {
                Ui.Say("[Mode test] Le pilote RoomEq distribue est signe-test.");
                Ui.Say("             Windows refuse de charger un pilote signe-test");
                Ui.Say("             tant qu'il n'est PAS en 'mode test' (bcdedit),");
                Ui.Say("             signale par un filigrane 'Mode de test' en bas a");
                Ui.Say("             droite du bureau apres redemarrage.");
                Ui.Say("");
                Ui.Say("             SANS le mode test, la carte RoomEq n'apparait pas.");
                Ui.Say("             (Trois voies : accepter le mode test ; attendre le");
                Ui.Say("              pilote signe Microsoft - docs/SIGNATURE-ET-STORE.md ;");
                Ui.Say("              ou utiliser RoomEq sans carte virtuelle : lecteur,");
                Ui.Say("              calibration et boucle WASAPI restent fonctionnels.)");
                Ui.Say("");
                if (noTestMode)
                {
                    Ui.Say(">>> --no-testmode : installation annulee sans rien modifier.");
                    Ui.Say(">>> L'application RoomEq reste utilisable (boucle WASAPI,");
                    Ui.Say(">>> lecteur + EQ, calibration au micro).");
                    return 0;
                }
                bool accord = Ui.Confirm("Activer le mode test de Windows et installer le pilote ?", false);
                if (!accord)
                {
                    Ui.Say("");
                    Ui.Say(">>> Choix NON : rien n'a ete modifie (aucun certificat importe,");
                    Ui.Say(">>> aucun pilote ajoute, aucun mode change).");
                    Ui.Say(">>> Revenez quand vous voulez : bouton Diagnostic complet");
                    Ui.Say(">>> (ou --status), bouton Revenir au mode normal.");
                    return 0;
                }
            }

            // -- 1) extraction du payload embarque ------------------------------
            Step(1, 6, "Extraction des fichiers pilote embarques");
            try
            {
                workDir = ExtractPayload(out cerPath);
                Ui.Say("       -> " + workDir);
            }
            catch (Exception ex)
            {
                return FailStep(ex.Message);
            }

            string inf = Path.Combine(workDir, InfName);

            // -- 2) certificat de test ----------------------------------------
            Step(2, 6, "Certificat de signature de test");
            if (cerPath == null)
            {
                Ui.Say("       [SAUT] aucun .cer embarque -> build officiellement signe");
                Ui.Say("       (ni certificat, ni mode test, ni redemarrage).");
            }
            else
            {
                try
                {
                    int imported = ImportCertToStores(cerPath);
                    Ui.Say("       -> importe dans Root + TrustedPublisher ("
                                      + imported + " nouveau(x) magasin(s))");
                }
                catch (Exception ex)
                {
                    return FailStep("importation du certificat impossible (" + ex.Message + ")");
                }
            }

            // -- 3) mode testsigning ------------------------------------------
            Step(3, 6, "Mode signature de test de Windows");
            if (cerPath == null)
            {
                Ui.Say("       [SAUT] inutile pour un pilote signe Microsoft.");
            }
            else if (IsTestSigningOn())
            {
                Ui.Say("       -> deja actif (pas de redemarrage necessaire).");
            }
            else
            {
                // pre-diagnostic : Secure Boot bloque le mode test d'office
                bool? secureBoot = IsSecureBootEnabled();
                string err;
                if (EnableTestSigning(out err))
                {
                    rebootNeeded = true;
                    Ui.Say("       -> ACTIVE. Le noyau l'appliquera au prochain demarrage.");
                    if (secureBoot == true)
                    {
                        Ui.Say("       [!] ATTENTION : Secure Boot semble ACTIF - le mode test");
                        Ui.Say("           risque d'etre ignore au demarrage. Si les cartes");
                        Ui.Say("           RoomEq n'apparaissent pas apres le reboot, desactivez");
                        Ui.Say("           Secure Boot dans le BIOS/UEFI (INSTALLATION-PILOTE.md).");
                    }
                }
                else
                {
                    Ui.Say("");
                    Ui.ErrorSafe("[ERREUR] impossible d'activer le mode testsigning (bcdedit : " + err + ").");
                    if (secureBoot == true)
                    {
                        Ui.Say("");
                        Ui.Say("         CAUSE CONFIRMEE : le Secure Boot est ACTIF. Windows refuse");
                        Ui.Say("         par conception de charger un pilote signe-test quand il l'est.");
                        Ui.Say("         Deux solutions :");
                        Ui.Say("           A. desactiver Secure Boot dans le BIOS/UEFI (redemarrage,");
                        Ui.Say("              touche Suppr/F2 selon la carte mere), puis relancer ce");
                        Ui.Say("              programme - voir INSTALLATION-PILOTE.md, cas 5 ;");
                        Ui.Say("           B. attendre le pilote signe Microsoft (phase A du plan de");
                        Ui.Say("              distribution, docs/SIGNATURE-ET-STORE.md).");
                    }
                    else
                    {
                        Ui.Say("         Cause la plus frequente : Secure Boot actif (voir ci-dessus).");
                        Ui.Say("         Journal complet ci-dessus. En cas de blocage, transmettre");
                        Ui.Say("         cette fenetre en copier-coller.");
                    }
                    return 1;
                }
            }

            // -- 4) magasin de pilotes ----------------------------------------
            Step(4, 6, "Ajout du pilote au magasin Windows (pnputil)");
            {
                // 259 (ERROR_NO_MORE_ITEMS) = ajoute mais aucun peripherique ne
                // correspond encore : normal, le peripherique est cree apres.
                string output;
                int code = RunHidden("pnputil.exe", "/add-driver \"" + inf + "\" /install", out output);
                if (code != 0 && code != 259)
                {
                    return FailStep("pnputil a echoue (code " + code + ") : "
                                    + OneLine(output, 200));
                }
                string oem = ParsePublishedName(output);
                Ui.Say("       -> paquet ajoute" + (oem != null ? " (" + oem + ")" : ""));
            }

            // -- 5) peripherique ROOT\ROOMEQVAD --------------------------------
            Step(5, 6, "Peripherique 'RoomEq Virtual Audio'");
            try
            {
                bool created = EnsureVirtualDevice(inf);
                Ui.Say("       -> " + (created ? "cree et pilote lie." : "deja present, pilote lie."));
            }
            catch (Exception ex)
            {
                if (rebootNeeded)
                {
                    Ui.Say("       [!] liaison differee au redemarrage (" + ex.Message + ")");
                    Ui.Say("           PnP reessaiera automatiquement au boot - pas bloquant.");
                }
                else
                {
                    return FailStep("creation du peripherique impossible (" + ex.Message + ")");
                }
            }

            // -- 6) DLL ASIO ----------------------------------------------------
            Step(6, 6, "Pilote ASIO (RoomEqAsio64.dll)");
            try
            {
                string dll = InstallAsio(workDir);
                Ui.Say("       -> " + dll);
                Ui.Say("          + enregistrement COM ASIO (HKLM).");
            }
            catch (Exception ex)
            {
                Ui.Say("       [!] ASIO non installe (" + ex.Message + ")");
                Ui.Say("           Le pilote WASAPI fonctionne sans ASIO ; reessayer plus tard");
                Ui.Say("           avec --install est sans risque (installation idempotente).");
            }

            // -- verdict ---------------------------------------------------------
            Ui.Say("");
            Ui.Say("==============================================================");
            Ui.HeadingSafe("RESULTAT : installation du pilote terminee.");
            Ui.Say("Apres redemarrage, les cartes 'RoomEq Input / RoomEq Output'");
            Ui.Say("figurent dans les parametres son de Windows.");
            if (rebootNeeded)
            {
                Ui.Say("");
                Ui.Say("* REDEMARRAGE REQUIS * (le mode test vient d'etre active).");
                Ui.Say("Un filigrane 'Mode de test' apparaitra en bas a droite de");
                Ui.Say("l'ecran : c'est normal et attendu avec un pilote signe-test.");
                Ui.Say("");
                if (Ui.Confirm("Redemarrer maintenant ?", true))
                {
                    Ui.Say("Redemarrage dans 5 secondes...");
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5") { UseShellExecute = false });
                }
                else
                {
                    Ui.Say("Redemarrez manuellement avant d'utiliser la carte son.");
                }
            }
            Ui.Say("");
            Ui.Pause();
            return 0;
        }

        // ==================================================== DESINSTALLATION

        internal static int RunUninstall()
        {
            Ui.Say("Desinstallation de la carte son virtuelle RoomEq");
            Ui.Say("(best effort : chaque etape est tentee, jamais bloquante)");
            Ui.Say("");

            Step(1, 5, "Peripherique ROOT\\ROOMEQVAD");
            try
            {
                bool removed = RemoveRootDevice();
                Ui.Say("       -> " + (removed ? "retire."
                        : "introuvable (rien a faire)."));
            }
            catch (Exception ex) { Ui.Say("       [!] " + ex.Message); }

            Step(2, 5, "Paquet du magasin de pilotes");
            try
            {
                string oem = ResolveStoreInfName();
                if (oem == null)
                {
                    Ui.Say("       -> introuvable dans le magasin (rien a faire).");
                }
                else
                {
                    string output;
                    int code = RunHidden("pnputil.exe",
                        "/delete-driver " + oem + " /uninstall /force", out output);
                    Ui.Say("       -> pnputil /delete-driver " + oem + " (code " + code + ")");
                }
            }
            catch (Exception ex) { Ui.Say("       [!] " + ex.Message); }

            Step(3, 5, "Enregistrement ASIO + DLL");
            try
            {
                UninstallAsio();
                Ui.Say("       -> cles registre + C:\\Program Files\\RoomEq retires.");
            }
            catch (Exception ex) { Ui.Say("       [!] " + ex.Message); }

            Step(4, 5, "Certificat de test");
            try
            {
                string cer = ExtractCertOnly();
                bool removedCert = RemoveCertFromStores(cer);
                Ui.Say("       -> " + (removedCert
                        ? "retire de Root + TrustedPublisher."
                        : "introuvable ou deja retire."));
            }
            catch (Exception ex) { Ui.Say("       [!] " + ex.Message); }

            Step(5, 5, "Fichiers temporaires");
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "RoomEqDriverSetup");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Ui.Say("       -> nettoyes.");
            }
            catch (Exception ex) { Ui.Say("       [!] " + ex.Message); }

            Ui.Say("");
            Ui.Say("Desinstallation terminee.");

            // le mode test n'a plus d'interet sans le pilote signe-test :
            // proposer de le couper maintenant (retour au mode normal)
            bool testWasOn = IsTestSigningOn();
            if (testWasOn)
            {
                Ui.Say("");
                if (Ui.Confirm("Le mode test de Windows est actif. Le couper maintenant ?", true))
                    return DisableTestSigningAndOfferReboot();
                Ui.Say(">>> mode test laisse actif. Pour le couper plus tard :");
                Ui.Say(">>> bouton Revenir au mode normal (ou --mode-normal,");
                Ui.Say(">>> ou bcdedit /set testsigning off).");
            }
            else
            {
                Ui.Say("(Le mode test de Windows n'est pas actif : rien a couper.)");
            }
            Ui.Say("");
            Ui.Pause();
            return 0;
        }

        // ==================================================== RETOUR AU MODE NORMAL

        /// <summary>
        /// --mode-normal : coupe le mode test de Windows (le filigrane « Mode de
        /// test » disparait apres redemarrage), puis propose le retrait complet
        /// du pilote signe-test devenu inutilisable en mode normal.
        /// </summary>
        internal static int RunModeNormal()
        {
            Ui.Say("Retour au mode NORMAL de Windows");
            Ui.Say("================================");
            Ui.Say("");

            if (!IsTestSigningOn())
            {
                Ui.Say("[SAUT] Windows n'est pas en mode test : rien a couper.");
                Ui.Say("       (Le filigrane 'Mode de test', s'il est visible, provient");
                Ui.Say("       d'une autre source ; aucun changement apporte ici.)");
            }
            else
            {
                int rc = DisableTestSigningAndOfferReboot();
                if (rc != 0) return rc;
            }

            // sans mode test, le pilote signe-test ne peut pas se charger :
            // proposer le retrait propre (defaut : OUI, mais rien n'est impose)
            bool deviceHere = false, storeHere = false;
            try { deviceHere = RootDeviceExists(); } catch { }
            try { storeHere = ResolveStoreInfName() != null; } catch { }
            if (deviceHere || storeHere)
            {
                Ui.Say("");
                Ui.Say("En mode normal, le pilote RoomEq signe-test ne peut PAS se");
                Ui.Say("charger : la carte virtuelle restera invisible tant que le");
                Ui.Say("pilote n'est pas officiellement signe Microsoft.");
                if (Ui.Confirm("Retirer le pilote et le certificat maintenant ?", true))
                {
                    Ui.Say("");
                    return RunUninstall();
                }
                Ui.Say(">>> pilote laisse en place (invisible mais inoffensif :");
                Ui.Say(">>> le bouton Desinstaller le retirera quand vous voudrez).");
            }
            else
            {
                Ui.Say("");
                Ui.Say("(Aucune trace du pilote RoomEq sur cette machine :");
                Ui.Say(" rien d'autre a nettoyer.)");
            }
            Ui.Say("");
            Ui.Pause();
            return 0;
        }

        /// <summary>Coupe le mode test (bcdedit off, puis deletevalue en secours) puis propose le redemarrage.</summary>
        private static int DisableTestSigningAndOfferReboot()
        {
            string err;
            int code = RunHidden("bcdedit.exe", "/set testsigning off", out err);
            if (code != 0)
            {
                // valeur absente de l'entree de boot : essayer la suppression pure
                string err2;
                int code2 = RunHidden("bcdedit.exe", "/deletevalue {current} testsigning", out err2);
                if (code2 != 0 && IsTestSigningOn())
                {
                    Ui.ErrorSafe("[ERREUR] impossible de couper le mode test (bcdedit : "
                                      + OneLine(err, 160) + " / " + OneLine(err2, 120) + ")");
                    Ui.Say("         Cause la plus frequente : Secure Boot actif (le mode");
                    Ui.Say("         test n'a alors jamais pu etre mis en route).");
                    return 1;
                }
            }

            Ui.Say("");
            Ui.Say(">>> MODE TEST DESACTIVE. Le filigrane 'Mode de test' disparaitra");
            Ui.Say(">>> definitivement apres le PROCHAIN REDEMARRAGE.");
            Ui.Say("");
            if (Ui.Confirm("Redemarrer maintenant ?", true))
            {
                Ui.Say("Redemarrage dans 5 secondes...");
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5") { UseShellExecute = false });
            }
            else
            {
                Ui.Say("Redemarrez quand vous voulez pour appliquer le retour au normal.");
            }
            return 0;
        }

        // ==================================================== ETAT

        internal static int RunStatus()
        {
            bool? sb = IsSecureBootEnabled();
            bool testMode = IsTestSigningOn();
            Ui.Say("Etat de la machine (rien n'est modifie) :");
            Ui.Say("");
            Ui.Say("  Secure Boot        : " + NullSafe(sb, "Actif", "Inactif"));
            Ui.Say("  Mode testsigning   : " + (testMode ? "Actif" : "Inactif"));
            Ui.Say("  Build pilote       : " + (HasEmbeddedCert()
                ? "signe-test (mode test requis pour charger)"
                : "signature officielle (aucun mode test requis)"));

            string oem = null;
            try { oem = ResolveStoreInfName(); } catch { }
            Ui.Say("  Pilote magasin     : " + (oem != null ? "Present (" + oem + ")" : "Absent"));

            bool dev = false;
            uint problem = 0, status = 0;
            bool started = false, hasProblem = false;
            try { dev = ProbeRootDevice(ref status, ref problem); } catch { }
            if (dev)
            {
                started = (status & 0x00000008) != 0;        // DN_STARTED
                hasProblem = (status & 0x00000400) != 0;     // DN_HAS_PROBLEM
                Ui.Say("  Peripherique       : Present (" + DeviceStateText(started, hasProblem, problem) + ")");
            }
            else
            {
                Ui.Say("  Peripherique       : Absent");
            }

            string svc = ServiceState("roomeqvad");
            Ui.Say("  Service roomeqvad  : " + svc);

            string asio = AsioDllRegisteredPath();
            Ui.Say("  ASIO (registre)    : " + (asio != null ? "Enregistre -> " + asio : "Non enregistre"));
            Ui.Say("");

            // ---------------- VERDICT : pourquoi la carte n'apparait pas -------
            Ui.Say("--------------------------------------------------------------");
            Ui.HeadingSafe("VERDICT");
            Ui.Say("--------------------------------------------------------------");
            if (!dev && oem == null)
            {
                Ui.Say("  Le pilote RoomEq n'est pas installe sur cette machine.");
                Ui.Say("  -> " + (GuiActive ? "bouton 'Installer le pilote' en bas de la fenetre."
                                    : "Lancez : RoomEqDriverSetup.exe --install"));
                Ui.Say("     (le mode test sera PROPOSE explicitement ; repondre non");
                Ui.Say("      n'installe rien et laisse Windows en mode normal)");
            }
            else if (!dev)
            {
                Ui.Say("  Le paquet pilote est dans le magasin mais le PERIPHERIQUE");
                Ui.Say("  n'existe pas : c'est lui qui cree les entrees son.");
                Ui.Say("  -> " + (GuiActive ? "bouton 'Installer le pilote' : l'etape 5 cree"
                                    : "Lancez : RoomEqDriverSetup.exe --install"));
                Ui.Say("     le peripherique ROOT\\ROOMEQVAD ; une installation partielle");
                Ui.Say("      par pnputil seul ne suffit pas)");
            }
            else if (!testMode && HasEmbeddedCert())
            {
                Ui.Say("  CAUSE TROUVEE : Windows n'est PAS en mode test, il refuse");
                Ui.Say("  donc de charger ce pilote signe-test (code " + problem + ").");
                Ui.Say("  TROIS VOIES :");
                Ui.Say("   A. Accepter le mode test : " + (GuiActive
                        ? "bouton Installer (question explicite),"
                        : "--install (question explicite),"));
                Ui.Say("      filigrane 'Mode de test' sur le bureau ensuite ;");
                Ui.Say("   B. Rester en mode normal SANS carte virtuelle : RoomEq reste");
                Ui.Say("      utilisable (lecteur + EQ, calibration, boucle WASAPI) ;");
                Ui.Say("   C. Pilote signe Microsoft (definitif, sans mode test) :");
                Ui.Say("      voir docs/SIGNATURE-ET-STORE.md (phase A).");
            }
            else if (testMode && sb == true)
            {
                Ui.Say("  CAUSE PROBABLE : le mode test est actif mais le Secure Boot");
                Ui.Say("  l'IGNORE au demarrage (conception de Windows). Le pilote ne");
                Ui.Say("  peut donc pas se charger.");
                Ui.Say("  -> Desactiver Secure Boot dans le BIOS/UEFI, redemarrer ;");
                Ui.Say("     ou " + (GuiActive ? "bouton Revenir au mode normal."
                              : "revenir au mode normal : --mode-normal."));
            }
            else if (hasProblem && !started)
            {
                Ui.Say("  Le peripherique existe mais son pilote ne demarre pas");
                Ui.Say("  (code " + problem + " : " + ProblemText(problem) + ").");
                Ui.Say("  -> Relancez " + (GuiActive ? "l'installation"
                              : "--install") + " (liaison du pilote retentee) puis");
                Ui.Say("     redemarrer ; si le code persiste, transmettre ce rapport.");
            }
            else
            {
                Ui.Say("  Tout semble correct cote pilote. Si la carte n'apparait");
                Ui.Say("  toujours pas dans les peripheriques Windows :");
                Ui.Say("   1. Parametres > Systeme > Son > Panneau son (mmsys.cpl) ;");
                Ui.Say("   2. clic droit dans la liste > 'Afficher les peripheriques");
                Ui.Say("      desactives' : 'RoomEq Input / RoomEq Output' doivent y");
                Ui.Say("      figurer (clic droit > Activer le cas echeant) ;");
                Ui.Say("   3. sinon, redemarrer Windows puis " + (GuiActive
                        ? "bouton Diagnostic complet." : "relancer --status."));
            }
            Ui.Say("--------------------------------------------------------------");
            Ui.Say("");
            Ui.Pause();
            return 0;
        }

        // ------------------------------------------------ capture d'etat (GUI)

        /// <summary>
        /// Instantane des sondes de la machine pour le panneau d'etat de la
        /// fenetre graphique (les textes completes/VERDICT restent dans
        /// RunStatus, ce capture ne modifie rien).
        /// </summary>
        internal sealed class StatusSnap
        {
            public bool? SecureBoot;
            public bool TestMode;
            public bool TestSignedBuild;
            public string StoreInf;        // null = absent du magasin
            public bool Device;
            public bool DeviceStarted;
            public bool DeviceHasProblem;
            public uint Problem;
            public string Service;
            public string AsioPath;       // null = non enregistre
        }

        internal static StatusSnap CaptureStatus()
        {
            var s = new StatusSnap();
            try { s.SecureBoot = IsSecureBootEnabled(); } catch { }
            s.TestMode = IsTestSigningOn();
            s.TestSignedBuild = HasEmbeddedCert();
            try { s.StoreInf = ResolveStoreInfName(); } catch { }
            uint status = 0, problem = 0;
            try { s.Device = ProbeRootDevice(ref status, ref problem); } catch { }
            s.DeviceStarted = (status & 0x00000008) != 0;   // DN_STARTED
            s.DeviceHasProblem = (status & 0x00000400) != 0; // DN_HAS_PROBLEM
            s.Problem = problem;
            s.Service = ServiceState("roomeqvad");
            s.AsioPath = AsioDllRegisteredPath();
            return s;
        }

        /// <summary>Etat lisible du peripherique racine.</summary>
        private static string DeviceStateText(bool started, bool hasProblem, uint problem)
        {
            if (hasProblem) return "probleme code " + problem + " : " + ProblemText(problem);
            if (started) return "demarre, pilote actif";
            return "present, pilote non demarre";
        }

        /// <summary>Libelles des codes de probleme PnP les plus frequents.</summary>
        private static string ProblemText(uint code)
        {
            switch (code)
            {
                case 10: return "impossible de demarrer le peripherique";
                case 28: return "pilotes non installes";
                case 31: return "peripherique ne fonctionne pas correctement";
                case 37: return "echec d'initialisation du pilote (signature ?)";
                case 39: return "chargement du pilote impossible (signature ?)";
                case 43: return "peripherique signale un probleme et a ete arrete";
                case 45: return "peripherique non present (debranche ou supprime)";
                case 52: return "signature numerique non verifiable (pilote signe-test refuse en mode normal)";
                default: return "code PnP " + code;
            }
        }

        /// <summary>Etat du service pilote via sc.exe (RUNNING / STOPPED / ...).</summary>
        private static string ServiceState(string service)
        {
            try
            {
                string output;
                // 1060 = ERROR_SERVICE_DOES_NOT_EXIST : etat NORMAL avant
                // installation, pas une erreur (v2.2.1 : sortie brute masquee).
                int code = RunHidden("sc.exe", "query " + service, out output,
                                     new[] { 1060 });
                if (code != 0) return "non installe";
                if (output.IndexOf("RUNNING", StringComparison.OrdinalIgnoreCase) >= 0) return "en cours";
                if (output.IndexOf("STOPPED", StringComparison.OrdinalIgnoreCase) >= 0) return "arrete";
                if (output.IndexOf("PAUSED", StringComparison.OrdinalIgnoreCase) >= 0) return "en pause";
                return "etat inconnu";
            }
            catch { return "indetermine"; }
        }

        /// <summary>Le build embarque-t-il le certificat de test ? (faux = signature officielle)</summary>
        private static bool HasEmbeddedCert()
        {
            try
            {
                using (Stream s = Assembly.GetExecutingAssembly()
                           .GetManifestResourceStream(CertResName))
                {
                    return s != null;
                }
            }
            catch { return false; }
        }

        /// <summary>
        /// Sonde le peripherique ROOT\ROOMEQVAD : true s'il existe, avec son
        /// statut PnP et son code de probleme en sortie.
        /// </summary>
        private static bool ProbeRootDevice(ref uint status, ref uint problem)
        {
            IntPtr set = NativeMethods.SetupDiGetClassDevsW(
                IntPtr.Zero, "ROOT", IntPtr.Zero,
                NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_ALLCLASSES);
            if (set == IntPtr.Zero || set == new IntPtr(-1))
                throw new Exception("SetupDiGetClassDevs(ROOT) a echoue");
            try
            {
                NativeMethods.SP_DEVINFO_DATA dev;
                if (!FindRoomEqDevice(set, out dev)) return false;
                uint st = 0, pb = 0;
                NativeMethods.CM_Get_DevNode_Status(out st, out pb, dev.DevInst, 0);
                status = st;
                problem = pb;
                return true;
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(set);
            }
        }

        // ==================================================== payload embarque

        /// <summary>Extrait les ressources vers %TEMP%\RoomEqDriverSetup.</summary>
        private static string ExtractPayload(out string cerPath)
        {
            string dir = Path.Combine(Path.GetTempPath(), "RoomEqDriverSetup");
            if (Directory.Exists(dir))
            {
                try { Directory.Delete(dir, true); } catch { }
            }
            Directory.CreateDirectory(dir);

            Assembly asm = Assembly.GetExecutingAssembly();
            foreach (string name in new[] { InfName, "roomeqvad.sys", "roomeqvad.cat", AsioDllName })
            {
                WriteResource(asm, name, Path.Combine(dir, name));
            }

            cerPath = null;
            using (Stream s = asm.GetManifestResourceStream(CertResName))
            {
                if (s != null) cerPath = Path.Combine(dir, CertResName);
            }
            if (cerPath != null) WriteResource(asm, CertResName, cerPath);

            return dir;
        }

        private static string ExtractCertOnly()
        {
            Assembly asm = Assembly.GetExecutingAssembly();
            using (Stream s = asm.GetManifestResourceStream(CertResName))
            {
                if (s == null) return null;
            }
            string dir = Path.Combine(Path.GetTempPath(), "RoomEqDriverSetup");
            Directory.CreateDirectory(dir);
            string p = Path.Combine(dir, CertResName);
            WriteResource(asm, CertResName, p);
            return p;
        }

        private static void WriteResource(Assembly asm, string name, string file)
        {
            using (Stream s = asm.GetManifestResourceStream(name))
            {
                if (s == null)
                    throw new Exception("ressource embarquee manquante : " + name);
                using (var fs = File.Create(file))
                {
                    s.CopyTo(fs);
                }
            }
        }

        // ==================================================== ASIO

        private static string InstallAsio(string workDir)
        {
            string programDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RoomEq");
            Directory.CreateDirectory(programDir);
            string target = Path.Combine(programDir, AsioDllName);
            File.Copy(Path.Combine(workDir, AsioDllName), target, true);

            using (RegistryKey asio = Registry.LocalMachine.CreateSubKey(@"Software\ASIO\RoomEq ASIO"))
            {
                asio.SetValue("CLSID", AsioClsid);
                asio.SetValue("Description", "RoomEq ASIO (pont vers l'egaliseur RoomEq)");
            }
            using (RegistryKey clsid = Registry.LocalMachine.CreateSubKey(
                @"Software\Classes\CLSID\" + AsioClsid))
            {
                clsid.SetValue(null, "RoomEq ASIO");
            }
            using (RegistryKey inproc = Registry.LocalMachine.CreateSubKey(
                @"Software\Classes\CLSID\" + AsioClsid + @"\InprocServer32"))
            {
                inproc.SetValue(null, target);
                inproc.SetValue("ThreadingModel", "Apartment");
            }
            return target;
        }

        private static void UninstallAsio()
        {
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(@"Software\Classes\CLSID\" + AsioClsid);
            }
            catch { }
            try
            {
                Registry.LocalMachine.DeleteSubKeyTree(@"Software\ASIO\RoomEq ASIO");
            }
            catch { }
            string dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "RoomEq");
            if (Directory.Exists(dir)) Directory.Delete(dir, true);
        }

        private static string AsioDllRegisteredPath()
        {
            try
            {
                using (RegistryKey k = Registry.LocalMachine.OpenSubKey(
                    @"Software\Classes\CLSID\" + AsioClsid + @"\InprocServer32"))
                {
                    if (k == null) return null;
                    return k.GetValue(null) as string;
                }
            }
            catch { return null; }
        }

        // ==================================================== certificats

        private static int ImportCertToStores(string cerPath)
        {
            var cert = new X509Certificate2(cerPath);
            int imported = 0;
            foreach (StoreName name in new[] { StoreName.Root, StoreName.TrustedPublisher })
            {
                using (var store = new X509Store(name, StoreLocation.LocalMachine))
                {
                    store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
                    var found = store.Certificates.Find(
                        X509FindType.FindByThumbprint, cert.Thumbprint, false);
                    if (found.Count == 0)
                    {
                        store.Add(cert);
                        imported++;
                    }
                }
            }
            return imported;
        }

        private static bool RemoveCertFromStores(string cerPath)
        {
            string thumbprint = null;
            if (cerPath != null && File.Exists(cerPath))
            {
                try { thumbprint = new X509Certificate2(cerPath).Thumbprint; }
                catch { }
            }
            if (thumbprint == null)
            {
                using (var root = new X509Store(StoreName.Root, StoreLocation.LocalMachine))
                {
                    root.Open(OpenFlags.ReadOnly | OpenFlags.OpenExistingOnly);
                    foreach (X509Certificate2 c in root.Certificates)
                    {
                        if (c.Subject != null && c.Subject.Contains(CertSubject))
                        {
                            thumbprint = c.Thumbprint;
                            break;
                        }
                    }
                }
            }
            if (thumbprint == null) return false;

            bool removed = false;
            foreach (StoreName name in new[] { StoreName.Root, StoreName.TrustedPublisher })
            {
                using (var store = new X509Store(name, StoreLocation.LocalMachine))
                {
                    store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
                    var col = store.Certificates.Find(
                        X509FindType.FindByThumbprint, thumbprint, false);
                    if (col.Count > 0)
                    {
                        store.Remove(col[0]);
                        removed = true;
                    }
                }
            }
            return removed;
        }

        // ==================================================== testsigning / Secure Boot

        private static bool IsTestSigningOn()
        {
            try
            {
                string output;
                RunHidden("bcdedit.exe", "/enum {current}", out output);
                foreach (string raw in (output ?? "").Split('\n'))
                {
                    string line = raw.Trim().TrimEnd('\r');
                    if (line.ToLowerInvariant().IndexOf("testsigning", StringComparison.Ordinal) < 0)
                        continue;
                    string[] toks = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (toks.Length < 2) return false;
                    string val = toks[toks.Length - 1].ToLowerInvariant();
                    return val == "yes" || val == "oui";
                }
            }
            catch { }
            return false;
        }

        private static bool EnableTestSigning(out string error)
        {
            string stdout;
            int code = RunHidden("bcdedit.exe", "/set testsigning on", out stdout);
            error = code == 0 ? null
                : "code " + code + (((stdout ?? "").Trim().Length > 0)
                    ? " ; '" + OneLine(stdout, 160) + "'" : "");
            return code == 0;
        }

        /// <summary>True/False = etat Secure Boot, null = indetermine.</summary>
        private static bool? IsSecureBootEnabled()
        {
            try
            {
                using (var searcher = new System.Management.ManagementObjectSearcher(
                    @"root\Microsoft\Windows\SecureBoot",
                    "SELECT SecureBootEnabled FROM Win32_SecureBootStatus"))
                {
                    foreach (var o in searcher.Get())
                    {
                        try { return Convert.ToBoolean(o["SecureBootEnabled"]); }
                        finally { o.Dispose(); }
                    }
                }
            }
            catch { }
            return null;
        }

        // ==================================================== peripherique ROOT

        /// <summary>true = cree par l'appel, false = deja present.</summary>
        private static bool EnsureVirtualDevice(string inf)
        {
            bool exists = RootDeviceExists();
            if (!exists) CreateRootDevice(inf);

            bool driverReboot;
            if (!NativeMethods.UpdateDriverForPlugAndPlayDevicesW(
                    IntPtr.Zero, HwId, inf,
                    NativeMethods.INSTALLFLAG_FORCE | NativeMethods.INSTALLFLAG_NONINTERACTIVE,
                    out driverReboot))
            {
                int err = Marshal.GetLastWin32Error();
                throw new Exception("UpdateDriverForPlugAndPlayDevices a echoue (erreur 0x"
                                    + err.ToString("X") + ")");
            }
            return !exists;
        }

        private static bool RootDeviceExists()
        {
            IntPtr set = NativeMethods.SetupDiGetClassDevsW(
                IntPtr.Zero, "ROOT", IntPtr.Zero,
                NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_ALLCLASSES);
            if (set == IntPtr.Zero || set == new IntPtr(-1))
                throw new Exception("SetupDiGetClassDevs(ROOT) a echoue (erreur 0x"
                                    + Marshal.GetLastWin32Error().ToString("X") + ")");
            try
            {
                NativeMethods.SP_DEVINFO_DATA dev;
                return FindRoomEqDevice(set, out dev);
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(set);
            }
        }

        private static void CreateRootDevice(string inf)
        {
            Guid classGuid = new Guid("4d36e96c-e325-11ce-bfc1-08002be10318"); // classe MEDIA
            var className = new StringBuilder(64);
            uint needed;
            Guid fromInf;
            if (NativeMethods.SetupDiGetINFClassW(inf, out fromInf, className, 64, out needed))
            {
                classGuid = fromInf;
            }
            else
            {
                // v2.2.1 : plus d'echec silencieux. Un INF de style legacy
                // ($Windows NT$) fait echouer GetINFClass AVEC 0xE0000205,
                // et cette erreur residuelle pollue ensuite les appels
                // SetupDi de creation du peripherique.
                Ui.Say("       [!] classe illisible dans l'INF (0x"
                    + Marshal.GetLastWin32Error().ToString("X")
                    + ") -> classe MEDIA utilisee par defaut.");
            }

            IntPtr set = NativeMethods.SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
            if (set == IntPtr.Zero || set == new IntPtr(-1))
                throw new Exception("SetupDiCreateDeviceInfoList a echoue (erreur 0x"
                                    + Marshal.GetLastWin32Error().ToString("X") + ")");
            try
            {
                var dev = new NativeMethods.SP_DEVINFO_DATA();
                dev.cbSize = Marshal.SizeOf(typeof(NativeMethods.SP_DEVINFO_DATA));

                // v2.2.1 : DICD_GENERATE_ID fabrique lui-meme l'instance
                // "ROOT\<nom>\NNNN" -> il ne faut JAMAIS passer le prefixe
                // "ROOT\" ici (sinon instance en ROOT\ROOT\ROOMEQVAD\NNNN
                // et echec selon les builds de Windows). Le prefixe ne sert
                // que pour le HARDWAREID ci-dessous.
                string deviceName = HwId.StartsWith("ROOT\\", StringComparison.OrdinalIgnoreCase)
                    ? HwId.Substring(5) : HwId;

                if (!NativeMethods.SetupDiCreateDeviceInfoW(
                        set, deviceName, ref classGuid, null, IntPtr.Zero,
                        NativeMethods.DICD_GENERATE_ID, ref dev))
                    throw new Exception("SetupDiCreateDeviceInfo a echoue (erreur 0x"
                                        + Marshal.GetLastWin32Error().ToString("X") + ")");

                byte[] hwid = Encoding.Unicode.GetBytes(HwId + "\0\0");
                if (!NativeMethods.SetupDiSetDeviceRegistryPropertyW(
                        set, ref dev, NativeMethods.SPDRP_HARDWAREID,
                        hwid, (uint)hwid.Length))
                    throw new Exception("SetupDiSetDeviceRegistryProperty a echoue (erreur 0x"
                                        + Marshal.GetLastWin32Error().ToString("X") + ")");

                if (!NativeMethods.SetupDiCallClassInstaller(
                        NativeMethods.DIF_REGISTERDEVICE, set, ref dev))
                    throw new Exception("DIF_REGISTERDEVICE a echoue (erreur 0x"
                                        + Marshal.GetLastWin32Error().ToString("X") + ")");
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(set);
            }
        }

        private static bool RemoveRootDevice()
        {
            IntPtr set = NativeMethods.SetupDiGetClassDevsW(
                IntPtr.Zero, "ROOT", IntPtr.Zero,
                NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_ALLCLASSES);
            if (set == IntPtr.Zero || set == new IntPtr(-1))
                throw new Exception("SetupDiGetClassDevs(ROOT) a echoue");
            try
            {
                NativeMethods.SP_DEVINFO_DATA dev;
                if (!FindRoomEqDevice(set, out dev)) return false;
                if (!NativeMethods.SetupDiCallClassInstaller(
                        NativeMethods.DIF_REMOVE, set, ref dev))
                    throw new Exception("DIF_REMOVE a echoue (erreur 0x"
                                        + Marshal.GetLastWin32Error().ToString("X") + ")");
                return true;
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(set);
            }
        }

        private static bool FindRoomEqDevice(IntPtr set, out NativeMethods.SP_DEVINFO_DATA found)
        {
            for (uint i = 0; ; i++)
            {
                var dev = new NativeMethods.SP_DEVINFO_DATA();
                dev.cbSize = Marshal.SizeOf(typeof(NativeMethods.SP_DEVINFO_DATA));
                if (!NativeMethods.SetupDiEnumDeviceInfo(set, i, ref dev))
                    break;
                string id = GetDeviceId(dev.DevInst);
                if (id != null && id.StartsWith("ROOT\\ROOMEQVAD", StringComparison.OrdinalIgnoreCase))
                {
                    found = dev;
                    return true;
                }
            }
            found = new NativeMethods.SP_DEVINFO_DATA();
            return false;
        }

        private static string GetDeviceId(uint devInst)
        {
            var sb = new StringBuilder(260);
            if (NativeMethods.CM_Get_Device_IDW(devInst, sb, (uint)sb.Capacity, 0) == 0)
                return sb.ToString();
            return null;
        }

        // ==================================================== magasin PnP

        private static string ResolveStoreInfName()
        {
            string output;
            RunHidden("pnputil.exe", "/enum-drivers", out output);
            return ParseDriverStoreName(output, InfName);
        }

        /// <summary>
        /// Premier "nom publié" (oemNN.inf) d'une sortie pnputil, quelle que
        /// soit la langue (v2.2.1 : tolère les accents mal décodés).
        /// </summary>
        private static string ParsePublishedName(string enumOutput)
        {
            foreach (string raw in (enumOutput ?? "").Split('\n'))
            {
                string line = raw.Trim().TrimEnd('\r');
                int i = line.IndexOf(':');
                if (i <= 0) continue;
                string key = line.Substring(0, i).Trim().ToLowerInvariant();
                // "Published Name" / "nom du (fichier|pilote) publié" :
                // contient toujours "publi", accents ou pas (mojibake inclus).
                if (!key.Contains("publi")) continue;
                string value = line.Substring(i + 1).Trim();
                if (value.Length > 0) return value;
            }
            return null;
        }

        /// <summary>Analyse 'pnputil /enum-drivers' (EN/FR) -> oemNN.inf.</summary>
        private static string ParseDriverStoreName(string enumOutput, string originalInfName)
        {
            if (string.IsNullOrEmpty(enumOutput) || string.IsNullOrEmpty(originalInfName))
                return null;
            string published = null;
            foreach (string raw in enumOutput.Split('\n'))
            {
                string line = raw.Trim().TrimEnd('\r');
                if (line.Length == 0) { published = null; continue; }
                int i = line.IndexOf(':');
                if (i <= 0) continue;
                string key = line.Substring(0, i).Trim();
                string value = line.Substring(i + 1).Trim();
                // v2.2.1 : cles tolérantes aux accents et aux libellés
                // (EN "published name"/"original name", FR "nom du fichier
                // publié"/"nom du pilote publié"/"nom inf d'origine"...) :
                // les sorties pnputil passent par la page OEM CP850 et le "é"
                // peut arriver mal décodé ; "publi" et "orig" matchent tout.
                string k = key.ToLowerInvariant();
                if (k.Contains("publi"))
                {
                    published = value;
                }
                else if (k.Contains("orig") && (k.Contains("inf") || k.Contains("name")))
                {
                    if (published != null
                        && string.Equals(value, originalInfName, StringComparison.OrdinalIgnoreCase))
                        return published;
                }
            }
            return null;
        }

        // ==================================================== outils

        internal static bool IsElevated()
        {
            using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                return new System.Security.Principal.WindowsPrincipal(id)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }

        private static void Step(int n, int total, string title)
        {
            Ui.Say("");
            Ui.Say("[" + n + "/" + total + "] " + title);
        }

        private static int FailStep(string message)
        {
            Ui.Say("");
            Ui.ErrorSafe("[ERREUR] " + message);
            Ui.Say("");
            Ui.Say("Rien n'a ete casse : les etapes precedentes sont inoffensives");
            Ui.Say("(certificat uniquement, ou paquet ajoute sans liaison).");
            Ui.Say("Transmettre " + (GuiActive ? "le contenu du journal (copier-coller)"
                    : "cette fenetre (copier-coller)") + " pour un diagnostic precis.");
            Ui.Say("");
            Ui.Pause();
            return 1;
        }

        private static string NullSafe(bool? v, string ifTrue, string ifFalse)
        {
            if (v == null) return "Indetermine";
            return v.Value ? ifTrue : ifFalse;
        }

        private static string OneLine(string text, int max)
        {
            if (text == null) return "";
            string s = text.Replace("\r", " ").Replace("\n", " ").Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "...";
        }

        private static int RunHidden(string exe, string args, out string output,
                                     int[] quietExitCodes = null)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                // v2.2.1 : sc.exe / pnputil / bcdedit sortent dans la page de
                // code OEM (CP850 en francais). Sans ce decodage explicite,
                // .NET les lisait en Windows-1252 : accents mojibakes
                // ("echec" -> "\u201achec") et les cles accentuees de pnputil
                // ("nom du fichier publie") ne matchaient plus -> faux
                // "Pilote magasin : Absent" apres un ajout reussi.
                StandardOutputEncoding = Encoding.GetEncoding(850),
                StandardErrorEncoding = Encoding.GetEncoding(850),
            };
            using (var p = Process.Start(psi))
            {
                output = p.StandardOutput.ReadToEnd();
                string err = p.StandardError.ReadToEnd();
                if (!p.WaitForExit(120000))
                {
                    try { p.Kill(); } catch { }
                    p.WaitForExit();
                }
                // v2.2.1 : certains codes d'echec sont des etats NORMAUX
                // attendus (ex. sc.exe 1060 = service absent avant
                // installation) : pas de sortie brute dans le journal.
                if (p.ExitCode != 0
                    && (quietExitCodes == null || Array.IndexOf(quietExitCodes, p.ExitCode) < 0))
                {
                    Ui.Say("       (sortie " + exe + " : " + OneLine(output, 300)
                        + (err.Trim().Length > 0 ? " [stderr] " + OneLine(err, 200) : "") + ")");
                }
                return p.ExitCode;
            }
        }
    }

    /// <summary>Interop SetupAPI / newdev / cfgmgr32 (recette devcon).</summary>
    internal static class NativeMethods
    {
        internal const uint DIGCF_PRESENT = 0x00000002;
        internal const uint DIGCF_ALLCLASSES = 0x00000004;
        internal const uint DICD_GENERATE_ID = 0x00000001;
        internal const uint SPDRP_HARDWAREID = 0x00000001;
        internal const uint DIF_REMOVE = 0x00000005;
        internal const uint DIF_REGISTERDEVICE = 0x00000019;
        internal const uint INSTALLFLAG_FORCE = 0x00000001;
        internal const uint INSTALLFLAG_NONINTERACTIVE = 0x00000004;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        internal struct SP_DEVINFO_DATA
        {
            public int cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 200)]
            public string DeviceDescription;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetupDiGetClassDevsW(
            IntPtr classGuid, string enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiEnumDeviceInfo(
            IntPtr deviceInfoSet, uint memberIndex, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiGetINFClassW(
            string infPath, out Guid classGuid, StringBuilder className,
            uint classNameSize, out uint requiredSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern IntPtr SetupDiCreateDeviceInfoList(
            ref Guid classGuid, IntPtr hwndParent);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiCreateDeviceInfoW(
            IntPtr deviceInfoSet, string deviceDescription, ref Guid classGuid,
            string deviceId, IntPtr hwndParent, uint creationFlags,
            ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiSetDeviceRegistryPropertyW(
            IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData,
            uint property, byte[] propertyBuffer, uint propertyBufferSize);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool SetupDiCallClassInstaller(
            uint installFunction, IntPtr deviceInfoSet, ref SP_DEVINFO_DATA deviceInfoData);

        [DllImport("newdev.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UpdateDriverForPlugAndPlayDevicesW(
            IntPtr hwndParent, string hardwareId, string fullInfPath,
            uint installFlags, out bool bRebootRequired);

        [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        internal static extern int CM_Get_Device_IDW(
            uint devInst, StringBuilder buffer, uint bufferLength, uint flags);

        [DllImport("cfgmgr32.dll", SetLastError = true)]
        internal static extern int CM_Get_DevNode_Status(
            out uint status, out uint problem, uint devInst, uint flags);
    }
}
