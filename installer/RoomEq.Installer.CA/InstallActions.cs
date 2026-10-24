using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using WixToolset.Dtf.WindowsInstaller;

namespace RoomEq.Installer.CA
{
    /// <summary>
    /// Actions personnalisées MSI du paquet RoomEq — installation
    /// « tout-en-un, un clic » (aucune étape manuelle après le double-clic).
    ///
    /// InstallRoomEqDriver (différée, SYSTEM) :
    ///   0. Si un certificat de test (.cer) accompagne le pilote (build
    ///      signé-test de la CI), sinon build « officiellement signé » :
    ///      a. import du certificat dans LocalMachine\Root et
    ///         \TrustedPublisher (le paquet PnP est alors accepté sans
    ///         invite, même en contexte SYSTEM silencieux) ;
    ///      b. activation du mode « testsigning » de Windows (bcdedit) —
    ///         échec propre avec message clair si le Secure Boot
    ///         l'interdit ; drapeau « redémarrage requis » si le mode vient
    ///         d'être activé (le noyau ne l'applique qu'au prochain boot) ;
    ///   1. pnputil /add-driver roomeqvad.inf /install (magasin de pilotes) ;
    ///   2. création du périphérique root-énuméré ROOT\ROOMEQVAD s'il est
    ///      absent (recette devcon : SetupDiCreateDeviceInfo + matériel +
    ///      DIF_REGISTERDEVICE), puis UpdateDriverForPlugAndPlayDevices pour
    ///      lier le pilote et démarrer la carte.
    ///   → cartes « RoomEq Input / RoomEq Output » opérationnelles
    ///     (dès la fin, ou après le redémarrage proposé).
    ///
    /// Un build sans .cer dans le paquet pilote (signature Microsoft) saute
    /// 0a/0b : ni certificat, ni mode test, ni redémarrage.
    ///
    /// RollbackRoomEqDriver (rollback, best effort) : retire périphérique,
    /// paquet de pilotes et certificat si l'installation échoue en cours.
    /// UninstallRoomEqDriver (différée) : idem, jamais bloquante.
    /// CheckRoomEqReboot (immédiate, après le script) : lit le drapeau posé
    ///   par l'installation → propriété ROOMEQ_DRIVER_REBOOT consommée par
    ///   <ScheduleReboot> (invite « Redémarrer maintenant ? »).
    /// </summary>
    public class CustomActions
    {
        private const string InfName = "roomeqvad.inf";
        private const string HwId = "ROOT\\ROOMEQVAD";
        private const string CertName = "RoomEq-TestSigning.cer";
        private const string CertSubject = "RoomEq Test Signing";
        private const string RebootFlagName = "roomeq-reboot.flag";

        // ===================================================== INSTALLATION

        [CustomAction]
        public static ActionResult InstallRoomEqDriver(Session session)
        {
            string installDir = session.CustomActionData["INSTALLDIR"];
            string driverDir = DriverDirOf(installDir);
            string inf = Path.Combine(driverDir, InfName);
            session.Log("RoomEq CA : installation tout-en-un depuis " + driverDir);

            if (!File.Exists(inf))
                return Fail(session, "INF du pilote introuvable : " + inf);

            // ── 0) certificat de test + mode testsigning (builds signés-test) ──
            string cer = FindCertFile(driverDir);
            bool rebootNeeded = false;

            if (cer != null)
            {
                session.Log("RoomEq CA : build signé-test — import du certificat " + cer);
                try
                {
                    ImportCertToStores(session, cer);
                }
                catch (Exception ex)
                {
                    return Fail(session, "Importation du certificat de signature de test impossible (" + ex.Message + ").");
                }

                if (!IsTestSigningOn(session))
                {
                    session.Log("RoomEq CA : mode testsigning inactif → activation");
                    string err;
                    if (!EnableTestSigning(session, out err))
                        return Fail(session,
                            "Impossible d'activer le mode de signature de test de Windows (bcdedit : "
                            + err + "). Cause la plus fréquente : le Secure Boot est actif — désactivez-le "
                            + "dans le BIOS/UEFI puis relancez l'installation. (Distribution au grand "
                            + "public sans cette contrainte : signature Microsoft, voir INSTALLATION-PILOTE.md.)");
                    rebootNeeded = true; // appliqué par le noyau au prochain démarrage
                }
                else
                {
                    session.Log("RoomEq CA : mode testsigning déjà actif — pas de redémarrage nécessaire");
                }
            }
            else
            {
                session.Log("RoomEq CA : aucun .cer dans le paquet → build signé officiellement "
                            + "(ni certificat, ni mode test, ni redémarrage)");
            }

            // ── 1) paquet dans le magasin de pilotes Windows ──
            // 259 (ERROR_NO_MORE_ITEMS) = paquet ajouté mais aucun périphérique
            // ne correspond ENCORE : normal, le périphérique est créé à
            // l'étape 2 et la liaison se fait par UpdateDriver…
            string dummy;
            int code = RunHidden(session, "pnputil.exe", "/add-driver \"" + inf + "\" /install", out dummy);
            if (code != 0 && code != 259)
                return Fail(session, "Ajout du pilote au magasin Windows impossible (pnputil, code " + code + ").");

            // ── 2) périphérique ROOT\ROOMEQVAD + liaison du pilote ──
            // Si le mode test vient d'être activé, le démarrage effectif de la
            // carte ne pourra réussir qu'après le redémarrage (signature noyau)
            // : PnP réessaiera automatiquement au boot — pas d'échec bloquant.
            try
            {
                EnsureVirtualDevice(session, inf);
            }
            catch (Exception ex)
            {
                if (rebootNeeded)
                    session.Log("RoomEq CA AVERTISSEMENT : périphérique non lié avant redémarrage ("
                                + ex.Message + ") — PnP réessaiera au démarrage");
                else
                    return Fail(session, "Création du périphérique « RoomEq Virtual Audio » impossible ("
                                        + ex.Message + ").");
            }

            if (rebootNeeded)
                WriteRebootFlag(session);

            session.Log("RoomEq CA : installation du pilote terminée"
                        + (rebootNeeded ? " (redémarrage requis)" : " (carte immédiatement opérationnelle)"));
            return ActionResult.Success;
        }

        // ===================================================== ROLLBACK

        [CustomAction]
        public static ActionResult RollbackRoomEqDriver(Session session)
        {
            session.Log("RoomEq CA : ROLLBACK du pilote (best effort — les erreurs sont ignorées)");
            string installDir = null;
            try { installDir = session.CustomActionData["INSTALLDIR"]; }
            catch (Exception) { /* données absentes : tant pis */ }

            string driverDir = installDir != null ? DriverDirOf(installDir) : null;

            try { RemoveRootDevice(session); }
            catch (Exception ex) { session.Log("RoomEq CA rollback : périphérique non retiré (" + ex.Message + ")"); }

            try
            {
                string installedInf = ResolveStoreInfName(session);
                if (!string.IsNullOrEmpty(installedInf))
                {
                    string dummy;
                    RunHidden(session, "pnputil.exe",
                        "/delete-driver " + installedInf + " /uninstall /force", out dummy);
                }
            }
            catch (Exception ex) { session.Log("RoomEq CA rollback : paquet non retiré (" + ex.Message + ")"); }

            if (driverDir != null)
            {
                try { RemoveCertFromStores(session, driverDir); }
                catch (Exception ex) { session.Log("RoomEq CA rollback : certificat non retiré (" + ex.Message + ")"); }
            }

            try { File.Delete(RebootFlagPath()); }
            catch (Exception) { }
            return ActionResult.Success;
        }

        // ===================================================== DÉSINSTALLATION

        [CustomAction]
        public static ActionResult UninstallRoomEqDriver(Session session)
        {
            session.Log("RoomEq CA : désinstallation du pilote");
            string installDir = session.CustomActionData["INSTALLDIR"];
            string driverDir = DriverDirOf(installDir);

            // 1) retirer le périphérique root-énuméré
            try { RemoveRootDevice(session); }
            catch (Exception ex) { session.Log("RoomEq CA : périphérique non retiré (" + ex.Message + ")"); }

            // 2) retirer le paquet du magasin (nom oemNN.inf mémorisé ou réénuméré)
            try
            {
                string installedInf = session.CustomActionData["DRIVERSTOREINF"];
                if (string.IsNullOrEmpty(installedInf)
                    || !installedInf.EndsWith(".inf", StringComparison.OrdinalIgnoreCase))
                {
                    installedInf = ResolveStoreInfName(session);
                }

                if (string.IsNullOrEmpty(installedInf))
                {
                    session.Log("RoomEq CA : pilote absent du magasin — rien à désinstaller");
                }
                else
                {
                    string dummy;
                    int code = RunHidden(session, "pnputil.exe",
                        "/delete-driver " + installedInf + " /uninstall /force", out dummy);
                    session.Log("RoomEq CA : pnputil /delete-driver a retourné " + code);
                }
            }
            catch (Exception ex) { session.Log("RoomEq CA : retrait du paquet impossible (" + ex.Message + ")"); }

            // 3) retirer le certificat de test des magasins de confiance
            try { RemoveCertFromStores(session, driverDir); }
            catch (Exception ex) { session.Log("RoomEq CA : certificat non retiré (" + ex.Message + ")"); }

            // une désinstallation ratée ne doit pas empêcher de retirer l'application
            return ActionResult.Success;
        }

        // ===================================================== DRAPEAU REDÉMARRAGE

        /// <summary>
        /// Immédiate, séquencée APRÈS InstallExecute (côté client, élevée) :
        /// lit le drapeau posé par la CA différée et expose
        /// ROOMEQ_DRIVER_REBOOT=1 à <ScheduleReboot>.
        /// </summary>
        [CustomAction]
        public static ActionResult CheckRoomEqReboot(Session session)
        {
            try
            {
                string flag = RebootFlagPath();
                if (File.Exists(flag))
                {
                    try { File.Delete(flag); }
                    catch (Exception) { }

                    session["ROOMEQ_DRIVER_REBOOT"] = "1";
                    session.Log("RoomEq CA : redémarrage requis (mode test activé) → ScheduleReboot");

                    MessageBox(session, InstallMessage.User,
                        "Le pilote « RoomEq Virtual Audio » est installé. Windows doit redémarrer pour "
                        + "activer la signature de test du pilote (un filigrane « Mode de test » apparaîtra "
                        + "en bas à droite de l'écran — c'est normal). Cliquez sur « Oui » à l'invite "
                        + "suivante pour redémarrer maintenant, ou « Non » pour redémarrer plus tard.");
                }
                else
                {
                    session.Log("RoomEq CA : aucun redémarrage pilote requis");
                }
            }
            catch (Exception ex)
            {
                session.Log("RoomEq CA : CheckRoomEqReboot a échoué (" + ex.Message + ")");
            }
            return ActionResult.Success;
        }

        // ===================================================== DÉCOUVERTE (immédiat)

        [CustomAction]
        public static ActionResult FindRoomEqDriver(Session session)
        {
            // immédiat, AVANT la suppression : mémorise le nom oemNN.inf courant
            // pour le passer à l'action différée de désinstallation.
            try
            {
                string installedInf = ResolveStoreInfName(session);
                session["DRIVERSTOREINF"] = installedInf ?? "";
                session.Log("RoomEq CA : pilote du magasin = " + session["DRIVERSTOREINF"]);
            }
            catch (Exception ex)
            {
                session.Log("RoomEq CA : énumération impossible (" + ex.Message + ")");
                session["DRIVERSTOREINF"] = "";
            }
            return ActionResult.Success;
        }

        // ===================================================== certificats

        private static void ImportCertToStores(Session session, string cerPath)
        {
            var cert = new X509Certificate2(cerPath);
            foreach (StoreName name in new[] { StoreName.Root, StoreName.TrustedPublisher })
            {
                using (var store = new X509Store(name, StoreLocation.LocalMachine))
                {
                    store.Open(OpenFlags.ReadWrite | OpenFlags.OpenExistingOnly);
                    var found = store.Certificates.Find(
                        X509FindType.FindByThumbprint, cert.Thumbprint, false);
                    if (found.Count > 0)
                    {
                        session.Log("RoomEq CA : certificat déjà présent dans " + name);
                    }
                    else
                    {
                        store.Add(cert);
                        session.Log("RoomEq CA : certificat importé dans " + name);
                    }
                }
            }
        }

        private static void RemoveCertFromStores(Session session, string driverDir)
        {
            string thumbprint = null;
            string cer = FindCertFile(driverDir);
            if (cer != null)
            {
                try { thumbprint = new X509Certificate2(cer).Thumbprint; }
                catch (Exception ex) { session.Log("RoomEq CA : lecture du .cer impossible (" + ex.Message + ")"); }
            }

            if (thumbprint == null)
            {
                // repli : retrouver le certificat par son sujet dans Root
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

            if (thumbprint == null)
            {
                session.Log("RoomEq CA : certificat de test introuvable — rien à retirer");
                return;
            }

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
                        session.Log("RoomEq CA : certificat retiré de " + name);
                    }
                }
            }
        }

        /// <summary>Cherche le certificat public livré avec le pilote.</summary>
        private static string FindCertFile(string driverDir)
        {
            if (string.IsNullOrEmpty(driverDir) || !Directory.Exists(driverDir))
                return null;
            string p = Path.Combine(driverDir, CertName);
            if (File.Exists(p)) return p;
            string[] others = Directory.GetFiles(driverDir, "*.cer");
            return others.Length > 0 ? others[0] : null;
        }

        // ===================================================== mode testsigning

        private static bool IsTestSigningOn(Session session)
        {
            try
            {
                string output;
                RunHidden(null, "bcdedit.exe", "/enum {current}", out output);
                foreach (string raw in output.Split('\n'))
                {
                    string line = raw.Trim().TrimEnd('\r');
                    if (line.ToLowerInvariant().IndexOf("testsigning", StringComparison.Ordinal) < 0)
                        continue;
                    // « testsigning    Yes » (EN) / « testsigning    Oui » (FR)
                    string[] toks = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (toks.Length < 2)
                        return false;
                    string val = toks[toks.Length - 1].ToLowerInvariant();
                    return val == "yes" || val == "oui";
                }
            }
            catch (Exception ex)
            {
                session.Log("RoomEq CA : bcdedit /enum a échoué (" + ex.Message + ") → mode test considéré inactif");
            }
            return false;
        }

        private static bool EnableTestSigning(Session session, out string error)
        {
            string stdout;
            int code = RunHidden(session, "bcdedit.exe", "/set testsigning on", out stdout);
            if (code == 0)
                error = null;
            else
                error = "code " + code
                        + ((stdout ?? "").Trim().Length > 0
                            ? " ; « " + OneLine(stdout, 160) + " »" : "");
            return code == 0;
        }

        // ===================================================== périphérique ROOT

        private static void EnsureVirtualDevice(Session session, string inf)
        {
            bool exists = RootDeviceExists(session);

            if (!exists)
            {
                session.Log("RoomEq CA : périphérique ROOT\\ROOMEQVAD absent — création");
                CreateRootDevice(session, inf);
            }
            else
            {
                session.Log("RoomEq CA : périphérique ROOT\\ROOMEQVAD déjà présent");
            }

            bool driverReboot;
            if (!NativeMethods.UpdateDriverForPlugAndPlayDevicesW(
                    IntPtr.Zero, HwId, inf,
                    NativeMethods.INSTALLFLAG_FORCE | NativeMethods.INSTALLFLAG_NONINTERACTIVE,
                    out driverReboot))
            {
                int err = Marshal.GetLastWin32Error();
                throw new Exception("UpdateDriverForPlugAndPlayDevices a échoué (erreur 0x"
                                    + err.ToString("X") + ")");
            }
            session.Log("RoomEq CA : pilote lié au périphérique (reboot=" + driverReboot + ")");
            if (driverReboot)
                WriteRebootFlag(session);
        }

        private static bool RootDeviceExists(Session session)
        {
            IntPtr set = NativeMethods.SetupDiGetClassDevsW(
                IntPtr.Zero, "ROOT", IntPtr.Zero,
                NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_ALLCLASSES);
            if (set == IntPtr.Zero || set == new IntPtr(-1))
                throw new Exception("SetupDiGetClassDevs(ROOT) a échoué (erreur 0x"
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

        /// <summary>Recette « devcon install » : devnode ROOT\ROOMEQVAD.</summary>
        private static void CreateRootDevice(Session session, string inf)
        {
            Guid classGuid = new Guid("4d36e96c-e325-11ce-bfc1-08002be10318"); // classe MEDIA
            var className = new StringBuilder(64);
            uint needed;
            Guid fromInf;
            if (NativeMethods.SetupDiGetINFClassW(inf, out fromInf, className, 64, out needed))
                classGuid = fromInf;
            else
                session.Log("RoomEq CA : SetupDiGetINFClass a échoué → classe MEDIA par défaut");

            IntPtr set = NativeMethods.SetupDiCreateDeviceInfoList(ref classGuid, IntPtr.Zero);
            if (set == IntPtr.Zero || set == new IntPtr(-1))
                throw new Exception("SetupDiCreateDeviceInfoList a échoué (erreur 0x"
                                    + Marshal.GetLastWin32Error().ToString("X") + ")");
            try
            {
                var dev = new NativeMethods.SP_DEVINFO_DATA();
                dev.cbSize = Marshal.SizeOf(typeof(NativeMethods.SP_DEVINFO_DATA));

                if (!NativeMethods.SetupDiCreateDeviceInfoW(
                        set, HwId, ref classGuid, null, IntPtr.Zero,
                        NativeMethods.DICD_GENERATE_ID, ref dev))
                    throw new Exception("SetupDiCreateDeviceInfo a échoué (erreur 0x"
                                        + Marshal.GetLastWin32Error().ToString("X") + ")");

                byte[] hwid = Encoding.Unicode.GetBytes(HwId + "\0\0"); // REG_MULTI_SZ terminé
                if (!NativeMethods.SetupDiSetDeviceRegistryPropertyW(
                        set, ref dev, NativeMethods.SPDRP_HARDWAREID,
                        hwid, (uint)hwid.Length))
                    throw new Exception("SetupDiSetDeviceRegistryProperty a échoué (erreur 0x"
                                        + Marshal.GetLastWin32Error().ToString("X") + ")");

                if (!NativeMethods.SetupDiCallClassInstaller(
                        NativeMethods.DIF_REGISTERDEVICE, set, ref dev))
                    throw new Exception("DIF_REGISTERDEVICE a échoué (erreur 0x"
                                        + Marshal.GetLastWin32Error().ToString("X") + ")");

                session.Log("RoomEq CA : périphérique root-énuméré créé (ROOT\\ROOMEQVAD)");
            }
            finally
            {
                NativeMethods.SetupDiDestroyDeviceInfoList(set);
            }
        }

        private static void RemoveRootDevice(Session session)
        {
            IntPtr set = NativeMethods.SetupDiGetClassDevsW(
                IntPtr.Zero, "ROOT", IntPtr.Zero,
                NativeMethods.DIGCF_PRESENT | NativeMethods.DIGCF_ALLCLASSES);
            if (set == IntPtr.Zero || set == new IntPtr(-1))
            {
                session.Log("RoomEq CA : énumération ROOT impossible — périphérique non retiré");
                return;
            }
            try
            {
                NativeMethods.SP_DEVINFO_DATA dev;
                if (!FindRoomEqDevice(set, out dev))
                {
                    session.Log("RoomEq CA : périphérique ROOT\\ROOMEQVAD introuvable — rien à retirer");
                    return;
                }
                if (NativeMethods.SetupDiCallClassInstaller(NativeMethods.DIF_REMOVE, set, ref dev))
                    session.Log("RoomEq CA : périphérique retiré");
                else
                    session.Log("RoomEq CA : DIF_REMOVE a échoué (erreur 0x"
                                + Marshal.GetLastWin32Error().ToString("X") + ")");
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

        // ===================================================== magasin PnP

        private static string ResolveStoreInfName(Session session)
        {
            string output;
            RunHidden(session, "pnputil.exe", "/enum-drivers", out output);
            return ParseDriverStoreName(output, InfName);
        }

        /// <summary>
        /// Analyse la sortie de « pnputil /enum-drivers » (anglais ou français) et
        /// retourne le nom publié (oemNN.inf) dont le fichier INF d'origine est
        /// celui du pilote RoomEq.
        /// </summary>
        public static string ParseDriverStoreName(string enumOutput, string originalInfName)
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

        // ===================================================== drapeau reboot

        private static string RebootFlagPath()
        {
            string sysRoot = Environment.GetEnvironmentVariable("SystemRoot");
            if (string.IsNullOrEmpty(sysRoot))
                sysRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            if (string.IsNullOrEmpty(sysRoot)) sysRoot = @"C:\Windows";
            return Path.Combine(sysRoot, "Temp", RebootFlagName);
        }

        private static void WriteRebootFlag(Session session)
        {
            try
            {
                File.WriteAllText(RebootFlagPath(), "testsigning active : redemarrage requis");
                session.Log("RoomEq CA : drapeau de redémarrage posé (" + RebootFlagPath() + ")");
            }
            catch (Exception ex)
            {
                session.Log("RoomEq CA : drapeau de redémarrage non écrit (" + ex.Message + ")");
            }
        }

        // ===================================================== outils

        private static string DriverDirOf(string installDir)
        {
            string d = Path.Combine(installDir ?? "", "driver");
            return Directory.Exists(d) ? d : (installDir ?? "");
        }

        /// <summary>Échec avec boîte de message (l'affiche même en UI basique).</summary>
        private static ActionResult Fail(Session session, string message)
        {
            session.Log("RoomEq CA ERREUR : " + message);
            MessageBox(session, InstallMessage.Error, message);
            return ActionResult.Failure;
        }

        private static void MessageBox(Session session, InstallMessage kind, string message)
        {
            try
            {
                using (var rec = new Record(1))
                {
                    rec[1] = message;
                    rec.FormatString = "[1]";
                    session.Message(kind, rec);
                }
            }
            catch (Exception ex)
            {
                session.Log("RoomEq CA : boîte de message impossible (" + ex.Message + ")");
            }
        }

        private static string OneLine(string text, int max)
        {
            if (text == null) return "";
            string s = text.Replace("\r", " ").Replace("\n", " ").Trim();
            while (s.Contains("  ")) s = s.Replace("  ", " ");
            return s.Length <= max ? s : s.Substring(0, max) + "…";
        }

        private static int RunHidden(Session session, string exe, string args, out string output)
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
                    try { p.Kill(); } catch (Exception) { }
                    p.WaitForExit();
                }
                if (session != null)
                {
                    session.Log("RoomEq CA : " + exe + " " + args + " -> code " + p.ExitCode
                                + "\n" + (output ?? "").Trim()
                                + ((err ?? "").Trim().Length > 0
                                    ? "\n[stderr] " + OneLine(err, 300) : ""));
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
