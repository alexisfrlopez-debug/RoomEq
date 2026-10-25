// ============================================================================
//  RoomEqDriverSetup - installation de la carte son virtuelle RoomEq SANS MSI.
//
//  Distribution '2 executables' (demande utilisateur 2026-10) :
//    - RoomEq-<version>-win-x64.exe : l'application Avalonia, portable,
//      auto-contenue, sans installation ni UAC ;
//    - RoomEqDriverSetup.exe (ce programme) : pilote + ASIO en un seul
//      fichier, les fichiers pilote sont EMBARQUES en ressource.
//
//  Usage :
//    RoomEqDriverSetup.exe                 installation (defaut)
//    RoomEqDriverSetup.exe --status        etat de la machine (rien ne change) +
//                                           VERDICT : pourquoi la carte n'apparait
//                                           pas, le cas echeant, et quoi faire
//    RoomEqDriverSetup.exe --uninstall     retrait complet (best effort)
//    RoomEqDriverSetup.exe --mode-normal   RETOUR AU MODE NORMAL de Windows :
//                                           coupe le mode test (filigrane inclus),
//                                           propose le retrait du pilote signe-test
//    RoomEqDriverSetup.exe --install --accept-testmode
//                                         installe sans demander la confirmation
//                                           du passage en mode test (automatisation)
//    RoomEqDriverSetup.exe --install --no-testmode
//                                         refuse le mode test sans question :
//                                           installation annulee proprement
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

        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            try { Console.Title = "RoomEqDriverSetup"; } catch { }

            bool acceptTestMode = Array.IndexOf(args, "--accept-testmode") >= 0;
            bool noTestMode = Array.IndexOf(args, "--no-testmode") >= 0;
            string mode = "install";
            foreach (string a in args)
            {
                string m = a.TrimStart('-').ToLowerInvariant();
                if (m == "u") m = "uninstall";
                if (m == "s") m = "status";
                if (m == "n" || m == "normal") m = "mode-normal";
                if (m == "install" || m == "uninstall" || m == "status" || m == "mode-normal")
                {
                    mode = m;
                    break;
                }
                if (m == "accept-testmode" || m == "no-testmode" || m == "i") continue;
                if (m.Length > 0)
                {
                    Console.WriteLine("Argument inconnu : " + a);
                    Console.WriteLine("Usage : RoomEqDriverSetup.exe [--install | --status | --uninstall | --mode-normal]");
                    Console.WriteLine("        [--install --accept-testmode | --install --no-testmode]");
                    return 1;
                }
            }

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

            switch (mode)
            {
                case "status": return RunStatus();
                case "uninstall": return RunUninstall();
                case "mode-normal": return RunModeNormal();
                case "install": return RunInstall(acceptTestMode, noTestMode);
                default: return 1;
            }
        }

        // ==================================================== INSTALLATION

        private static int RunInstall(bool acceptTestMode, bool noTestMode)
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
                Console.WriteLine("[Mode test] Le pilote RoomEq distribue est signe-test.");
                Console.WriteLine("             Windows refuse de charger un pilote signe-test");
                Console.WriteLine("             tant qu'il n'est PAS en 'mode test' (bcdedit),");
                Console.WriteLine("             signale par un filigrane 'Mode de test' en bas a");
                Console.WriteLine("             droite du bureau apres redemarrage.");
                Console.WriteLine();
                Console.WriteLine("             SANS le mode test, la carte RoomEq n'apparait pas.");
                Console.WriteLine("             (Trois voies : accepter le mode test ; attendre le");
                Console.WriteLine("              pilote signe Microsoft - docs/SIGNATURE-ET-STORE.md ;");
                Console.WriteLine("              ou utiliser RoomEq sans carte virtuelle : lecteur,");
                Console.WriteLine("              calibration et boucle WASAPI restent fonctionnels.)");
                Console.WriteLine();
                if (noTestMode)
                {
                    Console.WriteLine(">>> --no-testmode : installation annulee sans rien modifier.");
                    Console.WriteLine(">>> L'application RoomEq reste utilisable (boucle WASAPI,");
                    Console.WriteLine(">>> lecteur + EQ, calibration au micro).");
                    return 0;
                }
                Console.Write(">>> Activer le mode test de Windows et installer le pilote ? (o/N) : ");
                string rep;
                try { rep = Console.ReadLine(); } catch { rep = ""; }
                if (rep == null || !rep.Trim().ToLowerInvariant().StartsWith("o"))
                {
                    Console.WriteLine();
                    Console.WriteLine(">>> Choix NON : rien n'a ete modifie (aucun certificat importe,");
                    Console.WriteLine(">>> aucun pilote ajoute, aucun mode change).");
                    Console.WriteLine(">>> Revenez quand vous voulez : --status (diagnostic),");
                    Console.WriteLine(">>> --mode-normal (si le mode test etait deja actif).");
                    return 0;
                }
            }

            // -- 1) extraction du payload embarque ------------------------------
            Step(1, 6, "Extraction des fichiers pilote embarques");
            try
            {
                workDir = ExtractPayload(out cerPath);
                Console.WriteLine("       -> " + workDir);
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
                Console.WriteLine("       [SAUT] aucun .cer embarque -> build officiellement signe");
                Console.WriteLine("       (ni certificat, ni mode test, ni redemarrage).");
            }
            else
            {
                try
                {
                    int imported = ImportCertToStores(cerPath);
                    Console.WriteLine("       -> importe dans Root + TrustedPublisher ("
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
                Console.WriteLine("       [SAUT] inutile pour un pilote signe Microsoft.");
            }
            else if (IsTestSigningOn())
            {
                Console.WriteLine("       -> deja actif (pas de redemarrage necessaire).");
            }
            else
            {
                // pre-diagnostic : Secure Boot bloque le mode test d'office
                bool? secureBoot = IsSecureBootEnabled();
                string err;
                if (EnableTestSigning(out err))
                {
                    rebootNeeded = true;
                    Console.WriteLine("       -> ACTIVE. Le noyau l'appliquera au prochain demarrage.");
                    if (secureBoot == true)
                    {
                        Console.WriteLine("       [!] ATTENTION : Secure Boot semble ACTIF - le mode test");
                        Console.WriteLine("           risque d'etre ignore au demarrage. Si les cartes");
                        Console.WriteLine("           RoomEq n'apparaissent pas apres le reboot, desactivez");
                        Console.WriteLine("           Secure Boot dans le BIOS/UEFI (INSTALLATION-PILOTE.md).");
                    }
                }
                else
                {
                    Console.WriteLine();
                    Console.WriteLine("[ERREUR] impossible d'activer le mode testsigning (bcdedit : " + err + ").");
                    if (secureBoot == true)
                    {
                        Console.WriteLine();
                        Console.WriteLine("         CAUSE CONFIRMEE : le Secure Boot est ACTIF. Windows refuse");
                        Console.WriteLine("         par conception de charger un pilote signe-test quand il l'est.");
                        Console.WriteLine("         Deux solutions :");
                        Console.WriteLine("           A. desactiver Secure Boot dans le BIOS/UEFI (redemarrage,");
                        Console.WriteLine("              touche Suppr/F2 selon la carte mere), puis relancer ce");
                        Console.WriteLine("              programme - voir INSTALLATION-PILOTE.md, cas 5 ;");
                        Console.WriteLine("           B. attendre le pilote signe Microsoft (phase A du plan de");
                        Console.WriteLine("              distribution, docs/SIGNATURE-ET-STORE.md).");
                    }
                    else
                    {
                        Console.WriteLine("         Cause la plus frequente : Secure Boot actif (voir ci-dessus).");
                        Console.WriteLine("         Journal complet ci-dessus. En cas de blocage, transmettre");
                        Console.WriteLine("         cette fenetre en copier-coller.");
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
                string oem = ParseDriverStoreName(output, InfName);
                Console.WriteLine("       -> paquet ajoute" + (oem != null ? " (" + oem + ")" : ""));
            }

            // -- 5) peripherique ROOT\ROOMEQVAD --------------------------------
            Step(5, 6, "Peripherique 'RoomEq Virtual Audio'");
            try
            {
                bool created = EnsureVirtualDevice(inf);
                Console.WriteLine("       -> " + (created ? "cree et pilote lie." : "deja present, pilote lie."));
            }
            catch (Exception ex)
            {
                if (rebootNeeded)
                {
                    Console.WriteLine("       [!] liaison differee au redemarrage (" + ex.Message + ")");
                    Console.WriteLine("           PnP reessaiera automatiquement au boot - pas bloquant.");
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
                Console.WriteLine("       -> " + dll);
                Console.WriteLine("          + enregistrement COM ASIO (HKLM).");
            }
            catch (Exception ex)
            {
                Console.WriteLine("       [!] ASIO non installe (" + ex.Message + ")");
                Console.WriteLine("           Le pilote WASAPI fonctionne sans ASIO ; reessayer plus tard");
                Console.WriteLine("           avec --install est sans risque (installation idempotente).");
            }

            // -- verdict ---------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("==============================================================");
            Console.WriteLine("RESULTAT : installation du pilote terminee.");
            Console.WriteLine("Apres redemarrage, les cartes 'RoomEq Input / RoomEq Output'");
            Console.WriteLine("figurent dans les parametres son de Windows.");
            if (rebootNeeded)
            {
                Console.WriteLine();
                Console.WriteLine("* REDEMARRAGE REQUIS * (le mode test vient d'etre active).");
                Console.WriteLine("Un filigrane 'Mode de test' apparaitra en bas a droite de");
                Console.WriteLine("l'ecran : c'est normal et attendu avec un pilote signe-test.");
                Console.WriteLine();
                Console.Write("Redemarrer maintenant ? (O/n) : ");
                string rep = Console.ReadLine();
                if (string.IsNullOrEmpty(rep) || rep.Trim().ToLowerInvariant().StartsWith("o"))
                {
                    Console.WriteLine("Redemarrage dans 5 secondes...");
                    Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5") { UseShellExecute = false });
                }
                else
                {
                    Console.WriteLine("Redemarrez manuellement avant d'utiliser la carte son.");
                }
            }
            Console.WriteLine();
            Console.Write("Appuyez sur Entree pour fermer...");
            try { Console.ReadLine(); } catch { }
            return 0;
        }

        // ==================================================== DESINSTALLATION

        private static int RunUninstall()
        {
            Console.WriteLine("Desinstallation de la carte son virtuelle RoomEq");
            Console.WriteLine("(best effort : chaque etape est tentee, jamais bloquante)");
            Console.WriteLine();

            Step(1, 5, "Peripherique ROOT\\ROOMEQVAD");
            try
            {
                bool removed = RemoveRootDevice();
                Console.WriteLine("       -> " + (removed ? "retire."
                        : "introuvable (rien a faire)."));
            }
            catch (Exception ex) { Console.WriteLine("       [!] " + ex.Message); }

            Step(2, 5, "Paquet du magasin de pilotes");
            try
            {
                string oem = ResolveStoreInfName();
                if (oem == null)
                {
                    Console.WriteLine("       -> introuvable dans le magasin (rien a faire).");
                }
                else
                {
                    string output;
                    int code = RunHidden("pnputil.exe",
                        "/delete-driver " + oem + " /uninstall /force", out output);
                    Console.WriteLine("       -> pnputil /delete-driver " + oem + " (code " + code + ")");
                }
            }
            catch (Exception ex) { Console.WriteLine("       [!] " + ex.Message); }

            Step(3, 5, "Enregistrement ASIO + DLL");
            try
            {
                UninstallAsio();
                Console.WriteLine("       -> cles registre + C:\\Program Files\\RoomEq retires.");
            }
            catch (Exception ex) { Console.WriteLine("       [!] " + ex.Message); }

            Step(4, 5, "Certificat de test");
            try
            {
                string cer = ExtractCertOnly();
                bool removedCert = RemoveCertFromStores(cer);
                Console.WriteLine("       -> " + (removedCert
                        ? "retire de Root + TrustedPublisher."
                        : "introuvable ou deja retire."));
            }
            catch (Exception ex) { Console.WriteLine("       [!] " + ex.Message); }

            Step(5, 5, "Fichiers temporaires");
            try
            {
                string dir = Path.Combine(Path.GetTempPath(), "RoomEqDriverSetup");
                if (Directory.Exists(dir)) Directory.Delete(dir, true);
                Console.WriteLine("       -> nettoyes.");
            }
            catch (Exception ex) { Console.WriteLine("       [!] " + ex.Message); }

            Console.WriteLine();
            Console.WriteLine("Desinstallation terminee.");

            // le mode test n'a plus d'interet sans le pilote signe-test :
            // proposer de le couper maintenant (retour au mode normal)
            bool testWasOn = IsTestSigningOn();
            if (testWasOn)
            {
                Console.WriteLine();
                Console.Write("Le mode test de Windows est actif. Le couper maintenant ? (O/n) : ");
                string repMode;
                try { repMode = Console.ReadLine(); } catch { repMode = ""; }
                if (repMode == null || repMode.Trim().ToLowerInvariant().StartsWith("o"))
                    return DisableTestSigningAndOfferReboot();
                Console.WriteLine(">>> mode test laisse actif. Pour le couper plus tard :");
                Console.WriteLine(">>> RoomEqDriverSetup.exe --mode-normal   (ou bcdedit /set testsigning off)");
            }
            else
            {
                Console.WriteLine("(Le mode test de Windows n'est pas actif : rien a couper.)");
            }
            Console.WriteLine();
            Console.Write("Appuyez sur Entree pour fermer...");
            try { Console.ReadLine(); } catch { }
            return 0;
        }

        // ==================================================== RETOUR AU MODE NORMAL

        /// <summary>
        /// --mode-normal : coupe le mode test de Windows (le filigrane « Mode de
        /// test » disparait apres redemarrage), puis propose le retrait complet
        /// du pilote signe-test devenu inutilisable en mode normal.
        /// </summary>
        private static int RunModeNormal()
        {
            Console.WriteLine("Retour au mode NORMAL de Windows");
            Console.WriteLine("================================");
            Console.WriteLine();

            if (!IsTestSigningOn())
            {
                Console.WriteLine("[SAUT] Windows n'est pas en mode test : rien a couper.");
                Console.WriteLine("       (Le filigrane 'Mode de test', s'il est visible, provient");
                Console.WriteLine("       d'une autre source ; aucun changement apporte ici.)");
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
                Console.WriteLine();
                Console.WriteLine("En mode normal, le pilote RoomEq signe-test ne peut PAS se");
                Console.WriteLine("charger : la carte virtuelle restera invisible tant que le");
                Console.WriteLine("pilote n'est pas officiellement signe Microsoft.");
                Console.Write("Retirer le pilote et le certificat maintenant ? (O/n) : ");
                string rep;
                try { rep = Console.ReadLine(); } catch { rep = ""; }
                if (rep == null || rep.Trim().ToLowerInvariant().StartsWith("o"))
                {
                    Console.WriteLine();
                    return RunUninstall();
                }
                Console.WriteLine(">>> pilote laisse en place (invisible mais inoffensif :");
                Console.WriteLine(">>> --uninstall le retirera quand vous voudrez).");
            }
            else
            {
                Console.WriteLine();
                Console.WriteLine("(Aucune trace du pilote RoomEq sur cette machine :");
                Console.WriteLine(" rien d'autre a nettoyer.)");
            }
            Console.WriteLine();
            Console.Write("Appuyez sur Entree pour fermer...");
            try { Console.ReadLine(); } catch { }
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
                    Console.WriteLine("[ERREUR] impossible de couper le mode test (bcdedit : "
                                      + OneLine(err, 160) + " / " + OneLine(err2, 120) + ")");
                    Console.WriteLine("         Cause la plus frequente : Secure Boot actif (le mode");
                    Console.WriteLine("         test n'a alors jamais pu etre mis en route).");
                    return 1;
                }
            }

            Console.WriteLine();
            Console.WriteLine(">>> MODE TEST DESACTIVE. Le filigrane 'Mode de test' disparaitra");
            Console.WriteLine(">>> definitivement apres le PROCHAIN REDEMARRAGE.");
            Console.WriteLine();
            Console.Write("Redemarrer maintenant ? (O/n) : ");
            string rep;
            try { rep = Console.ReadLine(); } catch { rep = ""; }
            if (rep == null || rep.Trim().ToLowerInvariant().StartsWith("o"))
            {
                Console.WriteLine("Redemarrage dans 5 secondes...");
                Process.Start(new ProcessStartInfo("shutdown.exe", "/r /t 5") { UseShellExecute = false });
            }
            else
            {
                Console.WriteLine("Redemarrez quand vous voulez pour appliquer le retour au normal.");
            }
            return 0;
        }

        // ==================================================== ETAT

        private static int RunStatus()
        {
            bool? sb = IsSecureBootEnabled();
            bool testMode = IsTestSigningOn();
            Console.WriteLine("Etat de la machine (rien n'est modifie) :");
            Console.WriteLine();
            Console.WriteLine("  Secure Boot        : " + NullSafe(sb, "Actif", "Inactif"));
            Console.WriteLine("  Mode testsigning   : " + (testMode ? "Actif" : "Inactif"));
            Console.WriteLine("  Build pilote       : " + (HasEmbeddedCert()
                ? "signe-test (mode test requis pour charger)"
                : "signature officielle (aucun mode test requis)"));

            string oem = null;
            try { oem = ResolveStoreInfName(); } catch { }
            Console.WriteLine("  Pilote magasin     : " + (oem != null ? "Present (" + oem + ")" : "Absent"));

            bool dev = false;
            uint problem = 0, status = 0;
            bool started = false, hasProblem = false;
            try { dev = ProbeRootDevice(ref status, ref problem); } catch { }
            if (dev)
            {
                started = (status & 0x00000008) != 0;        // DN_STARTED
                hasProblem = (status & 0x00000400) != 0;     // DN_HAS_PROBLEM
                Console.WriteLine("  Peripherique       : Present (" + DeviceStateText(started, hasProblem, problem) + ")");
            }
            else
            {
                Console.WriteLine("  Peripherique       : Absent");
            }

            string svc = ServiceState("roomeqvad");
            Console.WriteLine("  Service roomeqvad  : " + svc);

            string asio = AsioDllRegisteredPath();
            Console.WriteLine("  ASIO (registre)    : " + (asio != null ? "Enregistre -> " + asio : "Non enregistre"));
            Console.WriteLine();

            // ---------------- VERDICT : pourquoi la carte n'apparait pas -------
            Console.WriteLine("--------------------------------------------------------------");
            Console.WriteLine("VERDICT");
            Console.WriteLine("--------------------------------------------------------------");
            if (!dev && oem == null)
            {
                Console.WriteLine("  Le pilote RoomEq n'est pas installe sur cette machine.");
                Console.WriteLine("  -> Lancez : RoomEqDriverSetup.exe --install");
                Console.WriteLine("     (le mode test sera PROPOSE explicitement ; repondre non");
                Console.WriteLine("      n'installe rien et laisse Windows en mode normal)");
            }
            else if (!dev)
            {
                Console.WriteLine("  Le paquet pilote est dans le magasin mais le PERIPHERIQUE");
                Console.WriteLine("  n'existe pas : c'est lui qui cree les entrees son.");
                Console.WriteLine("  -> Lancez : RoomEqDriverSetup.exe --install");
                Console.WriteLine("     (l'etape 5 cree le peripherique ROOT\\ROOMEQVAD ; une");
                Console.WriteLine("      installation partielle par pnputil seul ne suffit pas)");
            }
            else if (!testMode && HasEmbeddedCert())
            {
                Console.WriteLine("  CAUSE TROUVEE : Windows n'est PAS en mode test, il refuse");
                Console.WriteLine("  donc de charger ce pilote signe-test (code " + problem + ").");
                Console.WriteLine("  TROIS VOIES :");
                Console.WriteLine("   A. Accepter le mode test : --install (question explicite),");
                Console.WriteLine("      filigrane 'Mode de test' sur le bureau ensuite ;");
                Console.WriteLine("   B. Rester en mode normal SANS carte virtuelle : RoomEq reste");
                Console.WriteLine("      utilisable (lecteur + EQ, calibration, boucle WASAPI) ;");
                Console.WriteLine("   C. Pilote signe Microsoft (definitif, sans mode test) :");
                Console.WriteLine("      voir docs/SIGNATURE-ET-STORE.md (phase A).");
            }
            else if (testMode && sb == true)
            {
                Console.WriteLine("  CAUSE PROBABLE : le mode test est actif mais le Secure Boot");
                Console.WriteLine("  l'IGNORE au demarrage (conception de Windows). Le pilote ne");
                Console.WriteLine("  peut donc pas se charger.");
                Console.WriteLine("  -> Desactiver Secure Boot dans le BIOS/UEFI, redemarrer ;");
                Console.WriteLine("     ou revenir au mode normal : --mode-normal.");
            }
            else if (hasProblem && !started)
            {
                Console.WriteLine("  Le peripherique existe mais son pilote ne demarre pas");
                Console.WriteLine("  (code " + problem + " : " + ProblemText(problem) + ").");
                Console.WriteLine("  -> Relancez --install (liaison du pilote retentee) puis");
                Console.WriteLine("     redemarrer ; si le code persiste, transmettre ce rapport.");
            }
            else
            {
                Console.WriteLine("  Tout semble correct cote pilote. Si la carte n'apparait");
                Console.WriteLine("  toujours pas dans les peripheriques Windows :");
                Console.WriteLine("   1. Parametres > Systeme > Son > Panneau son (mmsys.cpl) ;");
                Console.WriteLine("   2. clic droit dans la liste > 'Afficher les peripheriques");
                Console.WriteLine("      desactives' : 'RoomEq Input / RoomEq Output' doivent y");
                Console.WriteLine("      figurer (clic droit > Activer le cas echeant) ;");
                Console.WriteLine("   3. sinon, redemarrer Windows puis relancer --status.");
            }
            Console.WriteLine("--------------------------------------------------------------");
            Console.WriteLine();
            Console.Write("Appuyez sur Entree pour fermer...");
            try { Console.ReadLine(); } catch { }
            return 0;
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
                int code = RunHidden("sc.exe", "query " + service, out output);
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
                classGuid = fromInf;

            IntPtr set = NativeMethods.SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
            if (set == IntPtr.Zero || set == new IntPtr(-1))
                throw new Exception("SetupDiCreateDeviceInfoList a echoue (erreur 0x"
                                    + Marshal.GetLastWin32Error().ToString("X") + ")");
            try
            {
                var dev = new NativeMethods.SP_DEVINFO_DATA();
                dev.cbSize = Marshal.SizeOf(typeof(NativeMethods.SP_DEVINFO_DATA));

                if (!NativeMethods.SetupDiCreateDeviceInfoW(
                        set, HwId, ref classGuid, null, IntPtr.Zero,
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

                if (key.EndsWith("published name", StringComparison.OrdinalIgnoreCase)
                    || key.EndsWith("nom du fichier publié", StringComparison.OrdinalIgnoreCase)
                    || key.EndsWith("nom du fichier publie", StringComparison.OrdinalIgnoreCase))
                {
                    published = value;
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

        // ==================================================== outils

        private static bool IsElevated()
        {
            using (var id = System.Security.Principal.WindowsIdentity.GetCurrent())
            {
                return new System.Security.Principal.WindowsPrincipal(id)
                    .IsInRole(System.Security.Principal.WindowsBuiltInRole.Administrator);
            }
        }

        private static void Step(int n, int total, string title)
        {
            Console.WriteLine();
            Console.WriteLine("[" + n + "/" + total + "] " + title);
        }

        private static int FailStep(string message)
        {
            Console.WriteLine();
            Console.WriteLine("[ERREUR] " + message);
            Console.WriteLine();
            Console.WriteLine("Rien n'a ete casse : les etapes precedentes sont inoffensives");
            Console.WriteLine("(certificat uniquement, ou paquet ajoute sans liaison).");
            Console.WriteLine("Transmettre cette fenetre (copier-coller) pour un diagnostic precis.");
            Console.WriteLine();
            Console.Write("Appuyez sur Entree pour fermer...");
            try { Console.ReadLine(); } catch { }
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

        private static int RunHidden(string exe, string args, out string output)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
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
                if (p.ExitCode != 0)
                {
                    Console.WriteLine("       (sortie " + exe + " : " + OneLine(output, 300)
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
