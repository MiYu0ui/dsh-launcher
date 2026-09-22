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
        /// <summary>
        /// 用户做出的选择。
        /// Cancel = 没有做出决定（✕ / Esc / 直接关掉）—— 调用方必须把这个值当成"用户还没决定"，
        /// 而不是"用户拒绝"；后两者在业务上的处理往往完全不同。
        /// </summary>
        public enum Choice { Cancel, Confirm, Alt, Alt2 }

        private const int DesignW = 640;
        private const int Pad = 28;
        private const int HeaderH = 76;

        private readonly string _track;
        private readonly string _headline;
        private readonly string _message;
        private readonly string _warning;
        private readonly string[] _fields;
        private readonly bool _danger;
        /// <summary>只读提示：不画取消 / 第三选项，只留一个「知道了」，且不带危险样式。</summary>
        private readonly bool _infoOnly;
        private readonly string _altText;      // 最左按钮（第三选项）→ Choice.Alt
        private readonly string _thirdText;    // 中间按钮；给定时取代「取消」→ Choice.Alt2

        private int _messageH;
        private int _warningH;
        private int _fieldsTop;
        /// <summary>
        /// 选择结果。默认 <c>Cancel</c> 是刻意的兜底：任何非按钮路径（✕ / Esc / 异常关闭）
        /// 都会走到这个初始值，调用方看到的就是"用户没做决定"。
        /// </summary>
        private Choice _result = Choice.Cancel;

        private ConfirmDialog(string track, string headline, string message, string[] fields,
                              string warning, string confirmText, string altText, string thirdText, bool danger, bool infoOnly)
        {
            _track = track;
            _headline = headline;
            _message = message == null ? "" : message;
            _fields = fields;
            _warning = warning;
            _danger = danger;
            _infoOnly = infoOnly;
            _altText = altText;
            _thirdText = thirdText;

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = infoOnly ? FormStartPosition.CenterParent : FormStartPosition.CenterParent;
            // 说明：上面三元的两个分支取值相同，恒等于 CenterParent。
            // 看着像笔误，但按「只动注释」的范围本轮不改代码；行为与直接写 CenterParent 一致。
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
            _reveal = new WindowReveal(this, WindowReveal.Level, true);   // 对话框不做章节导轨，只有括号 + 标签块 + 标题擦入
        }

        private readonly WindowReveal _reveal;

        /// <summary>起播入场动画（对话框档：300ms，不做章节版式）。</summary>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_reveal != null) _reveal.BeginEnter();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_reveal != null && _reveal.InterceptClose(e)) return;      // 先播 200ms 退场再真关
            base.OnFormClosing(e);
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

        /// <summary>
        /// 给无边框窗口补一点系统投影（<c>CS_DROPSHADOW</c>），让它从主界面上浮起来。
        /// 这个样式位必须在窗口类注册时就带上，而 Form 没有对应属性，只能走 <c>CreateParams</c>。
        /// </summary>
        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000;   // CS_DROPSHADOW
                return cp;
            }
        }

        // ---------------- 静态入口 ----------------
        /// <summary>只读提示（一个「知道了」按钮）。</summary>
        public static void Info(IWin32Window owner, string track, string headline, string message,
                                string[] fields, string warning)
        {
            using (ConfirmDialog d = new ConfirmDialog(track, headline, message, fields, warning, "知道了", null, null, false, true))
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
            return Show(owner, track, headline, message, fields, warning, confirmText, altText, null, danger);
        }

        /// <summary>
        /// 三选项确认：最左 [altText] …… 中间 [thirdText]，最右 [confirmText]。
        /// thirdText 给定时中间那个按钮就是它（返回 Choice.Alt2），此时没有「取消」按钮；
        /// 右上角 ✕ 与 Esc 仍然返回 Choice.Cancel，专门用来表达「还没决定」。
        /// </summary>
        public static Choice Show(IWin32Window owner, string track, string headline, string message,
                                  string[] fields, string warning, string confirmText, string altText,
                                  string thirdText, bool danger)
        {
            using (ConfirmDialog d = new ConfirmDialog(track, headline, message, fields, warning, confirmText, altText, thirdText, danger, false))
            {
                d.ShowDialog(owner);
                return d._result;
            }
        }

        // ---------------- 布局 ----------------
        /// <summary>
        /// 建控件并按设计像素摆位。
        ///
        /// ⚠️ 与打字确认框同一套单位约定：控件位置最终要物理像素，所以每个推进步都过 <see cref="Theme.S"/>；
        /// 正文 / 警告高度是量出来后除以缩放的**设计像素**（见 <see cref="_messageH"/>），推进时要再乘回去。
        /// </summary>
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
            if (_messageH > 0) y += Theme.S(_messageH) + Theme.S(18);   // _messageH 是设计像素，推进要过 Theme.S（绘制那边也是）
            _fieldsTop = y;
            if (_fields != null) y += _fields.Length * Theme.S(24) + Theme.S(6);
            if (_warningH > 0) y += Theme.S(_warningH) + Theme.S(12);   // 同上：原来按设计值推进、按物理值绘制 → 警告会压住下一行
            y += Theme.S(16);                                  // 按钮行上方留白
            int btnH = Theme.S(42);
            int totalH = y + btnH + Theme.S(Pad);
            ClientSize = new Size(Theme.S(DesignW), totalH);

            // 按钮：右侧 [确认]；中间默认是「取消」，给了 thirdText 就换成第三选项；altText 放最左
            bool hasThird = !string.IsNullOrEmpty(_thirdText);
            int btnW = Theme.S(hasThird ? 136 : 120), altW = Theme.S(hasThird ? 136 : 150);
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
                // 两个分支同值：只读提示框点「知道了」同样记成 Choice.Confirm
                _result = _infoOnly ? Choice.Confirm : Choice.Confirm;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(ok);

            if (!_infoOnly)
            {
                // 中间按钮：没有第三选项时它就是「取消」，有第三选项时它是那个选项本身 ——
                // 所以这里按 hasThird 决定文案、返回值，连 DialogResult 都跟着变（第三选项算「已选择」，用 OK）
                FlatButton cancel = new FlatButton();
                cancel.Text = hasThird ? _thirdText : "取消";
                cancel.Location = new Point(rx - btnW * 2 - Theme.S(10), by);
                cancel.Size = new Size(btnW, btnH);
                cancel.BackColor = Theme.Bg;
                cancel.Click += delegate(object s, EventArgs e)
                {
                    _result = hasThird ? Choice.Alt2 : Choice.Cancel;
                    DialogResult = hasThird ? DialogResult.OK : DialogResult.Cancel;
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

        /// <summary>
        /// 快捷键：Esc = 取消，Enter = 确认。入场期间先把动画跳完，但**不吞按键** ——
        /// 吞掉会让 Esc / Enter 在头 300ms 内像失灵，而模态框一旦关不掉就是挂死。
        /// </summary>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            // 入场没播完时只是把动画跳完，**不吞按键** —— 吞掉会让 Esc/Enter 在头 300ms 内像失灵；
            // 而模态框一旦"关不掉"就是挂死（实测踩过：误用大窗 600ms 时间轴时直接收不了场）。
            if (_reveal != null && _reveal.Busy) _reveal.Skip();
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

        /// <summary>入场期间点一下 = 跳过动画（不改变任何选择）；否则顶部标题区按住左键可拖动窗口。</summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_reveal != null && _reveal.Busy) { _reveal.Skip(); return; }   // 点一下跳过动画（不改变任何选择）
            if (e.Button == MouseButtons.Left && e.Y <= Theme.S(HeaderH)) WindowChrome.BeginDrag(this);
        }

        // ---------------- 绘制 ----------------
        /// <summary>
        /// 全自绘：顶边 + 面板标题区 + 巡行细线框，再叠正文 / 字段 / 警告；
        /// 危险态把顶边、巡行彗尾、四角括号统一换成告警色。
        /// </summary>
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

            // 顶部标注：标签块（chip）+ 标题擦入。对话框不做章节导轨 —— 它没有"章节"。
            double chipP = _reveal != null ? _reveal.ChipP : 1.0;
            double titleP = _reveal != null ? _reveal.TitleP : 1.0;
            UiPaint.Chip(g, Theme.S(Pad), band + Theme.S(11), _track, chipP);

            int y = Theme.S(HeaderH) + Theme.S(16);
            // 标题：**底色→目标色插值淡入**，不用 SetClip 擦入 —— GDI 文字不认 GDI+ 裁剪区，
            // SetClip 对它无效（表现出来就是"整行提前出现"）。
            TextRenderer.DrawText(g, _headline, Theme.FontStatus,
                new Rectangle(Theme.S(Pad), y, right - Theme.S(Pad), Theme.S(26)),
                Theme.Mix(Theme.Bg, _danger ? Theme.SignalAlert : Theme.Ink, titleP),
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            y += Theme.S(26);
            Theme.SweepRule(g, Theme.S(Pad), right, y + Theme.S(4), Theme.Line,
                            _danger ? Theme.SignalAlert : Theme.Green, 8.0, 0.2);
            y += Theme.S(10);

            if (_messageH > 0)
            {
                y += Theme.S(12);
                TextRenderer.DrawText(g, _message, Theme.FontUi,
                    new Rectangle(Theme.S(Pad), y, right - Theme.S(Pad), Theme.S(_messageH)),
                    Theme.Mix(Theme.Bg, Theme.InkSoft, 1.0),
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
                    y += Theme.S(24);              // 游标同步跳过字段块，警告行才不会压在字段上
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

            // 四角括号（模板里的 ⌐ 元素）：框住整张卡片，比整圈描边轻
            if (_reveal != null && _reveal.WantsLayout)
                UiPaint.Brackets(g, Rectangle.Inflate(ClientRectangle, -Theme.S(2), -Theme.S(2)),
                                 _danger ? Theme.SignalAlert : Theme.Ink, _reveal.BracketPhases());

            base.OnPaint(e);
        }
    }
}
