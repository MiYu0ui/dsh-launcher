using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// Rhine Lab 档案终端风格的主题。
    /// 色值取自 RhineLabUI 的设计基准：暖灰白底、近黑文字、细线、暖杏金信号，
    /// 暗面板为 #11181b / #263136 / 分隔线 #536166。
    /// </summary>
    internal static class Theme
    {
        private static float _scale = 1f;

        public static float Scale { get { return _scale; } }
        public static void InitScale(float scale) { if (scale > 0.5f && scale < 4f) _scale = scale; }

        /// <summary>设计像素 -> 实际像素（DPI 150% 时 1.5 倍）。</summary>
        public static int S(int designPx) { return (int)Math.Round(designPx * _scale); }
        public static float SF(float designPx) { return designPx * _scale; }

        // ---- 亮面（主界面） ----
        public static readonly Color Bg = Color.FromArgb(0xEA, 0xE5, 0xE1);      // 统一背景
        public static readonly Color BgShade = Color.FromArgb(0xDF, 0xD9, 0xD3);
        public static readonly Color Panel = Color.FromArgb(0xF5, 0xF2, 0xEF);
        public static readonly Color PanelHi = Color.FromArgb(0xFC, 0xFA, 0xF8);
        public static readonly Color Ink = Color.FromArgb(0x08, 0x0A, 0x08);     // 近黑正文
        public static readonly Color InkSoft = Color.FromArgb(0x3C, 0x3A, 0x35);
        public static readonly Color Sub = Color.FromArgb(0x77, 0x75, 0x6D);     // 暖灰次要
        public static readonly Color Line = Color.FromArgb(0xAA, 0xA5, 0x9A);    // 细线
        public static readonly Color LineSoft = Color.FromArgb(0xCB, 0xC6, 0xBE);
        public static readonly Color Amber = Color.FromArgb(0x9B, 0x72, 0x47);   // 暖褐（强调/悬停）
        public static readonly Color AmberHi = Color.FromArgb(0xC5, 0xA1, 0x6B); // 暖杏金信号

        // ---- 绿色强调（顶部边框）----
        // 与暖灰白 / 暖杏金同属低饱和体系，避免跳色：偏冷的深松绿 + 柔和版
        public static readonly Color Green = Color.FromArgb(0x2E, 0x7D, 0x5B);
        public static readonly Color GreenSoft = Color.FromArgb(0x6C, 0xB0, 0x8F);
        public static readonly Color GreenMist = Color.FromArgb(0xDD, 0xEA, 0xE2);

        // ---- 暗面（环形加载器所在的"屏幕"） ----
        public static readonly Color Dark = Color.FromArgb(0x11, 0x18, 0x1B);
        public static readonly Color DarkPanel = Color.FromArgb(0x26, 0x31, 0x36);
        public static readonly Color DarkLine = Color.FromArgb(0x53, 0x61, 0x66);
        public static readonly Color DarkText = Color.FromArgb(0xE0, 0xE3, 0xDC);
        public static readonly Color DarkSub = Color.FromArgb(0xA6, 0xB0, 0xB1);

        // ---- 状态信号 ----
        public static readonly Color SignalRun = Color.FromArgb(0x9B, 0x72, 0x47);
        public static readonly Color SignalBusy = Color.FromArgb(0xC5, 0xA1, 0x6B);
        public static readonly Color SignalIdle = Color.FromArgb(0x9A, 0x96, 0x8C);
        public static readonly Color SignalAlert = Color.FromArgb(0xA0, 0x4A, 0x3C);

        // ---- 字体 ----
        private static string _cjk;
        private static string _tech;
        private static string _mono;

        private static string PickFamily(string[] candidates, string fallback)
        {
            foreach (string name in candidates)
            {
                try { using (FontFamily ff = new FontFamily(name)) { return name; } }
                catch { }
            }
            return fallback;
        }

        // 站点的后备字体链正是 PingFang SC / Microsoft YaHei / system-ui
        private static string Cjk
        {
            get
            {
                if (_cjk == null)
                    _cjk = PickFamily(new string[] { "MiSans", "Microsoft YaHei UI", "Microsoft YaHei", "PingFang SC", "Segoe UI" },
                                      FontFamily.GenericSansSerif.Name);
                return _cjk;
            }
        }

        /// <summary>科技感拉丁字形：站点的 Novecento Sans Wide 是几何宽体，这里用 DIN 系的 Bahnschrift 近似。</summary>
        private static string Tech
        {
            get
            {
                if (_tech == null)
                    _tech = PickFamily(new string[] { "Bahnschrift", "Segoe UI Variable Display", "Segoe UI" },
                                       FontFamily.GenericSansSerif.Name);
                return _tech;
            }
        }

        private static string Mono
        {
            get
            {
                if (_mono == null)
                    _mono = PickFamily(new string[] { "Cascadia Mono", "Consolas", "Microsoft YaHei UI" },
                                       FontFamily.GenericMonospace.Name);
                return _mono;
            }
        }

        // 字号用 pt 会随 DPI 自动放大；只有像素几何需要乘 Scale
        public static Font FontUi { get { return new Font(Cjk, 9.75f, FontStyle.Regular, GraphicsUnit.Point); } }
        public static Font FontUiBold { get { return new Font(Cjk, 9.75f, FontStyle.Bold, GraphicsUnit.Point); } }
        public static Font FontSmall { get { return new Font(Cjk, 8.25f, FontStyle.Regular, GraphicsUnit.Point); } }
        public static Font FontStatus { get { return new Font(Cjk, 15f, FontStyle.Bold, GraphicsUnit.Point); } }
        public static Font FontWord { get { return new Font(Tech, 19f, FontStyle.Bold, GraphicsUnit.Point); } }
        public static Font FontTech { get { return new Font(Tech, 10f, FontStyle.Regular, GraphicsUnit.Point); } }
        public static Font FontTechBold { get { return new Font(Tech, 10f, FontStyle.Bold, GraphicsUnit.Point); } }
        public static Font FontMono { get { return new Font(Mono, 9f, FontStyle.Regular, GraphicsUnit.Point); } }
        public static Font FontMonoSmall { get { return new Font(Mono, 8.25f, FontStyle.Regular, GraphicsUnit.Point); } }

        // ---- 绘制工具 ----
        public static void Fill(Graphics g, Rectangle r, Color c)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            using (SolidBrush b = new SolidBrush(c)) g.FillRectangle(b, r);
        }

        /// <summary>1 像素细线（Rhine Lab 的基本组织元素）。</summary>
        public static void Rule(Graphics g, int x1, int y, int x2, Color c)
        {
            using (Pen p = new Pen(c, 1f)) g.DrawLine(p, x1, y, x2, y);
        }

        public static void VRule(Graphics g, int x, int y1, int y2, Color c)
        {
            using (Pen p = new Pen(c, 1f)) g.DrawLine(p, x, y1, x, y2);
        }

        // ---- 动效缓动（全部动画共用同一套曲线，衔接才一致）----
        public static double Clamp01(double p) { return p < 0.0 ? 0.0 : (p > 1.0 ? 1.0 : p); }

        /// <summary>进度区间映射：在 [start, end] 秒之间取 0→1。</summary>
        public static double Phase(double now, double start, double end)
        {
            if (end <= start) return now >= end ? 1.0 : 0.0;
            return Clamp01((now - start) / (end - start));
        }

        public static double EaseOutCubic(double p) { return 1.0 - Math.Pow(1.0 - Clamp01(p), 3.0); }
        public static double EaseInCubic(double p) { double q = Clamp01(p); return q * q * q; }

        public static double EaseInOutCubic(double p)
        {
            double q = Clamp01(p);
            return q < 0.5 ? 4.0 * q * q * q : 1.0 - Math.Pow(-2.0 * q + 2.0, 3.0) / 2.0;
        }

        public static double EaseOutBack(double p)
        {
            const double c1 = 1.70158, c3 = c1 + 1.0;
            double q = Clamp01(p) - 1.0;
            return 1.0 + c3 * q * q * q + c1 * q * q;
        }

        public static int Lerp(int a, int b, double p) { return (int)Math.Round(a + (b - a) * Clamp01(p)); }

        public static Rectangle Lerp(Rectangle a, Rectangle b, double p)
        {
            return new Rectangle(Lerp(a.X, b.X, p), Lerp(a.Y, b.Y, p), Lerp(a.Width, b.Width, p), Lerp(a.Height, b.Height, p));
        }

        public static void StrokeRect(Graphics g, Rectangle r, Color c, float width)        {
            using (Pen p = new Pen(c, width)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
        }

        /// <summary>四角刻度线（技术制图感）。</summary>
        public static void CornerTicks(Graphics g, Rectangle r, Color c, int len)
        {
            using (Pen p = new Pen(c, 1f))
            {
                g.DrawLine(p, r.Left, r.Top, r.Left + len, r.Top);
                g.DrawLine(p, r.Left, r.Top, r.Left, r.Top + len);
                g.DrawLine(p, r.Right - 1, r.Top, r.Right - 1 - len, r.Top);
                g.DrawLine(p, r.Right - 1, r.Top, r.Right - 1, r.Top + len);
                g.DrawLine(p, r.Left, r.Bottom - 1, r.Left + len, r.Bottom - 1);
                g.DrawLine(p, r.Left, r.Bottom - 1, r.Left, r.Bottom - 1 - len);
                g.DrawLine(p, r.Right - 1, r.Bottom - 1, r.Right - 1 - len, r.Bottom - 1);
                g.DrawLine(p, r.Right - 1, r.Bottom - 1, r.Right - 1, r.Bottom - 1 - len);
            }
        }

        // ---- 宽字距排字 ----
        /// <summary>
        /// 动画卡片的统一外框：绿色顶边 + 双层细线框 + 四角刻度。
        /// 开启动画与接入过渡动画共用，保证两段动画是同一套语言。
        /// </summary>
        public static void CardFrame(Graphics g, int w, int h)
        {
            Fill(g, new Rectangle(0, 0, w, h), Bg);
            Fill(g, new Rectangle(0, 0, w, S(3)), Green);
            StrokeRect(g, new Rectangle(0, 0, w - 1, h - 1), Line, 1f);
            Rectangle inner = new Rectangle(S(6), S(6), w - S(13), h - S(13));
            StrokeRect(g, inner, LineSoft, 1f);
            CornerTicks(g, Rectangle.Inflate(inner, -S(5), -S(5)), Line, S(12));
        }

        public static Size MeasureTracked(Graphics g, string text, Font font, float tracking)
        {
            int w = 0, h = 0;
            foreach (char ch in text)
            {
                Size s = TextRenderer.MeasureText(g, ch.ToString(), font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                w += s.Width + (int)Math.Round(tracking);
                if (s.Height > h) h = s.Height;
            }
            if (w > 0) w -= (int)Math.Round(tracking);
            return new Size(w, h);
        }

        public static void DrawTracked(Graphics g, string text, Font font, int x, int y, Color color, float tracking)
        {
            int cx = x;
            foreach (char ch in text)
            {
                Size s = TextRenderer.MeasureText(g, ch.ToString(), font, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                TextRenderer.DrawText(g, ch.ToString(), font, new Point(cx, y), color, TextFormatFlags.NoPadding);
                cx += s.Width + (int)Math.Round(tracking);
            }
        }

        // ---- 动态边框（缺口沿轮廓移动，取自站点标志轮廓的动法）----

        public static void DrawTrackedRight(Graphics g, string text, Font font, int right, int y, Color color, float tracking)
        {
            Size s = MeasureTracked(g, text, font, tracking);
            DrawTracked(g, text, font, right - s.Width, y, color, tracking);
        }

        /// <summary>返回矩形轮廓上距起点 d 像素处的点（顺时针：上→右→下→左）。</summary>
        public static PointF PerimeterPoint(Rectangle r, double d)
        {
            double w = r.Width, h = r.Height;
            double total = 2.0 * (w + h);
            if (total <= 0) return new PointF(r.Left, r.Top);
            d = d % total;
            if (d < 0) d += total;
            if (d < w) return new PointF(r.Left + (float)d, r.Top);
            d -= w;
            if (d < h) return new PointF(r.Right, r.Top + (float)d);
            d -= h;
            if (d < w) return new PointF(r.Right - (float)d, r.Bottom);
            d -= w;
            return new PointF(r.Left, r.Bottom - (float)d);
        }

        /// <summary>脉冲值：在 [min,max] 之间按周期正弦往返。</summary>
        public static double Pulse(double period, double offset, double min, double max)
        {
            double p = Anim.Cycle(period, offset);
            double s = 0.5 - 0.5 * Math.Cos(p * Math.PI * 2.0);
            return min + (max - min) * s;
        }

        /// <summary>
        /// 会呼吸的细线边框：静态细线 + 四角刻度（透明度脉动）+ 一道沿轮廓巡行的彗尾高光。
        /// </summary>
        public static void DrawLiveFrame(Graphics g, Rectangle r, Color line, Color accent,
                                         bool ticks, double period, double phase)
        {
            StrokeRect(g, r, line, 1f);

            if (ticks)
            {
                int a = (int)Pulse(3.4, phase, 90, 235);
                CornerTicks(g, Rectangle.Inflate(r, -S(5), -S(5)), Color.FromArgb(a, line), S(10));
            }

            // 彗尾：沿周长行进的一小段高光，尾部渐隐
            double perim = 2.0 * (r.Width + r.Height);
            if (perim < S(60)) return;
            double head = Anim.Cycle(period, phase) * perim;
            double len = Math.Max(S(46), perim * 0.13);
            int steps = 14;
            float width = Math.Max(1.4f, SF(1.6f));
            for (int i = 0; i < steps; i++)
            {
                double d0 = head - len * (i + 1) / steps;
                double d1 = head - len * i / steps;
                double k = 1.0 - (double)i / steps;          // 0 尾部 → 1 头部
                int alpha = (int)(210 * k * k);
                if (alpha < 6) continue;
                PointF p0 = PerimeterPoint(r, d0);
                PointF p1 = PerimeterPoint(r, d1);
                using (Pen pen = new Pen(Color.FromArgb(alpha, accent), width))
                    g.DrawLine(pen, p0, p1);
            }
        }

        /// <summary>带巡行高光的细线：静态线 + 一段扫过的高光（站点"横向扫过"的静态化用法）。</summary>
        public static void SweepRule(Graphics g, int x1, int x2, int y, Color lineColor, Color sweepColor,
                                     double period, double phase)
        {
            Rule(g, x1, y, x2, lineColor);

            int span = x2 - x1;
            if (span < S(40)) return;
            int width = Math.Max(S(60), (int)(span * 0.18));
            double p = Anim.Cycle(period, phase);
            int head = x1 + (int)(p * (span + width * 2)) - width;

            int steps = 12;
            for (int i = 0; i < steps; i++)
            {
                int a0 = head - width + width * i / steps;
                int a1 = head - width + width * (i + 1) / steps;
                double k = (double)i / (steps - 1);          // 0 尾 → 1 头
                int alpha = (int)(120 * k * k);
                if (alpha < 6) continue;
                int cx0 = Math.Max(x1, a0), cx1 = Math.Min(x2, a1);
                if (cx1 <= cx0) continue;
                using (Pen pen = new Pen(Color.FromArgb(alpha, sweepColor), 1f))
                    g.DrawLine(pen, cx0, y, cx1, y);
            }
        }

        public static void DrawTrackedCenter(Graphics g, string text, Font font, Rectangle bounds, Color color, float tracking)
        {
            Size s = MeasureTracked(g, text, font, tracking);
            DrawTracked(g, text, font, bounds.X + (bounds.Width - s.Width) / 2,
                        bounds.Y + (bounds.Height - s.Height) / 2, color, tracking);
        }
    }
}
