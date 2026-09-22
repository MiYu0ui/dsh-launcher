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

        /// <summary>在服务地址那一行上单击时触发（只在 <c>_urlRect</c> 命中且有地址时才会发出）。</summary>
        public event EventHandler UrlClicked;

        /// <summary>构造：开双缓冲并登记 30fps 动效 —— 面板从不真正静止（边框在呼吸、信号条在脉动）。</summary>
        public StatusPanel()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Bg;
            Anim.Track(this, 1);
        }

        /// <summary>刷新面板显示的全部内容，并据此调整刷新率。</summary>
        /// <param name="track">左上角的轨道标注行（宽字距小字）。</param>
        /// <param name="headline">状态标题；值变了会记下时刻，让它重新从左往右扫入一次。</param>
        /// <param name="sub">标题下的一行说明；null 按空串处理。</param>
        /// <param name="signal">状态色，同时决定标题 / 信号条 / 彗尾的颜色与刷新率。</param>
        /// <param name="workspace">工作目录（只显示、不可点）；null 按空串处理。</param>
        /// <param name="url">服务地址；它变化时同样重新扫入，空串表示还没起来，不响应点击。</param>
        /// <remarks>
        /// 自适应帧率：<see cref="Theme.SignalBusy"/>（接入中）与 <see cref="Theme.SignalAlert"/>（异常）按 30fps，
        /// 其余状态降到约 10fps —— 静态界面不该白烧 CPU。判断只看 <paramref name="signal"/>，
        /// 所以调用方换状态时务必把颜色一起换掉，否则刷新率会留在旧档。
        /// </remarks>
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

        /// <summary>销毁前注销动效登记，免得全局时钟继续往已销毁的控件上发重绘。</summary>
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

        /// <summary>指针在服务地址那一行上（且确实有地址）时变手型，否则恢复默认。</summary>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            Cursor = (_urlRect.Contains(e.Location) && _url.Length > 0) ? Cursors.Hand : Cursors.Default;
            base.OnMouseMove(e);
        }

        /// <summary>点中服务地址那一行就发 <see cref="UrlClicked"/>，由外层去打开浏览器。</summary>
        protected override void OnMouseClick(MouseEventArgs e)
        {
            if (_urlRect.Contains(e.Location) && _url.Length > 0)
            {
                EventHandler h = UrlClicked;
                if (h != null) h(this, EventArgs.Empty);
            }
            base.OnMouseClick(e);
        }

        /// <summary>自绘：呼吸细线框 + 左侧信号条 + 轨道标注行 + 状态标题 + 两行字段。</summary>
        /// <remarks>
        /// 纵向位置全是设计像素、经 <see cref="Theme.S"/> 缩放后落笔，改版式时几处 y 值要对齐着改。
        /// 工作目录的 <c>born</c> 传 0，含义是"不播放扫入、直接整行显示"。
        /// </remarks>
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

        /// <summary>画一行"标注 + 值"：左侧宽字距小字标注，右侧等宽字体的值。</summary>
        /// <returns>
        /// 值所在的可点击矩形（服务地址那一行靠它判断鼠标是否落在链接上）；
        /// 值为空串时返回 <see cref="Rectangle.Empty"/>，于是这一行既不变手型也点不动。
        /// </returns>
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
