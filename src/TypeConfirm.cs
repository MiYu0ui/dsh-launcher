using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 危险操作的「打字确认」对话框：涉及凭据 / 会话 / 配置这类私密数据的卸载，
    /// 必须让用户手打出确认词才放行。
    /// 版式与 ConfirmDialog 完全一致（绿顶边、巡行彗尾、四角刻度、宽字距标注），
    /// 只是多一行等宽输入框与一句红色要求。
    /// </summary>
    internal class TypeConfirm : Form
    {
        private const int DesignW = 640;
        private const int Pad = 28;
        private const int HeaderH = 76;

        private readonly string _track;
        private readonly string _headline;
        private readonly string _message;
        private readonly string _warning;
        private readonly string[] _fields;
        private readonly string _want;

        private TextBox _input;
        private FlatButton _ok;
        private int _messageH;
        private int _warningH;
        private int _fieldsTop;
        private int _inputY;
        private bool _confirmed;

        private TypeConfirm(string track, string headline, string message, string[] fields,
                            string warning, string wantWord, string okText)
        {
            _track = track;
            _headline = headline;
            _message = message == null ? "" : message;
            _fields = fields;
            _warning = warning;
            _want = wantWord;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            MaximizeBox = false;
            MinimizeBox = false;
            KeyPreview = true;
            DoubleBuffered = true;
            BackColor = Theme.Bg;
            Font = Theme.FontUi;
            ForeColor = Theme.Ink;
            AllowTransparency = true;

            Layout(okText);
            Anim.Track(this, 1);
            _reveal = new WindowReveal(this, WindowReveal.Level, true);
        }

        private readonly WindowReveal _reveal;

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_reveal != null) _reveal.BeginEnter();
            // 先给输入框焦点再播动画：打字随时可以开始，入场不挡输入
            try { _input.Focus(); } catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_reveal != null && _reveal.InterceptClose(e)) return;
            base.OnFormClosing(e);
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
            if (disposing)
            {
                Anim.Untrack(this);
                if (_reveal != null) _reveal.Dispose();
            }
            base.Dispose(disposing);
        }

        /// <summary>返回 true = 用户打对了确认词并点了确认。</summary>
        public static bool Ask(IWin32Window owner, string track, string headline, string message,
                               string[] fields, string warning, string wantWord, string okText)
        {
            using (TypeConfirm d = new TypeConfirm(track, headline, message, fields, warning, wantWord, okText))
            {
                d.ShowDialog(owner);
                return d._confirmed;
            }
        }

        // ---------------- 布局 ----------------
        private void Layout(string okText)
        {
            int contentW = Theme.S(DesignW) - Theme.S(Pad * 2);
            using (Graphics g = CreateGraphics())
            {
                float sc = Theme.Scale <= 0f ? 1f : Theme.Scale;
                _messageH = _message.Length == 0 ? 0
                    : (int)Math.Ceiling(TextRenderer.MeasureText(g, _message, Theme.FontUi,
                        new Size(contentW, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height / sc);
                _warningH = string.IsNullOrEmpty(_warning) ? 0
                    : (int)Math.Ceiling(TextRenderer.MeasureText(g, _warning, Theme.FontUiBold,
                        new Size(contentW, int.MaxValue), TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height / sc);
            }

            int y = Theme.S(HeaderH) + Theme.S(16);
            y += Theme.S(26);                                  // 标题
            y += Theme.S(10);                                  // 分隔线
            if (_messageH > 0) y += _messageH + Theme.S(18);
            _fieldsTop = y;
            if (_fields != null) y += _fields.Length * Theme.S(24) + Theme.S(6);
            if (_warningH > 0) y += _warningH + Theme.S(14);

            y += Theme.S(10);                                  // 「请输入」提示行
            y += Theme.S(20);
            _inputY = y;
            y += Theme.S(30);

            y += Theme.S(16);                                  // 按钮行上方留白
            int btnH = Theme.S(42);
            int totalH = y + btnH + Theme.S(Pad);
            ClientSize = new Size(Theme.S(DesignW), totalH);

            int btnW = Theme.S(136);
            int by = totalH - Theme.S(Pad) - btnH;
            int rx = Theme.S(DesignW) - Theme.S(Pad);

            _input = new TextBox();
            _input.Location = new Point(Theme.S(Pad), Theme.S(_inputY));
            _input.Size = new Size(Theme.S(DesignW - Pad * 2), Theme.S(28));
            _input.BorderStyle = BorderStyle.FixedSingle;
            _input.BackColor = Theme.PanelHi;
            _input.ForeColor = Theme.Ink;
            _input.Font = Theme.FontMono;
            _input.TextChanged += delegate(object s, EventArgs e) { SyncOk(); };
            Controls.Add(_input);

            _ok = new FlatButton();
            _ok.Text = okText;
            _ok.Danger = true;
            _ok.Enabled = false;
            _ok.Location = new Point(rx - btnW, by);
            _ok.Size = new Size(btnW, btnH);
            _ok.BackColor = Theme.Bg;
            _ok.Click += delegate(object s, EventArgs e)
            {
                if (!Matches()) return;
                _confirmed = true;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(_ok);

            FlatButton cancel = new FlatButton();
            cancel.Text = "取消";
            cancel.Location = new Point(rx - btnW * 2 - Theme.S(10), by);
            cancel.Size = new Size(Theme.S(104), btnH);
            cancel.BackColor = Theme.Bg;
            cancel.Click += delegate(object s, EventArgs e)
            {
                _confirmed = false;
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(cancel);

            ChromeButton close = new ChromeButton(ChromeButton.GlyphKind.Close);
            close.Location = new Point(Theme.S(DesignW - Pad - 26), Theme.S(8));
            close.Size = new Size(Theme.S(26), Theme.S(22));
            close.BackColor = Theme.Panel;
            close.Invoked += delegate(object s, EventArgs e)
            {
                _confirmed = false;
                DialogResult = DialogResult.Cancel;
                Close();
            };
            Controls.Add(close);
            close.BringToFront();
        }

        private bool Matches()
        {
            if (_input == null) return false;
            return string.Equals(_input.Text.Trim(), _want, StringComparison.OrdinalIgnoreCase);
        }

        private void SyncOk()
        {
            if (_ok == null) return;
            bool ok = Matches();
            if (_ok.Enabled != ok) _ok.Enabled = ok;
            Invalidate();
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // 入场动画期间**不吞按键**：这个框全靠打字，吞掉第一个字符是不可接受的。
            // 所以只是顺手把动画跳完，按键照旧往下走（与确认框不同 —— 那边可以吞，这边不行）。
            if (_reveal != null && _reveal.Busy) _reveal.Skip();

            if (keyData == Keys.Escape)
            {
                _confirmed = false;
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            if (keyData == Keys.Enter)
            {
                if (Matches())
                {
                    _confirmed = true;
                    DialogResult = DialogResult.OK;
                    Close();
                }
                return true;      // 没打对时回车不关闭，逼用户打完
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
            Theme.Fill(g, new Rectangle(0, 0, w, band), Theme.SignalAlert);
            Theme.Fill(g, new Rectangle(0, band, w, Theme.S(HeaderH) - band), Theme.Panel);
            Theme.Rule(g, 0, Theme.S(HeaderH) - 1, w, Theme.Line);
            Theme.DrawLiveFrame(g, new Rectangle(0, 0, w - 1, h - 1), Theme.Line, Theme.SignalAlert, false, 6.2, 0.4);

            // 顶部标注：标签块（chip）+ 标题擦入（与确认框同一套）
            double chipP = _reveal != null ? _reveal.ChipP : 1.0;
            double titleP = _reveal != null ? _reveal.TitleP : 1.0;
            UiPaint.Chip(g, Theme.S(Pad), band + Theme.S(11), _track, chipP);

            int y = Theme.S(HeaderH) + Theme.S(16);
            // 标题用插值淡入（GDI 文字不认 GDI+ 裁剪区，SetClip 擦入对它无效）
            TextRenderer.DrawText(g, _headline, Theme.FontStatus,
                new Rectangle(Theme.S(Pad), y, right - Theme.S(Pad), Theme.S(26)),
                Theme.Mix(Theme.Bg, Theme.SignalAlert, titleP),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            y += Theme.S(26);
            Theme.SweepRule(g, Theme.S(Pad), right, y + Theme.S(4), Theme.Line, Theme.SignalAlert, 8.0, 0.2);
            y += Theme.S(10);

            if (_messageH > 0)
            {
                y += Theme.S(12);
                TextRenderer.DrawText(g, _message, Theme.FontUi,
                    new Rectangle(Theme.S(Pad), y, right - Theme.S(Pad), Theme.S(_messageH)),
                    Theme.InkSoft,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                y += Theme.S(_messageH) + Theme.S(18);
            }

            if (_fields != null)
            {
                int fy = _fieldsTop;
                foreach (string raw in _fields)
                {
                    string label = raw, value = "";
                    int eq = raw.IndexOf('=');
                    if (eq > 0) { label = raw.Substring(0, eq); value = raw.Substring(eq + 1); }
                    Theme.DrawTracked(g, label, Theme.FontMonoSmall, Theme.S(Pad), fy + Theme.S(4),
                                      Theme.Sub, Theme.SF(1.2f));
                    if (value.Length > 0)
                    {
                        TextRenderer.DrawText(g, value, Theme.FontMono,
                            new Rectangle(Theme.S(Pad + 92), fy, right - Theme.S(Pad + 92), Theme.S(22)),
                            Theme.Ink,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                    }
                    fy += Theme.S(24);
                    y += Theme.S(24);
                }
                y += Theme.S(6);
            }

            if (_warningH > 0)
            {
                TextRenderer.DrawText(g, _warning, Theme.FontUiBold,
                    new Rectangle(Theme.S(Pad), y + Theme.S(6), right - Theme.S(Pad), Theme.S(_warningH)),
                    Theme.SignalAlert,
                    TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            }

            // 「请输入 <确认词>」提示行
            int hintY = Theme.S(_inputY) - Theme.S(22);
            Theme.DrawTracked(g, "请输入", Theme.FontMonoSmall, Theme.S(Pad), hintY + Theme.S(4),
                              Theme.Sub, Theme.SF(1.2f));
            TextRenderer.DrawText(g, _want, Theme.FontMono,
                new Rectangle(Theme.S(Pad + 58), hintY, right - Theme.S(Pad + 58), Theme.S(20)),
                Matches() ? Theme.Green : Theme.Amber,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            // 四角括号：框住整张卡片（danger 态用告警色）
            if (_reveal != null && _reveal.WantsLayout)
                UiPaint.Brackets(g, Rectangle.Inflate(ClientRectangle, -Theme.S(2), -Theme.S(2)),
                                 Theme.SignalAlert, _reveal.BracketPhases());

            base.OnPaint(e);
        }
    }
}
