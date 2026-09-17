using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 状态面板：细线方框 + 四角刻度 + 左侧信号条，
    /// 用宽字距的等宽标注行组织信息（替代原来的圆角粉色卡片）。
    /// </summary>
    internal class StatusPanel : Panel
    {
        private string _track = "STATUS";
        private string _headline = "未运行";
        private string _sub = "";
        private string _workspace = "";
        private string _url = "";
        private Color _signal = Theme.SignalIdle;
        private Rectangle _urlRect;
        private double _headlineBorn;
        private double _urlBorn;

        public event EventHandler UrlClicked;

        public StatusPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Anim.Track(this, 1);
        }

        public void SetState(string track, string headline, string sub, Color signal, string workspace, string url)
        {
            _track = track;
            if (_headline != headline) _headlineBorn = Anim.Now;      // 文案变化：扫入
            if (_url != url) _urlBorn = Anim.Now;
            _headline = headline;
            _sub = sub == null ? "" : sub;
            _signal = signal;
            _workspace = workspace == null ? "" : workspace;
            _url = url == null ? "" : url;
            // 自适应帧率：忙（接入/异常）时 30fps，空闲时降到 ~10fps，别让静态界面白烧 CPU
            bool busy = signal == Theme.SignalBusy || signal == Theme.SignalAlert;
            Anim.Track(this, busy ? 1 : 3);
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Anim.Untrack(this);
            base.Dispose(disposing);
        }

        /// <summary>数值行的扫入：从左往右揭开，前端带一道高光。</summary>
        private void DrawWipeText(Graphics g, string text, Font font, Rectangle rect, Color color, double born, int y)
        {
            double k = Theme.Clamp01((Anim.Now - born) / 0.32);
            if (k >= 1.0)
            {
                TextRenderer.DrawText(g, text, font, rect, color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis);
                return;
            }
            // ⚠️ 原来是 SetClip + TextRenderer，但 **GDI 文字不认 GDI+ 的裁剪区** ——
            //    扫入根本裁不住，文字会整行提前出现。改成按宽度截字：共享 Theme.ClipTracked
            //    （与 DrawTracked 的擦入、帮助列表的标题截断是同一套逐字测量逻辑）。
            int maxW = Math.Max(1, (int)(rect.Width * k));
            string shown = Theme.ClipTracked(g, text, font, 0f, maxW);
            TextRenderer.DrawText(g, shown, font, rect, color,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis);
            int cx = rect.X + maxW;
            using (Pen p = new Pen(Color.FromArgb(150, color), 1.6f))
                g.DrawLine(p, cx, rect.Y + 2, cx, rect.Bottom - 2);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = (_urlRect.Contains(e.Location) && _url.Length > 0) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (_urlRect.Contains(e.Location) && _url.Length > 0)
            {
                EventHandler h = UrlClicked;
                if (h != null) h(this, EventArgs.Empty);
            }
            base.OnMouseClick(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            Theme.Fill(g, ClientRectangle, BackColor);

            Rectangle r = new Rectangle(0, 0, Width - 1, Height - 1);
            Theme.Fill(g, r, Theme.PanelHi);
            // 会呼吸的细线边框 + 沿轮廓巡行的彗尾；四角刻度随呼吸明暗
            Theme.DrawLiveFrame(g, r, Theme.Line, _signal, true, 5.6, 0.0);

            // 左侧信号条：随状态呼吸
            int barAlpha = (int)Theme.Pulse(2.6, 0.0, 140, 255);
            Theme.Fill(g, new Rectangle(r.X + 1, r.Y + 1, Theme.S(3), r.Height - 2),
                       Color.FromArgb(barAlpha, _signal));

            int x = Theme.S(22);
            int right = Width - Theme.S(22);

            // 轨道标注行 + 带巡行高光的细线
            Theme.DrawTracked(g, _track, Theme.FontMonoSmall, x, Theme.S(16), Theme.Sub, Theme.SF(1.6f));
            Theme.SweepRule(g, x, right, Theme.S(34), Theme.LineSoft, _signal, 7.5, 0.15);

            // 状态标题：文案变化时从左往右扫入
            DrawWipeText(g, _headline, Theme.FontStatus,
                new Rectangle(x, Theme.S(44), right - x, Theme.S(30)), _signal, _headlineBorn, Theme.S(44));

            if (_sub.Length > 0)
            {
                TextRenderer.DrawText(g, _sub, Theme.FontSmall,
                    new Rectangle(x, Theme.S(76), right - x, Theme.S(20)), Theme.Sub,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            }

            Theme.SweepRule(g, x, right, Theme.S(104), Theme.Line, _signal, 9.5, 0.55);

            DrawField(g, "工作目录", _workspace, x, Theme.S(116), Theme.Ink, 0);
            _urlRect = DrawField(g, "服务地址", _url.Length > 0 ? _url : "—", x, Theme.S(146),
                                 _url.Length > 0 ? Theme.Amber : Theme.SignalIdle, _urlBorn);
        }

        private Rectangle DrawField(Graphics g, string label, string value, int x, int y, Color valueColor, double born)
        {
            Theme.DrawTracked(g, label, Theme.FontMonoSmall, x, y + Theme.S(3), Theme.Sub, Theme.SF(1.2f));
            int vx = x + Theme.S(86);
            int vw = Width - vx - Theme.S(22);
            if (vw < Theme.S(20)) vw = Theme.S(20);
            Rectangle rect = new Rectangle(vx, y, vw, Theme.S(20));
            if (born > 0 && Anim.Now - born < 0.32) DrawWipeText(g, value, Theme.FontMono, rect, valueColor, born, y);
            else
                TextRenderer.DrawText(g, value, Theme.FontMono, rect, valueColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.PathEllipsis);
            return value.Length > 0 ? rect : Rectangle.Empty;
        }
    }
}
