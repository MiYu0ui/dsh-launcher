using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 版式元素画法 —— 从《莱茵生命 PPT 模板》与 RhineLabUI 的界面里拆出来的六个母题。
    /// 每个函数都接受一个 <c>p</c>（0..1 的显形进度），由 <see cref="WindowReveal"/> 按时间轴喂进来，
    /// 所以「入场动画」本质上是**这套静态版式在逐笔就位**，而不是盖住内容再揭开。
    ///
    /// 母题清单（对应模板里的具体元素）：
    ///   ① 四角括号   —— 1px L 形，只画四个角，比整框轻
    ///   ② 标签块     —— 近黑实心矩形 + 白色大写字距小字（模板里是 SERVICES SECT.）
    ///   ③ 高亮条     —— 浅色实心长条，压在标题背后（模板里是 RHINE LAB.LLC. 那条）
    ///   ④ 章节导轨   —— 1px 竖线 + 端点圆点 + 短引线（模板里的 leader line + dot）
    ///   ⑤ 极淡网格   —— 模板的"网格透明图"，工程纸质感
    ///   ⑥ 收尾短线   —— POWERED BY RHINE LAB 后面那一小段粗横线
    /// </summary>
    internal static class UiPaint
    {
        // ---------------- ① 四角括号 ----------------

        /// <summary>括号臂长（设计像素）。模板里约 14px@1920，桌面上取 14 更看得清。</summary>
        public const int BracketArm = 14;

        /// <summary>
        /// 画四个角的 L 形括号。四个 <paramref name="phases"/> 各有自己的进度（左上 → 右上 → 左下 → 右下），
        /// 每支括号沿自己的两条臂**从角点向外**长出来。
        /// </summary>
        /// <param name="g">目标画布。</param>
        /// <param name="r">要围出四角的外接矩形，括号贴它的四条边。</param>
        /// <param name="c">括号颜色；实际 alpha 随各自的进度从 25% 升到 100%。</param>
        /// <param name="phases">四支括号的显形进度（0..1）。数组为空、长度不足 4 或为 null 时，缺的那几支按"已完成"（1.0）画。</param>
        /// <remarks>
        /// 进度小于等于 0.01 的括号整支不画；臂长按进度取整后为 0 也跳过 —— 避免先落一个点再长出来。
        /// 线宽取 <see cref="Theme.SF"/> 缩放后的值但不低于 1px，免得高 DPI 下细线消失。
        /// </remarks>
        public static void Brackets(Graphics g, Rectangle r, Color c, double[] phases)
        {
            int arm = Theme.S(BracketArm);
            int[] xs = new int[] { r.Left, r.Right, r.Left, r.Right };
            int[] ys = new int[] { r.Top, r.Top, r.Bottom, r.Bottom };
            int[] dx = new int[] { 1, -1, 1, -1 };
            int[] dy = new int[] { 1, 1, -1, -1 };

            for (int i = 0; i < 4; i++)
            {
                double p = (phases != null && i < phases.Length) ? UiMotion.Clamp01(phases[i]) : 1.0;
                if (p <= 0.01) continue;
                int len = (int)Math.Round(arm * p);
                if (len <= 0) continue;
                Color col = Color.FromArgb((int)(255 * Math.Min(1.0, 0.25 + 0.75 * p)), c);
                using (Pen pen = new Pen(col, Math.Max(1f, Theme.SF(1f))))
                {
                    g.DrawLine(pen, xs[i], ys[i], xs[i] + dx[i] * len, ys[i]);
                    g.DrawLine(pen, xs[i], ys[i], xs[i], ys[i] + dy[i] * len);
                }
            }
        }

        // ---------------- ② 标签块（chip）----------------

        /// <summary>标签块高度（设计像素）。</summary>
        public const int ChipH = 15;
        // ChipPadX 与 ChipTracking 不对外：它们与上面的高度、下面的测量/绘制一起定死了标签块的版式，
        // 改动会同时改变 ChipWidth 量出的宽度与 Chip 的版式。
        private const int ChipPadX = 6;
        private const float ChipTracking = 1.4f;

        /// <summary>
        /// 量出标签块的宽度（含内边距）。
        /// ⚠️ 逐字测量与逐字绘制之间会有几像素的差（字形右侧 overhang），
        /// 所以这里额外留 3px 余量 —— 少了这个余量就会出现"黑框比字窄"。
        /// </summary>
        /// <returns>标签块总宽（含左右内边距与 3px 余量），不是文字的宽度。</returns>
        public static int ChipWidth(Graphics g, string text)
        {
            Size s = Theme.MeasureTracked(g, text, Theme.FontMonoSmall, Theme.SF(ChipTracking));
            return s.Width + Theme.S(ChipPadX * 2) + Theme.S(3);
        }

        /// <summary>
        /// 近黑标签块 + 白色大写字距小字。<paramref name="p"/> 控制"从左往右刷出来"的进度。
        ///
        /// ⚠️ 这里**不能用 SetClip**：这套排字走 TextRenderer（GDI），而 **GDI 文字不认 GDI+ 的裁剪区** ——
        /// 框被裁窄了、里面的白字却照画不误，于是就是"黑框没盖住字"。改成框画到擦入宽度、
        /// 文字用手动截断（maxX）同步收住。
        /// </summary>
        /// <returns>
        /// 整个标签块所占的宽度（与 <see cref="ChipWidth"/> 同值），调用方据此接着往右排后面的内容；
        /// 进度尚未起步（返回 0）时它同样返回 0。
        /// </returns>
        public static int Chip(Graphics g, int x, int y, string text, double p)
        {
            int w = ChipWidth(g, text);
            int h = Theme.S(ChipH);
            double q = UiMotion.Clamp01(p);
            if (q <= 0.01) return 0;

            int wipe = (int)Math.Round(w * UiMotion.EaseIn(q));
            if (wipe <= 0) return 0;
            if (wipe > w) wipe = w;

            Theme.Fill(g, new Rectangle(x, y, wipe, h), Theme.Ink);
            Theme.DrawTracked(g, text, Theme.FontMonoSmall, x + Theme.S(ChipPadX),
                              y + Theme.S(3), Theme.PanelHi, Theme.SF(ChipTracking), x + wipe);
            return w;
        }

        // ---------------- ③ 高亮条 ----------------

        /// <summary>
        /// 浅色高亮条：从左侧刷入，右端一个小圈（模板里 RHINE LAB.LLC. 末尾那个 © 的位置）。
        /// 标题文字由调用方压在它上面画。
        /// </summary>
        /// <param name="g">目标画布。</param>
        /// <param name="r">高亮条的位置与尺寸，宽度是"刷满"时的最终宽度。</param>
        /// <param name="p">刷入进度（0..1），内部按 0..1 夹紧。</param>
        /// <param name="circle"><c>true</c> 时在右端画装饰小圈；它额外要等 <paramref name="p"/> 超过 0.75 才出现。</param>
        public static void HighlightBar(Graphics g, Rectangle r, double p, bool circle)
        {
            double q = UiMotion.Clamp01(p);
            if (q <= 0.01) return;
            int w = (int)Math.Round(r.Width * UiMotion.EaseIn(q));
            if (w <= 0) return;
            Theme.Fill(g, new Rectangle(r.X, r.Y, w, r.Height), Theme.PanelHi);

            if (circle && q > 0.75)
            {
                int rad = Theme.S(4);
                using (Pen pen = new Pen(Theme.Line, Math.Max(1f, Theme.SF(1f))))
                    g.DrawEllipse(pen, r.Right - Theme.S(14) - rad, r.Y + r.Height / 2 - rad, rad * 2, rad * 2);
            }
        }

        // ---------------- ④ 章节导轨 ----------------

        /// <summary>
        /// 章节导轨：左侧 1px 竖线自上而下画出；线上每个节点 = 一个实心圆点 + 一段指向右侧的短引线。
        /// <paramref name="dotPhases"/> 与 <paramref name="dotYs"/> 一一对应，各自控制显形。
        /// 这就是模板里那套 "leader line + dot"：**它明确告诉你这块内容在哪**。
        /// </summary>
        /// <param name="g">目标画布。</param>
        /// <param name="x">竖线的横坐标，圆点就压在这条线上。</param>
        /// <param name="yTop">导轨起点（上端）；竖线总是由这里向下画。</param>
        /// <param name="yBottom">导轨终点（下端），与 <paramref name="progress"/> 一起决定此刻画到哪。</param>
        /// <param name="progress">导轨自上而下的画出进度（0..1）。</param>
        /// <param name="dotYs">各节点的纵坐标；为 null 则整条导轨只画线、不画节点。</param>
        /// <param name="dotPhases">各节点的显形进度，与 <paramref name="dotYs"/> 按下标一一对应。</param>
        /// <remarks>
        /// <paramref name="dotPhases"/> 为 null 或长度不够时，缺的节点退化为跟随 <paramref name="progress"/>。
        /// 纵坐标落在导轨当前末端之下的节点会被跳过（留 2px 余量）：导轨还没走到，圆点就不该先冒出来。
        /// </remarks>
        public static void SectionRail(Graphics g, int x, int yTop, int yBottom, double progress,
                                       int[] dotYs, double[] dotPhases)
        {
            double p = UiMotion.Clamp01(progress);
            if (p <= 0.005) return;

            int railBottom = yTop + (int)Math.Round((yBottom - yTop) * UiMotion.EaseIn(p));
            using (Pen pen = new Pen(Theme.Line, Math.Max(1f, Theme.SF(1f))))
                g.DrawLine(pen, x, yTop, x, railBottom);

            if (dotYs == null) return;
            for (int i = 0; i < dotYs.Length; i++)
            {
                double dp = (dotPhases != null && i < dotPhases.Length) ? UiMotion.Clamp01(dotPhases[i]) : p;
                if (dp <= 0.02) continue;
                int y = dotYs[i];
                if (y > railBottom + Theme.S(2)) continue;      // 导轨还没走到这一节

                int rad = Theme.S(3);
                using (SolidBrush b = new SolidBrush(Theme.Ink))
                    g.FillEllipse(b, x - rad, y - rad, rad * 2, rad * 2);

                int len = (int)Math.Round(Theme.S(11) * dp);
                if (len > 0)
                {
                    int a = (int)(255 * Math.Min(1.0, 0.35 + 0.65 * dp));
                    using (Pen pen = new Pen(Color.FromArgb(a, Theme.Line), Math.Max(1f, Theme.SF(1f))))
                        g.DrawLine(pen, x + rad + 1, y, x + rad + 1 + len, y);
                }
            }
        }

        // ---------------- ⑤ 极淡网格 ----------------

        /// <summary>
        /// 极淡方格（模板的"网格透明图"）。间距 44 设计像素，1px 线，透明度很低 ——
        /// 它是**纸质**而不是**图案**，所以宁可再淡一点也别抢内容。
        /// <paramref name="p"/> 让网格自上而下浮出来。
        /// </summary>
        /// <param name="g">目标画布。</param>
        /// <param name="r">网格覆盖的矩形。</param>
        /// <param name="p">浮现进度（0..1）；它同时控制线的不透明度和"画到多深"。</param>
        /// <param name="pitch">格线间距（像素）。调用方负责缩放，这里直接用。</param>
        /// <param name="alpha">网格满显形时的最大 alpha（0..255）。</param>
        /// <remarks>
        /// 横线画在 <c>r.Top + pitch</c> 起步、竖线画在 <c>r.Left + pitch</c> 起步，
        /// 所以最上 / 最左那一条边框线不会被重复描一遍。是否够淡取决于调用方给的
        /// <paramref name="alpha"/>（当前各处传 16），别把这条压给这个函数。
        /// </remarks>
        public static void Grid(Graphics g, Rectangle r, double p, int pitch, int alpha)
        {
            double q = UiMotion.Clamp01(p);
            if (q <= 0.01 || alpha <= 0) return;
            int a = (int)Math.Round(alpha * UiMotion.EaseIn(q));
            if (a <= 1) return;

            int reach = r.Top + (int)Math.Round(r.Height * UiMotion.EaseIn(q));
            Color c = Color.FromArgb(a, Theme.Line);
            using (Pen pen = new Pen(c, 1f))
            {
                for (int y = r.Top + pitch; y < reach; y += pitch)
                    g.DrawLine(pen, r.Left, y, r.Right, y);
                for (int x = r.Left + pitch; x < r.Right; x += pitch)
                    g.DrawLine(pen, x, r.Top, x, Math.Min(r.Bottom, reach));
            }
        }

        // ---------------- ⑥ 收尾短线 ----------------

        /// <summary>POWERED BY 之后那一小段粗横线。</summary>
        /// <param name="g">目标画布。</param>
        /// <param name="right">对齐用的右边界；短线钉在这个坐标上，从它往左生长。</param>
        /// <param name="y">短线上沿的纵坐标。</param>
        /// <param name="p">生长进度（0..1），总长固定为 22 设计像素。</param>
        public static void EndDash(Graphics g, int right, int y, double p)
        {
            double q = UiMotion.Clamp01(p);
            if (q <= 0.01) return;
            int total = Theme.S(22);
            int w = (int)Math.Round(total * UiMotion.EaseIn(q));
            if (w <= 0) return;
            Theme.Fill(g, new Rectangle(right - total, y, w, Theme.S(3)), Theme.Ink);
        }
    }
}
