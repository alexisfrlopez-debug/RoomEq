// ============================================================================
//  RoomEqAppSetup - version INSTALLABLE de l'application RoomEq (sans MSI).
//
//  Complement de la distribution « executables » (demande utilisateur
//  2026-10) : en plus de l'application PORTABLE (RoomEq-<version>-win-x64.exe,
//  aucun fichier pose), cet installateur offre une vraie installation par
//  utilisateur avec lancement automatique au demarrage de Windows.
//
//  Usage :
//    RoomEqAppSetup.exe                  installation (defaut)
//    RoomEqAppSetup.exe --status         etat de l'installation (rien ne change)
//    RoomEqAppSetup.exe --uninstall      desinstallation complete de l'application
//    RoomEqAppSetup.exe --help           aide
//  Options d'installation (combinables) :
//    --no-autostart                      ne pas proposer le demarrage automatique
//    --no-desktop                        ne pas creer le raccourci Bureau
//
//  Aucun droit administrateur n'est requis (app.manifest asInvoker) :
//    - fichiers   -> %LOCALAPPDATA%\Programs\RoomEq\RoomEq.exe
//    - raccourcis -> Menu Demarrer (+ Bureau au choix) de l'utilisateur
//    - demarrage  -> HKCU\...\CurrentVersion\Run
//    - entree « Applications & fonctionnalites » -> HKCU\...\Uninstall\RoomEq
//  L'application PORTABLE embarquee (charge utile) est extraite telle quelle :
//  ses donnees (reglages, mesures, presets) restent dans %APPDATA%\RoomEq,
//  communes aux deux versions et CONSERVEES a la desinstallation.
//
//  Le pilote (carte son virtuelle + ASIO) reste installe separement par
//  RoomEqDriverSetup.exe : il touche des zones systeme et reclame l'UAC.
// ============================================================================

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Text;
using Microsoft.Win32;

namespace RoomEqAppSetup
{
    internal static class Program
    {
        // ---- noms / chemins -------------------------------------------------
        private const string PayloadResName = "RoomEq-portable.exe";
        private const string AppExeName     = "RoomEq.exe";
        private const string SetupCopyName  = "RoomEqAppSetup.exe";
        private const string ShortcutName   = "RoomEq.lnk";
        private const string RunKeyPath     = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName   = "RoomEq";
        private const string UninstKeyPath  = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\RoomEq";

        private static readonly string TargetDir =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "Programs", "RoomEq");

        private static int Main(string[] args)
        {
            try { Console.OutputEncoding = Encoding.UTF8; } catch { }
            try { Console.Title = "RoomEqAppSetup"; } catch { }

            string mode = "install";
            bool noAutostart = false, noDesktop = false;
            foreach (string raw in args)
            {
                string a = raw.Trim().ToLowerInvariant();
                if (a == "--install" || a == "-install" || a == "install") mode = "install";
                else if (a == "--status" || a == "-status" || a == "s" || a == "status") mode = "status";
                else if (a == "--uninstall" || a == "-uninstall" || a == "u" || a == "uninstall") mode = "uninstall";
                else if (a == "--help" || a == "-help" || a == "-h" || a == "/?") mode = "help";
                else if (a == "--no-autostart") noAutostart = true;
                else if (a == "--no-desktop") noDesktop = true;
                else
                {
                    Console.WriteLine("Argument inconnu : " + raw);
                    Console.WriteLine("Usage : RoomEqAppSetup.exe [--install | --status | --uninstall]");
                    Console.WriteLine("        options install : --no-autostart  --no-desktop");
                    return 1;
                }
            }

            Console.WriteLine("RoomEqAppSetup - version installee de l'application RoomEq");
            Console.WriteLine("============================================================");
            Console.WriteLine();
            Console.WriteLine("Installation pour l'utilisateur '" + Environment.UserName
                              + "' : aucun droit administrateur requis.");
            Console.WriteLine();

            switch (mode)
            {
                case "help":     return RunHelp();
                case "status":   return RunStatus();
                case "uninstall": return RunUninstall();
                default:         return RunInstall(noAutostart, noDesktop);
            }
        }

        // ==================================================== AIDE

        private static int RunHelp()
        {
            Console.WriteLine("Installe l'application RoomEq pour l'utilisateur courant :");
            Console.WriteLine();
            Console.WriteLine("  - dossier      : " + TargetDir);
            Console.WriteLine("  - raccourcis   : Menu Demarrer (+ Bureau au choix)");
            Console.WriteLine("  - demarrage    : lance RoomEq a l'ouverture de session Windows");
            Console.WriteLine("                   (visible/desactivable dans le Gestionnaire des");
            Console.WriteLine("                    taches, onglet 'Applications au demarrage')");
            Console.WriteLine("  - desinstallation : Parametres > Applications > RoomEq");
            Console.WriteLine();
            Console.WriteLine("Commandes :");
            Console.WriteLine("  RoomEqAppSetup.exe               installer (ou reinstaller/mettre a jour)");
            Console.WriteLine("  RoomEqAppSetup.exe --status      etat de l'installation");
            Console.WriteLine("  RoomEqAppSetup.exe --uninstall   desinstaller l'application");
            Console.WriteLine();
            Console.WriteLine("Options d'installation : --no-autostart   --no-desktop");
            Console.WriteLine();
            Console.WriteLine("L'application PORTABLE (aucun fichier pose) reste disponible");
            Console.WriteLine("separement ; les donnees (%APPDATA%\\RoomEq) sont partagees et");
            Console.WriteLine("conservees lors de la desinstallation.");
            Console.WriteLine();
            Console.Write("Appuyez sur Entree pour fermer...");
            try { Console.ReadLine(); } catch { }
            return 0;
        }

        // ==================================================== INSTALLATION

        private static int RunInstall(bool noAutostart, bool noDesktop)
        {
            // -- 1) dossier cible ------------------------------------------------
            Step(1, 6, "Dossier d'installation");
            try
            {
                Directory.CreateDirectory(TargetDir);
            }
            catch (Exception ex)
            {
                return FailStep("creation du dossier impossible (" + ex.Message + ")");
            }
            bool update = File.Exists(Path.Combine(TargetDir, AppExeName));
            Console.WriteLine("       -> " + TargetDir);
            if (update) Console.WriteLine("          (installation existante detectee -> mise a jour)");

            // -- 2) extraction de l'application embarquee -----------------------
            Step(2, 6, "Extraction de l'application (" + PayloadResName + ")");
            string appExe = Path.Combine(TargetDir, AppExeName);
            try
            {
                long size = ExtractPayloadTo(appExe);
                string ver = SafeFileVersion(appExe);
                Console.WriteLine("       -> " + AppExeName + " (" + FormatSize(size) + ")"
                                  + (ver != null ? ", version " + ver : ""));
            }
            catch (IOException ex)
            {
                Console.WriteLine();
                Console.WriteLine("[ERREUR] ecriture impossible dans le dossier (" + ex.Message + ").");
                if (Process.GetProcessesByName("RoomEq").Length > 0)
                {
                    Console.WriteLine("         RoomEq est en cours d'execution : fermez-le puis");
                    Console.WriteLine("         relancez cet installateur (vos donnees sont intactes).");
                }
                return 1;
            }
            catch (Exception ex)
            {
                return FailStep("extraction impossible (" + ex.Message + ")");
            }

            // -- 3) copie du desinstalleur --------------------------------------
            Step(3, 6, "Desinstalleur (Applications & fonctionnalites)");
            try
            {
                string here = Assembly.GetExecutingAssembly().Location;
                string copy = Path.Combine(TargetDir, SetupCopyName);
                if (!string.Equals(here, copy, StringComparison.OrdinalIgnoreCase))
                {
                    string tmp = copy + ".new";
                    if (File.Exists(tmp)) File.Delete(tmp);
                    File.Copy(here, tmp, true);
                    if (File.Exists(copy)) File.Delete(copy);
                    File.Move(tmp, copy);
                }
                Console.WriteLine("       -> " + SetupCopyName + " pose dans le dossier d'installation");
            }
            catch (Exception ex)
            {
                return FailStep("copie du desinstalleur impossible (" + ex.Message + ")");
            }

            // -- 4) raccourcis ---------------------------------------------------
            Step(4, 6, "Raccourcis (Menu Demarrer"
                       + (noDesktop ? "" : " + Bureau") + ")");
            string startMenuLnk = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu),
                "Programs", ShortcutName);
            try
            {
                CreateShortcut(startMenuLnk, appExe, "RoomEq - correction acoustique de la piece");
                Console.WriteLine("       -> Menu Demarrer > RoomEq");
            }
            catch (Exception ex)
            {
                Console.WriteLine("       [!] raccourci Menu Demarrer non cree (" + ex.Message + ")");
            }
            if (noDesktop)
            {
                Console.WriteLine("       [SAUT] --no-desktop");
                TryDeleteFile(Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName));
            }
            else
            {
                string desktopLnk = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);
                if (Ask("Creer aussi un raccourci sur le Bureau ?", true))
                {
                    try
                    {
                        CreateShortcut(desktopLnk, appExe, "RoomEq - correction acoustique de la piece");
                        Console.WriteLine("       -> Bureau > RoomEq");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine("       [!] raccourci Bureau non cree (" + ex.Message + ")");
                    }
                }
                else
                {
                    Console.WriteLine("       -> aucun raccourci Bureau demande.");
                    TryDeleteFile(desktopLnk);
                }
            }

            // -- 5) demarrage automatique de Windows ------------------------------
            Step(5, 6, "Demarrage automatique de Windows");
            bool autoStart;
            if (noAutostart)
            {
                autoStart = false;
                Console.WriteLine("       [SAUT] --no-autostart");
            }
            else
            {
                autoStart = Ask("Lancer RoomEq automatiquement au demarrage de Windows ?", true);
            }
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (autoStart) k.SetValue(RunValueName, "\"" + appExe + "\"");
                    else if (k.GetValue(RunValueName) != null) k.DeleteValue(RunValueName);
                }
                Console.WriteLine("       -> " + (autoStart
                    ? "ACTIF : RoomEq s'ouvrira a chaque ouverture de session."
                    : "inactif (relancez l'installateur pour l'activer plus tard)."));
                if (autoStart)
                {
                    Console.WriteLine("          Desactivable a tout moment : Gestionnaire des taches >");
                    Console.WriteLine("          Applications au demarrage > RoomEq.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("       [!] demarrage automatique non configure (" + ex.Message + ")");
            }

            // -- 6) entree de desinstallation -------------------------------------
            Step(6, 6, "Entree 'Applications & fonctionnalites'");
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(UninstKeyPath))
                {
                    k.SetValue("DisplayName",     "RoomEq");
                    k.SetValue("DisplayVersion",  VersionString());
                    k.SetValue("Publisher",       "RoomEq");
                    k.SetValue("DisplayIcon",     appExe + ",0");
                    k.SetValue("InstallLocation", TargetDir);
                    k.SetValue("InstallDate",     DateTime.Now.ToString("yyyyMMdd"));
                    k.SetValue("EstimatedSize",   DirSizeKb(TargetDir));
                    k.SetValue("NoModify",        1, RegistryValueKind.DWord);
                    k.SetValue("NoRepair",        1, RegistryValueKind.DWord);
                    k.SetValue("UninstallString", "\"" + Path.Combine(TargetDir, SetupCopyName)
                                                  + "\" --uninstall");
                }
                Console.WriteLine("       -> Parametres > Applications > RoomEq > Desinstaller");
            }
            catch (Exception ex)
            {
                Console.WriteLine("       [!] entree de desinstallation non creee (" + ex.Message + ")");
                Console.WriteLine("           La desinstallation reste possible via --uninstall.");
            }

            // -- verdict -----------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("==============================================================");
            Console.WriteLine("RESULTAT : RoomEq " + VersionString() + " installe pour '"
                              + Environment.UserName + "'.");
            Console.WriteLine("  Demarrer        : Menu Demarrer > RoomEq");
            Console.WriteLine("  Demarrage auto  : " + (autoStart ? "ACTIVE (a l'ouverture de session)"
                                                                 : "desactive"));
            Console.WriteLine("  Desinstallation : Parametres > Applications > RoomEq");
            Console.WriteLine();
            Console.WriteLine("Le pilote (carte son virtuelle + ASIO) s'installe separement");
            Console.WriteLine("avec RoomEqDriverSetup.exe si ce n'est pas deja fait.");
            Console.WriteLine();
            Console.Write("Appuyez sur Entree pour fermer...");
            try { Console.ReadLine(); } catch { }
            return 0;
        }

        // ==================================================== DESINSTALLATION

        private static int RunUninstall()
        {
            Console.WriteLine("Desinstallation de l'application RoomEq (pilote non concerne)");
            Console.WriteLine();
            if (!Directory.Exists(TargetDir) && RegistryGetValue(RunKeyPath, RunValueName) == null)
            {
                Console.WriteLine("Aucune installation detectee : rien a faire.");
                Console.WriteLine();
                Console.Write("Appuyez sur Entree pour fermer...");
                try { Console.ReadLine(); } catch { }
                return 0;
            }
            if (!Ask("Desinstaller RoomEq (raccourcis, demarrage auto, fichiers) ?", false))
            {
                Console.WriteLine("Annule : rien n'a ete modifie.");
                Console.WriteLine();
                Console.Write("Appuyez sur Entree pour fermer...");
                try { Console.ReadLine(); } catch { }
                return 0;
            }
            Console.WriteLine();

            // -- 1) demarrage automatique ---------------------------------------
            Step(1, 4, "Demarrage automatique de Windows");
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKeyPath, true))
                {
                    if (k != null && k.GetValue(RunValueName) != null)
                    {
                        k.DeleteValue(RunValueName);
                        Console.WriteLine("       -> retire.");
                    }
                    else Console.WriteLine("       -> absent (rien a faire).");
                }
            }
            catch (Exception ex) { Console.WriteLine("       [!] " + ex.Message); }

            // -- 2) entree Applications & fonctionnalites ------------------------
            Step(2, 4, "Entree 'Applications & fonctionnalites'");
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstKeyPath))
                {
                    if (k != null)
                    {
                        Registry.CurrentUser.DeleteSubKey(UninstKeyPath);
                        Console.WriteLine("       -> retiree.");
                    }
                    else Console.WriteLine("       -> absente (rien a faire).");
                }
            }
            catch (Exception ex) { Console.WriteLine("       [!] " + ex.Message); }

            // -- 3) raccourcis -----------------------------------------------------
            Step(3, 4, "Raccourcis");
            TryDeleteFile(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", ShortcutName));
            TryDeleteFile(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName));

            // -- 4) fichiers ---------------------------------------------------------
            Step(4, 4, "Fichiers de l'application");
            if (Directory.Exists(TargetDir))
            {
                if (Process.GetProcessesByName("RoomEq").Length > 0)
                {
                    Console.WriteLine("       [!] RoomEq est en cours d'execution : fermez-le pour que");
                    Console.WriteLine("           la suppression des fichiers reussisse.");
                }
                // Le dossier contient l'exemplaire du desinstalleur en cours
                // d'execution : la suppression est differee de quelques secondes
                // apres la fermeture de cette fenetre (2 tentatives).
                string cmd = "/c ping -n 5 127.0.0.1 >nul & rmdir /s /q \"" + TargetDir + "\""
                           + " & ping -n 5 127.0.0.1 >nul & rmdir /s /q \"" + TargetDir + "\"";
                try
                {
                    Process.Start(new ProcessStartInfo("cmd.exe", cmd)
                    {
                        UseShellExecute = false,
                        CreateNoWindow = true
                    });
                    Console.WriteLine("       -> suppression de \"" + TargetDir + "\"");
                    Console.WriteLine("          programmee (quelques secondes apres la fermeture).");
                }
                catch (Exception ex) { Console.WriteLine("       [!] " + ex.Message); }
            }
            else
            {
                Console.WriteLine("       -> dossier absent (rien a faire).");
            }

            // -- verdict -----------------------------------------------------------
            Console.WriteLine();
            Console.WriteLine("==============================================================");
            Console.WriteLine("RESULTAT : application RoomEq desinstallee.");
            Console.WriteLine("Vos reglages, mesures et presets sont CONSERVES dans :");
            Console.WriteLine("  " + Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "RoomEq"));
            Console.WriteLine("Supprimez ce dossier manuellement pour un nettoyage complet.");
            Console.WriteLine("Le pilote (carte son virtuelle) n'a pas ete touche : utilisez");
            Console.WriteLine("RoomEqDriverSetup.exe --uninstall si vous voulez aussi le retirer.");
            Console.WriteLine();
            Console.Write("Appuyez sur Entree pour fermer...");
            try { Console.ReadLine(); } catch { }
            return 0;
        }

        // ==================================================== STATUS

        private static int RunStatus()
        {
            string appExe = Path.Combine(TargetDir, AppExeName);
            bool payload = Array.IndexOf(
                Assembly.GetExecutingAssembly().GetManifestResourceNames(), PayloadResName) >= 0;

            Console.WriteLine("Etat de l'installation (application, utilisateur '"
                              + Environment.UserName + "') :");
            Console.WriteLine();
            Console.WriteLine("  Version de l'installateur : " + VersionString()
                              + (payload ? " (application embarquee)"
                                         : " (AVERTISSEMENT : aucune application embarquee,"
                                           + " build incomplet)"));
            Console.WriteLine("  Dossier                   : " + TargetDir
                              + (Directory.Exists(TargetDir) ? "" : "  [absent]"));
            if (File.Exists(appExe))
            {
                long kb = DirSizeKb(TargetDir);
                string ver = SafeFileVersion(appExe);
                Console.WriteLine("  Application installee     : " + AppExeName
                                  + (ver != null ? " (version " + ver + ")" : "")
                                  + " ~" + kb + " Ko");
            }
            else
            {
                Console.WriteLine("  Application installee     : [absente]");
            }

            object run = RegistryGetValue(RunKeyPath, RunValueName);
            Console.WriteLine("  Demarrage automatique     : "
                              + (run != null ? "ACTIF (" + run + ")" : "inactif"));

            string startMenuLnk = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", ShortcutName);
            string desktopLnk = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), ShortcutName);
            Console.WriteLine("  Raccourci Menu Demarrer   : " + (File.Exists(startMenuLnk) ? "present" : "absent"));
            Console.WriteLine("  Raccourci Bureau          : " + (File.Exists(desktopLnk) ? "present" : "absent"));

            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(UninstKeyPath))
            {
                Console.WriteLine("  Entree de desinstallation : " + (k != null ? "presente" : "absente"));
            }

            Console.WriteLine();
            Console.WriteLine("Etat du pilote (carte son virtuelle) : voir RoomEqDriverSetup.exe --status");
            Console.WriteLine();
            Console.Write("Appuyez sur Entree pour fermer...");
            try { Console.ReadLine(); } catch { }
            return 0;
        }

        // ==================================================== OUTILS

        private static void Step(int n, int total, string label)
        {
            Console.WriteLine();
            Console.WriteLine("-- " + n + "/" + total + " : " + label);
        }

        private static int FailStep(string message)
        {
            Console.WriteLine();
            Console.WriteLine("[ERREUR] " + message);
            Console.WriteLine("         Installation interrompue ; l'installation existante");
            Console.WriteLine("         (le cas echeant) n'a pas ete endommagee.");
            return 1;
        }

        private static bool Ask(string question, bool defaultYes)
        {
            Console.Write(question + " (" + (defaultYes ? "O/n" : "o/N") + ") : ");
            string r;
            try { r = Console.ReadLine(); }
            catch { return defaultYes; }
            if (string.IsNullOrEmpty(r)) return defaultYes;
            r = r.Trim().ToLowerInvariant();
            if (r == "o" || r == "oui" || r == "y" || r == "yes") return true;
            if (r == "n" || r == "non" || r == "no") return false;
            return defaultYes;
        }

        /// <summary>Extrait la ressource embarquee vers target (temporaire puis
        /// remplacement atomique). Retourne la taille ecrite.</summary>
        private static long ExtractPayloadTo(string target)
        {
            using (Stream src = Assembly.GetExecutingAssembly()
                                        .GetManifestResourceStream(PayloadResName))
            {
                if (src == null)
                {
                    throw new InvalidOperationException(
                        "cet executable ne contient pas l'application embarquee ("
                        + PayloadResName + "). Build incomplet : utilisez installer\\build_exes.ps1.");
                }
                string tmp = target + ".new";
                if (File.Exists(tmp)) File.Delete(tmp);
                using (FileStream fs = new FileStream(tmp, FileMode.Create, FileAccess.Write,
                                                      FileShare.None, 1 << 20))
                {
                    byte[] buf = new byte[1 << 20];
                    int read; long total = 0;
                    while ((read = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        fs.Write(buf, 0, read);
                        total += read;
                    }
                }
                if (File.Exists(target)) File.Delete(target);   // IOException si RoomEq tourne
                File.Move(tmp, target);
                return new FileInfo(target).Length;
            }
        }

        private static void CreateShortcut(string lnkPath, string targetExe, string description)
        {
            // WScript.Shell en liaison tardive : aucun paquet COM a referencer.
            Type shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null)
                throw new InvalidOperationException("WScript.Shell indisponible");
            object shell = Activator.CreateInstance(shellType);
            object sc = shellType.InvokeMember("CreateShortcut",
                BindingFlags.InvokeMethod, null, shell, new object[] { lnkPath });
            Type t = sc.GetType();
            t.InvokeMember("TargetPath",        BindingFlags.SetProperty, null, sc, new object[] { targetExe });
            t.InvokeMember("WorkingDirectory",  BindingFlags.SetProperty, null, sc,
                new object[] { Path.GetDirectoryName(targetExe) });
            t.InvokeMember("Description",       BindingFlags.SetProperty, null, sc, new object[] { description });
            t.InvokeMember("IconLocation",      BindingFlags.SetProperty, null, sc,
                new object[] { targetExe + ",0" });
            t.InvokeMember("Save",              BindingFlags.InvokeMethod, null, sc, null);
        }

        private static object RegistryGetValue(string keyPath, string valueName)
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(keyPath))
                    return k == null ? null : k.GetValue(valueName);
            }
            catch { return null; }
        }

        private static void TryDeleteFile(string path)
        {
            try { if (File.Exists(path)) { File.Delete(path); Console.WriteLine("       -> " + path + " supprime"); } }
            catch (Exception ex) { Console.WriteLine("       [!] " + path + " : " + ex.Message); }
        }

        private static int DirSizeKb(string dir)
        {
            long bytes = 0;
            try
            {
                foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
                {
                    try { bytes += new FileInfo(f).Length; } catch { }
                }
            }
            catch { }
            return (int)(bytes / 1024);
        }

        private static string SafeFileVersion(string exe)
        {
            try { return FileVersionInfo.GetVersionInfo(exe).FileVersion; }
            catch { return null; }
        }

        private static string VersionString()
        {
            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            return v == null ? "?" : (v.Major + "." + v.Minor + "." + v.Build);
        }

        private static string FormatSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return string.Format("{0:0.#} Go", bytes / 1024.0 / 1024 / 1024);
            if (bytes >= 1024L * 1024)        return string.Format("{0:0.#} Mo", bytes / 1024.0 / 1024);
            return string.Format("{0:0} Ko", bytes / 1024.0);
        }
    }
}
