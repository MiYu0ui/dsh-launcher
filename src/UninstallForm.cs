using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Threading;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 卸载明细列表：与 DeployPanel 同一套画法（细线框 + 巡行彗尾 + 四角刻度 + 状态标记）。
    /// 三组：将删除 / 将保留 / 不会动。
    /// </summary>
    internal class UninstallList : Panel
    {
        private List<UninstallTarget> _rows = new List<UninstallTarget>();
        private string _empty = "正在清点…";

        public UninstallList()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Anim.Track(this, 1);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Anim.Untrack(this);
            base.Dispose(disposing);
        }

        public void SetRows(List<UninstallTarget> rows, string emptyText)
        {
            _rows = rows == null ? new List<UninstallTarget>() : rows;
            _empty = emptyText ?? "";
            Invalidate();
        }

        // ---------------- 三种形态：清单 → 过渡 → 加载 ----------------

        /// <summary>过渡时长（秒）。清单形态 → 加载形态的"变形"总耗时。</summary>
        public const double MorphSeconds = 0.72;

        private bool _running;
        private double _morphAt;
        private string _stage = "UNINSTALLING";
        private UninstallProgress _prog;
        private DateTime _startedAt = DateTime.MinValue;

        /// <summary>开始执行：从清单形态过渡到加载形态（同一块区域"变成"，不是换一个窗口）。</summary>
        public void BeginRun(string stage)
        {
            _stage = string.IsNullOrEmpty(stage) ? "UNINSTALLING" : stage;
            _running = true;
            _morphAt = Anim.Now;
            _startedAt = DateTime.Now;
            _prog = null;
            Anim.Track(this, 1);
            Invalidate();
        }

        /// <summary>进度更新（**必须在 UI 线程调用**；引擎在工作线程上跑）。</summary>
        public void UpdateProgress(UninstallProgress p) { _prog = p; }

        /// <summary>结束执行，回到清单形态。</summary>
        public void EndRun() { _running = false; _prog = null; Invalidate(); }

        /// <summary>过渡还差多少秒走完（0 = 已经在常驻加载形态）。</summary>
        public double MorphLeft()
        {
            if (!_running) return 0.0;
            return Math.Max(0.0, MorphSeconds - (Anim.Now - _morphAt));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            Theme.Fill(g, ClientRectangle, BackColor);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.Fill(g, r, Theme.PanelHi);

            if (!_running)
            {
                Theme.DrawLiveFrame(g, r, Theme.Line, Theme.Amber, true, 5.6, 0.35);
                DrawRows(g, r, 0.0);
                base.OnPaint(e);
                return;
            }

            // 执行期间：细线框转成暖褐，巡行彗尾仍在跑（同一个 Anim 时间轴）
            Theme.DrawLiveFrame(g, r, Theme.Line, Theme.Amber, true, 3.4, 0.0);

            double p = Math.Min(1.0, Math.Max(0.0, (Anim.Now - _morphAt) / MorphSeconds));
            if (p >= 1.0)
            {
                // 过渡结束，之后每帧都只画加载动画
                _morphAt = Anim.Now - MorphSeconds;
                DrawLoader(g, r, 1.0, 1.0);
                base.OnPaint(e);
                return;
            }

            // 清单向上微移并淡出；加载动画在同一矩形里淡入 —— 接缝处再补一道横扫高光
            double outP = Theme.EaseInCubic(Math.Min(1.0, p / 0.62));
            double inP = Theme.EaseOutCubic(Math.Max(0.0, (p - 0.42) / 0.58));
            double textP = Theme.EaseOutCubic(Math.Max(0.0, (p - 0.62) / 0.38));
            DrawRows(g, r, outP);
            if (outP > 0.01)
                Theme.Fill(g, Rectangle.Inflate(r, -1, -1), Color.FromArgb((int)(255 * outP), Theme.PanelHi));
            if (inP > 0.01) DrawLoader(g, r, inP, textP);
            DrawMorphSweep(g, r, p);

            base.OnPaint(e);
        }

        /// <summary>过渡的接缝：一道彗尾横扫面板（与按钮/细线框同一手法）。</summary>
        private void DrawMorphSweep(Graphics g, Rectangle r, double p)
        {
            int bandW = Math.Max(Theme.S(40), r.Width / 4);
            int x0 = r.X - bandW + (int)(p * (r.Width + bandW * 2));
            const int slices = 12;
            for (int i = 0; i < slices; i++)
            {
                int a0 = x0 + bandW * i / slices;
                int a1 = x0 + bandW * (i + 1) / slices;
                double t = (i + 0.5) / slices;
                int alpha = (int)(72.0 * Math.Sin(Math.PI * t));
                if (alpha <= 2) continue;
                int cx0 = Math.Max(r.X + 1, a0), cx1 = Math.Min(r.Right - 1, a1);
                if (cx1 <= cx0) continue;
                Theme.Fill(g, new Rectangle(cx0, r.Y + 1, cx1 - cx0, r.Height - 2), Color.FromArgb(alpha, Theme.AmberHi));
            }
        }

        private static Color C(Color c, double a)
        {
            return Color.FromArgb((int)(255 * Math.Min(1.0, Math.Max(0.0, a))), c);
        }


        /// <summary>
        /// 加载动画：授权环那套语言的小尺寸版 —— 细环 + 按项数走的进度弧 +
        /// 两条反向旋转的内弧 + 六颗依次点亮的琥珀节点 + 会呼吸的中心点。
        /// 进度分母是**真实项数**，不编造百分比。
        /// appear = 环的淡入；textP = 文字的淡入（TextRenderer 不吃 alpha，所以用
        /// 面板底色到目标色插值来"淡"出来，视觉上等价）。
        /// </summary>
        private void DrawLoader(Graphics g, Rectangle r, double appear, double textP)
        {
            int cx = r.X + r.Width / 2;
            int cy = r.Y + Theme.S(122);
            int R = Theme.S(52);
            double frac = _prog != null ? _prog.Fraction : 0.0;

            SmoothingMode saved = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                using (Pen p = new Pen(C(Theme.LineSoft, appear), 1f))
                    g.DrawEllipse(p, cx - R, cy - R, R * 2, R * 2);

                if (frac > 0.001)
                {
                    using (Pen p = new Pen(C(Theme.Amber, appear), Math.Max(1f, Theme.SF(2.2f))))
                    {
                        p.StartCap = LineCap.Round;
                        p.EndCap = LineCap.Round;
                        g.DrawArc(p, cx - R, cy - R, R * 2, R * 2, -90f, (float)(360.0 * frac));
                    }
                }

                int ri = (int)(R * 0.70);
                using (Pen p = new Pen(C(Theme.Ink, appear * 0.85), Math.Max(1f, Theme.SF(1.6f))))
                    g.DrawArc(p, cx - ri, cy - ri, ri * 2, ri * 2,
                              (float)(Anim.Cycle(3.2, 0.0) * 360.0), 110f);
                using (Pen p = new Pen(C(Theme.Amber, appear * 0.75), Math.Max(1f, Theme.SF(1.6f))))
                    g.DrawArc(p, cx - ri, cy - ri, ri * 2, ri * 2,
                              (float)(-Anim.Cycle(2.3, 0.35) * 360.0), 110f);

                int active = (int)(Anim.Cycle(2.4, 0.0) * 6.0);
                for (int i = 0; i < 6; i++)
                {
                    double a = -Math.PI / 2 + i * Math.PI / 3.0;
                    int nx = cx + (int)(Math.Cos(a) * R);
                    int ny = cy + (int)(Math.Sin(a) * R);
                    bool on = i == active;
                    int rad = on ? Theme.S(3) : Theme.S(2);
                    using (SolidBrush b = new SolidBrush(on ? C(Theme.Amber, appear) : C(Theme.LineSoft, appear)))
                        g.FillEllipse(b, nx - rad, ny - rad, rad * 2, rad * 2);
                }

                int dot = Theme.S(3);
                int pulse = (int)Theme.Pulse(2.2, 0.0, 90, 220);
                using (SolidBrush b = new SolidBrush(Color.FromArgb((int)(pulse * appear), Theme.Ink)))
                    g.FillEllipse(b, cx - dot, cy - dot, dot * 2, dot * 2);
            }
            finally { g.SmoothingMode = saved; }

            if (textP <= 0.02) return;

            Color stageC = Theme.Mix(Theme.PanelHi, Theme.Amber, textP);
            Color subC = Theme.Mix(Theme.PanelHi, Theme.Sub, textP);
            Color inkC = Theme.Mix(Theme.PanelHi, Theme.Ink, textP);

            Theme.DrawTracked(g, _stage, Theme.FontMonoSmall, r.X + Theme.S(14), r.Y + Theme.S(10), stageC, Theme.SF(1.6f));
            if (_prog != null)
                Theme.DrawTrackedRight(g, _prog.Done + " / " + _prog.Total, Theme.FontMonoSmall,
                    r.Right - Theme.S(14), r.Y + Theme.S(10), subC, Theme.SF(1.4f));

            int tx = r.X + Theme.S(14);
            int tw = r.Width - Theme.S(28);
            int ty = cy + R + Theme.S(24);

            string cur = (_prog != null && _prog.CurrentPath.Length > 0) ? _prog.CurrentPath : "准备中…";
            TextRenderer.DrawText(g, cur, Theme.FontMonoSmall, new Rectangle(tx, ty, tw, Theme.S(18)), inkC,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis);

            string stat;
            if (_prog != null)
            {
                TimeSpan el = _startedAt == DateTime.MinValue ? TimeSpan.Zero : (DateTime.Now - _startedAt);
                stat = "已删除 " + Uninstaller.Human(_prog.BytesDone) + " / " + Uninstaller.Human(_prog.BytesTotal)
                     + "　·　" + ((int)el.TotalMinutes).ToString("00") + ":" + el.Seconds.ToString("00");
            }
            else stat = "正在准备…";
            TextRenderer.DrawText(g, stat, Theme.FontMonoSmall, new Rectangle(tx, ty + Theme.S(20), tw, Theme.S(18)), subC,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            if (_prog != null && _prog.CurrentLabel.Length > 0)
                TextRenderer.DrawText(g, _prog.CurrentLabel, Theme.FontSmall,
                    new Rectangle(tx, ty + Theme.S(40), tw, Theme.S(18)), stageC,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
        }


        /// <summary>明细列表本体（清单形态用；过渡时由 slide 让整块向上微移）。</summary>
        private void DrawRows(Graphics g, Rectangle r, double slide)
        {
            int x = Theme.S(12);
            int right = Width - Theme.S(12);

            if (_rows.Count == 0)
            {
                TextRenderer.DrawText(g, _empty, Theme.FontSmall,
                    new Rectangle(x, Theme.S(10), right - x, Theme.S(18)), Theme.Sub,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }

            int rowH = Theme.S(17);
            int y = Theme.S(6) - (int)(slide * Theme.S(24));
            int maxRows = Math.Max(1, (Height - Theme.S(14)) / rowH);
            int drawn = 0;
            int lastGroup = -1;

            foreach (UninstallTarget t in _rows)
            {
                int gid = (int)t.Group;
                if (t.Action == TargetAction.Skip) gid = 100;      // 「不动」单独成组
                if (gid != lastGroup)
                {
                    if (drawn >= maxRows) break;
                    string head = t.Action == TargetAction.Skip ? "不会动"
                                : (t.Action == TargetAction.Delete ? "将删除" : "将保留");
                    if (gid != lastGroup)
                    {
                        // 同组只画一次标题
                        bool already = false;
                        foreach (UninstallTarget prev in _rows)
                        {
                            if (prev == t) break;
                            int pg = (int)prev.Group; if (prev.Action == TargetAction.Skip) pg = 100;
                            if (pg == gid) { already = true; break; }
                        }
                        if (!already)
                        {
                            Theme.DrawTracked(g, head, Theme.FontMonoSmall, x, y + Theme.S(2),
                                t.Action == TargetAction.Delete ? Theme.SignalAlert
                                : (t.Action == TargetAction.Keep ? Theme.Green : Theme.SignalIdle), Theme.SF(1.4f));
                            y += rowH;
                            drawn++;
                        }
                    }
                    lastGroup = gid;
                }
                if (drawn >= maxRows) break;

                string glyph; Color gc;
                if (t.Action == TargetAction.Delete) { glyph = "OK"; gc = Theme.SignalAlert; }
                else if (t.Action == TargetAction.Keep) { glyph = "--"; gc = Theme.Green; }
                else { glyph = "··"; gc = Theme.SignalIdle; }

                Theme.DrawTracked(g, glyph, Theme.FontMonoSmall, x, y + Theme.S(1), gc, 0f);

                int labelX = x + Theme.S(26);
                int sizeW = Theme.S(72);
                int labelW = Theme.S(250);
                bool hasNote = t.Note.Length > 0;
                int pathX = labelX + labelW;
                int pathW = right - sizeW - pathX;

                TextRenderer.DrawText(g, t.Label, Theme.FontSmall,
                    new Rectangle(labelX, y, labelW - Theme.S(6), rowH),
                    t.Action == TargetAction.Skip ? Theme.Sub : Theme.Ink,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                if (pathW > Theme.S(40))
                {
                    TextRenderer.DrawText(g, hasNote ? t.Note : t.Path, Theme.FontMonoSmall,
                        new Rectangle(pathX, y, pathW, rowH), Theme.Sub,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis);
                }

                TextRenderer.DrawText(g, t.Exists ? t.SizeText : "—", Theme.FontMonoSmall,
                    new Rectangle(right - sizeW, y, sizeW, rowH), Theme.Sub,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                y += rowH;
                drawn++;
            }

            if (drawn < _rows.Count)
            {
                TextRenderer.DrawText(g, "…还有 " + (_rows.Count - drawn) + " 项（导出清单可看全部）", Theme.FontMonoSmall,
                    new Rectangle(x, y, right - x, rowH), Theme.Sub,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }
        }
    }

    /// <summary>
    /// 卸载二级界面：独立无边框窗口，与设置窗同一套档案终端语言。
    /// 上半部选卸载范围，中间摊开"将删除 / 将保留 / 不会动"的明细，下半部是可选开关。
    /// </summary>
    internal class UninstallForm : Form
    {
        private const int DesignW = 720;
        private const int DesignH = 940;
        private const int PadX = 30;
        private const int HeaderH = 76;

        private readonly AppConfig _cfg;
        private readonly DshServer _server;
        private readonly UninstallOptions _opt = new UninstallOptions();

        private RadioButton[] _radios;
        private CheckBox _cbStop, _cbBackup, _cbShortcuts, _cbWallpaper, _cbMemory, _cbLegacy, _cbPermanent;
        private UninstallList _list;
        private Label _summary;
        private FlatButton _btnRun, _btnExport;
        private UninstallMode _mode = UninstallMode.DshKeepData;
        private UninstallPlan _plan;
        private readonly long[] _modeBytes = new long[5];
        private readonly bool[] _modeDanger = new bool[5];
        private bool _scanning = true;
        private bool _busy;

        /// <summary>本次是否把启动器自己也卸了（调用方据此收尾退出）。</summary>
        public bool LauncherRemoved;

        private static readonly UninstallMode[] Modes = new UninstallMode[]
        {
            UninstallMode.DshKeepData, UninstallMode.DshEverything,
            UninstallMode.LauncherKeepData, UninstallMode.LauncherEverything,
            UninstallMode.All
        };

        private static readonly string[] ModeTitles = new string[]
        {
            "① 卸载 DSH（保留用户数据）",
            "② 彻底卸载 DSH（连同所有相关文件）",
            "③ 卸载启动器（保留配置与日志）",
            "④ 彻底卸载启动器（连同所有相关文件）",
            "⑤ 彻底卸载全部（DSH + 启动器）"
        };

        public UninstallForm(AppConfig cfg, DshServer server)
        {
            _cfg = cfg;
            _server = server;
            // 必须在 BuildUi 之前建：BuildUi 里的 AddSection 要把章节锚点登记进来
            _reveal = new WindowReveal(this, WindowReveal.Level, false);
            BuildUi();
        }

        private readonly WindowReveal _reveal;

        /// <summary>登记一个章节锚点：标题由 OnPaint 按导轨进度自绘，不再用子控件 Label。</summary>
        private void AddSection(string index, string cn, string en, int y)
        {
            if (_reveal != null) _reveal.AddSection(y, index, cn, en);
        }

        private bool _scanStarted;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            // 必须等窗口句柄建好再开后台扫描：StartScan 结束时要用 BeginInvoke 切回 UI 线程
            if (!_scanStarted) { _scanStarted = true; StartScan(); }
            // 扫描先起，再播入场 —— 动画绝不能挡在清点前面（"正在清点…"那句话还要实时更新）
            if (_reveal != null) _reveal.BeginEnter();
        }

        private void BuildUi()
        {
            SuspendLayout();
            Text = "卸载";
            Icon = Res.AppIcon(32);
            Font = Theme.FontUi;
            ForeColor = Theme.Ink;
            BackColor = Theme.Bg;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            // 小屏兜底：设计高 940（150% 缩放下 1410px）。屏幕装不下时**不裁内容**，改成可滚动 ——
            // 否则「执行卸载」会落在可视区之外，而它是不可逆操作唯一的入口。
            // （内容与按钮仍按 DesignH 排布，滚动即可触达；按钮 Y 因此不用改。）
            int designH = DesignH;
            try
            {
                int fit = (int)Math.Floor(Screen.PrimaryScreen.WorkingArea.Height /
                                          (double)(Theme.Scale <= 0f ? 1f : Theme.Scale)) - 24;
                if (fit > 420 && fit < designH) { designH = fit; AutoScroll = true; }
            }
            catch { }
            ClientSize = new Size(Theme.S(DesignW), Theme.S(designH));
            DoubleBuffered = true;

            AddSection("SECT. 01", "选择卸载范围", "SCOPE", 96);

            _radios = new RadioButton[Modes.Length];
            int y = 120;
            for (int i = 0; i < Modes.Length; i++)
            {
                int idx = i;
                RadioButton rb = new RadioButton();
                rb.Text = ModeTitles[i] + "   · 计算中…";
                rb.Font = Theme.FontUi;
                rb.ForeColor = Theme.Ink;
                rb.BackColor = Theme.Bg;
                rb.UseVisualStyleBackColor = false;
                rb.FlatStyle = FlatStyle.Standard;
                rb.AutoSize = false;
                rb.Location = new Point(Theme.S(PadX + 6), Theme.S(y));
                rb.Size = new Size(Theme.S(DesignW - PadX * 2 - 12), Theme.S(24));
                rb.Checked = (i == 0);
                rb.CheckedChanged += delegate(object s, EventArgs e)
                {
                    if (!rb.Checked) return;
                    _mode = Modes[idx];
                    ReloadPlan();
                };
                Controls.Add(rb);
                _radios[i] = rb;
                y += 32;
            }

            AddSection("SECT. 02", "将执行的操作", "PLANNED OPERATIONS", y + 10);

            _list = new UninstallList();
            _list.Location = new Point(Theme.S(PadX), Theme.S(y + 34));
            _list.Size = new Size(Theme.S(DesignW - PadX * 2), Theme.S(344));
            Controls.Add(_list);

            int oy = y + 34 + 344 + 12;
            _cbStop = AddCheck("先停止正在运行的 DSH 服务（服务在跑时数据目录删不干净）", oy); oy += 26;
            _cbBackup = AddCheck("卸载前把凭据与配置备份到桌面（.credentials.yaml + config.ini）", oy); oy += 26;
            _cbShortcuts = AddCheck("一并删除快捷方式与开机自启项", oy); oy += 26;
            _cbWallpaper = AddCheck("同时删除壁纸引擎插件数据（386.7 MB）", oy); oy += 26;
            _cbMemory = AddCheck("同时删除插件记忆数据 .mnemon / .hindsight", oy); oy += 26;
            _cbLegacy = AddCheck("清理旧 PowerShell 方案与早期安装残留（含桌面旧文件）", oy); oy += 26;
            _cbPermanent = AddCheck("永久删除，不送回收站（需再输入 DELETE 确认）", oy);

            _cbStop.Checked = true;
            _cbBackup.Checked = true;
            _cbShortcuts.Checked = true;
            _cbWallpaper.Checked = true;
            _cbMemory.Checked = true;
            _cbLegacy.Checked = true;
            _cbPermanent.Checked = false;
            foreach (CheckBox cb in new CheckBox[] { _cbStop, _cbBackup, _cbShortcuts, _cbWallpaper, _cbMemory, _cbLegacy, _cbPermanent })
                cb.CheckedChanged += delegate(object s, EventArgs e) { ReadOptions(); ReloadPlan(); };

            _summary = new Label();
            _summary.Font = Theme.FontSmall;
            _summary.ForeColor = Theme.Sub;
            _summary.BackColor = Theme.Bg;
            _summary.AutoSize = false;
            _summary.TextAlign = ContentAlignment.MiddleLeft;
            _summary.Location = new Point(Theme.S(PadX), Theme.S(DesignH - 104));
            _summary.Size = new Size(Theme.S(DesignW - PadX * 2), Theme.S(20));
            Controls.Add(_summary);

            int btnY = DesignH - 66;
            _btnRun = new FlatButton();
            _btnRun.Text = "执行卸载";
            _btnRun.Danger = true;
            _btnRun.Enabled = false;
            _btnRun.Location = new Point(Theme.S(DesignW - PadX - 130), Theme.S(btnY));
            _btnRun.Size = new Size(Theme.S(130), Theme.S(44));
            _btnRun.BackColor = Theme.Bg;
            _btnRun.Click += delegate(object s, EventArgs e) { RunUninstall(); };
            Controls.Add(_btnRun);

            _btnExport = new FlatButton();
            _btnExport.Text = "导出清单";
            _btnExport.Enabled = false;
            _btnExport.Location = new Point(Theme.S(DesignW - PadX - 130 - 118), Theme.S(btnY));
            _btnExport.Size = new Size(Theme.S(110), Theme.S(44));
            _btnExport.BackColor = Theme.Bg;
            _btnExport.Click += delegate(object s, EventArgs e) { ExportList(); };
            Controls.Add(_btnExport);

            FlatButton cancel = new FlatButton();
            cancel.Text = "取消";
            cancel.Location = new Point(Theme.S(PadX), Theme.S(btnY));
            cancel.Size = new Size(Theme.S(110), Theme.S(44));
            cancel.BackColor = Theme.Bg;
            cancel.Click += delegate(object s, EventArgs e) { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(cancel);

            ChromeButton close = new ChromeButton(ChromeButton.GlyphKind.Close);
            close.Location = new Point(Theme.S(DesignW - PadX - 26), Theme.S(8));
            close.Size = new Size(Theme.S(26), Theme.S(22));
            close.BackColor = Theme.Panel;
            close.Invoked += delegate(object s, EventArgs e) { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(close);
            close.BringToFront();

            ResumeLayout(false);
        }

        private void AddLabel(string text, int x, int y)
        {
            Label l = new Label();
            l.Text = text;
            l.Font = Theme.FontUiBold;
            l.ForeColor = Theme.InkSoft;
            l.BackColor = Theme.Bg;
            l.AutoSize = true;
            l.Location = new Point(Theme.S(x), Theme.S(y));
            Controls.Add(l);
        }

        private CheckBox AddCheck(string text, int y)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Font = Theme.FontUi;
            c.ForeColor = Theme.Ink;
            c.BackColor = Theme.Bg;
            c.UseVisualStyleBackColor = false;
            c.FlatStyle = FlatStyle.Standard;
            c.AutoSize = false;
            c.Location = new Point(Theme.S(PadX + 6), Theme.S(y));
            c.Size = new Size(Theme.S(DesignW - PadX * 2 - 12), Theme.S(24));
            Controls.Add(c);
            return c;
        }

        private void ReadOptions()
        {
            _opt.StopService = _cbStop.Checked;
            _opt.BackupPrivate = _cbBackup.Checked;
            _opt.IncludeShortcuts = _cbShortcuts.Checked;
            _opt.IncludeWallpaper = _cbWallpaper.Checked;
            _opt.IncludeMemory = _cbMemory.Checked;
            _opt.IncludeLegacy = _cbLegacy.Checked;
            _opt.Permanent = _cbPermanent.Checked;
        }

        // ---------------- 清点（后台线程）----------------

        private void StartScan()
        {
            Thread t = new Thread(delegate()
            {
                try
                {
                    Uninstaller.ClearStats();
                    UninstallOptions probe = new UninstallOptions();
                    for (int i = 0; i < Modes.Length; i++)
                    {
                        UninstallPlan p = Uninstaller.Build(_cfg, Modes[i], probe, null);
                        _modeBytes[i] = p.DeleteBytes;
                        _modeDanger[i] = p.IsDangerous;
                    }
                }
                catch { }
                try { BeginInvoke(new MethodInvoker(ScanDone)); }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void ScanDone()
        {
            _scanning = false;
            for (int i = 0; i < _radios.Length; i++)
            {
                _radios[i].Text = ModeTitles[i] + "   · " + Uninstaller.Human(_modeBytes[i]) + (_modeDanger[i] ? "   · 需打字确认" : "");
            }
            _btnExport.Enabled = true;
            ReloadPlan();
        }


        // ---------------- 计划刷新 ----------------

        private void ReloadPlan()
        {
            if (_scanning || _busy) return;
            ReadOptions();
            try { _plan = Uninstaller.Build(_cfg, _mode, _opt, null); }
            catch (Exception ex)
            {
                _summary.Text = "清点失败：" + ex.Message;
                return;
            }

            bool dshAll = _mode == UninstallMode.DshEverything || _mode == UninstallMode.All;
            bool launcherAll = _mode == UninstallMode.LauncherEverything || _mode == UninstallMode.All;

            _cbWallpaper.Enabled = dshAll;
            _cbMemory.Enabled = dshAll;
            _cbLegacy.Enabled = launcherAll;
            _cbShortcuts.Enabled = _mode == UninstallMode.LauncherKeepData || launcherAll;
            _cbBackup.Enabled = dshAll;
            _cbStop.Enabled = true;   // 卸载启动器时也允许顺手停掉服务

            List<UninstallTarget> rows = new List<UninstallTarget>();
            foreach (UninstallTarget t in _plan.Targets)
            {
                bool relevant =
                    (t.Action == TargetAction.Skip) ||
                    (t.Action == TargetAction.Delete && t.Exists) ||
                    (t.Action == TargetAction.Keep && t.Exists);
                if (relevant) rows.Add(t);
            }
            foreach (UninstallTarget t in Uninstaller.Protected(_cfg)) rows.Add(t);
            _list.SetRows(rows, "没有需要处理的项目。");

            _summary.Text = _plan.Headline + "　·　将删除 " + _plan.DeleteSizeText +
                            "（" + _plan.DeleteFileCount + " 个文件）　·　" +
                            (_opt.Permanent ? "永久删除" : "送入回收站（可还原）");
            _btnRun.Enabled = _plan.DeleteFileCount > 0 || _plan.DeleteBytes > 0;
            _btnRun.Text = _plan.IsDangerous ? "彻底卸载…" : "执行卸载";
            Invalidate();
        }

        // ---------------- 执行 ----------------

        private void RunUninstall()
        {
            if (_busy || _plan == null) return;
            ReadOptions();

            // ① 危险方案：打字确认
            if (_plan.IsDangerous)
            {
                List<string> fields = new List<string>();
                fields.Add("方案=" + _plan.Headline);
                fields.Add("将删除=" + _plan.DeleteSizeText + "（" + _plan.DeleteFileCount + " 个文件）");
                fields.Add("删除方式=" + (_opt.Permanent ? "永久删除" : "送入回收站"));
                fields.Add("不可恢复=" + (_opt.Permanent ? "是" : "否，可从回收站还原"));
                string warn = _plan.Warnings.Count > 0 ? string.Join(" ", _plan.Warnings.ToArray()) : null;

                bool typed = TypeConfirm.Ask(this, "CONFIRM / 危险操作", "彻底卸载确认",
                    "这一步会删除涉及你个人数据的内容，且方案里包含私密文件。请手动输入下面的确认词。",
                    fields.ToArray(), warn, _plan.ConfirmWord, "确认卸载");
                if (!typed) return;
            }
            else
            {
                List<string> fields = new List<string>();
                fields.Add("方案=" + _plan.Headline);
                fields.Add("将删除=" + _plan.DeleteSizeText + "（" + _plan.DeleteFileCount + " 个文件）");
                ConfirmDialog.Choice r = ConfirmDialog.Show(this, "CONFIRM / 卸载", "确认执行卸载",
                    "将按上面的清单执行卸载。" + (_opt.Permanent ? "删除后无法从回收站还原。" : "默认送回收站，可还原。"),
                    fields.ToArray(),
                    _plan.Warnings.Count > 0 ? string.Join(" ", _plan.Warnings.ToArray()) : null,
                    "确认卸载", true);
                if (r != ConfirmDialog.Choice.Confirm) return;
            }

            // ② 永久删除：再叠一层打字确认
            if (_opt.Permanent)
            {
                bool ok2 = TypeConfirm.Ask(this, "CONFIRM / 永久删除", "永久删除确认",
                    "你选择了「不送回收站」。删除后无法通过回收站还原。",
                    new string[] { "将删除=" + _plan.DeleteSizeText, "回收站还原=不可用" },
                    "确认后文件将直接从磁盘移除。", "DELETE", "永久删除");
                if (!ok2) return;
            }

            // ③ 进入执行：清单形态过渡到加载形态，真正的删除丢到后台线程 ——
            //    以前是同步跑，删 1.95 GB / 11 万个文件期间整个窗口是冻住的（连动画都播不出来）。
            _busy = true;
            _btnRun.Enabled = false;
            _btnExport.Enabled = false;
            SetInputsEnabled(false);
            _list.BeginRun("UNINSTALLING");

            Thread worker = new Thread(delegate() { ExecuteWorker(); });
            worker.IsBackground = true;
            worker.Name = "dsh-uninstall";
            // 删除走 shell API（SHFileOperation / 回收站），跑在 STA 上最稳妥；
            // 这个线程不建窗口、不摸 UI，所以不需要消息泵。
            worker.SetApartmentState(ApartmentState.STA);
            worker.Start();
        }

        private bool _allowClose;

        protected override void Dispose(bool disposing)
        {
            if (disposing && _reveal != null) _reveal.Dispose();
            base.Dispose(disposing);
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_busy && !_allowClose && e.CloseReason != CloseReason.WindowsShutDown)
            {
                ConfirmDialog.Choice r = ConfirmDialog.Show(this, "CONFIRM / 中断卸载", "卸载正在进行中",
                    "现在关掉窗口会立刻中断删除。已经删掉的部分不会回来，剩下的内容保持原样 —— 相当于卸载只做了一半，下次还得再跑一次。",
                    new string[] { "已删除的文件=不可恢复（除非当初选了送回收站）", "剩下的内容=原封不动" },
                    "中断可能留下半卸载状态。", "仍然关闭", true);
                if (r != ConfirmDialog.Choice.Confirm) { e.Cancel = true; return; }
                _allowClose = true;
            }
            // 非执行期关闭：先播 200ms 退场再真关（_allowClose 已置位时直接放行）
            if (!_allowClose && _reveal != null && _reveal.InterceptClose(e)) return;
            base.OnFormClosing(e);
        }

        /// <summary>执行期间锁住输入，免得用户在删的过程中改方案。</summary>
        private void SetInputsEnabled(bool on)
        {
            foreach (Control c in Controls)
            {
                if (c is RadioButton || c is CheckBox) c.Enabled = on;
            }
        }

        /// <summary>后台执行体：**绝不碰 UI**，进度一律经 BeginInvoke 回主线程。</summary>
        private void ExecuteWorker()
        {
            int ok = 0, fail = 0;
            string report = null;
            try
            {
                // 先停服务（DshServer.Stop 本身线程安全，主界面「停止」也是这么调的）
                if (_opt.StopService && _server != null && _server.Owned &&
                    (_server.Status == ServerStatus.Running || _server.Status == ServerStatus.Starting))
                {
                    try { _server.Stop(); } catch { }
                    for (int i = 0; i < 40 && _server.Status == ServerStatus.Stopping; i++) Thread.Sleep(100);
                }

                Action<UninstallProgress> onProgress = delegate(UninstallProgress p)
                {
                    try { BeginInvoke(new MethodInvoker(delegate() { _list.UpdateProgress(p); })); }
                    catch { }
                };

                report = Uninstaller.Execute(_plan, _opt, null, onProgress, out ok, out fail);
                LauncherRemoved = _plan.AffectsRunningLauncher;
            }
            catch (Exception ex)
            {
                report = "卸载过程中发生异常：" + ex.Message;
            }

            int okF = ok, failF = fail;
            string rep = report;
            try { BeginInvoke(new MethodInvoker(delegate() { FinishExecution(rep, okF, failF); })); }
            catch { }
        }

        /// <summary>回到 UI 线程收尾：等过渡播完，再写报告、给结果、恢复界面。</summary>
        private void FinishExecution(string report, int ok, int fail)
        {
            // 小方案可能几百毫秒就删完了，而过渡要 0.72 秒。不等它走完就弹结果框的话，
            // 用户会看到"过渡走到一半突然跳回清单"的抽帧。这里用一个一次性定时器把
            // 收尾推迟到过渡结束 —— 期间 _busy 仍为 true，输入保持锁定，动画照常播。
            double left = _list.MorphLeft();
            if (left > 0.05)
            {
                System.Windows.Forms.Timer once = new System.Windows.Forms.Timer();
                once.Interval = (int)(left * 1000.0) + 40;
                once.Tick += delegate(object s, EventArgs e)
                {
                    once.Stop();
                    once.Dispose();
                    ShowResult(report, ok, fail);
                };
                once.Start();
                return;
            }
            ShowResult(report, ok, fail);
        }

        private void ShowResult(string report, int ok, int fail)
        {
            _busy = false;
            Cursor = Cursors.Default;
            SetInputsEnabled(true);

            string reportPath = null;
            if (!string.IsNullOrEmpty(report)) reportPath = Uninstaller.WriteReport(report);

            Uninstaller.ClearStats();

            // ⑥ 结果
            if (reportPath != null) Uninstaller.RevealInExplorer(reportPath);

            List<string> res = new List<string>();
            res.Add("成功=" + ok + " 项");
            res.Add("失败=" + fail + " 项");
            if (reportPath != null) res.Add("报告=" + reportPath);
            string msg = LauncherRemoved
                ? "卸载已完成。启动器即将退出，报告已存到桌面并已在资源管理器里定位。"
                : "卸载已完成。可以关闭本窗口。";

            ConfirmDialog.Choice after = ConfirmDialog.Show(this, "DONE / 卸载完成", "卸载完成",
                msg, res.ToArray(),
                fail > 0 ? "有项目删除失败，原因见报告。" : null,
                "打开报告位置", "打开任务管理器", false);

            if (after == ConfirmDialog.Choice.Confirm && reportPath != null) Uninstaller.RevealInExplorer(reportPath);
            else if (after == ConfirmDialog.Choice.Alt) Uninstaller.OpenTaskManager();

            if (LauncherRemoved)
            {
                DialogResult = DialogResult.OK;
                Close();
            }
            else
            {
                _list.EndRun();
                _btnRun.Enabled = true;
                _btnExport.Enabled = true;
                ReloadPlan();
            }
        }

        private void ExportList()
        {
            try
            {
                if (_plan == null) return;
                List<string> lines = new List<string>();
                lines.Add("DSH 启动器 · 卸载清单（仅清单，未执行任何删除）");
                lines.Add("时间  : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                lines.Add("方案  : " + _plan.Headline);
                lines.Add("");
                foreach (UninstallTarget t in _plan.Targets)
                {
                    string tag = t.Action == TargetAction.Delete ? "删除" : (t.Action == TargetAction.Keep ? "保留" : "不动");
                    lines.Add("[" + tag + "] " + t.Path + "   " + (t.Exists ? t.SizeText : "不存在") + (t.Note.Length > 0 ? "   —— " + t.Note : ""));
                }
                lines.Add("");
                lines.Add("== 不会动（硬护栏）==");
                foreach (UninstallTarget t in Uninstaller.Protected(_cfg))
                    lines.Add("[不动] " + t.Path + (t.Note.Length > 0 ? "   —— " + t.Note : ""));

                string path = Path.Combine(Uninstaller.DesktopDir, "DSH卸载清单-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                File.WriteAllText(path, string.Join("\r\n", lines.ToArray()), new System.Text.UTF8Encoding(true));
                Uninstaller.RevealInExplorer(path);
            }
            catch { }
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_reveal != null && _reveal.Busy) { _reveal.Skip(); return true; }   // 入场期间按一下就跳过
            if (keyData == Keys.Escape && !_busy)
            {
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_reveal != null && _reveal.Busy) { _reveal.Skip(); return; }
            if (e.Button == MouseButtons.Left && e.Y <= Theme.S(HeaderH)) WindowChrome.BeginDrag(this);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;
                return cp;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            int w = ClientSize.Width;
            int h = Theme.S(HeaderH);
            int band = Theme.S(3);
            int right = w - Theme.S(PadX);

            // ---- 版式就位（与设置窗同一套元素，同一套时间轴）----
            Theme.Fill(g, ClientRectangle, Theme.Bg);
            if (_reveal != null) UiPaint.Grid(g, ClientRectangle, _reveal.GridP, Theme.S(44), 16);
            Theme.Fill(g, new Rectangle(0, 0, w, band), Theme.Green);
            Theme.Fill(g, new Rectangle(0, band, w, h - band), Theme.Panel);
            Theme.Rule(g, 0, h - 1, w, Theme.Line);

            // 刻度尺逐根 11ms 错峰点亮（27 根 ≈ 300ms，与盖板揭开同窗；原库 34ms 是列表行的步长）
            int step = Theme.S(22);
            for (int x = Theme.S(PadX), i = 0; x < right; x += step, i++)
            {
                bool tall = (i % 5) == 0;
                double tp = _reveal != null ? _reveal.Tick(i, 11, 300) : 1.0;
                if (tp <= 0.02) continue;
                int th = tall ? 8 : 4;
                Theme.VRule(g, x, h - 1 - Theme.S((int)Math.Round(th * tp)), h - 1,
                            Theme.Mix(Theme.Panel, tall ? Theme.Line : Theme.LineSoft, tp));
            }

            Image logo = Res.Logo();
            int box = Theme.S(32);
            double markP = _reveal != null ? _reveal.TitleP : 1.0;
            double barP = _reveal != null ? _reveal.BarP : 1.0;
            if (logo != null && markP > 0.02)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(logo, new Rectangle(Theme.S(PadX), band + Theme.S(20), box, box));
            }

            // 标题：浅色高亮条先到位，文字再自左向右擦入
            int titleX = Theme.S(PadX + 44);
            int barW = Theme.S(112);
            UiPaint.HighlightBar(g, new Rectangle(titleX - Theme.S(6), band + Theme.S(17), barW, Theme.S(22)), barP, true);
            double titleP = _reveal != null ? _reveal.TitleP : 1.0;
            if (titleP > 0.01)
            {
                Theme.DrawTracked(g, "UNINSTALL", Theme.FontTechBold, titleX, band + Theme.S(23), Theme.Ink, Theme.SF(2.2f), titleX + (int)Math.Round(barW * titleP));
            }
            if (markP > 0.02)
                Theme.DrawTracked(g, "卸载", Theme.FontSmall, Theme.S(PadX + 46), band + Theme.S(42),
                                  Theme.Mix(Theme.Panel, Theme.Sub, markP), Theme.SF(1.2f));

            Image mark = Res.Mark();
            int markH = Theme.S(18);
            int markW = (int)Math.Round(markH * 2.2);
            if (mark != null && markP > 0.02)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(mark, new Rectangle(right - markW, Theme.S(34), markW, markH));
            }
            Theme.DrawTrackedRight(g, "RHINE · LAB", Theme.FontMonoSmall,
                right - markW - Theme.S(10), Theme.S(37), Theme.Mix(Theme.Panel, Theme.Sub, markP), Theme.SF(1.6f));

            DrawSections(g);

            if (_reveal != null && _reveal.WantsLayout)
                UiPaint.Brackets(g, Rectangle.Inflate(ClientRectangle, -Theme.S(2), -Theme.S(2)),
                                 Theme.Ink, _reveal.BracketPhases());

            base.OnPaint(e);
        }

        /// <summary>章节导轨 + 每节的「chip + 中文标题 + 灰色英文小字」（与设置窗同一套画法）。</summary>
        private void DrawSections(Graphics g)
        {
            if (_reveal == null || !_reveal.WantsLayout || _reveal.Sections.Count == 0) return;

            int railX = Theme.S(14);
            int railTop = Theme.S(_reveal.Sections[0].Y) + Theme.S(7);
            int railBottom = Theme.S(_reveal.Sections[_reveal.Sections.Count - 1].Y) + Theme.S(7);
            UiPaint.SectionRail(g, railX, railTop, railBottom, _reveal.RailP,
                                _reveal.SectionYs(), _reveal.SectionPhases());

            for (int i = 0; i < _reveal.Sections.Count; i++)
            {
                WindowReveal.Section s = _reveal.Sections[i];
                double p = _reveal.SectionP(i);
                if (p <= 0.01) continue;

                int x = Theme.S(PadX);
                int y = Theme.S(s.Y);
                int chipW = UiPaint.Chip(g, x, y, s.Index, p);

                int tx = x + chipW + Theme.S(10);
                Theme.DrawTracked(g, s.Cn, Theme.FontUiBold, tx, y + Theme.S(3),
                                  Theme.Mix(Theme.Bg, Theme.Ink, p), Theme.SF(0.6f));
                Size cn = Theme.MeasureTracked(g, s.Cn, Theme.FontUiBold, Theme.SF(0.6f));
                if (s.En.Length > 0)
                    Theme.DrawTracked(g, s.En, Theme.FontMonoSmall, tx + cn.Width + Theme.S(12), y + Theme.S(4),
                                      Theme.Mix(Theme.Bg, Theme.Sub, p), Theme.SF(1.6f));
            }
        }
    }
}
