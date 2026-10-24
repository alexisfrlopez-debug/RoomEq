using System;
using System.Diagnostics;
using System.Text;
using WixToolset.Dtf.WindowsInstaller;

namespace RoomEq.Installer.CA
{
    /// <summary>
    /// Actions personnalisées MSI du paquet RoomEq : installation/désinstallation
    /// du pilote de la carte son virtuelle (roomeqvad) via pnputil, sans fenêtre.
    /// Les actions sont différées, non-impersonnées : elles tournent en SYSTEM,
    /// donc avec les droits nécessaires — aucune invite UAC supplémentaire.
    /// </summary>
    public class CustomActions
    {
        private const string InfName = "roomeqvad.inf";

        [CustomAction]
        public static ActionResult InstallRoomEqDriver(Session session)
        {
            string installDir = session.CustomActionData["INSTALLDIR"];
            session.Log("RoomEq CA : installation du pilote depuis " + installDir);

            string inf = System.IO.Path.Combine(infDirOrDriver(session, installDir), InfName);
            if (!System.IO.File.Exists(inf))
            {
                session.Log("RoomEq CA : INF introuvable (" + inf + ")");
                session["DRIVERCA_RESULT"] = "INF introuvable : " + inf;
                return ActionResult.Failure;
            }

            int code = RunHidden(session, "pnputil.exe", "/add-driver \"" + inf + "\" /install");
            if (code != 0)
            {
                session.Log("RoomEq CA : pnputil /add-driver a retourné " + code);
                session["DRIVERCA_RESULT"] = "pnputil a retourné le code " + code;
                return ActionResult.Failure;
            }
            session["DRIVERCA_RESULT"] = "OK";
            return ActionResult.Success;
        }

        [CustomAction]
        public static ActionResult UninstallRoomEqDriver(Session session)
        {
            session.Log("RoomEq CA : désinstallation du pilote");
            string installedInf = session.CustomActionData["DRIVERSTOREINF"];

            if (string.IsNullOrEmpty(installedInf) || !installedInf.EndsWith(".inf",
                    StringComparison.OrdinalIgnoreCase))
            {
                // le nom oemNN.inf n'a pas été résolu par la séquence : énumérer
                string output = CaptureHidden("pnputil.exe", "/enum-drivers");
                installedInf = ParseDriverStoreName(output, InfName);
            }

            if (string.IsNullOrEmpty(installedInf))
            {
                session.Log("RoomEq CA : pilote absent du magasin — rien à désinstaller");
                session["DRIVERCA_RESULT"] = "absent";
                return ActionResult.Success;   // idempotent
            }

            int code = RunHidden(session, "pnputil.exe",
                "/delete-driver " + installedInf + " /uninstall /force");
            session.Log("RoomEq CA : pnputil /delete-driver a retourné " + code);
            session["DRIVERCA_RESULT"] = code == 0 ? "OK" : ("code " + code);
            return code == 0 ? ActionResult.Success : ActionResult.Success;
            // une désinstallation ratée ne doit pas empêcher de retirer l'application
        }

        [CustomAction]
        public static ActionResult FindRoomEqDriver(Session session)
        {
            // immédiat, AVANT la suppression : mémorise le nom oemNN.inf courant
            // pour le passer à l'action différée de désinstallation.
            try
            {
                string output = CaptureHidden("pnputil.exe", "/enum-drivers");
                session["DRIVERSTOREINF"] = ParseDriverStoreName(output, InfName) ?? "";
                session.Log("RoomEq CA : pilote du magasin = " + session["DRIVERSTOREINF"]);
            }
            catch (Exception ex)
            {
                session.Log("RoomEq CA : énumération impossible (" + ex.Message + ")");
                session["DRIVERSTOREINF"] = "";
            }
            return ActionResult.Success;
        }

        // ---------------------------------------------------------------- outils

        private static string infDirOrDriver(Session session, string installDir)
        {
            string inDriver = System.IO.Path.Combine(installDir, "driver");
            if (System.IO.Directory.Exists(inDriver)) return inDriver;
            return installDir;
        }

        private static int RunHidden(Session session, string exe, string args)
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
                string stdout = p.StandardOutput.ReadToEnd();
                p.WaitForExit(120000);
                session.Log("RoomEq CA : " + exe + " " + args + " -> " + p.ExitCode
                            + "\n" + (stdout ?? "").Trim());
                return p.ExitCode;
            }
        }

        private static string CaptureHidden(string exe, string args)
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
            };
            using (var p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                p.WaitForExit(30000);
                return output ?? "";
            }
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
    }
}
