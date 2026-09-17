using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 接入过渡动画：服务启动成功、交接给 Web UI 之前播放。
    /// 与开启动画同一套语言（绿顶边细线卡片、宽字距排字），
    /// 结构参照站点开场：授权环闭合 → 黑底横条扫过公司名 → 冲击波扩散 → 淡出交接。
    /// 卡片尺寸与主窗口客户区一致，铺在原窗口之上，看上去像软件自身在过渡。
    /// </summary>
    internal class LinkTransition : Form
    {
        // 时间轴（秒）
        private const double TRing = 0.45;     // 双弧闭合
        private const double TMark = 0.25;     // 中心标志出现
        private const double TSweep = 0.50;    // 黑条开始扫过
        private const double TSweepEnd = 0.95;
        private const double THandoff = 1.15;  // 可选：若给了交接回调，在此刻触发（接入过渡不给，改为播完再打开）
        private const double TEnd = 1.30;      // 内容结束
        private const double TFade = 0.40;     // 淡出

        private readonly Timer _timer;
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly string _displayUrl;
        private readonly Action _handoff;
        private readonly Pen _arcPen;
        private readonly Pen _barFill;
        private readonly Pen _barTrack;
        private readonly Pen _wave;
        private bool _handedOff;
        private bool _closing;
        private double _timeShift;

        private string _title = "LINK / ESTABLISHED";
        private string _headline = "OPENING ARCHIVE";
        private string _sub = "";
        private string _footerWord = "ARRAY / ACCESS";

        /// <summary>进入自动部署时的过渡卡片：同样的版式与节奏，换文案。</summary>
        public static LinkTransition ForDeploy(string sub, Action handoff)
        {
            LinkTransition t = new LinkTransition("", handoff);
            t._title = "DEPLOY / REQUIRED";
            t._headline = "AUTO DEPLOYMENT";
            t._sub = sub == null ? "" : sub;
            t._footerWord = "BOOTSTRAP";
            return t;
        }

        /// <param name="displayUrl">卡片上展示的地址（不带令牌）</param>
        /// <param name="handoff">交接动作：真正打开浏览器</param>
        public LinkTransition(string displayUrl, Action handoff)
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            _displayUrl = displayUrl == null ? "" : displayUrl;
            _handoff = handoff;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.Manual;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            BackColor = Theme.Bg;
            KeyPreview = true;
            DoubleBuffered = true;
            // 必须在句柄创建前打开：否则动画里第一次改 Opacity 会触发句柄重建
            // （模态对话框重建句柄可能丢失模态状态甚至消失）
            AllowTransparency = true;

            _arcPen = new Pen(Theme.Ink, 2f);
            _barTrack = new Pen(Theme.LineSoft, 2f);
            _barFill = new Pen(Theme.Green, 2f);
            _wave = new Pen(Color.FromArgb(200, Theme.Green), 1.6f);

            _clock.Start();
            _timer = new Timer();
            _timer.Interval = 25;
            _timer.Tick += delegate(object s, EventArgs e) { Tick(); };
            _timer.Start();
        }

        /// <summary>把卡片精确铺到宿主窗口的客户区上。</summary>
        public void CoverOwner(Form owner)
        {
            if (owner == null) return;
            Rectangle area = owner.RectangleToScreen(owner.ClientRectangle);
            ClientSize = new Size(area.Width, area.Height);
            Location = new Point(area.Left, area.Top);
        }

        private double Now { get { return _clock.Elapsed.TotalSeconds + _timeShift; } }

        private void Tick()
        {
            double now = Now;
            if (!_handedOff && now >= THandoff && _handoff != null)
            {
                _handedOff = true;
                try { _handoff(); } catch { }
            }
            if (now >= TEnd + TFade)
            {
                if (!_closing) { _closing = true; Close(); }
                return;
            }
            // 淡入用入场曲线、淡出用退场曲线（原库的规矩：退场更短更急，且换另一条曲线）
            double inP = UiMotion.EaseIn(now / 0.18);
            double outP = now > TEnd ? UiMotion.EaseOut((now - TEnd) / TFade) : 0.0;
            Opacity = Math.Min(1.0, inP) * (now > TEnd ? Math.Max(0.0, 1.0 - outP) : 1.0);
            Invalidate();
        }

        private void Skip()
        {
            double now = Now;
            if (now < TEnd) _timeShift += TEnd - now;
        }

        protected override void OnShown(EventArgs e) { base.OnShown(e); Activate(); }
        protected override void OnMouseDown(MouseEventArgs e) { Skip(); base.OnMouseDown(e); }
        protected override void OnKeyDown(KeyEventArgs e) { Skip(); base.OnKeyDown(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            double now = Now;
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            Theme.CardFrame(g, w, h);

            float cx = w / 2f;
            float cy = h * 0.40f;                            // 环心：卡片高度 40% 处
            float R = Theme.SF(84);

            // ---- 授权环：双弧自上下闭合 ----
            double closeP = Ease(Math.Min(1.0, now / TRing));
            using (Pen p = new Pen(Color.FromArgb(90, Theme.Line), 1f))
                g.DrawEllipse(p, cx - R, cy - R, R * 2, R * 2);

            double sweepEach = 180.0 * closeP;
            if (sweepEach > 0.5)
            {
                _arcPen.Color = Theme.Ink;
                g.DrawArc(_arcPen, cx - R, cy - R, R * 2, R * 2, 90f, (float)sweepEach);
                g.DrawArc(_arcPen, cx - R, cy - R, R * 2, R * 2, 270f, (float)sweepEach);
            }

            // 环上的琥珀刻度点（闭合后依次点亮）
            if (closeP > 0.98)
            {
                int dots = 12;
                for (int i = 0; i < dots; i++)
                {
                    double a = i * 2.0 * Math.PI / dots;
                    double seq = (now * 1.6) - i * 0.06;
                    double wave = 0.5 + 0.5 * Math.Cos(seq * Math.PI * 2.0);
                    int alpha = (int)(60 + 170 * wave);
                    float r = Theme.SF(1.8f);
                    float px = cx + (float)Math.Cos(a) * R;
                    float py = cy + (float)Math.Sin(a) * R;
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(Math.Min(255, alpha), Theme.Amber)))
                        g.FillEllipse(b, px - r, py - r, r * 2, r * 2);
                }
            }

            // ---- 中心标志 ----
            double markAge = now - TMark;
            if (markAge > 0)
            {
                double p = Math.Min(1.0, markAge / 0.40);
                float scale = (float)EaseOutBack(p);
                float mw = Theme.SF(96) * scale;
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
            double typeP = Math.Min(1.0, now / 0.45);
            Theme.DrawTracked(g, Clip(_title, typeP), Theme.FontMonoSmall,
                pad, Theme.S(18), Theme.Sub, Theme.SF(1.6f));
            Theme.DrawTrackedRight(g, "v" + LauncherContext.Version, Theme.FontMonoSmall,
                right, Theme.S(18), Theme.Sub, Theme.SF(1.6f));

            double textP = Math.Min(1.0, Math.Max(0.0, (now - 0.55) / 0.30));
            if (textP > 0)
            {
                int alpha = (int)(255 * textP);
                Theme.DrawTrackedCenter(g, _headline, Theme.FontTechBold,
                    new Rectangle(0, (int)(h * 0.62f), w, Theme.S(22)),
                    Color.FromArgb(alpha, Theme.Ink), Theme.SF(3.0f));
                if (_displayUrl.Length > 0)
                {
                    TextRenderer.DrawText(g, _displayUrl, Theme.FontMono,
                        new Rectangle(0, (int)(h * 0.68f), w, Theme.S(20)), Color.FromArgb(alpha, Theme.Amber),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
                else if (_sub.Length > 0)
                {
                    TextRenderer.DrawText(g, _sub, Theme.FontSmall,
                        new Rectangle(Theme.S(40), (int)(h * 0.68f), w - Theme.S(80), Theme.S(20)), Color.FromArgb(alpha, Theme.Sub),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                }
            }

            // ---- 进度线 ----
            double prog = Math.Min(1.0, Math.Max(0.0, (now - 0.20) / (TEnd - 0.20)));
            int barX = Theme.S(90);
            int barW = w - barX * 2;
            int barY = h - Theme.S(96);
            g.DrawLine(_barTrack, barX, barY, barX + barW, barY);
            float fill = (float)(barW * Ease(prog));
            if (fill > 0.5f) g.DrawLine(_barFill, barX, barY, barX + fill, barY);
            Theme.DrawTrackedRight(g, ((int)Math.Round(prog * 100)) + "%", Theme.FontMonoSmall,
                barX + barW, barY - Theme.S(20), Theme.Sub, Theme.SF(1.4f));

            Theme.DrawTracked(g, "RHINE · LAB", Theme.FontMonoSmall, pad, h - Theme.S(42), Theme.Sub, Theme.SF(1.8f));
            Theme.DrawTrackedRight(g, _footerWord, Theme.FontMonoSmall, right, h - Theme.S(42), Theme.Line, Theme.SF(1.4f));

            // ---- 黑底横条扫过公司名（覆盖在最上层，与站点开场同一手法）----
            if (now >= TSweep && now <= TSweepEnd + 0.15)
            {
                double p = Math.Min(1.0, (now - TSweep) / (TSweepEnd - TSweep));
                float barH = Theme.SF(46);
                float barWid = w * 0.86f;
                float travel = (float)(-barWid + p * (w + barWid * 1.6));
                RectangleF bar = new RectangleF(travel, cy - barH / 2f, barWid, barH);
                Theme.Fill(g, Rectangle.Round(bar), Theme.Ink);
                Theme.DrawTracked(g, "DEEPSEEK HARNESS", Theme.FontTechBold,
                    (int)(bar.X + Theme.SF(24)), (int)(cy - Theme.SF(9)), Theme.PanelHi, Theme.SF(4.0f));
            }

            // ---- 冲击波：信号已发出 ----
            if (now > TEnd - 0.30)
            {
                double p = Math.Min(1.0, (now - (TEnd - 0.30)) / 0.30);
                float rr = R * (1f + (float)p * 5.5f);
                int alpha = (int)(200 * (1.0 - p));
                if (alpha > 3)
                {
                    _wave.Color = Color.FromArgb(alpha, Theme.Green);
                    g.DrawEllipse(_wave, cx - rr, cy - rr, rr * 2, rr * 2);
                }
            }

            base.OnPaint(e);
        }

        private static string Clip(string text, double p)
        {
            int n = (int)Math.Round(text.Length * Math.Min(1.0, Math.Max(0.0, p)));
            return text.Substring(0, n);
        }

        /// <summary>缓动统一收到 UiMotion（原库 ui-transitions.ts 的两条全局曲线）。</summary>
        private static double Ease(double p) { return UiMotion.EaseIn(p); }

        /// <summary>
        /// 唯一保留的过冲曲线：中心标志的弹入。
        /// 原库没有 overshoot 母题，所以这是本项目**有意保留的一处例外**（去掉就少了那一下"弹"）。
        /// </summary>
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
                Pen[] pens = new Pen[] { _arcPen, _barFill, _barTrack, _wave };
                foreach (Pen p in pens) { if (p != null) p.Dispose(); }
            }
            base.Dispose(disposing);
        }
    }
}
