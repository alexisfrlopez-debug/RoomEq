// ============================================================================
//  GuiRunner — interface graphique de RoomEqDriverSetup (v2.2.0).
//
//  Fenetre unique « zero ligne de commande » :
//    - panneau d'etat (Secure Boot, mode test, pilote, peripherique,
//      service, ASIO) rafraichi a l'ouverture, apres chaque action et via
//      le bouton Actualiser ;
//    - journal (etapes + VERDICT) ;
//    - boutons : Installer le pilote / Diagnostic complet / Revenir au mode
//      normal / Desinstaller / Fermer.
//
//  Les questions de la logique (mode test, redemarrage, retrait) arrivent
//  en boite de dialogue Oui/Non via Ui.Confirm ; le redemarrage propose
//  reste 100 % graphique (shutdown /r /t 5 apres accord).
//
//  Threading : chaque action tourne sur un thread de fond ; le journal et
//  les dialogues passent par Invoke (l'UI ne bloque jamais sur le fond).
// ============================================================================
using System;
using System.Drawing;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace RoomEqDriverSetup
{
    internal static class GuiRunner
    {
        /// <summary>
        /// Lance la fenetre. autoAction : "" (rien), "install", "uninstall"
        /// ou "status" — action demarree automatiquement a l'ouverture
        /// (utilise par le bouton « Installer le pilote » de l'application).
        /// </summary>
        public static int Run(string autoAction)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.ThreadException += (s, e) =>
                MessageBox.Show(e.Exception.Message, "RoomEqDriverSetup",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
                MessageBox.Show((e.ExceptionObject as Exception ?? new Exception("?")).Message,
                    "RoomEqDriverSetup", MessageBoxButtons.OK, MessageBoxIcon.Error);
            using (var f = new MainForm(autoAction))
            {
                Application.Run(f);
                return f.LastExitCode;
            }
        }
    }

    internal sealed class MainForm : Form
    {
        // ---- controles ----------------------------------------------------
        private readonly TableLayoutPanel _statusTable;
        private readonly RichTextBox _log;
        private readonly Button _btnInstall;
        private readonly Button _btnDiag;
        private readonly Button _btnNormal;
        private readonly Button _btnUninstall;
        private readonly Button _btnRefresh;
        private readonly Button _btnClose;
        private readonly ToolStripStatusLabel _statusLabel;

        // labels valeurs du panneau d'etat (indexes = lignes)
        private readonly Label[] _values = new Label[6];

        private readonly Font _logFont = new Font("Consolas", 9f);
        private readonly Font _logFontBold = new Font("Consolas", 9.25f, FontStyle.Bold);

        private bool _busy;
        public int LastExitCode { get; private set; }

        public MainForm(string autoAction)
        {
            Text = "RoomEq — Pilote audio virtuel";
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("Segoe UI", 9f);
            MinimumSize = new Size(700, 560);
            ClientSize = new Size(780, 660);
            Icon = SystemIcons.Application;

            // ---------- panneau d'etat (haut) ------------------------------
            var statusGroup = new GroupBox
            {
                Text = "État de la machine",
                Dock = DockStyle.Top,
                Height = 226,
                Padding = new Padding(12, 4, 12, 8),
            };
            _statusTable = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 150,
                ColumnCount = 2,
                RowCount = 6,
                AutoSize = false,
            };
            _statusTable.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 170));
            _statusTable.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            for (int i = 0; i < 6; i++)
            {
                _statusTable.RowStyles.Add(new RowStyle(SizeType.Absolute, 24));
                _statusTable.Controls.Add(new Label
                {
                    Text = StatusNames[i],
                    AutoSize = false,
                    Anchor = AnchorStyles.Left,
                    ForeColor = SystemColors.ControlDarkDark,
                }, 0, i);
                _values[i] = new Label
                {
                    Text = "…",
                    AutoSize = false,
                    Anchor = AnchorStyles.Left,
                    ForeColor = SystemColors.ControlText,
                };
                _statusTable.Controls.Add(_values[i], 1, i);
            }
            var refreshRow = new FlowLayoutPanel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                FlowDirection = FlowDirection.RightToLeft,
                WrapContents = false,
            };
            _btnRefresh = new Button { Text = "Actualiser", AutoSize = true, UseVisualStyleBackColor = true };
            _btnRefresh.Click += (s, e) => RefreshStatus();
            refreshRow.Controls.Add(_btnRefresh);
            statusGroup.Controls.Add(refreshRow);
            statusGroup.Controls.Add(_statusTable);

            // ---------- journal (milieu, rempli) ---------------------------
            var logGroup = new GroupBox
            {
                Text = "Journal",
                Dock = DockStyle.Fill,
                Padding = new Padding(12, 4, 12, 12),
            };
            _log = new RichTextBox
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                BackColor = Color.FromArgb(30, 30, 30),
                ForeColor = Color.Gainsboro,
                Font = new Font("Consolas", 9f),
                DetectUrls = false,
                WordWrap = false,
                ScrollBars = RichTextBoxScrollBars.Both,
                HideSelection = false,
            };
            logGroup.Controls.Add(_log);

            // ---------- boutons (bas) --------------------------------------
            var buttonPanel = new Panel { Dock = DockStyle.Bottom, Height = 58 };
            var flow = new FlowLayoutPanel
            {
                Dock = DockStyle.Left,
                Width = 560,
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = false,
                Padding = new Padding(12, 12, 0, 0),
            };
            _btnInstall = MakeButton("Installer le pilote…", 150, true);
            _btnDiag = MakeButton("Diagnostic complet", 125, false);
            _btnNormal = MakeButton("Revenir au mode normal", 150, false);
            _btnUninstall = MakeButton("Désinstaller…", 110, false);
            _btnUninstall.ForeColor = Color.Firebrick;
            _btnInstall.Click += (s, e) => RunAction("install");
            _btnDiag.Click += (s, e) => RunAction("status");
            _btnNormal.Click += (s, e) => RunAction("mode-normal");
            _btnUninstall.Click += (s, e) => RunAction("uninstall");
            flow.Controls.Add(_btnInstall);
            flow.Controls.Add(_btnDiag);
            flow.Controls.Add(_btnNormal);
            flow.Controls.Add(_btnUninstall);
            _btnClose = new Button
            {
                Text = "Fermer",
                AutoSize = true,
                Anchor = AnchorStyles.Right | AnchorStyles.Bottom,
                UseVisualStyleBackColor = true,
            };
            _btnClose.Click += (s, e) => Close();
            buttonPanel.Controls.Add(_btnClose);
            buttonPanel.Controls.Add(flow);

            var strip = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel("Prêt.")
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft,
            };
            strip.Items.Add(_statusLabel);

            // ordre de docking : le Fill d'abord, puis haut/bas
            Controls.Add(logGroup);
            Controls.Add(statusGroup);
            Controls.Add(buttonPanel);
            Controls.Add(strip);

            FormClosing += (s, e) =>
            {
                if (_busy)
                {
                    e.Cancel = true;
                    _statusLabel.Text = "Action en cours — attendez la fin…";
                }
            };
            Load += (s, e) =>
            {
                WireUi();
                AppendLog("RoomEqDriverSetup — carte son virtuelle RoomEq", true);
                RefreshStatus();
                if (!string.IsNullOrEmpty(autoAction))
                    RunAction(autoAction);
            };
        }

        private static string[] StatusNames
        {
            get
            {
                return new[]
                {
                    "Secure Boot",
                    "Mode test de Windows",
                    "Pilote (magasin Windows)",
                    "Périphérique virtuel",
                    "Service roomeqvad",
                    "ASIO (registre)",
                };
            }
        }

        private static Button MakeButton(string text, int width, bool accent)
        {
            var b = new Button
            {
                Text = text,
                AutoSize = false,
                Size = new Size(width, 34),
                UseVisualStyleBackColor = true,
            };
            if (accent)
            {
                b.Font = new Font(b.Font, FontStyle.Bold);
                b.ForeColor = Color.FromArgb(0, 96, 48);
            }
            return b;
        }

        // ---- rebranchement de la logique vers la fenetre -------------------

        private void WireUi()
        {
            Ui.Say = t => BeginInvoke(new Action(() => AppendLog(t)));
            Ui.Heading = t => BeginInvoke(new Action(() => AppendLog(t, true)));
            Ui.SayError = t => BeginInvoke(new Action(() => AppendLog(t, false, true)));
            Ui.Confirm = (question, defautOui) =>
            {
                bool result = false;
                Invoke(new Action(() => { result = ConfirmDialog.Ask(this, question, defautOui); }));
                return result;
            };
            Ui.Pause = () => { };
        }

        private void AppendLog(string text, bool heading = false, bool error = false)
        {
            try
            {
                _log.ReadOnly = false;   // (les couleurs ne s'appliquent pas en ReadOnly)
                _log.SelectionStart = _log.TextLength;
                _log.SelectionLength = 0;
                _log.SelectionColor = error ? Color.OrangeRed
                    : heading ? Color.White : Color.Gainsboro;
                _log.SelectionFont = heading ? _logFontBold : _logFont;
                string prefix = heading ? Environment.NewLine + new string('=', 62)
                                          + Environment.NewLine : "";
                _log.AppendText(prefix + text + Environment.NewLine);
                _log.SelectionColor = Color.Gainsboro;
                _log.SelectionFont = _logFont;
                _log.ReadOnly = true;
                _log.ScrollToCaret();
            }
            catch (ObjectDisposedException) { /* fermeture */ }
        }

        // ---- panneau d'etat -------------------------------------------------

        private void RefreshStatus()
        {
            if (_busy) return;
            _statusLabel.Text = "Lecture de l'état…";
            Task.Run(() =>
            {
                Program.StatusSnap snap = null;
                try { snap = Program.CaptureStatus(); }
                catch { }
                BeginInvoke(new Action(() =>
                {
                    if (snap == null)
                    {
                        for (int i = 0; i < 6; i++) SetRow(i, "Indéterminé", SystemColors.GrayText);
                        _statusLabel.Text = "État illisible — voir le journal.";
                        return;
                    }
                    // Secure Boot
                    if (snap.SecureBoot == null) SetRow(0, "Indéterminé", SystemColors.GrayText);
                    else if (snap.SecureBoot.Value)
                        SetRow(0, "Actif (bloque le mode test)", Color.DarkOrange);
                    else
                        SetRow(0, "Inactif", Color.SeaGreen);
                    // Mode test
                    if (snap.TestMode)
                        SetRow(1, "Actif" + (snap.TestSignedBuild ? " (requis par ce pilote)" : ""),
                            Color.SeaGreen);
                    else if (snap.TestSignedBuild)
                        SetRow(1, "Inactif (requis pour charger ce pilote)", Color.DarkOrange);
                    else
                        SetRow(1, "Inactif", Color.SeaGreen);
                    // Pilote magasin
                    if (snap.StoreInf != null) SetRow(2, "Présent (" + snap.StoreInf + ")", Color.SeaGreen);
                    else SetRow(2, "Absent", SystemColors.GrayText);
                    // Peripherique
                    if (!snap.Device) SetRow(3, "Absent — c'est lui qui crée les entrées son", SystemColors.GrayText);
                    else if (snap.DeviceStarted && !snap.DeviceHasProblem)
                        SetRow(3, "Présent, démarré", Color.SeaGreen);
                    else if (snap.DeviceHasProblem)
                        SetRow(3, "Présent, bloqué (code " + snap.Problem + ")", Color.Firebrick);
                    else SetRow(3, "Présent, non démarré", Color.DarkOrange);
                    // Service / ASIO
                    SetRow(4, snap.Service, string.IsNullOrEmpty(snap.Service)
                        ? SystemColors.GrayText : SystemColors.ControlText);
                    SetRow(5, snap.AsioPath != null ? "Enregistré" : "Non enregistré",
                        snap.AsioPath != null ? Color.SeaGreen : SystemColors.GrayText);
                    _statusLabel.Text = "Prêt.";
                }));
            });
        }

        private void SetRow(int row, string text, Color color)
        {
            _values[row].Text = text;
            _values[row].ForeColor = color;
        }

        // ---- execution des actions -----------------------------------------

        private void RunAction(string action)
        {
            if (_busy) return;
            if (!Program.IsElevated())
            {
                MessageBox.Show(this,
                    "Droits administrateur manquants.\nRelancez ce programme et acceptez l'invite UAC.",
                    "RoomEqDriverSetup", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                LastExitCode = 1;
                return;
            }
            _busy = true;
            SetButtonsEnabled(false);
            _statusLabel.Text = "Action en cours…";
            Task.Run(() =>
            {
                int rc;
                try
                {
                    switch (action)
                    {
                        case "install": rc = Program.RunInstall(false, false); break;
                        case "uninstall": rc = Program.RunUninstall(); break;
                        case "mode-normal": rc = Program.RunModeNormal(); break;
                        default: rc = Program.RunStatus(); break;
                    }
                }
                catch (Exception ex)
                {
                    Ui.ErrorSafe("[ERREUR] " + ex.Message);
                    rc = 1;
                }
                BeginInvoke(new Action(() =>
                {
                    LastExitCode = rc;
                    _busy = false;
                    SetButtonsEnabled(true);
                    _statusLabel.Text = rc == 0
                        ? "Terminé — voir le journal."
                        : "Terminé avec problème — voir le journal.";
                    RefreshStatus();
                }));
            });
        }

        private void SetButtonsEnabled(bool enabled)
        {
            _btnInstall.Enabled = enabled;
            _btnDiag.Enabled = enabled;
            _btnNormal.Enabled = enabled;
            _btnUninstall.Enabled = enabled;
            _btnRefresh.Enabled = enabled;
        }
    }

    // =======================================================================
    //  Boite de dialogue Oui/Non (question de la logique, defaut explicite).
    // =======================================================================
    internal sealed class ConfirmDialog : Form
    {
        private bool _result;

        private ConfirmDialog(string question, bool defautOui)
        {
            Text = "RoomEqDriverSetup";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterParent;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            Font = new Font("Segoe UI", 9.75f);

            var icon = new PictureBox
            {
                Image = SystemIcons.Warning.ToBitmap(),
                Size = new Size(36, 36),
                SizeMode = PictureBoxSizeMode.Zoom,
                Location = new Point(14, 16),
            };
            int textTop = string.IsNullOrEmpty(question) ? 16 : 16;
            Size labelSize = TextRenderer.MeasureText(question, Font,
                new Size(420, int.MaxValue), TextFormatFlags.WordBreak);
            var label = new Label
            {
                Text = question,
                Bounds = new Rectangle(62, textTop, 430, Math.Max(36, labelSize.Height)),
            };
            int buttonsTop = label.Bottom + 18;
            var no = new Button { Text = "Non", DialogResult = DialogResult.No, Size = new Size(90, 30), Location = new Point(62 + 430 - 190, buttonsTop) };
            var yes = new Button { Text = "Oui", DialogResult = DialogResult.Yes, Size = new Size(90, 30), Location = new Point(62 + 430 - 92, buttonsTop) };
            yes.Click += (s, e) => _result = true;
            no.Click += (s, e) => _result = false;
            // Entree = choix propose ; Echap = Non (le plus conservateur)
            AcceptButton = defautOui ? yes : no;
            CancelButton = no;
            Controls.Add(icon);
            Controls.Add(label);
            Controls.Add(yes);
            Controls.Add(no);
            ClientSize = new Size(62 + 430 + 14, buttonsTop + 42);
        }

        public static bool Ask(IWin32Window owner, string question, bool defautOui)
        {
            using (var dlg = new ConfirmDialog(question, defautOui))
            {
                dlg.ShowDialog(owner);
                return dlg._result;
            }
        }
    }
}
