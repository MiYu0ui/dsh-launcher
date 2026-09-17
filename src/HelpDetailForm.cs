using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 帮助详情（三级界面）：把一个主题的正文块按顺序排出来，可滚动。
    /// 左侧仍是「章节导轨 + 圆点 + 引线」那套语言，圆点落在每个小节标题上（导轨画在正文面板的左边距里，
    /// 所以不会被正文盖住）。
    /// </summary>
    internal class HelpDetailForm : Form
    {
        private const int DesignW = 860;
        private const int DesignH = 700;
        private const int PadX = 34;
        private const int HeaderH = 76;
        private const int BodyTop = 96;

        private readonly WindowReveal _reveal;
        private readonly HelpTopic _topic;
        private readonly List<HelpTopic> _all;
        private readonly HelpBody _body;
        private readonly List<int> _subY = new List<int>();
        private bool _sectionsRegistered;

        /// <summary>点了「下一主题」时带出来的目标（DialogResult.Retry + Tag）。</summary>
        public HelpTopic NextTopic { get { return Tag as HelpTopic; } }

        public HelpDetailForm(HelpTopic topic, List<HelpTopic> all)
        {
            _topic = topic;
            _all = all;
            _reveal = new WindowReveal(this, WindowReveal.Level, false);
            BuildUi();

            _body = new HelpBody(this, topic, _subY);
            _body.Location = new Point(Theme.S(PadX), Theme.S(BodyTop));
            _body.Size = new Size(Theme.S(DesignW - PadX * 2), ClientSize.Height - Theme.S(BodyTop) - Theme.S(84));
            Controls.Add(_body);
            _body.BringToFront();
            _body.MeasureNow();     // 先量一遍：小节导轨的锚点要在 OnShown 登记之前就位
        }

        private void BuildUi()
        {
            SuspendLayout();
            Text = "帮助 · " + _topic.Title;
            Icon = Res.AppIcon(32);
            Font = Theme.FontUi;
            ForeColor = Theme.Ink;
            BackColor = Theme.Bg;
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            AutoScaleMode = AutoScaleMode.None;
            KeyPreview = true;

            int designH = DesignH;
            try
            {
                int fit = (int)Math.Floor(Screen.PrimaryScreen.WorkingArea.Height /
                                          (double)(Theme.Scale <= 0f ? 1f : Theme.Scale)) - 24;
                if (fit > 420 && fit < designH) designH = fit;
            }
            catch { }
            ClientSize = new Size(Theme.S(DesignW), Theme.S(designH));
            DoubleBuffered = true;

            ChromeButton close = new ChromeButton(ChromeButton.GlyphKind.Close);
            close.Location = new Point(Theme.S(DesignW - 30 - 26), Theme.S(8));
            close.Size = new Size(Theme.S(26), Theme.S(22));
            close.BackColor = Theme.Panel;
            close.Invoked += delegate(object s, EventArgs e) { Close(); };
            Controls.Add(close);
            close.BringToFront();

            FlatButton next = new FlatButton();
            next.Text = "下一主题";
            next.Location = new Point(Theme.S(DesignW - PadX - 130), Theme.S(DesignH - 60));
            next.Size = new Size(Theme.S(130), Theme.S(44));
            next.BackColor = Theme.Bg;
            next.Click += delegate(object s, EventArgs e) { GotoSibling(1); };
            Controls.Add(next);

            FlatButton prev = new FlatButton();
            prev.Text = "上一主题";
            prev.Location = new Point(Theme.S(DesignW - PadX - 130 - 10 - 130), Theme.S(DesignH - 60));
            prev.Size = new Size(Theme.S(130), Theme.S(44));
            prev.BackColor = Theme.Bg;
            prev.Click += delegate(object s, EventArgs e) { GotoSibling(-1); };
            Controls.Add(prev);

            FlatButton back = new FlatButton();
            back.Text = "返回目录";
            back.Location = new Point(Theme.S(PadX), Theme.S(DesignH - 60));
            back.Size = new Size(Theme.S(130), Theme.S(44));
            back.BackColor = Theme.Bg;
            back.Click += delegate(object s, EventArgs e) { Close(); };
            Controls.Add(back);

            ResumeLayout(false);
        }

        /// <summary>
        /// 换主题：本窗关掉并让上层再开一个。不在同一个窗里换内容 —— 那样滚动位置、
        /// 导轨锚点、入场进度都得手动复位，容易出错。
        /// </summary>
        private void GotoSibling(int step)
        {
            int i = _all.IndexOf(_topic);
            if (i < 0) return;
            int j = i + step;
            if (j < 0) j = _all.Count - 1;
            if (j >= _all.Count) j = 0;
            Tag = _all[j];
            DialogResult = DialogResult.Retry;
            Close();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RegisterSubSections();
            if (_reveal != null) _reveal.BeginEnter();
        }

        /// <summary>把正文里的小节标题登记成导轨锚点（正文自己画标题，这里只提供圆点位置）。</summary>
        private void RegisterSubSections()
        {
            if (_sectionsRegistered) return;
            _sectionsRegistered = true;
            for (int i = 0; i < _subY.Count; i++)
                _reveal.AddSection(_subY[i], "", "", "");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_reveal != null && _reveal.InterceptClose(e)) return;
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _reveal != null) _reveal.Dispose();
            base.Dispose(disposing);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_reveal != null && _reveal.Busy) { _reveal.Skip(); return true; }
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == Keys.Left) { GotoSibling(-1); return true; }
            if (keyData == Keys.Right) { GotoSibling(1); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_reveal != null && _reveal.Busy) { _reveal.Skip(); return; }
            if (e.Button == MouseButtons.Left && e.Y <= Theme.S(HeaderH)) WindowChrome.BeginDrag(this);
        }

        // ---------------- 绘制：头部 + 导轨 + 版式（正文在 HelpBody 里）----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            int w = ClientSize.Width;
            int h = Theme.S(HeaderH);
            int band = Theme.S(3);
            int right = w - Theme.S(PadX);

            Theme.Fill(g, ClientRectangle, Theme.Bg);
            if (_reveal != null) UiPaint.Grid(g, ClientRectangle, _reveal.GridP, Theme.S(44), 16);
            Theme.Fill(g, new Rectangle(0, 0, w, band), Theme.Green);
            Theme.Fill(g, new Rectangle(0, band, w, h - band), Theme.Panel);
            Theme.Rule(g, 0, h - 1, w, Theme.Line);

            int step = Theme.S(22);
            for (int x = Theme.S(PadX), i = 0; x < right; x += step, i++)
            {
                bool tall = (i % 5) == 0;
                double tp = _reveal != null ? _reveal.Tick(i, 11, 300) : 1.0;
                if (tp <= 0.02) continue;
                Theme.VRule(g, x, h - 1 - Theme.S((int)Math.Round((tall ? 8 : 4) * tp)), h - 1,
                            Theme.Mix(Theme.Panel, tall ? Theme.Line : Theme.LineSoft, tp));
            }

            Image logo = Res.Logo();
            int box = Theme.S(32);
            double titleP = _reveal != null ? _reveal.TitleP : 1.0;
            double barP = _reveal != null ? _reveal.BarP : 1.0;
            if (logo != null && titleP > 0.02)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(logo, new Rectangle(Theme.S(PadX), band + Theme.S(20), box, box));
            }
            int titleX = Theme.S(PadX + 44);
            int barW = Theme.S(152);
            UiPaint.HighlightBar(g, new Rectangle(titleX - Theme.S(6), band + Theme.S(17), barW, Theme.S(22)), barP, true);
            if (titleP > 0.01)
            {
                Theme.DrawTracked(g, "HELP / DETAIL", Theme.FontTechBold, titleX, band + Theme.S(23), Theme.Ink, Theme.SF(2.2f), titleX + (int)Math.Round(barW * titleP));
            }
            Theme.DrawTracked(g, _topic.Index + " · " + _topic.En, Theme.FontSmall, Theme.S(PadX + 46), band + Theme.S(42),
                              Theme.Mix(Theme.Panel, Theme.Sub, titleP), Theme.SF(1.2f));

            Image mark = Res.Mark();
            int markH = Theme.S(18);
            int markW = (int)Math.Round(markH * 2.2);
            if (mark != null && titleP > 0.02)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(mark, new Rectangle(right - markW, Theme.S(34), markW, markH));
            }
            Theme.DrawTrackedRight(g, "RHINE · LAB", Theme.FontMonoSmall,
                right - markW - Theme.S(10), Theme.S(37), Theme.Mix(Theme.Panel, Theme.Sub, titleP), Theme.SF(1.6f));

            // 小节导轨（画在正文面板左侧的边距里，所以不会被正文盖住）
            if (_reveal != null && _reveal.WantsLayout && _subY.Count > 0)
            {
                int scroll = _body.ScrollY;
                int[] ys = new int[_subY.Count];
                double[] ps = new double[_subY.Count];
                for (int i = 0; i < _subY.Count; i++)
                {
                    ys[i] = Theme.S(_subY[i]) + Theme.S(BodyTop) + Theme.S(6) - scroll;
                    ps[i] = _reveal.SectionP(i);
                }
                UiPaint.SectionRail(g, Theme.S(14), Theme.S(BodyTop), ClientSize.Height - Theme.S(78),
                                    _reveal.RailP, ys, ps);
            }

            // 底部（提示行抬到按钮行上方，别被按钮压住）
            Theme.DrawTracked(g, "← → 切换主题   Esc 返回", Theme.FontMonoSmall,
                              Theme.S(PadX + 142), ClientSize.Height - Theme.S(84), Theme.Sub, Theme.SF(1.2f));
            UiPaint.EndDash(g, right, ClientSize.Height - Theme.S(80), _reveal != null ? _reveal.Enter : 1.0);
            Theme.DrawTrackedRight(g, "POWERED BY RHINE LAB", Theme.FontMonoSmall,
                right - Theme.S(30), ClientSize.Height - Theme.S(84), Theme.Sub, Theme.SF(1.4f));

            if (_reveal != null && _reveal.WantsLayout)
                UiPaint.Brackets(g, Rectangle.Inflate(ClientRectangle, -Theme.S(2), -Theme.S(2)), Theme.Ink, _reveal.BracketPhases());

            base.OnPaint(e);
        }

        /// <summary>
        /// 正文滚动面板：构造时量一次总高（缓存），之后只在尺寸变化时重量。
        /// 全部自绘 —— 段落 / 步骤 / 键值 / 提示 / 警告 / 命令行底衬。
        /// </summary>
        private class HelpBody : Panel
        {
            private readonly HelpTopic _topic;
            private readonly List<int> _subY;
            private int _contentH;
            private int _measuredW = -1;

            public HelpBody(Control owner, HelpTopic topic, List<int> subY)
            {
                _topic = topic;
                _subY = subY;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Bg;
                AutoScroll = true;
            }

            public int ScrollY { get { return AutoScrollPosition.Y == 0 ? 0 : -AutoScrollPosition.Y; } }

            /// <summary>对外量一次（构造后立刻调用，好让导轨锚点先就位）。</summary>
            public void MeasureNow()
            {
                using (Graphics g = CreateGraphics()) EnsureMeasured(g);
            }

            private static int Wrapped(Graphics g, string text, Font f, int width)
            {
                if (string.IsNullOrEmpty(text)) return Theme.S(16);
                return TextRenderer.MeasureText(g, text, f, new Size(Math.Max(40, width), int.MaxValue),
                                                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            }

            /// <summary>量总高与小节标题的位置。宽度变了才重量。</summary>
            private void EnsureMeasured(Graphics g)
            {
                int w = ClientSize.Width - Theme.S(10);
                if (w == _measuredW) return;
                _measuredW = w;
                _subY.Clear();

                int y = Theme.S(4);
                foreach (HelpBlock b in _topic.Blocks)
                {
                    if (b.Type == HelpBlock.Kind.Sub) { _subY.Add(y); y += Theme.S(30); continue; }
                    switch (b.Type)
                    {
                        case HelpBlock.Kind.Para:
                            y += Wrapped(g, b.Lines.Count > 0 ? b.Lines[0] : "", Theme.FontUi, w) + Theme.S(12);
                            break;
                        case HelpBlock.Kind.Steps:
                            foreach (string s in b.Lines) y += Wrapped(g, s, Theme.FontUi, w - Theme.S(26)) + Theme.S(8);
                            y += Theme.S(10);
                            break;
                        case HelpBlock.Kind.Kv:
                            if (b.Title.Length > 0) y += Theme.S(22);
                            y += b.Lines.Count * Theme.S(22) + Theme.S(10);
                            break;
                        case HelpBlock.Kind.Note:
                        case HelpBlock.Kind.Warn:
                            if (b.Title.Length > 0) y += Theme.S(22);
                            foreach (string s in b.Lines) y += Wrapped(g, s, Theme.FontSmall, w - Theme.S(24)) + Theme.S(6);
                            y += Theme.S(14);
                            break;
                        case HelpBlock.Kind.Cmd:
                            y += b.Lines.Count * Theme.S(20) + Theme.S(26);
                            break;
                    }
                }
                _contentH = y + Theme.S(16);
                AutoScrollMinSize = new Size(0, _contentH);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.None;
                Theme.Fill(g, ClientRectangle, BackColor);
                EnsureMeasured(g);

                int w = ClientSize.Width - Theme.S(10);
                int x = 0;
                int y = -AutoScrollPosition.Y + Theme.S(4);

                foreach (HelpBlock b in _topic.Blocks)
                {
                    if (b.Type == HelpBlock.Kind.Sub)
                    {
                        Theme.Fill(g, new Rectangle(0, y + Theme.S(8), Theme.S(6), Theme.S(6)), Theme.Amber);
                        Theme.DrawTracked(g, b.Title, Theme.FontMonoSmall, Theme.S(16), y + Theme.S(5), Theme.Ink, Theme.SF(1.8f));
                        Size ts = Theme.MeasureTracked(g, b.Title, Theme.FontMonoSmall, Theme.SF(1.8f));
                        int ruleX = Theme.S(30) + ts.Width;
                        if (ruleX < x + w - Theme.S(20)) Theme.Rule(g, ruleX, y + Theme.S(11), x + w, Theme.LineSoft);
                        y += Theme.S(30);
                        continue;
                    }

                    switch (b.Type)
                    {
                        case HelpBlock.Kind.Para:
                            y += DrawWrapped(g, b.Lines.Count > 0 ? b.Lines[0] : "", Theme.FontUi, Theme.InkSoft, 0, y, w) + Theme.S(12);
                            break;

                        case HelpBlock.Kind.Steps:
                            for (int i = 0; i < b.Lines.Count; i++)
                            {
                                Theme.Fill(g, new Rectangle(0, y + Theme.S(1), Theme.S(18), Theme.S(18)), Theme.Ink);
                                Theme.DrawTracked(g, (i + 1).ToString(), Theme.FontMonoSmall, Theme.S(6), y + Theme.S(4), Theme.PanelHi, 0f);
                                y += DrawWrapped(g, b.Lines[i], Theme.FontUi, Theme.Ink, Theme.S(26), y, w - Theme.S(26)) + Theme.S(8);
                            }
                            y += Theme.S(10);
                            break;

                        case HelpBlock.Kind.Kv:
                            if (b.Title.Length > 0)
                            {
                                Theme.DrawTracked(g, b.Title, Theme.FontMonoSmall, 0, y, Theme.Sub, Theme.SF(1.4f));
                                y += Theme.S(22);
                            }
                            foreach (string line in b.Lines)
                            {
                                int eq = line.IndexOf('=');
                                string k = eq > 0 ? line.Substring(0, eq) : line;
                                string v = eq > 0 ? line.Substring(eq + 1) : "";
                                Theme.DrawTracked(g, k, Theme.FontMonoSmall, Theme.S(4), y + Theme.S(4), Theme.Ink, Theme.SF(1.2f));
                                if (v.Length > 0)
                                    TextRenderer.DrawText(g, v, Theme.FontSmall,
                                        new Rectangle(Theme.S(196), y, w - Theme.S(196), Theme.S(20)), Theme.Sub,
                                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                                y += Theme.S(22);
                            }
                            y += Theme.S(10);
                            break;

                        case HelpBlock.Kind.Note:
                        case HelpBlock.Kind.Warn:
                            bool warn = b.Type == HelpBlock.Kind.Warn;
                            if (b.Title.Length > 0)
                            {
                                Theme.Fill(g, new Rectangle(0, y, Theme.S(3), Theme.S(18)), warn ? Theme.SignalAlert : Theme.Line);
                                Theme.DrawTracked(g, b.Title, Theme.FontMonoSmall, Theme.S(14), y + Theme.S(2),
                                                  warn ? Theme.SignalAlert : Theme.Ink, Theme.SF(1.4f));
                                y += Theme.S(22);
                            }
                            foreach (string line in b.Lines)
                                y += DrawWrapped(g, line, Theme.FontSmall, warn ? Theme.SignalAlert : Theme.Sub, Theme.S(14), y, w - Theme.S(24)) + Theme.S(6);
                            y += Theme.S(14);
                            break;

                        case HelpBlock.Kind.Cmd:
                            int ch = b.Lines.Count * Theme.S(20) + Theme.S(10);
                            Theme.Fill(g, new Rectangle(0, y, w, ch), Theme.PanelHi);
                            Theme.StrokeRect(g, new Rectangle(0, y, w - 1, ch - 1), Theme.LineSoft, 1f);
                            int cy = y + Theme.S(5);
                            foreach (string line in b.Lines)
                            {
                                TextRenderer.DrawText(g, line, Theme.FontMono,
                                    new Rectangle(Theme.S(10), cy, w - Theme.S(20), Theme.S(20)), Theme.Ink,
                                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
                                cy += Theme.S(20);
                            }
                            y += ch + Theme.S(16);
                            break;
                    }
                }
            }

            private int DrawWrapped(Graphics g, string text, Font f, Color c, int x, int y, int width)
            {
                if (string.IsNullOrEmpty(text)) return Theme.S(16);
                int h = Wrapped(g, text, f, width);
                TextRenderer.DrawText(g, text, f, new Rectangle(x, y, width, h), c,
                                      TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                return h;
            }
        }
    }
}
