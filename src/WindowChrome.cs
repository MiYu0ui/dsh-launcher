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

    /// <summary>无边框窗口右上角的自绘按钮（最小化 / 关闭），细线字形。</summary>
    internal class ChromeButton : Control
    {
        public enum GlyphKind { Minimize, Close }

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
    }
}
