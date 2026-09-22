using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>无边框窗口的拖动支持。</summary>
    internal static class WindowChrome
    {
        /// <summary>释放当前线程的鼠标捕获；不先释放，下面那条非客户区消息会被当成"拖动已有捕获的窗口"而被忽略。</summary>
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        /// <summary>向窗口过程投递消息；这里只用来把按下伪装成标题栏按下。</summary>
        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        /// <summary>WM_NCLBUTTONDOWN：非客户区左键按下。系统收到它才会按"点的是哪一块"去分发。</summary>
        private const int WM_NCLBUTTONDOWN = 0x00A1;
        /// <summary>HTCAPTION：命中标题栏 —— 于是拖动、双击最大化、拖到屏幕边缘贴边全都由系统照常处理。</summary>
        private const int HTCAPTION = 2;

        /// <summary>把当前鼠标按下当作"按住标题栏"，交给系统去做拖动/贴边。</summary>
        /// <param name="form">要被拖动的窗体；为 null 直接返回。</param>
        /// <remarks>
        /// 必须在鼠标按下的事件里调用，系统的拖动循环会就地接管到左键松开为止。
        /// 窗体尚未创建句柄时 <c>Handle</c> 会现创建句柄，所以别在构造期间就调它。
        /// 整个调用包在 try 里：拖动只是便利功能，失败也不该把异常抛进控件的消息处理里。
        /// </remarks>
        public static void BeginDrag(Form form)
        {
            if (form == null) return;
            try
            {
                ReleaseCapture();
                SendMessage(form.Handle, WM_NCLBUTTONDOWN, (IntPtr)HTCAPTION, IntPtr.Zero);
            }
            catch { }
        }
    }

    /// <summary>无边框窗口右上角的自绘按钮（最小化 / 关闭 / 齿轮设置 / 问号帮助），细线字形。</summary>
    internal class ChromeButton : Control
    {
        /// <summary>字形种类：最小化、关闭、齿轮（设置）、问号（帮助）。窗口没有系统标题栏，这几笔全靠自绘。</summary>
        public enum GlyphKind { Minimize, Close, Gear, Help }

        private readonly GlyphKind _kind;
        private bool _hover;

        /// <summary>按下即触发（不用 Click）：窗口未激活时，第一下点击会被系统用于激活而吞掉 Click。</summary>
        public event EventHandler Invoked;

        /// <summary>按字形种类构造；背景用面板色，与标题栏同色，视觉上是一块无缝的区域。</summary>
        public ChromeButton(GlyphKind kind)
        {
            _kind = kind;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            BackColor = Theme.Panel;
            TabStop = false;
        }

        /// <summary>进入悬停：整块按钮填暖褐（关闭键填危险色），字形反白。</summary>
        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        /// <summary>离开悬停：恢复面板色 + 深灰字形。</summary>
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        /// <summary>
        /// 左键按下即回调 <see cref="Invoked"/>：不等 Click。
        /// 窗口未激活时第一次点击会被系统用来激活窗口，Click 就丢了 —— 那样用户得点两下才关得掉。
        /// </summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                EventHandler h = Invoked;
                if (h != null) { try { h(this, EventArgs.Empty); } catch { } }
            }
            base.OnMouseDown(e);
        }

        /// <summary>自绘：底色（悬停时整块高亮）→ 按种类画字形（横线 / 叉 / 齿轮 / 问号）。</summary>
        /// <remarks>
        /// 直线字形一律关抗锯齿、用 1px 细线，和这套界面的细线语言一致；
        /// 齿轮与问号是曲线，在那两个方法内部临时开抗锯齿并还原。
        /// </remarks>
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            Theme.Fill(g, ClientRectangle, BackColor);

            bool close = _kind == GlyphKind.Close;
            bool isGear = _kind == GlyphKind.Gear;
            bool isHelp = _kind == GlyphKind.Help;
            Color glyph = Theme.InkSoft;
            if (_hover)
            {
                Color fill = close ? Theme.SignalAlert : Theme.Amber;
                Theme.Fill(g, ClientRectangle, fill);
                glyph = Theme.PanelHi;
            }

            int cx = Width / 2;
            int cy = Height / 2;
            int half = Math.Max(4, Theme.S(5));
            if (isGear)
            {
                DrawGear(g, cx, cy, glyph);
                return;
            }
            if (isHelp)
            {
                DrawQuestion(g, cx, cy, glyph);
                return;
            }
            using (Pen p = new Pen(glyph, Math.Max(1f, Theme.SF(1.2f))))
            {
                if (_kind == GlyphKind.Minimize)
                {
                    g.DrawLine(p, cx - half, cy, cx + half, cy);
                }
                else
                {
                    g.DrawLine(p, cx - half, cy - half, cx + half, cy + half);
                    g.DrawLine(p, cx + half, cy - half, cx - half, cy + half);
                }
            }
        }

        /// <summary>
        /// 问号：纯 stroke，与齿轮同一套细线语言（上半弧 + 竖钩 + 圆点）。
        /// 上半弧用一个开口朝下的椭圆弧，竖钩从弧尾收到中心，最后点一个圆头短竖当点。
        /// </summary>
        private void DrawQuestion(Graphics g, int cx, int cy, Color c)
        {
            SmoothingMode saved = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                float r = Theme.SF(3.6f);          // 上半弧半径
                float top = cy - Theme.SF(3.4f);   // 弧心
                using (Pen p = new Pen(c, Math.Max(1f, Theme.SF(1.15f))))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    // 弧：从左下起、逆时针划过顶部到右下（留出下方开口）
                    g.DrawArc(p, cx - r, top - r, r * 2f, r * 2f, 200f, 230f);
                    // 竖钩：从弧的右下端收到中心线
                    g.DrawLine(p, cx + r * 0.72f, top + r * 0.72f, cx, top + r * 1.85f);
                    // 点：圆头短竖 = 圆点
                    g.DrawLine(p, cx, cy + Theme.SF(3.6f), cx, cy + Theme.SF(4.4f));
                }
            }
            finally { g.SmoothingMode = saved; }
        }

        /// <summary>
        /// 经典齿轮：齿根圆 + 8 根圆头齿 + 中心孔，纯 stroke 绘制，与这套界面的细线语言一致。
        /// 打开抗锯齿（其余字形是直线，关着也无所谓；齿轮是曲线，必须开）。
        /// </summary>
        private void DrawGear(Graphics g, int cx, int cy, Color c)
        {
            SmoothingMode saved = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            try
            {
                float R = Theme.SF(6.2f);        // 齿根圆半径
                float tooth = Theme.SF(3.0f);    // 齿长
                float hole = Theme.SF(2.0f);     // 中心孔半径
                const int teeth = 8;
                using (Pen p = new Pen(c, Math.Max(1f, Theme.SF(1.15f))))
                {
                    p.StartCap = LineCap.Round;
                    p.EndCap = LineCap.Round;
                    g.DrawEllipse(p, cx - R, cy - R, R * 2f, R * 2f);
                    for (int i = 0; i < teeth; i++)
                    {
                        double a = i * 2.0 * Math.PI / teeth;
                        float x1 = cx + (float)Math.Cos(a) * R;
                        float y1 = cy + (float)Math.Sin(a) * R;
                        float x2 = cx + (float)Math.Cos(a) * (R + tooth);
                        float y2 = cy + (float)Math.Sin(a) * (R + tooth);
                        g.DrawLine(p, x1, y1, x2, y2);
                    }
                    g.DrawEllipse(p, cx - hole, cy - hole, hole * 2f, hole * 2f);
                }
            }
            finally { g.SmoothingMode = saved; }
        }
    }
}
