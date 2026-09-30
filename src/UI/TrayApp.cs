using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using WslTray.Core;
using WslTray.Interop;

namespace WslTray.UI
{
    class TrayApp : ApplicationContext
    {
        readonly MainForm form = new MainForm();
        readonly NotifyIcon tray = new NotifyIcon();
        readonly ThemedList list = new ThemedList();
        readonly ToolStripStatusLabel statusLabel = new ToolStripStatusLabel();
        readonly Button btnTerm = new Button(), btnStop = new Button(), btnMore = new Button(), btnRefresh = new Button();
        readonly ToolStripMenuItem miDefault = new ToolStripMenuItem("Set default"), miShut = new ToolStripMenuItem("Shut down WSL");
        readonly ContextMenuStrip moreMenu = new ContextMenuStrip();
        readonly bool dark;
        static readonly Color DarkBack = Color.FromArgb(32, 32, 32), DarkFore = Color.FromArgb(235, 235, 235);
        readonly System.Windows.Forms.Timer windowTimer = new System.Windows.Forms.Timer();
        readonly System.Windows.Forms.Timer trayTimer = new System.Windows.Forms.Timer();
        readonly ToolStripMenuItem miLogin = new ToolStripMenuItem("Start at login");
        readonly ToolStripMenuItem miNotify = new ToolStripMenuItem("Notify when distros start/stop");
        readonly ToolStripMenuItem miTerm = new ToolStripMenuItem("Terminal");
        readonly ComboBox cmbTerm = new ComboBox();
        readonly Label lblTerm = new Label();
        readonly float scale;
        bool syncingTerm;

        int busy;                // 0/1: a worker is running
        bool exiting;
        Snapshot last;
        IntPtr iconHandle = IntPtr.Zero; Icon curIcon; int iconState = -1;
        string lastKey, pendingKey; int pendingCount; DateTime lastBalloon = DateTime.MinValue;

        int S(int v) { return (int)Math.Round(v * scale); }

        public TrayApp(bool showWindow)
        {
            using (Graphics g = form.CreateGraphics()) scale = g.DpiX / 96f;
            dark = IsDarkMode();
            // AutoScaleMode.None: all pixel sizes go through S(); the manifest makes the process system-DPI-aware and
            // MessageBoxFont is already sized for that DPI (Segoe UI 9pt at 100%), so nothing is scaled twice.
            form.AutoScaleMode = AutoScaleMode.None;
            form.Font = SystemFonts.MessageBoxFont;
            form.Text = "WSL Instances";
            form.Size = new Size(S(760), S(380));
            form.MinimumSize = new Size(S(360), S(240));
            form.StartPosition = FormStartPosition.CenterScreen;
            try { form.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch (Exception) { }
            if (dark) { form.BackColor = DarkBack; form.ForeColor = DarkFore; }

            list.View = View.Details; list.FullRowSelect = true; list.MultiSelect = false; list.HideSelection = false;
            list.Dock = DockStyle.Fill; list.Scrollable = true; list.BorderStyle = BorderStyle.None;
            if (dark) { list.Theme = "DarkMode_Explorer"; list.BackColor = DarkBack; list.ForeColor = DarkFore; }
            list.Columns.Add("Name", S(170)); list.Columns.Add("State", S(75)); list.Columns.Add("WSL", S(45));
            list.Columns.Add("Default", S(60)); list.Columns.Add("Disk (vhdx)", S(90));
            // last column takes the leftover width; when narrower than the minimum the list scrolls horizontally
            list.SizeChanged += delegate
            {
                int used = 0;
                for (int i = 0; i < list.Columns.Count - 1; i++) used += list.Columns[i].Width;
                int w = Math.Max(S(90), list.ClientSize.Width - used - 4);
                if (list.Columns[list.Columns.Count - 1].Width != w) list.Columns[list.Columns.Count - 1].Width = w;
            };

            StatusStrip status = new StatusStrip();
            statusLabel.Spring = true; statusLabel.TextAlign = ContentAlignment.MiddleLeft;
            status.Items.Add(statusLabel);
            if (dark) { status.Renderer = new ToolStripProfessionalRenderer(new DarkColors()); status.BackColor = DarkBack; status.ForeColor = DarkFore; }
            // buttons: grows in height when it wraps, never clips
            FlowLayoutPanel bar = new FlowLayoutPanel();
            bar.Dock = DockStyle.Top; bar.AutoSize = true; bar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            bar.WrapContents = true; bar.Padding = new Padding(S(6), S(6), S(6), 0);
            AddBtn(bar, btnTerm, "Open terminal", OpenTerminal);
            AddBtn(bar, btnStop, "Stop distro", StopDistro);
            AddBtn(bar, btnRefresh, "Refresh", delegate { RefreshAsync(); });
            AddBtn(bar, btnMore, "More \u25BE", delegate { moreMenu.Show(btnMore, new Point(0, btnMore.Height)); });
            miDefault.Click += SetDefault; miShut.Click += ShutDown;
            moreMenu.Items.Add(miDefault); moreMenu.Items.Add(miShut);
            // label + combo stay together on their own row
            TableLayoutPanel termBar = new TableLayoutPanel();
            termBar.Dock = DockStyle.Top; termBar.AutoSize = true; termBar.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            termBar.ColumnCount = 2; termBar.RowCount = 1; termBar.Padding = new Padding(S(6), S(2), S(6), S(6));
            termBar.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize)); termBar.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            lblTerm.Text = "Terminal:"; lblTerm.AutoSize = true; lblTerm.Anchor = AnchorStyles.Left; lblTerm.Margin = new Padding(S(3), 0, S(4), 0);
            cmbTerm.DropDownStyle = ComboBoxStyle.DropDownList; cmbTerm.Anchor = AnchorStyles.Left | AnchorStyles.Right;
            cmbTerm.MaximumSize = new Size(S(300), 0); cmbTerm.Margin = new Padding(0, S(3), S(3), S(3));
            if (dark) { cmbTerm.FlatStyle = FlatStyle.Flat; cmbTerm.BackColor = Color.FromArgb(58, 58, 58); cmbTerm.ForeColor = DarkFore; }
            cmbTerm.SelectedIndexChanged += OnTermCombo;
            termBar.Controls.Add(lblTerm, 0, 0); termBar.Controls.Add(cmbTerm, 1, 0);
            if (dark)
            {
                int on = 1;
                IntPtr hw = form.Handle;
                if (Native.DwmSetWindowAttribute(hw, 20, ref on, 4) != 0) Native.DwmSetWindowAttribute(hw, 19, ref on, 4);
            }
            TermResolution tr = Startup.ResolveTerminal();
            if (tr.StaleLabel != null)
                MessageBox.Show("Your chosen terminal (" + tr.StaleLabel + ") was not found on this machine, so " + Terminals.Label(tr.Id)
                    + " will be used instead.\n\nYou can pick another one in the tray menu under Terminal.", "WSL Tray - terminal not found");
            SyncTerminalUi();
            // dock order = reverse of add order: bar, then termBar, then status, and the list fills the rest
            form.Controls.Add(list); form.Controls.Add(status); form.Controls.Add(termBar); form.Controls.Add(bar);
            list.SelectedIndexChanged += delegate { UpdateButtons(); };
            list.DoubleClick += delegate { OpenTerminal(null, EventArgs.Empty); };
            UpdateButtons();

            form.FormClosing += delegate (object s, FormClosingEventArgs e)
            {
                if (!exiting && e.CloseReason == CloseReason.UserClosing) { e.Cancel = true; form.Hide(); }
            };
            form.VisibleChanged += delegate
            {
                // poll every 5 s ONLY while the window is visible
                windowTimer.Enabled = form.Visible;
                if (form.Visible) RefreshAsync();
            };
            form.ShowRequested = ShowWindow;
            IntPtr force = form.Handle; // create handle (not shown) so BeginInvoke and WndProc work

            windowTimer.Interval = 5000; windowTimer.Tick += delegate { RefreshAsync(); };
            trayTimer.Interval = 15000; trayTimer.Tick += delegate { if (!form.Visible) RefreshAsync(); };

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Items.Add("Open", null, delegate { ShowWindow(); });
            menu.Items.Add("Refresh", null, delegate { RefreshAsync(); });
            miLogin.CheckOnClick = false; miNotify.CheckOnClick = false;
            miLogin.Click += delegate
            {
                try { Startup.Set(!Startup.IsEnabled()); }
                catch (Exception ex) { MessageBox.Show(ex.Message, "WSL Tray"); }
            };
            miNotify.Click += delegate { Startup.SetNotify(!Startup.GetNotify()); };
            menu.Items.Add("-");
            menu.Items.Add(miTerm);
            menu.Items.Add(miLogin); menu.Items.Add(miNotify);
            menu.Items.Add("-");
            menu.Items.Add("Exit", null, delegate { ExitApp(); });
            menu.Opening += delegate { miLogin.Checked = Startup.IsEnabled(); miNotify.Checked = Startup.GetNotify(); SyncTerminalUi(); };
            tray.ContextMenuStrip = menu;
            tray.MouseClick += delegate (object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) ShowWindow(); };
            SetIcon(false);
            tray.Text = "WSL: checking...";
            tray.Visible = true;

            trayTimer.Start();
            RefreshAsync();
            if (showWindow) ShowWindow();
        }

        static bool IsDarkMode()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize"))
                    return k != null && (k.GetValue("AppsUseLightTheme") is int) && (int)k.GetValue("AppsUseLightTheme") == 0;
            }
            catch (Exception) { return false; }
        }

        void AddBtn(Control bar, Button b, string text, EventHandler h)
        {
            b.Text = text; b.AutoSize = true; b.AutoSizeMode = AutoSizeMode.GrowAndShrink;
            b.Padding = new Padding(S(8), S(3), S(8), S(3)); b.Margin = new Padding(S(3));
            b.MinimumSize = new Size(S(75), S(27));
            if (dark)
            {
                b.FlatStyle = FlatStyle.Flat; b.BackColor = Color.FromArgb(58, 58, 58); b.ForeColor = DarkFore;
                b.FlatAppearance.BorderColor = Color.FromArgb(100, 100, 100);
                b.FlatAppearance.MouseOverBackColor = Color.FromArgb(75, 75, 75); b.FlatAppearance.MouseDownBackColor = Color.FromArgb(90, 90, 90);
            }
            else b.FlatStyle = FlatStyle.System;
            b.Click += h; bar.Controls.Add(b);
        }

        void ShowWindow()
        {
            form.Show();
            if (form.WindowState == FormWindowState.Minimized) form.WindowState = FormWindowState.Normal;
            form.Activate();
        }

        void ExitApp()
        {
            exiting = true;
            windowTimer.Stop(); trayTimer.Stop();
            tray.Visible = false; tray.Dispose();
            if (iconHandle != IntPtr.Zero) { Native.DestroyIcon(iconHandle); iconHandle = IntPtr.Zero; }
            form.Dispose();
            ExitThread();
        }

        // ---- icon ----
        void SetIcon(bool active)
        {
            Size sz = SystemInformation.SmallIconSize;
            IntPtr h;
            using (Bitmap bmp = new Bitmap(sz.Width, sz.Height, PixelFormat.Format32bppArgb))
            {
                using (Graphics g = Graphics.FromImage(bmp))
                using (SolidBrush br = new SolidBrush(active ? Color.FromArgb(34, 139, 87) : Color.FromArgb(110, 110, 120)))
                using (Font f = new Font("Segoe UI", sz.Height * 0.6f, FontStyle.Bold, GraphicsUnit.Pixel))
                using (StringFormat sf = new StringFormat())
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                    sf.Alignment = StringAlignment.Center; sf.LineAlignment = StringAlignment.Center;
                    g.FillEllipse(br, 0.5f, 0.5f, sz.Width - 1f, sz.Height - 1f);
                    g.DrawString("W", f, Brushes.White, new RectangleF(0, sz.Height * 0.03f, sz.Width, sz.Height), sf);
                }
                h = bmp.GetHicon();
            }
            Icon ic = Icon.FromHandle(h);
            tray.Icon = ic;
            IntPtr old = iconHandle;
            iconHandle = h; curIcon = ic; iconState = active ? 1 : 0;
            if (old != IntPtr.Zero) Native.DestroyIcon(old);
        }

        // ---- refresh (worker thread, UI marshal) ----
        void RefreshAsync()
        {
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
            Thread t = new Thread(delegate ()
            {
                Snapshot s = null;
                try { s = Wsl.Collect(); } catch (Exception) { }
                Post(delegate { Interlocked.Exchange(ref busy, 0); if (s != null) Apply(s); });
            });
            t.IsBackground = true; t.Start();
        }

        void Post(Action a)
        {
            try { if (!exiting && form.IsHandleCreated) form.BeginInvoke(a); } catch (Exception) { }
        }

        string StateOf(Snapshot s, Distro d)
        {
            if (!s.VmPresent) return "Stopped";
            if (s.Running == null) return "Unknown";
            return s.Running.Contains(d.Name) ? "Running" : "Stopped";
        }

        void Apply(Snapshot s)
        {
            last = s;
            if (form.Visible)
            {
                string sel = SelectedName();
                list.BeginUpdate();
                list.Items.Clear();
                foreach (Distro d in s.Distros)
                {
                    string st = StateOf(s, d);
                    ListViewItem it = new ListViewItem(d.Name);
                    it.SubItems.Add(st); it.SubItems.Add(d.Version.ToString());
                    it.SubItems.Add(d.IsDefault ? "yes" : ""); it.SubItems.Add(Wsl.FormatSize(d.VhdxBytes));
                    it.ForeColor = st == "Running" ? (dark ? Color.FromArgb(90, 210, 130) : Color.FromArgb(20, 120, 60)) : Color.Gray;
                    list.Items.Add(it);
                    if (d.Name == sel) it.Selected = true;
                }
                list.EndUpdate();
                UpdateButtons();
                string mem = s.VmPresent ? string.Format(" | VM {0:0.0} GB", s.VmRamBytes / (double)(1L << 30)) : " | VM not running";
                int nRun = s.Running == null ? 0 : s.Running.Count;
                statusLabel.Text = (s.Running == null ? "state unknown (wsl.exe " + s.WslCall + ")" : nRun + " running") + " / " + s.Distros.Count + " installed" + mem;
            }
            if (s.Running != null)
            {
                bool active = s.Running.Count > 0;
                string tip = "WSL: " + s.Running.Count + " running";
                if (active) tip += ": " + string.Join(", ", s.Running.ToArray());
                if (tip.Length > 63) tip = tip.Substring(0, 60) + "...";
                tray.Text = tip;
                if (iconState != (active ? 1 : 0)) SetIcon(active);
                Notify(s.Running);
            }
        }

        void Notify(List<string> running)
        {
            List<string> sorted = new List<string>(running); sorted.Sort(StringComparer.OrdinalIgnoreCase);
            string key = string.Join(", ", sorted.ToArray());
            if (lastKey == null) { lastKey = key; return; }
            if (key == lastKey) { pendingKey = null; return; }
            if (key == pendingKey) pendingCount++; else { pendingKey = key; pendingCount = 1; }
            if (pendingCount < 2) return; // debounce: change must persist across two polls
            lastKey = key; pendingKey = null;
            if (Startup.GetNotify() && (DateTime.UtcNow - lastBalloon).TotalSeconds > 30)
            {
                lastBalloon = DateTime.UtcNow;
                tray.ShowBalloonTip(2000, "WSL", key.Length > 0 ? "Running: " + key : "No distros running", ToolTipIcon.Info);
            }
        }

        // ---- actions ----
        string SelectedName() { return list.SelectedItems.Count > 0 ? list.SelectedItems[0].Text : null; }

        void UpdateButtons()
        {
            bool has = list.SelectedItems.Count > 0;
            btnTerm.Enabled = btnStop.Enabled = miDefault.Enabled = has;
        }

        // ---- terminal choice: registry-backed, shown in tray submenu and window combo (kept in sync) ----
        void SyncTerminalUi()
        {
            string cur = Startup.GetTerminal();
            List<TermChoice> choices = Terminals.AvailableChoices(Terminals.RealAvail); // re-detected on every call
            syncingTerm = true;
            try
            {
                cmbTerm.Items.Clear();
                int sel = -1;
                for (int i = 0; i < choices.Count; i++) { cmbTerm.Items.Add(choices[i]); if (choices[i].Id == cur) sel = i; }
                cmbTerm.SelectedIndex = sel;
                miTerm.DropDownItems.Clear();
                foreach (TermChoice c in choices)
                {
                    if (c.Id == Terminals.Custom) continue;
                    ToolStripMenuItem mi = new ToolStripMenuItem(c.Label);
                    mi.Checked = c.Id == cur; mi.Tag = c.Id;
                    mi.Click += delegate (object s, EventArgs e) { ChooseTerminal((string)((ToolStripMenuItem)s).Tag); };
                    miTerm.DropDownItems.Add(mi);
                }
                miTerm.DropDownItems.Add("-");
                ToolStripMenuItem cm = new ToolStripMenuItem(Terminals.Label(Terminals.Custom));
                cm.Checked = cur == Terminals.Custom;
                cm.Click += delegate { EditCustomTerminal(); };
                miTerm.DropDownItems.Add(cm);
            }
            finally { syncingTerm = false; }
        }

        void ChooseTerminal(string id)
        {
            if (id == Terminals.Custom && Terminals.ValidateCustom(Startup.GetTerminalCustom()) != null) { EditCustomTerminal(); return; }
            Startup.SetTerminal(id);
            SyncTerminalUi();
        }

        void OnTermCombo(object sender, EventArgs e)
        {
            if (syncingTerm) return;
            TermChoice c = cmbTerm.SelectedItem as TermChoice;
            if (c == null) return;
            BeginInvoke0(delegate { ChooseTerminal(c.Id); });
        }

        // avoid mutating the combo from inside its own SelectedIndexChanged
        void BeginInvoke0(Action a) { try { form.BeginInvoke(a); } catch (Exception) { } }

        void EditCustomTerminal()
        {
            string t = PromptCustom(Startup.GetTerminalCustom());
            if (t != null)
            {
                Startup.SetTerminalCustom(t);
                Startup.SetTerminal(Terminals.Custom);
            }
            SyncTerminalUi();
        }

        // Returns the new template, or null if cancelled. Re-prompts until valid.
        string PromptCustom(string current)
        {
            using (Form f = new Form())
            {
                f.Text = "Custom terminal command";
                f.FormBorderStyle = FormBorderStyle.FixedDialog; f.MinimizeBox = false; f.MaximizeBox = false;
                f.StartPosition = FormStartPosition.CenterScreen; f.ShowInTaskbar = true;
                f.ClientSize = new Size(S(520), S(150));
                Label l = new Label();
                l.Text = "Command line template. Use " + Terminals.Placeholder + " for the distro name (put it in quotes), e.g.\n"
                    + "wt.exe wsl.exe -d \"{name}\"";
                l.SetBounds(S(12), S(10), S(496), S(44));
                TextBox tb = new TextBox(); tb.Text = current ?? ""; tb.SetBounds(S(12), S(60), S(496), S(24));
                Button ok = new Button(); ok.Text = "OK"; ok.DialogResult = DialogResult.OK; ok.SetBounds(S(342), S(105), S(80), S(28));
                Button cancel = new Button(); cancel.Text = "Cancel"; cancel.DialogResult = DialogResult.Cancel; cancel.SetBounds(S(428), S(105), S(80), S(28));
                f.Controls.Add(l); f.Controls.Add(tb); f.Controls.Add(ok); f.Controls.Add(cancel);
                f.AcceptButton = ok; f.CancelButton = cancel;
                f.FormClosing += delegate (object s, FormClosingEventArgs e)
                {
                    if (f.DialogResult != DialogResult.OK) return;
                    string err = Terminals.ValidateCustom(tb.Text);
                    if (err != null) { MessageBox.Show(f, err, "Custom terminal command"); e.Cancel = true; }
                };
                f.Shown += delegate { f.Activate(); tb.Focus(); tb.SelectAll(); };
                return f.ShowDialog() == DialogResult.OK ? tb.Text.Trim() : null;
            }
        }

        void OpenTerminal(object sender, EventArgs e)
        {
            string n = SelectedName(); if (n == null) return;
            try
            {
                LaunchPlan plan = Terminals.Plan(Startup.GetTerminal(), Startup.GetTerminalCustom(), n, Terminals.RealAvail);
                if (plan.Error != null) { MessageBox.Show(form, plan.Error, "Open terminal"); return; }
                if (plan.Warning != null) MessageBox.Show(form, plan.Warning, "Open terminal");
                ProcessStartInfo psi = new ProcessStartInfo(Terminals.LaunchExe(plan.Exe), plan.Args);
                psi.UseShellExecute = false;
                // cmd.exe + 'start' opens its own console window; the helper cmd itself must stay hidden.
                psi.CreateNoWindow = string.Equals(plan.Exe, "cmd.exe", StringComparison.OrdinalIgnoreCase);
                Process.Start(psi);
            }
            catch (Exception ex) { MessageBox.Show(form, ex.Message, "Open terminal"); }
            // starting a distro takes a moment; refresh once shortly after without blocking the UI thread
            System.Windows.Forms.Timer t = new System.Windows.Forms.Timer(); t.Interval = 2500;
            t.Tick += delegate { t.Stop(); t.Dispose(); RefreshAsync(); };
            t.Start();
        }

        void StopDistro(object sender, EventArgs e)
        {
            string n = SelectedName(); if (n == null) return;
            string msg = "Stop distro '" + n + "'? Running processes inside it will be terminated.";
            MessageBoxIcon icon = MessageBoxIcon.Question;
            if (n.StartsWith("docker-desktop", StringComparison.OrdinalIgnoreCase))
            {
                msg = "WARNING: '" + n + "' is the Docker Desktop backend. Stopping it will break Docker Desktop and stop all containers until Docker is restarted.\n\nStop it anyway?";
                icon = MessageBoxIcon.Warning;
            }
            if (MessageBox.Show(form, msg, "Stop distro", MessageBoxButtons.YesNo, icon, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            RunAction("--terminate \"" + n + "\"");
        }

        void SetDefault(object sender, EventArgs e)
        {
            string n = SelectedName(); if (n == null) return;
            if (MessageBox.Show(form, "Make '" + n + "' the default WSL distro?", "Set default", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            RunAction("--set-default \"" + n + "\"");
        }

        void ShutDown(object sender, EventArgs e)
        {
            if (MessageBox.Show(form, "Shut down ALL WSL distros and the WSL VM (including Docker Desktop's)?", "Shut down WSL",
                MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
            RunAction("--shutdown");
        }

        void RunAction(string args)
        {
            statusLabel.Text = "wsl.exe " + args + " ...";
            Thread t = new Thread(delegate ()
            {
                int code; byte[] o; bool to;
                bool ok = Wsl.Run("wsl.exe", args, 20000, out code, out o, out to);
                string err = to ? "timed out" : (!ok || code != 0 ? "failed (exit " + code + ")" : null);
                Post(delegate
                {
                    if (err != null) statusLabel.Text = "wsl.exe " + args + " " + err;
                    // wait for shutdown to settle a little before re-reading, without blocking UI
                    System.Windows.Forms.Timer d = new System.Windows.Forms.Timer(); d.Interval = 1000;
                    d.Tick += delegate { d.Stop(); d.Dispose(); RefreshAsync(); };
                    d.Start();
                });
            });
            t.IsBackground = true; t.Start();
        }
    }
}
