using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 莱茵风格按钮：直角、1px 细线、居中标签，悬停转暖褐。
    ///
    /// 动效全部挂在全局时钟 Anim 上，与窗体上的扫掠/彗尾同相位，不各动各的：
    ///   · 悬停 —— 一道彗尾横扫按钮面（中间亮两端渐隐）+ 四角刻度浮现
    ///   · 主按钮 —— 四角刻度缓慢呼吸（与状态面板左侧信号条同一手法）
    ///   · 按下 —— 边框内收 1px，做出"压下去"的收紧感
    ///
    /// 空闲开销：只有主按钮登记动效（10fps，够呼吸就行）；其余按钮**不悬停就完全不重绘**。
    /// </summary>
    internal class FlatButton : Control
    {
        private bool _hover;
        private bool _down;
        private bool _primary;
        private bool _danger;
        private bool _accent;

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = Theme.FontUi;
            BackColor = Theme.Bg;
            TabStop = false;
        }

        /// <summary>主按钮：常态做刻度的缓慢呼吸，所以要一直登记动效。</summary>
        public bool Primary
        {
            get { return _primary; }
            set
            {
                if (_primary == value) return;
                _primary = value;
                if (_primary) Anim.Track(this, 3);    // ≈10fps
                else Anim.Untrack(this);
                Invalidate();
            }
        }

        public bool Danger { get { return _danger; } set { _danger = value; Invalidate(); } }

        /// <summary>强调态（暖褐描边）：用于「有更新可用」这类需要抓注意力的次要操作。</summary>
        public bool Accent { get { return _accent; } set { _accent = value; Invalidate(); } }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Anim.Untrack(this);
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Anim.Track(this, 1);                  // 悬停期间才 30fps
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _down = false;
            if (!_primary) Anim.Untrack(this);    // 离开即注销，不留常驻动画
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e) { if (e.Button == MouseButtons.Left) { _down = true; Invalidate(); } base.OnMouseDown(e); }
        protected override void OnMouseUp(MouseEventArgs e) { _down = false; Invalidate(); base.OnMouseUp(e); }
        protected override void OnEnabledChanged(EventArgs e) { Invalidate(); base.OnEnabledChanged(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            Theme.Fill(g, ClientRectangle, BackColor);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Color fill, border, text;

            if (!Enabled)
            {
                fill = Theme.BgShade; border = Theme.LineSoft; text = Theme.SignalIdle;
            }
            else if (_danger)
            {
                fill = _hover || _down ? Theme.SignalAlert : Theme.PanelHi;
                border = Theme.SignalAlert;
                text = _hover || _down ? Theme.PanelHi : Theme.SignalAlert;
            }
            else if (_accent)
            {
                fill = _hover || _down ? Theme.Amber : Theme.PanelHi;
                border = Theme.Amber;
                text = _hover || _down ? Theme.PanelHi : Theme.Amber;
            }
            else if (_primary)
            {
                fill = _hover || _down ? Theme.Amber : Theme.Ink;
                border = fill;
                text = Theme.PanelHi;
            }
            else
            {
                fill = _down ? Theme.BgShade : (_hover ? Theme.Panel : Theme.PanelHi);
                border = _hover ? Theme.Amber : Theme.Line;
                text = _hover ? Theme.Amber : Theme.Ink;
            }

            Theme.Fill(g, r, fill);
            Theme.StrokeRect(g, r, border, 1f);

            // 按下：内收 1px（收紧感），不做位移，免得整行跳动
            if (_down && Enabled) Theme.StrokeRect(g, Rectangle.Inflate(r, -1, -1), border, 1f);

            // 悬停：彗尾横扫按钮面。
            // 注意取色：危险/强调态的悬停填充本身就是 SignalAlert / Amber，
            // 再用同色扫过去等于没扫 —— 这两种用浅色（PanelHi）才看得出来。
            if (_hover && Enabled && !_down)
            {
                Color sweep = _primary ? Theme.AmberHi
                            : (_danger || _accent ? Theme.PanelHi : Theme.Amber);
                DrawFaceSweep(g, r, sweep);
            }

            // 四角刻度：主按钮常态呼吸；其余按钮悬停时浮现
            if (Enabled)
            {
                Rectangle ticks = Rectangle.Inflate(r, -4, -4);
                if (_primary)
                {
                    int a = (int)Theme.Pulse(3.4, 0.0, 95, 175);
                    Theme.CornerTicks(g, ticks, Color.FromArgb(a, Theme.AmberHi), Theme.S(6));
                }
                else if (_hover)
                {
                    Theme.CornerTicks(g, ticks, Color.FromArgb(140, Theme.Amber), Theme.S(6));
                }
            }

            // 标签：编号已去掉，文字在整块按钮里居中
            Rectangle textRect = new Rectangle(Theme.S(10), 0, Width - Theme.S(20), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
                TextFormatFlags.EndEllipsis);
        }

        /// <summary>
        /// 沿按钮面横扫的彗尾高光：中间最亮、两端渐隐（与面板细线框的巡行彗尾同一手法）。
        /// 用切片+正弦包络来做出柔和的头尾，而不是一条硬边矩形。
        /// </summary>
        private void DrawFaceSweep(Graphics g, Rectangle r, Color accent)
        {
            int bandW = Math.Max(Theme.S(18), r.Width * 2 / 5);
            int x0 = r.X - bandW + (int)(Anim.Cycle(1.9, 0.0) * (r.Width + bandW * 2));
            const int slices = 10;
            for (int i = 0; i < slices; i++)
            {
                int a0 = x0 + bandW * i / slices;
                int a1 = x0 + bandW * (i + 1) / slices;
                double t = (i + 0.5) / slices;
                int alpha = (int)(72.0 * Math.Sin(Math.PI * t));     // 两端为 0，中间最亮
                if (alpha <= 2) continue;
                int cx0 = Math.Max(r.X + 1, a0), cx1 = Math.Min(r.Right - 1, a1);
                if (cx1 <= cx0) continue;
                Theme.Fill(g, new Rectangle(cx0, r.Y + 1, cx1 - cx0, r.Height - 2), Color.FromArgb(alpha, accent));
            }
        }
    }
}
