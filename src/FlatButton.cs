using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>莱茵风格按钮：直角、1px 细线、宽字距标签，悬停转为暖褐。</summary>
    internal class FlatButton : Control
    {
        private bool _hover;
        private bool _down;
        private bool _primary;
        private bool _danger;
        private bool _accent;
        private string _index = "";

        public FlatButton()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            Font = Theme.FontUi;
            BackColor = Theme.Bg;
            TabStop = false;
        }

        public bool Primary { get { return _primary; } set { _primary = value; Invalidate(); } }
        public bool Danger { get { return _danger; } set { _danger = value; Invalidate(); } }
        /// <summary>强调态（暖褐描边）：用于「有更新可用」这类需要抓注意力的次要操作。</summary>
        public bool Accent { get { return _accent; } set { _accent = value; Invalidate(); } }
        /// <summary>左侧编号（如 01），档案终端的技术标注感。</summary>
        public string Index { get { return _index; } set { _index = value == null ? "" : value; Invalidate(); } }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; _down = false; Invalidate(); base.OnMouseLeave(e); }
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
            if (_primary && Enabled) Theme.CornerTicks(g, Rectangle.Inflate(r, -4, -4), Color.FromArgb(150, Theme.AmberHi), Theme.S(6));

            int leftPad = Theme.S(12);
            if (_index.Length > 0)
            {
                Color idxColor = Enabled ? (_primary ? Color.FromArgb(190, Theme.AmberHi) : Theme.Sub) : Theme.SignalIdle;
                TextRenderer.DrawText(g, _index, Theme.FontMonoSmall,
                    new Rectangle(leftPad, 0, Theme.S(30), Height), idxColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                leftPad += Theme.S(30);
            }

            Rectangle textRect = new Rectangle(leftPad, 0, Width - leftPad - Theme.S(12), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, text,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix |
                TextFormatFlags.EndEllipsis);
        }
    }
}
