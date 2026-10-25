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
//    RoomEqDriverSetup.exe              installation (defaut)
//    RoomEqDriverSetup.exe --status     etat de la machine (rien ne change)
//    RoomEqDriverSetup.exe --uninstall  retrait complet (best effort)
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

            string mode = args.Length > 0 ? args[0].TrimStart('-').ToLowerInvariant() : "install";
            if (mode == "u") mode = "uninstall";
            if (mode == "s") mode = "status";

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
                case "install": return RunInstall();
                default:
                    Console.WriteLine("Argument inconnu : " + args[0]);
                    Console.WriteLine("Usage : RoomEqDriverSetup.exe [--install | --status | --uninstall]");
                    return 1;
            }
        }

        // ==================================================== INSTALLATION

        private static int RunInstall()
        {
            string workDir;
            string cerPath = null;
            bool rebootNeeded = false;

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
            Console.WriteLine("Desinstallation terminee. Si le mode test etait la seule raison");
            Console.WriteLine("d'etre du redemarrage de Windows, vous pouvez le couper avec :");
            Console.WriteLine("    bcdedit /set testsigning off   (invite administrateur)");
            Console.WriteLine();
            Console.Write("Appuyez sur Entree pour fermer...");
            try { Console.ReadLine(); } catch { }
            return 0;
        }

        // ==================================================== ETAT

        private static int RunStatus()
        {
            bool? sb = IsSecureBootEnabled();
            Console.WriteLine("Etat de la machine (rien n'est modifie) :");
            Console.WriteLine();
            Console.WriteLine("  Secure Boot        : " + NullSafe(sb, "Actif", "Inactif"));
            Console.WriteLine("  Mode testsigning   : " + (IsTestSigningOn() ? "Actif" : "Inactif"));

            string oem = null;
            try { oem = ResolveStoreInfName(); } catch { }
            Console.WriteLine("  Pilote magasin     : " + (oem != null ? "Present (" + oem + ")" : "Absent"));

            bool dev = false;
            try { dev = RootDeviceExists(); } catch { }
            Console.WriteLine("  Peripherique       : " + (dev ? "Present" : "Absent"));

            string asio = AsioDllRegisteredPath();
            Console.WriteLine("  ASIO (registre)    : " + (asio != null ? "Enregistre -> " + asio : "Non enregistre"));
            Console.WriteLine();
            Console.Write("Appuyez sur Entree pour fermer...");
            try { Console.ReadLine(); } catch { }
            return 0;
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
    }
}
