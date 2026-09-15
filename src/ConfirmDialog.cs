using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 档案终端风格的自绘对话框，替代系统 MessageBox。
    /// 与主界面同一套语言：绿色顶边、细线卡片、巡行彗尾、四角刻度、宽字距标注、直角细线按钮。
    /// 字段用"标签 + 等宽值"两列排版（单行省略号，不会再像系统框那样把长路径折成两行）。
    /// </summary>
    internal class ConfirmDialog : Form
    {
        public enum Choice { Cancel, Confirm, Alt }

        private const int DesignW = 640;
        private const int Pad = 28;
        private const int HeaderH = 76;

        private readonly string _track;
        private readonly string _headline;
        private readonly string _message;
        private readonly string _warning;
        private readonly string[] _fields;
        private readonly bool _danger;
        private readonly bool _infoOnly;
        private readonly string _altText;

        private int _messageH;
        private int _warningH;
        private int _fieldsTop;
        private Choice _result = Choice.Cancel;

        private ConfirmDialog(string track, string headline, string message, string[] fields,
                              string warning, string confirmText, string altText, bool danger, bool infoOnly)
        {
            _track = track;
            _headline = headline;
            _message = message == null ? "" : message;
            _fields = fields;
            _warning = warning;
            _danger = danger;
            _infoOnly = infoOnly;
            _altText = altText;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = infoOnly ? FormStartPosition.CenterParent : FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = Theme.Bg;
            Font = Theme.FontUi;
            ForeColor = Theme.Ink;
            AllowTransparency = true;

            Layout(confirmText);
            Anim.Track(this, 1);
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;   // CS_DROPSHADOW
                return cp;
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Anim.Untrack(this);
            base.Dispose(disposing);
        }

        // ---------------- 静态入口 ----------------
        /// <summary>只读提示（一个「知道了」按钮）。</summary>
        public static void Info(IWin32Window owner, string track, string headline, string message,
                                string[] fields, string warning)
        {
            using (ConfirmDialog d = new ConfirmDialog(track, headline, message, fields, warning, "知道了", null, false, true))
                d.ShowDialog(owner);
        }

        /// <summary>确认（取消 / 确认，可选第三选项）。</summary>
        public static Choice Show(IWin32Window owner, string track, string headline, string message,
                                  string[] fields, string warning, string confirmText, bool danger)
        {
            return Show(owner, track, headline, message, fields, warning, confirmText, null, danger);
        }

        public static Choice Show(IWin32Window owner, string track, string headline, string message,
                                  string[] fields, string warning, string confirmText, string altText, bool danger)
        {
            using (ConfirmDialog d = new ConfirmDialog(track, headline, message, fields, warning, confirmText, altText, danger, false))
            {
                d.ShowDialog(owner);
                return d._result;
            }
        }

        // ---------------- 布局 ----------------
        private void Layout(string confirmText)
        {
            int contentW = Theme.S(DesignW) - Theme.S(Pad * 2);

            using (Graphics g = CreateGraphics())
            {
                // 注意单位：CreateGraphics 是当前 DPI 的物理像素，布局用的是设计像素 → 要除以缩放
                float sc = Theme.Scale <= 0f ? 1f : Theme.Scale;
                _messageH = _message.Length == 0 ? 0
                    : (int)Math.Ceiling(TextRenderer.MeasureText(g, _message, Theme.FontUi, new Size(contentW, int.MaxValue),
                                               TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height / sc);
                _warningH = string.IsNullOrEmpty(_warning) ? 0
                    : (int)Math.Ceiling(TextRenderer.MeasureText(g, _warning, Theme.FontUiBold, new Size(contentW, int.MaxValue),
                                               TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height / sc);
            }

            int y = Theme.S(HeaderH) + Theme.S(16);
            y += Theme.S(26);                                  // 标题
            y += Theme.S(10);                                  // 分隔线
            if (_messageH > 0) y += _messageH + Theme.S(18);
            _fieldsTop = y;
            if (_fields != null) y += _fields.Length * Theme.S(24) + Theme.S(6);
            if (_warningH > 0) y += _warningH + Theme.S(12);
            y += Theme.S(16);                                  // 按钮行上方留白
            int btnH = Theme.S(42);
            int totalH = y + btnH + Theme.S(Pad);
            ClientSize = new Size(Theme.S(DesignW), totalH);

            // 按钮：右对齐 [取消][确认]，第三选项放最左
            int btnW = Theme.S(120), altW = Theme.S(150);
            int by = totalH - Theme.S(Pad) - btnH;
            int rx = Theme.S(DesignW) - Theme.S(Pad);

            FlatButton ok = new FlatButton();
            ok.Text = _infoOnly ? "知道了" : confirmText;
            ok.Primary = !_danger;
            ok.Danger = _danger;
            ok.Location = new Point(rx - btnW, by);
            ok.Size = new Size(btnW, btnH);
            ok.BackColor = Theme.Bg;
            ok.Click += delegate(object s, EventArgs e)
            {
                _result = _infoOnly ? Choice.Confirm : Choice.Confirm;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(ok);

            if (!_infoOnly)
            {
                FlatButton cancel = new FlatButton();
                cancel.Text = "取消";
                cancel.Location = new Point(rx - btnW * 2 - Theme.S(10), by);
                cancel.Size = new Size(btnW, btnH);
                cancel.BackColor = Theme.Bg;
                cancel.Click += delegate(object s, EventArgs e)
                {
                    _result = Choice.Cancel;
                    DialogResult = DialogResult.Cancel;
                    Close();
                };
                Controls.Add(cancel);

                if (!string.IsNullOrEmpty(_altText))
                {
                    FlatButton alt = new FlatButton();
                    alt.Text = _altText;
                    alt.Location = new Point(Theme.S(Pad), by);
                    alt.Size = new Size(altW, btnH);
                    alt.BackColor = Theme.Bg;
                    alt.Click += delegate(object s, EventArgs e)
                    {
                        _result = Choice.Alt;
                        DialogResult = DialogResult.OK;
                        Close();
                    };
                    Controls.Add(alt);
                }
            }

            // 右上角关闭
            ChromeButton close = new ChromeButton(ChromeButton.GlyphKind.Close);
            close.Location = new Point(Theme.S(DesignW - Pad - 26), Theme.S(8));
            close.Size = new Size(Theme.S(26), Theme.S(22));
            close.BackColor = Theme.Panel;
            close.Invoked += delegate(object s, EventArgs e)
            {
                _result = Choice.Cancel;
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(close);
            close.BringToFront();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                _result = Choice.Cancel;
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            if (keyData == Keys.Enter)
            {
                _result = Choice.Confirm;
                DialogResult = DialogResult.OK;
                Close();
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && e.Y <= Theme.S(HeaderH)) WindowChrome.BeginDrag(this);
        }

        // ---------------- 绘制 ----------------
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            int w = ClientSize.Width;
            int h = ClientSize.Height;
            int band = Theme.S(3);
            int right = w - Theme.S(Pad);

            Theme.Fill(g, ClientRectangle, Theme.Bg);
            Theme.Fill(g, new Rectangle(0, 0, w, band), Theme.Green);
            Theme.Fill(g, new Rectangle(0, band, w, Theme.S(HeaderH) - band), Theme.Panel);
            Theme.Rule(g, 0, Theme.S(HeaderH) - 1, w, Theme.Line);
            Theme.DrawLiveFrame(g, new Rectangle(0, 0, w - 1, h - 1), Theme.Line,
                                _danger ? Theme.SignalAlert : Theme.Green, false, 6.2, 0.4);
            Theme.CornerTicks(g, new Rectangle(Theme.S(9), Theme.S(9), w - Theme.S(19), h - Theme.S(19)), Theme.Line, Theme.S(11));

            // 顶部标注
            Theme.DrawTracked(g, _track, Theme.FontMonoSmall, Theme.S(Pad), band + Theme.S(14), Theme.Sub, Theme.SF(1.6f));

            int y = Theme.S(HeaderH) + Theme.S(16);
            TextRenderer.DrawText(g, _headline, Theme.FontStatus,
                new Rectangle(Theme.S(Pad), y, right - Theme.S(Pad), Theme.S(26)),
                _danger ? Theme.SignalAlert : Theme.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            y += Theme.S(26);
            Theme.SweepRule(g, Theme.S(Pad), right, y + Theme.S(4), Theme.Line,
                            _danger ? Theme.SignalAlert : Theme.Green, 8.0, 0.2);
            y += Theme.S(10);

            if (_messageH > 0)
            {
                y += Theme.S(12);
                TextRenderer.DrawText(g, _message, Theme.FontUi,
                    new Rectangle(Theme.S(Pad), y, right - Theme.S(Pad), Theme.S(_messageH)), Theme.InkSoft,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                y += Theme.S(_messageH) + Theme.S(18);
            }

            // 字段块：等宽标签 + 等宽值（单行省略号）
            if (_fields != null)
            {
                int fy = _fieldsTop;
                foreach (string raw in _fields)
                {
                    string label = raw, value = "";
                    int eq = raw.IndexOf('=');
                    if (eq > 0) { label = raw.Substring(0, eq); value = raw.Substring(eq + 1); }
                    Theme.DrawTracked(g, label, Theme.FontMonoSmall, Theme.S(Pad), fy + Theme.S(4), Theme.Sub, Theme.SF(1.2f));
                    if (value.Length > 0)
                    {
                        TextRenderer.DrawText(g, value, Theme.FontMono,
                            new Rectangle(Theme.S(Pad + 92), fy, right - Theme.S(Pad + 92), Theme.S(22)), Theme.Ink,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    }
                    fy += Theme.S(24);
                    y += Theme.S(24);              // 游标同步跳过字段块，警告行才不会压在字段上
                }
                y += Theme.S(6);
            }

            if (_warningH > 0)
            {
                TextRenderer.DrawText(g, _warning, Theme.FontUiBold,
                    new Rectangle(Theme.S(Pad), y + Theme.S(6), right - Theme.S(Pad), Theme.S(_warningH)), Theme.SignalAlert,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }

            base.OnPaint(e);
        }
    }
}
