using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 开启动画：螺旋 DNA 环形加载器 + 中心莱茵生命标志。
    /// 版式沿用档案终端风格（暖灰白底、细线、宽字距排字、绿色顶边），
    /// 节奏参照站点开场：逐字输入 → 轮廓绘制 → 旋转加载 → 就绪 → 淡出。
    /// </summary>
    internal class BootSplash : Form
    {
        private const int DesignW = 620;
        private const int DesignH = 400;

        // 时间轴（秒）——完整档 / 精简档（精简档用于"已有实例时再次启动"）
        private readonly double TDraw;        // 双螺旋绘制到位
        private readonly double TLogo;        // 中心标志开始出现
        private readonly double TFull;        // 加载完成
        private readonly double TFade;        // 收尾：卡片展开进主窗口并淡出
        private readonly double TitleStart, TitleDur, StatusStart, StatusDur, ProgStart;

        private const int Steps = 234;        // 螺旋采样段数（每圈 18 段，够顺滑又省一半绘制）
        private const double Turns = 13.0;    // 绕环圈数：螺距 ≈ 2.4×螺旋直径，才是 DNA 的比例
        private const int Rungs = 30;         // 碱基对数量

        private readonly Timer _timer;
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly Pen[] _strandInk = new Pen[6];
        private readonly Pen[] _strandAmber = new Pen[6];
        private readonly Pen _rung;
        private readonly Pen _rungHot;
        private readonly Pen _tick;
        private readonly Pen _tickLong;
        private readonly Pen _centerRing;
        private readonly Pen _centerArc;
        private readonly Pen _barTrack;
        private readonly Pen _barFill;

        private double _timeShift;
        private bool _closing;
        private Bitmap _ringCache;            // 静态刻度环的缓存位图
        private readonly GraphicsPath[] _bucketInk = new GraphicsPath[6];     // 按深度分桶的螺旋路径
        private readonly GraphicsPath[] _bucketAmber = new GraphicsPath[6];
        private Form _reveal;                 // 收尾时要淡入的主窗口
        private Rectangle _revealRect;
        private Rectangle _startRect;

        /// <summary>
        /// 收尾衔接：主窗口先以全透明就位，动画最后 0.55 秒里卡片边界插值到主窗口边界、
        /// 自身淡出，同时主窗口淡入——两段之间不留硬切。
        /// </summary>
        public void AttachReveal(Form target)
        {
            if (target == null) return;
            _reveal = target;
            try
            {
                target.Opacity = 0.0;
                _revealRect = target.Bounds;
            }
            catch { }
        }

        public BootSplash() : this(false) { }

        /// <param name="compact">true = 精简档（约 1 秒），用于已有实例时再次启动的快速开场</param>
        public BootSplash(bool compact)
        {
            if (compact)
            {
                TDraw = 0.28; TLogo = 0.12; TFull = 0.70; TFade = 0.34;
                TitleStart = 0.14; TitleDur = 0.24;
                StatusStart = 0.20; StatusDur = 0.16;
                ProgStart = 0.10;
            }
            else
            {
                TDraw = 0.60; TLogo = 0.35; TFull = 2.15; TFade = 0.55;
                TitleStart = 0.35; TitleDur = 0.55;
                StatusStart = 0.55; StatusDur = 0.30;
                ProgStart = 0.25;
            }

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(DesignW), Theme.S(DesignH));
            BackColor = Theme.Bg;
            KeyPreview = true;
            DoubleBuffered = true;
            AllowTransparency = true;   // 句柄创建前打开：避免淡入淡出时重建句柄

            // 按"深度"分 6 档预建画笔：越靠前越深越粗，形成体积感
            for (int i = 0; i < 6; i++)
            {
                int alpha = 44 + i * 36;
                float w = 1.0f + i * 0.24f;
                _strandInk[i] = new Pen(Color.FromArgb(alpha, Theme.Ink), w);
                _strandAmber[i] = new Pen(Color.FromArgb(alpha, Theme.Amber), w);
                _bucketInk[i] = new GraphicsPath();
                _bucketAmber[i] = new GraphicsPath();
            }
            _rung = new Pen(Theme.LineSoft, 1.2f);
            _rungHot = new Pen(Theme.Green, 1.6f);
            _tick = new Pen(Theme.LineSoft, 1f);
            _tickLong = new Pen(Theme.Line, 1f);
            _centerRing = new Pen(Theme.Line, 1f);
            _centerArc = new Pen(Theme.Green, 1.6f);
            _barTrack = new Pen(Theme.LineSoft, 2f);
            _barFill = new Pen(Theme.Green, 2f);

            _clock.Start();
            _timer = new Timer();
            _timer.Interval = 33;         // 30fps：够顺滑，比 40fps 省四分之一绘制
            _timer.Tick += delegate(object s, EventArgs e) { Tick(); };
            _timer.Start();
        }

        private void Tick()
        {
            double now = _clock.Elapsed.TotalSeconds + _timeShift;
            if (now >= TFull + TFade)
            {
                if (_reveal != null && !_reveal.IsDisposed) { try { _reveal.Opacity = 1.0; } catch { } }
                if (!_closing) { _closing = true; Close(); }
                return;
            }
            if (now < TFull)
            {
                Opacity = Theme.Clamp01(now / 0.20);                    // 入场淡入
            }
            else
            {
                double p = Theme.Clamp01((now - TFull) / TFade);
                Opacity = 1.0 - p;
                if (_reveal != null && !_reveal.IsDisposed)
                {
                    try
                    {
                        _reveal.Opacity = Theme.Clamp01(p);                // 与卡片淡出对称：同时到 0 / 1
                        if (_startRect.Width > 0 && _revealRect.Width > 0)
                            Bounds = Theme.Lerp(_startRect, _revealRect, Theme.EaseInOutCubic(p));
                    }
                    catch { }
                }
            }
            Invalidate();
        }

        private void Skip()
        {
            double now = _clock.Elapsed.TotalSeconds + _timeShift;
            if (now < TFull) _timeShift += TFull - now;
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _startRect = Bounds;                 // 收尾插值的起点
            Activate();
            BringToFront();
        }

        protected override void OnMouseDown(MouseEventArgs e) { Skip(); base.OnMouseDown(e); }
        protected override void OnKeyDown(KeyEventArgs e) { Skip(); base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            double now = _clock.Elapsed.TotalSeconds + _timeShift;
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            // ---- 底板 ----
            Theme.CardFrame(g, w, h);

            float cx = w / 2f;
            float cy = Theme.S(158);
            float R = Theme.SF(88);
            float A = Theme.SF(9f);           // 螺旋半径：直径 18，配 42 的螺距
            double sweep = 2.0 * Math.PI * Ease(Math.Min(1.0, now / TDraw));
            double phi = now * 0.85;

            // ---- 外圈刻度环 ----
            int tickCount = 72;
            float tickOuter = R + A + Theme.SF(18);
            // 刻度环是静态的：入场淡入结束后改用缓存位图，省掉每帧 72 次 DrawLine
            if (now > 0.65 && _ringCache != null)
            {
                g.DrawImageUnscaled(_ringCache, 0, 0);
            }
            else
            {
                for (int i = 0; i < tickCount; i++)
                {
                    double t = i * 2.0 * Math.PI / tickCount;
                    bool longTick = (i % 6) == 0;
                    float len = Theme.SF(longTick ? 7f : 3.4f);
                    double fade = Math.Min(1.0, Math.Max(0.0, (now - 0.10) / 0.5));
                    int a = (int)((longTick ? 170 : 110) * fade);
                    if (a <= 2) continue;
                    Pen p = longTick ? _tickLong : _tick;
                    p.Color = Color.FromArgb(a, longTick ? Theme.Line : Theme.LineSoft);
                    double ct = Math.Cos(t), st = Math.Sin(t);
                    g.DrawLine(p,
                        cx + (float)(ct * tickOuter), cy + (float)(st * tickOuter),
                        cx + (float)(ct * (tickOuter + len)), cy + (float)(st * (tickOuter + len)));
                }
                // 画完这一帧把刻度环缓存下来（整卡尺寸，后续直接贴图）
                if (now > 0.65 && _ringCache == null && Width > 0 && Height > 0)
                {
                    try
                    {
                        _ringCache = new Bitmap(Width, Height);
                        using (Graphics cg = Graphics.FromImage(_ringCache))
                        {
                            cg.SmoothingMode = SmoothingMode.AntiAlias;
                            for (int i = 0; i < tickCount; i++)
                            {
                                double t2 = i * 2.0 * Math.PI / tickCount;
                                bool longTick2 = (i % 6) == 0;
                                float len2 = Theme.SF(longTick2 ? 7f : 3.4f);
                                Pen p2 = longTick2 ? _tickLong : _tick;
                                p2.Color = Color.FromArgb(longTick2 ? 170 : 110, longTick2 ? Theme.Line : Theme.LineSoft);
                                double ct2 = Math.Cos(t2), st2 = Math.Sin(t2);
                                cg.DrawLine(p2,
                                    cx + (float)(ct2 * tickOuter), cy + (float)(st2 * tickOuter),
                                    cx + (float)(ct2 * (tickOuter + len2)), cy + (float)(st2 * (tickOuter + len2)));
                            }
                        }
                    }
                    catch { _ringCache = null; }
                }
            }

            // ---- 双螺旋：按深度分 6 桶攒成路径，每桶一次 DrawPath（原先每段一次 DrawLine，约 468 次）----
            for (int b = 0; b < 6; b++) { _bucketInk[b].Reset(); _bucketAmber[b].Reset(); }
            int lastInk = -1, lastAmber = -1;
            for (int i = 0; i < Steps; i++)
            {
                double t0 = i * 2.0 * Math.PI / Steps;
                if (t0 > sweep) break;
                double t1 = (i + 1) * 2.0 * Math.PI / Steps;
                double phase0 = Turns * t0 + phi;
                double z = Math.Cos(phase0);

                double phase1 = Turns * t1 + phi;
                double s0 = Math.Sin(phase0), s1 = Math.Sin(phase1);

                float x0 = cx + (float)(Math.Cos(t0) * (R + A * s0));
                float y0 = cy + (float)(Math.Sin(t0) * (R + A * s0));
                float x1 = cx + (float)(Math.Cos(t1) * (R + A * s1));
                float y1 = cy + (float)(Math.Sin(t1) * (R + A * s1));
                float x2 = cx + (float)(Math.Cos(t0) * (R - A * s0));
                float y2 = cy + (float)(Math.Sin(t0) * (R - A * s0));
                float x3 = cx + (float)(Math.Cos(t1) * (R - A * s1));
                float y3 = cy + (float)(Math.Sin(t1) * (R - A * s1));

                int bucket = (int)Math.Round((z + 1.0) * 0.5 * 5.0);
                if (bucket < 0) bucket = 0; else if (bucket > 5) bucket = 5;

                // 换桶就得起新图形，否则 AddLine 会把两段之间补一条连线
                if (bucket != lastInk) { _bucketInk[bucket].StartFigure(); lastInk = bucket; }
                _bucketInk[bucket].AddLine(x0, y0, x1, y1);
                if (bucket != lastAmber) { _bucketAmber[bucket].StartFigure(); lastAmber = bucket; }
                _bucketAmber[bucket].AddLine(x2, y2, x3, y3);
            }
            for (int b = 0; b < 6; b++)      // 0→5 即"由背到前"，与原先两趟绘制的遮挡关系一致
            {
                if (_bucketInk[b].PointCount > 1) g.DrawPath(_strandInk[b], _bucketInk[b]);
                if (_bucketAmber[b].PointCount > 1) g.DrawPath(_strandAmber[b], _bucketAmber[b]);
            }

            // ---- 碱基对（含沿环行进的高亮波）----
            double wave = (now * 0.5) % 1.0;
            for (int i = 0; i < Rungs; i++)
            {
                double t = i * 2.0 * Math.PI / Rungs;
                if (t > sweep) break;
                double phase = Turns * t + phi;
                double s = Math.Sin(phase);
                double z = Math.Cos(phase);
                float xa = cx + (float)(Math.Cos(t) * (R + A * s));
                float ya = cy + (float)(Math.Sin(t) * (R + A * s));
                float xb = cx + (float)(Math.Cos(t) * (R - A * s));
                float yb = cy + (float)(Math.Sin(t) * (R - A * s));

                double pos = t / (2.0 * Math.PI);
                double dist = Math.Abs(((pos - wave + 1.5) % 1.0) - 0.5);
                if (dist < 0.05 && now > TDraw)
                {
                    g.DrawLine(_rungHot, xa, ya, xb, yb);
                }
                else
                {
                    int a = (int)(30 + 112 * (z + 1.0) * 0.5);
                    _rung.Color = Color.FromArgb(a, Theme.Line);
                    g.DrawLine(_rung, xa, ya, xb, yb);
                }
            }

            // ---- 中心：莱茵生命标志（先铺一块净空圆盘，避免螺旋线穿过标志）----
            float coreR = R * 0.46f;
            using (SolidBrush disc = new SolidBrush(Theme.Bg))
                g.FillEllipse(disc, cx - coreR, cy - coreR, coreR * 2, coreR * 2);
            using (Pen p = new Pen(Color.FromArgb(150, Theme.Line), 1f))
                g.DrawEllipse(p, cx - coreR, cy - coreR, coreR * 2, coreR * 2);

            // 中心外缓慢旋转的绿色弧（呼应顶边）
            using (Pen p = new Pen(Color.FromArgb(200, Theme.Green), Theme.SF(1.6f)))
                g.DrawArc(p, cx - coreR - Theme.SF(8), cy - coreR - Theme.SF(8),
                          (coreR + Theme.SF(8)) * 2, (coreR + Theme.SF(8)) * 2, (float)(-now * 42.0), 92f);

            double logoAge = now - TLogo;
            if (logoAge > 0)
            {
                double p = Math.Min(1.0, logoAge / 0.45);
                float scale = (float)EaseOutBack(p);
                float mw = Theme.SF(80) * scale;
                float mh = mw / 2.2f;
                Image mark = Res.Mark();
                if (mark != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(mark, new RectangleF(cx - mw / 2f, cy - mh / 2f, mw, mh));
                }
            }

            // ---- 文案 ----
            int pad = Theme.S(24);
            int right = w - pad;
            double typeP = Math.Min(1.0, now / TitleDur);

            string head = "BOOT SEQUENCE / 001";
            Theme.DrawTracked(g, Clip(head, typeP), Theme.FontMonoSmall, pad, Theme.S(16), Theme.Sub, Theme.SF(1.6f));
            Theme.DrawTrackedRight(g, "v" + LauncherContext.Version, Theme.FontMonoSmall, right, Theme.S(16), Theme.Sub, Theme.SF(1.6f));

            string title = "DSH LAUNCHER";
            double titleP = Math.Min(1.0, Math.Max(0.0, (now - TitleStart) / TitleDur));
            Theme.DrawTrackedCenter(g, Clip(title, titleP), Theme.FontTechBold,
                new Rectangle(0, Theme.S(288), w, Theme.S(24)), Theme.Ink, Theme.SF(3.0f));

            double prog = Math.Min(1.0, Math.Max(0.0, (now - ProgStart) / (TFull - ProgStart)));
            string status;
            if (prog < 0.28) status = "INITIALIZING";
            else if (prog < 0.58) status = "LOADING ARCHIVE";
            else if (prog < 0.92) status = "AUTHORIZING";
            else status = "READY";
            Theme.DrawTrackedCenter(g, status, Theme.FontMonoSmall,
                new Rectangle(0, Theme.S(318), w, Theme.S(18)), Theme.Sub, Theme.SF(1.8f));

            int barX = Theme.S(76);
            int barW = w - barX * 2;
            int barY = Theme.S(354);
            g.DrawLine(_barTrack, barX, barY, barX + barW, barY);
            float fill = (float)(barW * Ease(prog));
            if (fill > 0.5f) g.DrawLine(_barFill, barX, barY, barX + fill, barY);
            Theme.DrawTrackedRight(g, ((int)Math.Round(prog * 100)) + "%", Theme.FontMonoSmall,
                barX + barW, barY - Theme.S(18), Theme.Sub, Theme.SF(1.4f));

            Theme.DrawTracked(g, "RHINE · LAB", Theme.FontMonoSmall, pad, Theme.S(376), Theme.Sub, Theme.SF(1.8f));
            Theme.DrawTrackedRight(g, "ARRAY / ACCESS", Theme.FontMonoSmall, right, Theme.S(376), Theme.Line, Theme.SF(1.4f));

            base.OnPaint(e);
        }

        private static string Clip(string text, double p)
        {
            int n = (int)Math.Round(text.Length * Math.Min(1.0, Math.Max(0.0, p)));
            return text.Substring(0, n);
        }

        private static double Ease(double p) { return 1.0 - Math.Pow(1.0 - p, 3.0); }

        private static double EaseOutBack(double p)
        {
            const double c1 = 1.70158, c3 = c1 + 1.0;
            double q = p - 1.0;
            return 1.0 + c3 * q * q * q + c1 * q * q;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null) { _timer.Stop(); _timer.Dispose(); }
                if (_ringCache != null) { _ringCache.Dispose(); _ringCache = null; }
                for (int i = 0; i < 6; i++)
                {
                    if (_strandInk[i] != null) _strandInk[i].Dispose();
                    if (_strandAmber[i] != null) _strandAmber[i].Dispose();
                    if (_bucketInk[i] != null) _bucketInk[i].Dispose();
                    if (_bucketAmber[i] != null) _bucketAmber[i].Dispose();
                }
                Pen[] rest = new Pen[] { _rung, _rungHot, _tick, _tickLong, _centerRing, _centerArc, _barTrack, _barFill };
                foreach (Pen p in rest) { if (p != null) p.Dispose(); }
            }
            base.Dispose(disposing);
        }
    }
}
