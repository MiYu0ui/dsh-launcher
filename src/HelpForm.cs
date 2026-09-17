using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 帮助（二级界面）：左侧主题目录、右侧概览，底部「打开详细说明」进三级界面。
    /// 与设置 / 卸载同一套档案终端语言，入场同样是版式逐笔就位。
    /// </summary>
    internal class HelpForm : Form
    {
        private const int DesignW = 900;
        private const int DesignH = 620;
        private const int PadX = 30;
        private const int HeaderH = 76;
        private const int ListW = 312;
        private const int RowH = 24;

        private readonly WindowReveal _reveal;
        private readonly List<HelpTopic> _topics = HelpTopics.All;
        private readonly List<string> _groups = HelpTopics.Groups();

        private int _selected;
        private int _listScroll;        // 目录滚动偏移（设计像素）
        private int _hoverRow = -1;
        private FlatButton _btnDetail;
        private FlatButton _btnClose;

        /// <summary>某一行对应的主题下标；-1 = 这一行是分组标题。行号 → 主题下标。</summary>
        private readonly List<int> _rowTopic = new List<int>();

        public HelpForm()
        {
            _reveal = new WindowReveal(this, WindowReveal.Level, false);
            BuildUi();
            BuildRows();
        }

        private void BuildUi()
        {
            SuspendLayout();
            Text = "帮助";
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
                int fit = (int)Math.Floor(Screen.PrimaryScreen.WorkingArea.Height / (double)(Theme.Scale <= 0f ? 1f : Theme.Scale)) - 24;
                if (fit > 420 && fit < designH) { designH = fit; AutoScroll = true; }
            }
            catch { }
            ClientSize = new Size(Theme.S(DesignW), Theme.S(designH));
            DoubleBuffered = true;

            // 章节锚点（版式用）
            _reveal.AddSection(96, "SECT. 01", "主题目录", "TOPIC INDEX");
            _reveal.AddSection(96, "SECT. 02", "概览", "OVERVIEW");

            ChromeButton close = new ChromeButton(ChromeButton.GlyphKind.Close);
            close.Location = new Point(Theme.S(DesignW - 30 - 26), Theme.S(8));
            close.Size = new Size(Theme.S(26), Theme.S(22));
            close.BackColor = Theme.Panel;
            close.Invoked += delegate(object s, EventArgs e) { Close(); };
            Controls.Add(close);
            close.BringToFront();

            _btnDetail = new FlatButton();
            _btnDetail.Text = "打开详细说明";
            _btnDetail.Primary = true;
            _btnDetail.Location = new Point(Theme.S(DesignW - PadX - 168), Theme.S(DesignH - 62));
            _btnDetail.Size = new Size(Theme.S(168), Theme.S(44));
            _btnDetail.BackColor = Theme.Bg;
            _btnDetail.Click += delegate(object s, EventArgs e) { OpenDetail(); };
            Controls.Add(_btnDetail);

            _btnClose = new FlatButton();
            _btnClose.Text = "关闭";
            _btnClose.Location = new Point(Theme.S(DesignW - PadX - 168 - 12 - 110), Theme.S(DesignH - 62));
            _btnClose.Size = new Size(Theme.S(110), Theme.S(44));
            _btnClose.BackColor = Theme.Bg;
            _btnClose.Click += delegate(object s, EventArgs e) { Close(); };
            Controls.Add(_btnClose);

            ResumeLayout(false);
        }

        /// <summary>把主题按分组摊平成"行"：分组标题行 + 若干主题行。</summary>
        private void BuildRows()
        {
            _rowTopic.Clear();
            foreach (string g in _groups)
            {
                _rowTopic.Add(-1);
                for (int i = 0; i < _topics.Count; i++)
                    if (_topics[i].Group == g) _rowTopic.Add(i);
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_reveal != null) _reveal.BeginEnter();
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
            if (keyData == Keys.Enter) { OpenDetail(); return true; }
            if (keyData == Keys.Down) { Move(1); return true; }
            if (keyData == Keys.Up) { Move(-1); return true; }
            if (keyData == Keys.PageDown) { Move(5); return true; }
            if (keyData == Keys.PageUp) { Move(-5); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        private void Move(int step)
        {
            int row = RowOfTopic(_selected) + step;
            if (row < 0) row = 0;
            if (row >= _rowTopic.Count) row = _rowTopic.Count - 1;
            while (row > 0 && row < _rowTopic.Count && _rowTopic[row] < 0) row += step >= 0 ? 1 : -1;   // 跳过分组标题
            if (row >= 0 && row < _rowTopic.Count && _rowTopic[row] >= 0) _selected = _rowTopic[row];
            EnsureVisible(row);
            Invalidate();
        }

        private int RowOfTopic(int topic)
        {
            for (int i = 0; i < _rowTopic.Count; i++) if (_rowTopic[i] == topic) return i;
            return 0;
        }

        private int ListTop { get { return Theme.S(122); } }
        private int ListBottom { get { return Theme.S(DesignH - 84); } }

        private void EnsureVisible(int row)
        {
            int y = row * Theme.S(RowH) - _listScroll;
            int h = ListBottom - ListTop;
            if (y < 0) _listScroll += y;
            else if (y + Theme.S(RowH) > h) _listScroll += (y + Theme.S(RowH) - h);
            if (_listScroll < 0) _listScroll = 0;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _listScroll -= e.Delta / 120 * Theme.S(RowH * 2);
            if (_listScroll < 0) _listScroll = 0;
            int max = Math.Max(0, _rowTopic.Count * Theme.S(RowH) - (ListBottom - ListTop));
            if (_listScroll > max) _listScroll = max;
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int row = RowAt(e.Location);
            if (row != _hoverRow) { _hoverRow = row; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverRow != -1) { _hoverRow = -1; Invalidate(); }
        }

        private int RowAt(Point p)
        {
            if (p.X < Theme.S(PadX) || p.X > Theme.S(PadX + ListW)) return -1;
            if (p.Y < ListTop || p.Y > ListBottom) return -1;
            int row = (p.Y - ListTop + _listScroll) / Theme.S(RowH);
            return (row >= 0 && row < _rowTopic.Count) ? row : -1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_reveal != null && _reveal.Busy) { _reveal.Skip(); return; }
            int row = RowAt(e.Location);
            if (row >= 0 && _rowTopic[row] >= 0)
            {
                if (_rowTopic[row] == _selected) OpenDetail();     // 再点一下当前项 = 直接进详情
                else { _selected = _rowTopic[row]; Invalidate(); }
                return;
            }
            if (e.Button == MouseButtons.Left && e.Y <= Theme.S(HeaderH)) WindowChrome.BeginDrag(this);
        }

        private void OpenDetail()
        {
            if (_selected < 0 || _selected >= _topics.Count) return;
            using (HelpDetailForm d = new HelpDetailForm(_topics[_selected], _topics))
                d.ShowDialog(this);
        }

        // ---------------- 绘制 ----------------

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            int w = ClientSize.Width;
            int h = Theme.S(HeaderH);
            int band = Theme.S(3);
            int right = w - Theme.S(PadX);

            Theme.Fill(g, ClientRectangle, Theme.Bg);
            UiPaint.Grid(g, ClientRectangle, _reveal.GridP, Theme.S(44), 16);
            Theme.Fill(g, new Rectangle(0, 0, w, band), Theme.Green);
            Theme.Fill(g, new Rectangle(0, band, w, h - band), Theme.Panel);
            Theme.Rule(g, 0, h - 1, w, Theme.Line);

            int step = Theme.S(22);
            for (int x = Theme.S(PadX), i = 0; x < right; x += step, i++)
            {
                bool tall = (i % 5) == 0;
                double tp = _reveal.Tick(i, 11, 300);
                if (tp <= 0.02) continue;
                Theme.VRule(g, x, h - 1 - Theme.S((int)Math.Round((tall ? 8 : 4) * tp)), h - 1,
                            Theme.Mix(Theme.Panel, tall ? Theme.Line : Theme.LineSoft, tp));
            }

            Image logo = Res.Logo();
            int box = Theme.S(32);
            if (logo != null && _reveal.TitleP > 0.02)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(logo, new Rectangle(Theme.S(PadX), band + Theme.S(20), box, box));
            }
            int titleX = Theme.S(PadX + 44);
            int barW = Theme.S(74);
            UiPaint.HighlightBar(g, new Rectangle(titleX - Theme.S(6), band + Theme.S(17), barW, Theme.S(22)), _reveal.BarP, true);
            if (_reveal.TitleP > 0.01)
                Theme.DrawTracked(g, "HELP", Theme.FontTechBold, titleX, band + Theme.S(23), Theme.Ink, Theme.SF(2.2f),
                                  titleX + (int)Math.Round(barW * _reveal.TitleP));   // 手动截断（GDI 文字不认 SetClip）
            Theme.DrawTracked(g, "帮助", Theme.FontSmall, Theme.S(PadX + 46), band + Theme.S(42),
                              Theme.Mix(Theme.Panel, Theme.Sub, _reveal.TitleP), Theme.SF(1.2f));

            Image mark = Res.Mark();
            int markH = Theme.S(18);
            int markW = (int)Math.Round(markH * 2.2);
            if (mark != null && _reveal.TitleP > 0.02)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(mark, new Rectangle(right - markW, Theme.S(34), markW, markH));
            }
            Theme.DrawTrackedRight(g, "RHINE · LAB", Theme.FontMonoSmall,
                right - markW - Theme.S(10), Theme.S(37), Theme.Mix(Theme.Panel, Theme.Sub, _reveal.TitleP), Theme.SF(1.6f));

            DrawTopics(g);
            DrawOverview(g, right);

            // 章节标题（chip + 中文 + 英文）
            DrawSectionHeads(g);

            if (_reveal.WantsLayout)
                UiPaint.Brackets(g, Rectangle.Inflate(ClientRectangle, -Theme.S(2), -Theme.S(2)), Theme.Ink, _reveal.BracketPhases());

            base.OnPaint(e);
        }

        private void DrawSectionHeads(Graphics g)
        {
            List<WindowReveal.Section> secs = _reveal.Sections;
            for (int i = 0; i < secs.Count; i++)
            {
                double p = _reveal.SectionP(i);
                if (p <= 0.01) continue;
                WindowReveal.Section s = secs[i];
                int x = Theme.S(i == 0 ? PadX : PadX + ListW + 40);
                int y = Theme.S(s.Y);
                int chipW = UiPaint.Chip(g, x, y, s.Index, p);
                int tx = x + chipW + Theme.S(10);
                Theme.DrawTracked(g, s.Cn, Theme.FontUiBold, tx, y + Theme.S(3), Theme.Mix(Theme.Bg, Theme.Ink, p), Theme.SF(0.6f));
                Size cn = Theme.MeasureTracked(g, s.Cn, Theme.FontUiBold, Theme.SF(0.6f));
                if (s.En.Length > 0)
                    Theme.DrawTracked(g, s.En, Theme.FontMonoSmall, tx + cn.Width + Theme.S(12), y + Theme.S(4),
                                      Theme.Mix(Theme.Bg, Theme.Sub, p), Theme.SF(1.6f));
            }
        }

        private void DrawTopics(Graphics g)
        {
            int left = Theme.S(PadX);
            int w = Theme.S(ListW);
            int top = ListTop;
            int bottom = ListBottom;

            // 目录底板：细线卡片 + 右侧竖线，与卸载窗的清单面板同一语言
            Theme.Fill(g, new Rectangle(left, top, w, bottom - top), Theme.PanelHi);
            Theme.StrokeRect(g, new Rectangle(left, top, w - 1, bottom - top - 1), Theme.Line, 1f);

            // ⚠️ 这里**不能靠 SetClip 裁剪**：行文字走 TextRenderer（GDI），GDI 文字不认 GDI+ 裁剪区，
            //    越界的行会照画到面板外面（实测踩到过）。改成只画"完整落在面板内"的行 + 整行滚动。
            int y = top + Theme.S(6) - _listScroll;
            int rowH = Theme.S(RowH);
            for (int row = 0; row < _rowTopic.Count; row++)
            {
                int topic = _rowTopic[row];
                int ry = y + row * rowH;
                if (ry < top + Theme.S(2) || ry + rowH > bottom - Theme.S(2)) continue;   // 半截的行不画

                if (topic < 0)
                {
                    // 分组标题：小一号、宽字距、右下延伸细线
                    string gname = GroupOfRow(row);
                    Theme.DrawTracked(g, gname, Theme.FontMonoSmall, left + Theme.S(10), ry + Theme.S(6), Theme.Sub, Theme.SF(1.6f));
                    Size gs = Theme.MeasureTracked(g, gname, Theme.FontMonoSmall, Theme.SF(1.6f));
                    Theme.Rule(g, left + Theme.S(18) + gs.Width, ry + Theme.S(12), left + w - Theme.S(12), Theme.LineSoft);
                    continue;
                }

                bool sel = topic == _selected;
                bool hov = row == _hoverRow;
                if (sel || hov)
                    Theme.Fill(g, new Rectangle(left + Theme.S(4), ry, w - Theme.S(8), rowH),
                               sel ? Theme.Ink : Theme.Panel);
                Color tc = sel ? Theme.PanelHi : Theme.Ink;
                Color sc = sel ? Theme.PanelHi : Theme.Sub;

                // 行首一个小方块（选中时实心）：与四角括号同一套几何
                int bx = left + Theme.S(14);
                if (sel) Theme.Fill(g, new Rectangle(bx, ry + Theme.S(9), Theme.S(6), Theme.S(6)), Theme.AmberHi);
                else Theme.StrokeRect(g, new Rectangle(bx, ry + Theme.S(9), Theme.S(5), Theme.S(5)), Theme.Line, 1f);

                // 标题按可用宽度截断，别顶到 SECT. 编号上（同样不能用 SetClip）
                int indexW = Theme.MeasureTracked(g, _topics[topic].Index, Theme.FontMonoSmall, Theme.SF(1.2f)).Width;
                int titleRight = left + w - Theme.S(16) - indexW;
                Theme.DrawTracked(g, Theme.ClipTracked(g, _topics[topic].Title, Theme.FontUi, Theme.SF(0.4f), titleRight - (bx + Theme.S(14))),
                                  Theme.FontUi, bx + Theme.S(14), ry + Theme.S(4), tc, Theme.SF(0.4f));
                Theme.DrawTrackedRight(g, _topics[topic].Index, Theme.FontMonoSmall,
                    left + w - Theme.S(12), ry + Theme.S(5), sc, Theme.SF(1.2f));
            }

            // 滚动条（内容超出才画）
            int contentH = _rowTopic.Count * Theme.S(RowH) + Theme.S(12);
            int viewH = bottom - top;
            if (contentH > viewH)
            {
                int barH = Math.Max(Theme.S(24), viewH * viewH / contentH);
                int maxScroll = contentH - viewH;
                int barY = top + (int)((viewH - barH) * (_listScroll / (double)Math.Max(1, maxScroll)));
                Theme.Fill(g, new Rectangle(left + w - Theme.S(5), barY, Theme.S(3), barH), Theme.Line);
            }
        }


        private string GroupOfRow(int row)        {
            for (int i = row; i >= 0; i--)
            {
                if (_rowTopic[i] < 0)
                {
                    // 第几个分组标题
                    int n = 0;
                    for (int j = 0; j <= i; j++) if (_rowTopic[j] < 0) n++;
                    return n - 1 < _groups.Count ? _groups[n - 1] : "";
                }
            }
            return "";
        }

        private void DrawOverview(Graphics g, int right)
        {
            if (_selected < 0 || _selected >= _topics.Count) return;
            HelpTopic t = _topics[_selected];
            int x = Theme.S(PadX + ListW + 40);
            int w = right - x;
            int y = Theme.S(140);

            UiPaint.Chip(g, x, y, t.Index, _reveal.SectionP(1));
            Theme.DrawTracked(g, t.En, Theme.FontMonoSmall, x + UiPaint.ChipWidth(g, t.Index) + Theme.S(10), y + Theme.S(4),
                              Theme.Mix(Theme.Bg, Theme.Sub, _reveal.SectionP(1)), Theme.SF(1.6f));
            y += Theme.S(30);

            TextRenderer.DrawText(g, t.Title, Theme.FontStatus,
                new Rectangle(x, y, w, Theme.S(28)), Theme.Ink,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);
            y += Theme.S(32);
            Theme.SweepRule(g, x, right, y, Theme.Line, Theme.Green, 8.0, 0.2);
            y += Theme.S(12);

            // 摘要（换行）
            int sh = TextRenderer.MeasureText(g, t.Summary, Theme.FontUi, new Size(w, int.MaxValue),
                                              TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            TextRenderer.DrawText(g, t.Summary, Theme.FontUi, new Rectangle(x, y, w, sh), Theme.InkSoft,
                                  TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
            y += sh + Theme.S(14);

            // 要点：每行一个小方块 + 文字
            foreach (string k in t.KeyPoints)
            {
                if (y > ClientSize.Height - Theme.S(110)) break;
                Theme.Fill(g, new Rectangle(x, y + Theme.S(6), Theme.S(5), Theme.S(5)), Theme.Amber);
                int kh = TextRenderer.MeasureText(g, k, Theme.FontSmall, new Size(w - Theme.S(16), int.MaxValue),
                                                  TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
                TextRenderer.DrawText(g, k, Theme.FontSmall, new Rectangle(x + Theme.S(14), y, w - Theme.S(16), kh), Theme.Sub,
                                      TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix);
                y += kh + Theme.S(8);
            }

            Theme.DrawTracked(g, "↑ ↓ 选择   Enter 打开详细说明   Esc 关闭", Theme.FontMonoSmall,
                              Theme.S(PadX), ClientSize.Height - Theme.S(56), Theme.Sub, Theme.SF(1.2f));
            UiPaint.EndDash(g, right, ClientSize.Height - Theme.S(52), _reveal.SectionP(1));
            Theme.DrawTrackedRight(g, "POWERED BY RHINE LAB", Theme.FontMonoSmall,
                right - Theme.S(30), ClientSize.Height - Theme.S(56), Theme.Sub, Theme.SF(1.4f));
        }
    }
}
