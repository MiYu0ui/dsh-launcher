using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 主界面：莱茵生命档案终端风格。
    /// 暖灰白底、细线分区、宽字距标注；右侧是黑底授权环加载器；运行日志只写文件不进界面。
    /// </summary>
    internal class LauncherForm : Form
    {
        private const int DesignW = 780;
        private const int HeaderH = 106;
        private const int PadX = 30;
        private const int PanelTop = 136;
        private const int PanelH = 190;
        private const int PanelW = 496;
        private const int BtnTop = 356;
        private const int BtnH = 54;
        private const int FootRule = 436;
        private const int DesignH = 492;

        private readonly LauncherContext _ctx;
        private StatusPanel _status;
        private DeployPanel _deploy;
        private bool _deployMode;
        private RingIndicator _ring;
        private FlatButton _btnStart;
        private FlatButton _btnOpen;
        private FlatButton _btnStop;
        private FlatButton _btnUpdate;
        private FlatButton _btnUninstall;
        private FlatButton _btnLogFolder;
        private FlatButton _btnExport;
        private FlatButton _btnCleanup;
        private ChromeButton _btnClose;
        private ChromeButton _btnMin;
        private ChromeButton _btnGear;
        private ChromeButton _btnHelp;
        private bool _gearHover;
        private bool _helpHover;
        private Rectangle _gearLabelRect;
        private Rectangle _helpLabelRect;
        private string _lastLog = "";
        private bool _allowClose;

        public LauncherForm(LauncherContext ctx)
        {
            _ctx = ctx;
            BuildUi();
            HookContext();
            SyncFromServer();
            RefreshUpdateBadge();
            // 只重绘三条会动的窄带（绿边高光、章节线、页脚线），别整窗重画
            Anim.Track(this, 4, new Rectangle[]
            {
                new Rectangle(0, 0, Theme.S(DesignW), Theme.S(6)),
                new Rectangle(Theme.S(PadX), Theme.S(112), Theme.S(DesignW - PadX * 2), Theme.S(18)),
                new Rectangle(Theme.S(PadX), Theme.S(FootRule - 2), Theme.S(DesignW - PadX * 2), Theme.S(6))
            });
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Anim.Untrack(this);
            base.Dispose(disposing);
        }

        private void BuildUi()
        {
            SuspendLayout();
            Text = "DSH 启动器 · ARRAY TERMINAL";
            Icon = Res.AppIcon(32);
            BackColor = Theme.Bg;
            ForeColor = Theme.Ink;
            Font = Theme.FontUi;
            FormBorderStyle = FormBorderStyle.None;   // 去掉系统标题栏：绿色顶边即窗口最上沿
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(DesignW), Theme.S(DesignH));
            DoubleBuffered = true;
            // 允许透明必须在这里（句柄创建前）打开：否则动画期间改 Opacity 会触发句柄重建，
            // 窗口会被销毁重建导致消失。提前打开后，改 Opacity 只是 UpdateLayered。
            AllowTransparency = true;

            _status = new StatusPanel();
            _status.Location = new Point(Theme.S(PadX), Theme.S(PanelTop));
            _status.Size = new Size(Theme.S(PanelW), Theme.S(PanelH));
            _status.UrlClicked += delegate(object s, EventArgs e) { _ctx.OpenUi(); };
            Controls.Add(_status);

            // 部署面板与状态面板共用同一格：未部署时顶掉状态面板
            _deploy = new DeployPanel();
            _deploy.Location = _status.Location;
            _deploy.Size = _status.Size;
            _deploy.Visible = false;
            Controls.Add(_deploy);

            _ring = new RingIndicator();
            _ring.Location = new Point(Theme.S(PadX + PanelW + 16), Theme.S(PanelTop));
            _ring.Size = new Size(Theme.S(DesignW - PadX * 2 - PanelW - 16), Theme.S(PanelH));
            Controls.Add(_ring);

            // 主按钮行：5 个按钮铺满 30..750，间距 14；主操作（启动 DSH）给更宽的 168，
            // 其余等宽 124 —— 原来带 01/02 编号时左侧被占 42px，"卸载…" 会被省略号吃掉。
            _btnStart = MakeButton("启动 DSH", PadX, 168, BtnTop, BtnH, true);
            _btnStart.Click += delegate(object s, EventArgs e)
            {
                if (_deployMode) _ctx.StartDeployment();
                else _ctx.StartServer();
            };

            _btnOpen = MakeButton("打开界面", 212, 124, BtnTop, BtnH, false);
            _btnOpen.Click += delegate(object s, EventArgs e) { _ctx.OpenUi(); };

            _btnStop = MakeButton("停止", 350, 124, BtnTop, BtnH, false);
            _btnStop.Danger = true;
            _btnStop.Click += delegate(object s, EventArgs e) { _ctx.StopServer(true); };

            _btnUpdate = MakeButton("检查更新", 488, 124, BtnTop, BtnH, false);
            _btnUpdate.Click += delegate(object s, EventArgs e) { _ctx.UpdateButtonClicked(); };

            // 设置已搬到右上角齿轮，这里只留卸载
            _btnUninstall = MakeButton("卸载…", 626, 124, BtnTop, BtnH, false);
            _btnUninstall.Click += delegate(object s, EventArgs e) { _ctx.OpenUninstall(this); };

            // 页脚工具按钮：日志 + 残留清扫
            _btnCleanup = MakeButton("清扫残留", PadX + 262, 132, FootRule + 8, 28, false);
            _btnCleanup.Click += delegate(object s, EventArgs e) { _ctx.CleanupResiduals(); };

            _btnLogFolder = MakeButton("打开日志文件夹", PadX + 420, 148, FootRule + 8, 28, false);
            _btnLogFolder.Click += delegate(object s, EventArgs e) { _ctx.OpenLogFolder(); };

            _btnExport = MakeButton("导出诊断日志", PadX + 576, 144, FootRule + 8, 28, false);
            _btnExport.Click += delegate(object s, EventArgs e) { _ctx.ExportDiagnostics(); };

            _status.SetState("STATUS / 运行状态", "未运行", "", Theme.SignalIdle, _ctx.Config.Workspace, "");

            // 自绘窗口按钮（无边框后由它们接管最小化/关闭）
            int chromeW = 26, chromeH = 22, chromeY = 8;
            int closeX = DesignW - PadX - chromeW;
            _btnClose = new ChromeButton(ChromeButton.GlyphKind.Close);
            _btnClose.Location = new Point(Theme.S(closeX), Theme.S(chromeY));
            _btnClose.Size = new Size(Theme.S(chromeW), Theme.S(chromeH));
            _btnClose.Invoked += delegate(object s, EventArgs e) { Close(); };
            Controls.Add(_btnClose);

            _btnMin = new ChromeButton(ChromeButton.GlyphKind.Minimize);
            _btnMin.Location = new Point(Theme.S(closeX - chromeW - 4), Theme.S(chromeY));
            _btnMin.Size = new Size(Theme.S(chromeW), Theme.S(chromeH));
            _btnMin.Invoked += delegate(object s, EventArgs e) { WindowState = FormWindowState.Minimized; };
            Controls.Add(_btnMin);

            // 齿轮 + 「设置」二字：放在右上角品牌锁排的左边。
            // 几何由 BrandCluster 统一算，和 OnPaint 里画标签/署名/标志用的是同一套测量，不会对不齐。
            _btnGear = new ChromeButton(ChromeButton.GlyphKind.Gear);
            using (Graphics gg = CreateGraphics())
            {
                BrandBox bx = BrandCluster(gg);
                _btnGear.Location = new Point(bx.GearLeft, bx.MarkY + (bx.MarkH - bx.GearH) / 2);
                _btnGear.Size = new Size(bx.GearW, bx.GearH);
                _gearLabelRect = new Rectangle(bx.LabelLeft, bx.MarkY, bx.LabelW, bx.MarkH);
            }
            _btnGear.BackColor = Theme.Panel;
            _btnGear.Invoked += delegate(object s, EventArgs e) { _ctx.OpenSettings(this); };
            // 悬停时把「设置」二字一起点亮 —— 标签与图标是一个整体
            _btnGear.MouseEnter += delegate(object s, EventArgs e) { _gearHover = true; Invalidate(); };
            _btnGear.MouseLeave += delegate(object s, EventArgs e) { _gearHover = false; Invalidate(); };
            Controls.Add(_btnGear);

            // 「? 帮助」：摆在「设置」左边，同样的图标 + 文字、同样的"两处都可点"
            _btnHelp = new ChromeButton(ChromeButton.GlyphKind.Help);
            using (Graphics gg = CreateGraphics())
            {
                BrandBox bx = BrandCluster(gg);
                _btnHelp.Location = new Point(bx.HelpGlyphLeft, bx.MarkY + (bx.MarkH - bx.HelpGlyphH) / 2);
                _btnHelp.Size = new Size(bx.HelpGlyphW, bx.HelpGlyphH);
                _helpLabelRect = new Rectangle(bx.HelpLabelLeft, bx.MarkY, bx.HelpLabelW, bx.MarkH);
            }
            _btnHelp.BackColor = Theme.Panel;
            _btnHelp.Invoked += delegate(object s, EventArgs e) { _ctx.OpenHelp(this); };
            _btnHelp.MouseEnter += delegate(object s, EventArgs e) { _helpHover = true; Invalidate(); };
            _btnHelp.MouseLeave += delegate(object s, EventArgs e) { _helpHover = false; Invalidate(); };
            Controls.Add(_btnHelp);

            ResumeLayout(false);
        }

        /// <summary>右上角「? 帮助 | ⚙ 设置 | RHINE · LAB | 莱茵标志」这一串的几何。</summary>
        private class BrandBox
        {
            public int MarkX, MarkY, MarkW, MarkH;
            public int TextLeft;
            public int LabelLeft, LabelW;
            public int GearLeft, GearW, GearH;
            public int HelpLabelLeft, HelpLabelW;
            public int HelpGlyphLeft, HelpGlyphW, HelpGlyphH;
        }

        /// <summary>
        /// 右侧品牌锁排的横向布局（从右往左排）。**绘制与控件定位共用它** ——
        /// 两处各算一套的话，字体或缩放一变就会错位。
        /// </summary>
        private BrandBox BrandCluster(Graphics g)
        {
            BrandBox b = new BrandBox();
            b.MarkH = Theme.S(20);
            b.MarkW = (int)Math.Round(b.MarkH * 2.2);
            b.MarkX = Theme.S(DesignW) - Theme.S(PadX) - b.MarkW;
            b.MarkY = Theme.S(36);

            Size tw = Theme.MeasureTracked(g, "RHINE · LAB", Theme.FontMonoSmall, Theme.SF(1.8f));
            b.TextLeft = b.MarkX - Theme.S(10) - tw.Width;

            // 「设置」二字贴在齿轮右侧；再留 26px 与品牌署名分开，免得两组字挤成一片
            b.LabelW = Theme.MeasureTracked(g, "设置", Theme.FontSmall, Theme.SF(1.2f)).Width;
            b.LabelLeft = b.TextLeft - Theme.S(26) - b.LabelW;

            b.GearW = Theme.S(28);
            b.GearH = Theme.S(24);
            b.GearLeft = b.LabelLeft - Theme.S(8) - b.GearW;

            // 再往左一组「帮助」：同样的 8px 图标-文字间距、26px 组间距，与设置那组等距
            b.HelpLabelW = Theme.MeasureTracked(g, "帮助", Theme.FontSmall, Theme.SF(1.2f)).Width;
            b.HelpLabelLeft = b.GearLeft - Theme.S(26) - b.HelpLabelW;
            b.HelpGlyphW = Theme.S(28);
            b.HelpGlyphH = Theme.S(24);
            b.HelpGlyphLeft = b.HelpLabelLeft - Theme.S(8) - b.HelpGlyphW;
            return b;
        }

        /// <summary>无边框窗口补一个系统投影，免得贴在桌面上"发飘"。</summary>
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
            if (e.Button != MouseButtons.Left) return;
            // 「设置」二字也能点：与齿轮同一个出口
            if (!_gearLabelRect.IsEmpty && _gearLabelRect.Contains(e.Location))
            {
                _ctx.OpenSettings(this);
                return;
            }
            // 「帮助」二字同理
            if (!_helpLabelRect.IsEmpty && _helpLabelRect.Contains(e.Location))
            {
                _ctx.OpenHelp(this);
                return;
            }
            // 标题栏区域按住即可拖动窗口
            if (e.Y <= Theme.S(HeaderH)) WindowChrome.BeginDrag(this);
        }

        private FlatButton MakeButton(string text, int x, int w, int y, int h, bool primary)
        {
            FlatButton b = new FlatButton();
            b.Text = text;
            b.Primary = primary;
            b.Location = new Point(Theme.S(x), Theme.S(y));
            b.Size = new Size(Theme.S(w), Theme.S(h));
            Controls.Add(b);
            return b;
        }

        private void HookContext()
        {
            _ctx.Server.StatusChanged += delegate(object s, EventArgs e) { SyncFromServer(); };
            _ctx.LogProduced += delegate(object s, LogEventArgs e)
            {
                _lastLog = e.Entry.Text;
                if (_ctx.Server.Status == ServerStatus.Starting) SyncFromServer();
            };
        }

        /// <summary>更新按钮与版本标注随检查结果变化。</summary>
        public void RefreshUpdateBadge()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new MethodInvoker(RefreshUpdateBadge)); }
                catch { }
                return;
            }
            UpdateInfo up = _ctx.Update;
            bool has = up != null && up.Available;
            if (_btnUpdate != null)
            {
                _btnUpdate.Text = has ? "更新 " + up.LatestVersion : "检查更新";
                _btnUpdate.Accent = has;
            }
            Invalidate();
        }

        /// <summary>进入部署模式：状态面板让位给部署清单。</summary>
        public void EnterDeployMode()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new MethodInvoker(EnterDeployMode)); }
                catch { }
                return;
            }
            _deployMode = true;
            _status.Visible = false;
            _deploy.Visible = true;
            SyncDeploy();
            Invalidate();
        }

        /// <summary>部署完成：恢复普通界面。</summary>
        public void ExitDeployMode()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new MethodInvoker(ExitDeployMode)); }
                catch { }
                return;
            }
            _deployMode = false;
            _deploy.Visible = false;
            _status.Visible = true;
            _btnStart.Text = "启动 DSH";
            SyncFromServer();
            Invalidate();
        }

        /// <summary>部署进度刷新（部署器每次变更都会调）。</summary>
        public void SyncDeploy()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new MethodInvoker(SyncDeploy)); }
                catch { }
                return;
            }
            if (!_deployMode) return;

            Deployer d = _ctx.Deployer;
            DeployReport rep = _ctx.EnvReport;
            bool running = d != null && d.Running;
            bool done = d != null && d.Finished && d.Succeeded;
            double prog = d != null ? d.Progress : 0;

            string head;
            Color signal;
            if (running) { head = "正在自动部署"; signal = Theme.SignalBusy; }
            else if (done) { head = "部署完成"; signal = Theme.Green; }
            else if (d != null && d.Finished) { head = "部署未完成"; signal = Theme.SignalAlert; }
            else if (rep != null && rep.NeedsDeploy) { head = rep.Headline; signal = Theme.SignalAlert; }
            else { head = "环境就绪"; signal = Theme.Green; }

            string summary = running && d.CurrentDetail.Length > 0
                ? d.CurrentDetail
                : (rep != null ? rep.Summary : "");
            _deploy.SetState("DEPLOY / 自动部署", head, summary, signal,
                             d != null ? d.Steps : null, running, prog);

            _btnStart.Enabled = !running;
            _btnStart.Text = running ? "部署中…" : (done ? "启动 DSH" : "一键部署");
            _btnOpen.Enabled = !running && !_deployMode;
            _btnStop.Enabled = false;
            _ring.SetStatus(running ? ServerStatus.Starting : ServerStatus.Stopped);
        }

        private void SyncFromServer()
        {
            if (IsDisposed) return;
            if (InvokeRequired)
            {
                try { BeginInvoke(new MethodInvoker(SyncFromServer)); }
                catch { }
                return;
            }
            DshServer server = _ctx.Server;
            ServerStatus st = server.Status;
            if (_deployMode) return;      // 部署模式下由 SyncDeploy 接管按钮与状态面板
            string head, sub, track;
            Color signal;

            switch (st)
            {
                case ServerStatus.Starting:
                    track = "STATUS / 正在接入";
                    head = "正在启动";
                    sub = _lastLog.Length > 0 ? _lastLog : "首次启动可能需要下载依赖，请稍候。";
                    signal = Theme.SignalBusy; break;
                case ServerStatus.Running:
                    track = "STATUS / 运行状态";
                    head = "运行中";
                    sub = "服务已在后台运行，浏览器中即可使用。";
                    signal = Theme.SignalRun; break;
                case ServerStatus.External:
                    track = "STATUS / 运行状态";
                    head = "运行中 · 外部启动";
                    sub = "检测到已有 DSH 服务，直接接管显示。";
                    signal = Theme.SignalRun; break;
                case ServerStatus.Stopping:
                    track = "STATUS / 正在断开";
                    head = "正在停止";
                    sub = "正在结束后台进程。";
                    signal = Theme.SignalBusy; break;
                case ServerStatus.PortConflict:
                    track = "STATUS / 异常";
                    head = "端口被占用";
                    sub = server.LastError;
                    signal = Theme.SignalAlert; break;
                case ServerStatus.Failed:
                    track = "STATUS / 异常";
                    head = "启动失败";
                    sub = server.LastError;
                    signal = Theme.SignalAlert; break;
                default:
                    track = "STATUS / 待机";
                    head = "未运行";
                    sub = "点“启动 DSH”即可，全程不会弹出黑色命令行窗口。";
                    signal = Theme.SignalIdle; break;
            }

            string url = (st == ServerStatus.Running || st == ServerStatus.External) ? server.WebUrl : "";
            _status.SetState(track, head, sub, signal, _ctx.Config.Workspace, url);
            _ring.SetStatus(st);

            bool busy = st == ServerStatus.Starting || st == ServerStatus.Stopping;
            bool running = st == ServerStatus.Running || st == ServerStatus.External;
            _btnStart.Enabled = !busy && !running;
            _btnOpen.Enabled = running;
            _btnStop.Enabled = !busy && (st == ServerStatus.Running || st == ServerStatus.External);
            _btnUpdate.Enabled = !busy;
            _btnUninstall.Enabled = !busy;
            if (_btnGear != null) _btnGear.Enabled = !busy;
            if (_btnHelp != null) _btnHelp.Enabled = !busy;
        }

        public void AllowClose() { _allowClose = true; }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!_allowClose && e.CloseReason == CloseReason.UserClosing)
            {
                if (_ctx.Config.CloseToTray)
                {
                    e.Cancel = true;
                    Hide();
                    _ctx.NotifyHiddenToTray();
                    return;
                }
                e.Cancel = true;
                _ctx.ExitApp();
                return;
            }
            base.OnFormClosing(e);
        }

        // ---- 自绘：档案终端版式 ----
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            int w = ClientSize.Width;
            int h = Theme.S(HeaderH);
            int right = w - Theme.S(PadX);
            int band = Theme.S(3);          // 绿色上边框厚度

            Theme.Fill(g, ClientRectangle, Theme.Bg);

            // 绿色上边框（贯通整窗，作为唯一的强色锚点）+ 沿它巡行的高光
            Theme.Fill(g, new Rectangle(0, 0, w, band), Theme.Green);
            int hlW = Math.Max(Theme.S(90), (int)(w * 0.14));
            int hlX = (int)(Anim.Cycle(4.6, 0.0) * (w + hlW * 2)) - hlW;
            for (int i = 0; i < 10; i++)
            {
                int a0 = hlX - hlW + hlW * i / 10;
                int a1 = hlX - hlW + hlW * (i + 1) / 10;
                int alpha = (int)(150.0 * (i / 9.0) * (i / 9.0));
                if (alpha < 8) continue;
                int cx0 = Math.Max(0, a0), cx1 = Math.Min(w, a1);
                if (cx1 <= cx0) continue;
                Theme.Fill(g, new Rectangle(cx0, 0, cx1 - cx0, band), Color.FromArgb(alpha, Theme.GreenSoft));
            }
            Theme.Fill(g, new Rectangle(0, band, w, h - band), Theme.Panel);
            Theme.Rule(g, 0, h - 1, w, Theme.Line);

            // 细刻度带：与内容左边距对齐的长短竖线
            int step = Theme.S(22);
            int tickLeft = Theme.S(PadX);
            for (int x = tickLeft, i = 0; x < right; x += step, i++)
            {
                bool tall = (i % 5) == 0;
                Theme.VRule(g, x, h - 1 - Theme.S(tall ? 8 : 4), h - 1, tall ? Theme.Line : Theme.LineSoft);
            }

            // 左侧：本应用标识
            Image logo = Res.Logo();
            int logoBox = Theme.S(46);
            if (logo != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(logo, new Rectangle(Theme.S(PadX), band + Theme.S(27), logoBox, logoBox));
            }
            int textX = Theme.S(PadX) + logoBox + Theme.S(16);
            Theme.DrawTracked(g, "DSH LAUNCHER", Theme.FontWord, textX, band + Theme.S(25), Theme.Ink, Theme.SF(2.6f));
            Theme.DrawTracked(g, "深度求索 · 档案接入终端", Theme.FontSmall, textX + Theme.S(1), band + Theme.S(57), Theme.Sub, Theme.SF(1.2f));

            // 右侧：「设置」齿轮 + 二字提示 | RHINE · LAB | 莱茵生命标志
            Image mark = Res.Mark();
            BrandBox bb = BrandCluster(g);
            if (mark != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(mark, new Rectangle(bb.MarkX, bb.MarkY, bb.MarkW, bb.MarkH));
            }
            Theme.DrawTrackedRight(g, "RHINE · LAB", Theme.FontMonoSmall,
                bb.MarkX - Theme.S(10), bb.MarkY + Theme.S(3), Theme.Ink, Theme.SF(1.8f));
            // 齿轮旁边的「设置」二字：让人一眼知道那里是设置（悬停时与图标一起转暖褐）
            Theme.DrawTracked(g, "设置", Theme.FontSmall,
                bb.LabelLeft, bb.MarkY + Theme.S(3), _gearHover ? Theme.Amber : Theme.Sub, Theme.SF(1.2f));
            // 再往左是「帮助」：与设置同一套"图标 + 文字"，两个字都可点
            Theme.DrawTracked(g, "帮助", Theme.FontSmall,
                bb.HelpLabelLeft, bb.MarkY + Theme.S(3), _helpHover ? Theme.Amber : Theme.Sub, Theme.SF(1.2f));

            UpdateInfo up = _ctx.Update;
            bool hasUpdate = up != null && up.Available;
            string ver = "v" + BuildInfo.Version + (hasUpdate ? "  ›  v" + up.LatestVersion : "") + " · ARRAY / ACCESS";
            Theme.DrawTrackedRight(g, ver, Theme.FontMonoSmall,
                right, Theme.S(60), hasUpdate ? Theme.Amber : Theme.Sub, Theme.SF(1.4f));

            // 章节分隔：标签 + 其后延伸的细线（不与标题栏刻度带同处一带，避免叠字）
            string section = "UNIT 001 · LAUNCH CONTROL";
            float tracking = Theme.SF(1.6f);
            int sectionTop = Theme.S(112);
            Theme.DrawTracked(g, section, Theme.FontMonoSmall, Theme.S(PadX), sectionTop, Theme.Sub, tracking);
            Size sectionSize = Theme.MeasureTracked(g, section, Theme.FontMonoSmall, tracking);
            int sectionRuleX = Theme.S(PadX) + sectionSize.Width + Theme.S(16);
            if (sectionRuleX < right)
                Theme.SweepRule(g, sectionRuleX, right, sectionTop + sectionSize.Height / 2, Theme.Line, Theme.Green, 7.2, 0.25);

            // 页脚：绿色小记号呼应上边框
            Theme.SweepRule(g, Theme.S(PadX), right, Theme.S(FootRule), Theme.LineSoft, Theme.Amber, 9.0, 0.6);
            Theme.Fill(g, new Rectangle(Theme.S(PadX), Theme.S(FootRule + 17), Theme.S(6), Theme.S(6)), Theme.Green);
            string sess = "SESSION · " + _ctx.Config.Workspace;
            Theme.DrawTracked(g, sess, Theme.FontMonoSmall, Theme.S(PadX + 14), Theme.S(FootRule + 12), Theme.Sub, Theme.SF(1.0f));

            base.OnPaint(e);
        }
    }
}
