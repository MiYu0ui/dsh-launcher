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
        [DllImport("user32.dll")]
        private static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        private const int WM_NCLBUTTONDOWN = 0x00A1;
        private const int HTCAPTION = 2;

        /// <summary>把当前鼠标按下当作"按住标题栏"，交给系统去做拖动/贴边。</summary>
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
        public enum GlyphKind { Minimize, Close, Gear, Help }

        private readonly GlyphKind _kind;
        private bool _hover;

        /// <summary>按下即触发（不用 Click）：窗口未激活时，第一下点击会被系统用于激活而吞掉 Click。</summary>
        public event EventHandler Invoked;

        public ChromeButton(GlyphKind kind)
        {
            _kind = kind;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            BackColor = Theme.Panel;
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                EventHandler h = Invoked;
                if (h != null) { try { h(this, EventArgs.Empty); } catch { } }
            }
            base.OnMouseDown(e);
        }

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
