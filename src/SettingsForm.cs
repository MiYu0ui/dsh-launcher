using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>设置窗口：与主界面同一套档案终端风格。</summary>
    internal class SettingsForm : Form
    {
        private const int DesignW = 664;
        private const int DesignH = 712;
        private const int PadX = 34;
        private const int HeaderH = 76;

        private readonly AppConfig _cfg;
        private readonly DshServer _server;

        private TextBox _dir;
        private NumericUpDown _port;
        private RadioButton _modeNpx;
        private RadioButton _modeDirect;
        private CheckBox _verify;
        private TextBox _version;
        private ComboBox _registry;
        private CheckBox _autoOpen;
        private CheckBox _edge;
        private CheckBox _tray;
        private CheckBox _autostart;
        private CheckBox _boot;
        private CheckBox _transition;
        private Label _shortcutHint;

        public SettingsForm(AppConfig cfg, DshServer server)
        {
            _cfg = cfg;
            _server = server;
            BuildUi();
            LoadValues();
        }

        private void BuildUi()
        {
            SuspendLayout();
            Text = "设置";
            Icon = Res.AppIcon(32);
            Font = Theme.FontUi;
            ForeColor = Theme.Ink;
            BackColor = Theme.Bg;
            FormBorderStyle = FormBorderStyle.None;   // 与主界面一致的档案终端卡片式窗口
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(DesignW), Theme.S(DesignH));
            DoubleBuffered = true;

            int y = 96;

            AddLabel("工作目录（DSH 以该目录作为工作区）", PadX, y);
            y += 22;
            _dir = new TextBox();
            _dir.Location = new Point(Theme.S(PadX), Theme.S(y));
            _dir.Size = new Size(Theme.S(438), Theme.S(28));
            _dir.BorderStyle = BorderStyle.FixedSingle;
            _dir.BackColor = Theme.PanelHi;
            _dir.ForeColor = Theme.Ink;
            _dir.Font = Theme.FontMono;
            Controls.Add(_dir);

            FlatButton browse = new FlatButton();
            browse.Text = "浏览…";
            browse.Location = new Point(Theme.S(PadX + 450), Theme.S(y - 5));
            browse.Size = new Size(Theme.S(140), Theme.S(38));
            browse.BackColor = Theme.Bg;
            browse.Click += delegate(object s, EventArgs e) { BrowseFolder(); };
            Controls.Add(browse);

            y += 54;
            AddLabel("端口", PadX, y);
            y += 22;
            _port = new NumericUpDown();
            _port.Minimum = 1;
            _port.Maximum = 65535;
            _port.Location = new Point(Theme.S(PadX), Theme.S(y));
            _port.Size = new Size(Theme.S(120), Theme.S(28));
            _port.BorderStyle = BorderStyle.FixedSingle;
            _port.BackColor = Theme.PanelHi;
            _port.ForeColor = Theme.Ink;
            _port.Font = Theme.FontMono;
            Controls.Add(_port);
            AddHint("默认 3080；被占用时可改成 3081 等", PadX + 140, y + 5);

            y += 52;
            AddLabel("启动方式", PadX, y);
            y += 24;
            _modeNpx = AddRadio("npm 方式：每次经 npm 检查版本（可核验下载源完整性，推荐）", PadX + 6, y);
            y += 28;
            _modeDirect = AddRadio("极速模式：直接启动本地已缓存的 DSH（最快、离线可用）", PadX + 6, y);

            y += 40;
            _verify = AddCheck("启动前核验下载源完整性（防止被投毒，需要联网）", PadX + 6, y);
            y += 36;
            AddLabel("固定版本", PadX + 26, y + 7);
            _version = new TextBox();
            _version.Location = new Point(Theme.S(PadX + 96), Theme.S(y));
            _version.Size = new Size(Theme.S(140), Theme.S(26));
            _version.BorderStyle = BorderStyle.FixedSingle;
            _version.BackColor = Theme.PanelHi;
            _version.ForeColor = Theme.Ink;
            _version.Font = Theme.FontMono;
            Controls.Add(_version);
            AddLabel("首选源", PadX + 254, y + 7);
            _registry = new ComboBox();
            _registry.DropDownStyle = ComboBoxStyle.DropDownList;
            _registry.Location = new Point(Theme.S(PadX + 314), Theme.S(y));
            _registry.Size = new Size(Theme.S(276), Theme.S(26));
            _registry.BackColor = Theme.PanelHi;
            _registry.ForeColor = Theme.Ink;
            _registry.Font = Theme.FontMono;
            _registry.Items.Add("https://registry.npmmirror.com");
            _registry.Items.Add("https://registry.npmjs.org");
            Controls.Add(_registry);

            y += 46;
            _autoOpen = AddCheck("服务就绪后自动打开界面（浏览器）", PadX + 6, y);
            y += 28;
            _edge = AddCheck("用 Edge 应用窗口打开（没有地址栏，像独立软件）", PadX + 6, y);
            y += 28;
            _tray = AddCheck("关闭窗口时最小化到托盘（服务继续在后台运行）", PadX + 6, y);
            y += 28;
            _autostart = AddCheck("开机自动启动（后台静默，不弹出窗口）", PadX + 6, y);
            y += 28;
            _boot = AddCheck("打开界面时播放开启动画（螺旋 DNA 授权环，点击可跳过）", PadX + 6, y);
            y += 28;
            _transition = AddCheck("服务启动成功后播放接入过渡动画，再打开 Web 界面", PadX + 6, y);

            y += 46;
            _shortcutHint = new Label();
            _shortcutHint.Text = "创建 / 修复桌面与开始菜单快捷方式 →";
            _shortcutHint.Font = Theme.FontSmall;
            _shortcutHint.ForeColor = Theme.Amber;
            _shortcutHint.BackColor = Theme.Bg;
            _shortcutHint.AutoSize = false;
            _shortcutHint.TextAlign = ContentAlignment.MiddleLeft;
            _shortcutHint.Cursor = Cursors.Hand;
            _shortcutHint.Location = new Point(Theme.S(PadX + 6), Theme.S(y));
            _shortcutHint.Size = new Size(Theme.S(330), Theme.S(24));
            _shortcutHint.Click += delegate(object s, EventArgs e) { RepairShortcuts(); };
            Controls.Add(_shortcutHint);

            int btnY = DesignH - 66;
            FlatButton save = new FlatButton();
            save.Text = "保存";
            save.Primary = true;
            save.Location = new Point(Theme.S(DesignW - PadX - 120), Theme.S(btnY));
            save.Size = new Size(Theme.S(120), Theme.S(44));
            save.BackColor = Theme.Bg;
            save.Click += delegate(object s, EventArgs e) { Save(); };
            Controls.Add(save);

            FlatButton cancel = new FlatButton();
            cancel.Text = "取消";
            cancel.Location = new Point(Theme.S(DesignW - PadX - 236), Theme.S(btnY));
            cancel.Size = new Size(Theme.S(104), Theme.S(44));
            cancel.BackColor = Theme.Bg;
            cancel.Click += delegate(object s, EventArgs e) { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            FlatButton reset = new FlatButton();
            reset.Text = "恢复默认";
            reset.Location = new Point(Theme.S(PadX), Theme.S(btnY));
            reset.Size = new Size(Theme.S(120), Theme.S(44));
            reset.BackColor = Theme.Bg;
            reset.Click += delegate(object s, EventArgs e) { ResetDefaults(); };
            Controls.Add(reset);

            ResumeLayout(false);

            // 无边框窗口的关闭按钮（右上角细线 ✕）
            ChromeButton close = new ChromeButton(ChromeButton.GlyphKind.Close);
            close.Location = new Point(Theme.S(DesignW - PadX - 26), Theme.S(8));
            close.Size = new Size(Theme.S(26), Theme.S(22));
            close.BackColor = Theme.Panel;
            close.Invoked += delegate(object s, EventArgs e) { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(close);
            close.BringToFront();
        }

        /// <summary>无边框窗口补一个系统投影。</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;   // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y <= Theme.S(HeaderH)) WindowChrome.BeginDrag(this);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void AddLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.FontUiBold;
            l.ForeColor = Theme.InkSoft;
            l.BackColor = Theme.Bg;
            l.AutoSize = true;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Location = new Point(Theme.S(x), Theme.S(y));
            Controls.Add(l);
        }

        private void AddHint(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.FontSmall;
            l.ForeColor = Theme.Sub;
            l.BackColor = Theme.Bg;
            l.AutoSize = true;
            l.TextAlign = ContentAlignment.MiddleLeft;
            l.Location = new Point(Theme.S(x), Theme.S(y));
            Controls.Add(l);
        }

        private RadioButton AddRadio(string text, int x, int y)
        {
            RadioButton r = new RadioButton();
            r.Text = text;
            r.Font = Theme.FontUi;
            r.ForeColor = Theme.Ink;
            r.BackColor = Theme.Bg;
            r.UseVisualStyleBackColor = false;
            r.FlatStyle = FlatStyle.Standard;
            r.AutoSize = false;
            r.Location = new Point(Theme.S(x), Theme.S(y));
            r.Size = new Size(Theme.S(590), Theme.S(24));
            Controls.Add(r);
            return r;
        }

        private CheckBox AddCheck(string text, int x, int y)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Font = Theme.FontUi;
            c.ForeColor = Theme.Ink;
            c.BackColor = Theme.Bg;
            c.UseVisualStyleBackColor = false;
            c.FlatStyle = FlatStyle.Standard;
            c.AutoSize = false;
            c.Location = new Point(Theme.S(x), Theme.S(y));
            c.Size = new Size(Theme.S(590), Theme.S(24));
            Controls.Add(c);
            return c;
        }

        private void LoadValues()
        {
            _dir.Text = _cfg.Workspace;
            _port.Value = Math.Min(65535, Math.Max(1, _cfg.Port));
            _modeNpx.Checked = _cfg.LaunchMode != "direct";
            _modeDirect.Checked = _cfg.LaunchMode == "direct";
            _verify.Checked = _cfg.VerifyIntegrity;
            _version.Text = _cfg.PinnedVersion;
            int idx = _registry.Items.IndexOf(_cfg.Registry);
            _registry.SelectedIndex = idx >= 0 ? idx : 0;
            _autoOpen.Checked = _cfg.AutoOpenBrowser;
            _edge.Checked = _cfg.EdgeAppMode;
            _tray.Checked = _cfg.CloseToTray;
            _autostart.Checked = _cfg.AutoStart;
            _boot.Checked = _cfg.BootAnimation;
            _transition.Checked = _cfg.TransitionAnimation;
        }

        private void ResetDefaults()
        {
            AppConfig def = new AppConfig();
            _port.Value = def.Port;
            _modeNpx.Checked = true;
            _modeDirect.Checked = false;
            _verify.Checked = def.VerifyIntegrity;
            _version.Text = def.PinnedVersion;
            _registry.SelectedIndex = 0;
            _autoOpen.Checked = def.AutoOpenBrowser;
            _edge.Checked = false;
            _tray.Checked = def.CloseToTray;
            _autostart.Checked = false;
            _boot.Checked = def.BootAnimation;
            _transition.Checked = def.TransitionAnimation;
        }

        private void BrowseFolder()
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择 DSH 的工作目录";
                dlg.ShowNewFolderButton = true;
                try { if (Directory.Exists(_dir.Text)) dlg.SelectedPath = _dir.Text; } catch { }
                if (dlg.ShowDialog(this) == DialogResult.OK) _dir.Text = dlg.SelectedPath;
            }
        }

        private void Save()
        {
            string dir = _dir.Text.Trim().Trim('"');
            if (!Directory.Exists(dir))
            {
                ConfirmDialog.Info(this, "CONFIG / 目录无效", "工作目录不存在", dir, null,
                    "请选择一个已存在的目录，或先创建它。");
                return;
            }
            bool portChanged = ((int)_port.Value) != _cfg.Port;
            if (portChanged && _server != null && _server.Owned &&
                (_server.Status == ServerStatus.Running || _server.Status == ServerStatus.Starting))
            {
                ConfirmDialog.Info(this, "CONFIG / 提示", "端口改动将在下次启动时生效",
                    "服务当前正在运行，新的端口会在下次启动服务时使用。",
                    new string[] { "新端口=" + ((int)_port.Value) }, null);
            }

            _cfg.Workspace = dir;
            _cfg.Port = (int)_port.Value;
            _cfg.LaunchMode = _modeDirect.Checked ? "direct" : "npx";
            _cfg.VerifyIntegrity = _verify.Checked;
            _cfg.PinnedVersion = _version.Text.Trim();
            _cfg.Registry = _registry.SelectedItem == null ? _cfg.Registry : _registry.SelectedItem.ToString();
            _cfg.AutoOpenBrowser = _autoOpen.Checked;
            _cfg.EdgeAppMode = _edge.Checked;
            _cfg.CloseToTray = _tray.Checked;
            _cfg.BootAnimation = _boot.Checked;
            _cfg.TransitionAnimation = _transition.Checked;

            bool wantAutostart = _autostart.Checked;
            if (wantAutostart != _cfg.AutoStart || wantAutostart) ApplyAutoStart(wantAutostart);
            _cfg.AutoStart = wantAutostart;

            DialogResult = DialogResult.OK;
            Close();
        }

        private void ApplyAutoStart(bool enable)
        {
            try
            {
                if (enable)
                {
                    string legacy = Shortcuts.DisableLegacyAutoStart();
                    bool ok = Shortcuts.Create(Shortcuts.AutoStartLinkPath, AppPaths.ExePath, "--autostart",
                                               AppPaths.InstallDir, AppPaths.ExePath, 7);
                    if (!ok)
                        ConfirmDialog.Info(this, "CONFIG / 自启失败", "写入开机自启快捷方式失败",
                            "无法在启动文件夹里创建快捷方式。",
                            new string[] { "目标=" + Shortcuts.AutoStartLinkPath },
                            "请检查启动目录的写入权限。");
                    else if (legacy != null)
                        ConfirmDialog.Info(this, "CONFIG / 已切换自启方式", "已停用旧的 PowerShell 自启项",
                            "开机将由本启动器静默启动服务，不再出现黑窗口。",
                            new string[] { "旧自启项备份=" + Path.GetFileName(legacy) }, null);
                }
                else
                {
                    Shortcuts.Delete(Shortcuts.AutoStartLinkPath);
                }
            }
            catch { }
        }

        private void RepairShortcuts()
        {
            string desktop = Path.Combine(Shortcuts.DesktopDir, "DSH 启动器.lnk");
            string startMenu = Path.Combine(Shortcuts.ProgramsDir, "DSH 启动器.lnk");
            bool a = Shortcuts.Create(desktop, AppPaths.ExePath, "", AppPaths.InstallDir, AppPaths.ExePath, 1);
            bool b = Shortcuts.Create(startMenu, AppPaths.ExePath, "", AppPaths.InstallDir, AppPaths.ExePath, 1);
            ConfirmDialog.Info(this, "SHORTCUT / 快捷方式", "快捷方式已处理",
                (a && b) ? "桌面与开始菜单快捷方式均已就绪。" : "部分快捷方式创建失败，详见下方路径。",
                new string[]
                {
                    "桌面=" + desktop,
                    "开始菜单=" + startMenu
                },
                (a && b) ? null : "请检查目标目录的写入权限。");
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            int w = ClientSize.Width;
            int h = Theme.S(HeaderH);
            int band = Theme.S(3);
            int right = w - Theme.S(PadX);

            Theme.Fill(g, ClientRectangle, Theme.Bg);
            Theme.Fill(g, new Rectangle(0, 0, w, band), Theme.Green);
            Theme.Fill(g, new Rectangle(0, band, w, h - band), Theme.Panel);
            Theme.Rule(g, 0, h - 1, w, Theme.Line);

            int step = Theme.S(22);
            int tickLeft = Theme.S(PadX);
            for (int x = tickLeft, i = 0; x < right; x += step, i++)
            {
                bool tall = (i % 5) == 0;
                Theme.VRule(g, x, h - 1 - Theme.S(tall ? 8 : 4), h - 1, tall ? Theme.Line : Theme.LineSoft);
            }

            Image logo = Res.Logo();
            int box = Theme.S(32);
            if (logo != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(logo, new Rectangle(Theme.S(PadX), band + Theme.S(20), box, box));
            }
            Theme.DrawTracked(g, "SETTINGS", Theme.FontTechBold, Theme.S(PadX + 44), band + Theme.S(23), Theme.Ink, Theme.SF(2.2f));
            Theme.DrawTracked(g, "配置", Theme.FontSmall, Theme.S(PadX + 46), band + Theme.S(42), Theme.Sub, Theme.SF(1.2f));

            Image mark = Res.Mark();
            int markH = Theme.S(18);
            int markW = (int)Math.Round(markH * 2.2);
            if (mark != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(mark, new Rectangle(right - markW, Theme.S(34), markW, markH));
            }
            Theme.DrawTrackedRight(g, "RHINE · LAB", Theme.FontMonoSmall,
                right - markW - Theme.S(10), Theme.S(37), Theme.Sub, Theme.SF(1.6f));

            base.OnPaint(e);
        }
    }
}
