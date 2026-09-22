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
        // 版式基准（设计像素）：实际像素 = 设计值 × Theme 的缩放系数（Theme.S / Theme.SF），
        // 下面的坐标一律写设计值，别再乘一遍缩放。
        private const int DesignW = 900;
        private const int DesignH = 620;
        private const int PadX = 30;
        private const int HeaderH = 76;
        private const int ListW = 312;
        private const int RowH = 24;

        private readonly WindowReveal _reveal;                          // 只管时间的入场/退场驱动器，画法在 OnPaint 这边
        private readonly List<HelpTopic> _topics = HelpTopics.All;       // 主题数据（静态缓存的纯数据，只读）
        private readonly List<string> _groups = HelpTopics.Groups();     // 分组名，顺序 = 主题首次出现的顺序

        private int _selected;          // 选中主题在 _topics 里的下标
        private int _listScroll;        // 目录滚动偏移（设计像素）
        private int _hoverRow = -1;     // 悬停的行号（行 = 分组标题与主题摊平后的序号），-1 = 不在列表上
        private FlatButton _btnDetail;
        private FlatButton _btnClose;

        /// <summary>某一行对应的主题下标；-1 = 这一行是分组标题。行号 → 主题下标。</summary>
        private readonly List<int> _rowTopic = new List<int>();

        /// <summary>
        /// 建帮助（二级界面）：先起入场驱动器，再搭界面与目录行。
        /// </summary>
        /// <remarks>
        /// 驱动器必须在窗口句柄创建之前构造 —— 它要靠 <c>AllowTransparency</c> + <c>Opacity = 0</c>
        /// 让窗口隐形就位，句柄建好之后再改透明度会触发句柄重建。
        /// 界面的内容全部来自 <see cref="HelpTopics"/>（纯数据），本类只负责排版与交互。
        /// </remarks>
        public HelpForm()
        {
            _reveal = new WindowReveal(this, WindowReveal.Level, false);
            BuildUi();
            BuildRows();
        }

        /// <summary>
        /// 搭界面：固定 900 设计宽，高度按屏幕工作区收缩。
        /// </summary>
        /// <remarks>
        /// 高度有两套基准，改的时候别混：窗口客户区用收缩后的 <c>designH</c>，
        /// 而底部按钮一律按设计高 <c>DesignH</c> 定位 —— 窗口被压矮时按钮会落到可视区之外，
        /// 靠 <c>AutoScroll</c> 滚出来（这也是本窗唯一需要 AutoScroll 的地方）。
        /// 章节锚点 96 用设计像素登记，与 OnPaint 里画章节头的位置一一对应。
        /// </remarks>
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
            KeyPreview = true;      // 键盘统一交给 ProcessCmdKey：方向键选主题、Enter 进详情、Esc 返回

            int designH = DesignH;      // 收缩后的客户区高（只有缩不下时才开 AutoScroll）
            try
            {
                // 屏幕工作区（设备像素）换算回设计像素，再留 24 设计像素余量。
                // 太矮（不到 420）就不缩了 —— 再缩目录只剩两三行，不如保持原尺寸让用户滚。
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
        /// <remarks>
        /// 行序决定目录的显示顺序：组按 <c>_groups</c> 的顺序，组内按主题在 <c>_topics</c> 里的声明顺序。
        /// 选中、悬停、滚动全都以**行号**为单位，所以要调整目录顺序只需要动这里；
        /// 分组标题行记 -1，移动选择时会被跳过。
        /// </remarks>
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

        /// <summary>窗口显示之后再起入场动画。</summary>
        /// <remarks>
        /// 顺序不能反过来：<c>BeginEnter</c> 要拿窗口当前的位置当位移基准（先下沉再升上来），
        /// 而窗口没显示时那个位置还没定下来。
        /// </remarks>
        /// <param name="e">事件参数，未使用。</param>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (_reveal != null) _reveal.BeginEnter();
        }

        /// <summary>关闭前先让驱动器播完退场。</summary>
        /// <remarks>
        /// <c>InterceptClose</c> 只在第一次关闭时接管（返回 true 时本方法立即 return，这次关闭被取消）；
        /// 退场途中再点一次关闭就会真正放行 —— 这个"只拦一次"是有意的，保证窗口永远关得掉。
        /// </remarks>
        /// <param name="e">关闭参数；被接管时驱动器会把它置为 Cancel。</param>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (_reveal != null && _reveal.InterceptClose(e)) return;
            base.OnFormClosing(e);
        }

        /// <summary>释放入场驱动器（它自己持有一个 Timer）。</summary>
        /// <param name="disposing">true = 走托管释放路径；只有这种情形才需要释放驱动器。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && _reveal != null) _reveal.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>本窗的全部键盘操作（窗口没有可聚焦的输入控件，所以在这里统一处理）。</summary>
        /// <remarks>
        /// 第一分支是"入场期间按任意键先跳过动画"：<c>Skip</c> 之后 <c>Busy</c> 立刻变 false，
        /// 所以只有入场中的第一次按键会被吃掉，之后的按键照常生效 —— 不会把方向键/Esc 一直吞掉。
        /// </remarks>
        /// <param name="msg">消息引用，原样透传给基类。</param>
        /// <param name="keyData">按键；方向键与 PageUp/PageDown 移动选择，Enter 进详情，Esc 关闭。</param>
        /// <returns>true = 已处理，不再往下传。</returns>
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

        /// <summary>把选择上下移动若干行。</summary>
        /// <remarks>
        /// 先夹到 [0, 行数-1]，再沿步进方向跳过 <c>-1</c> 的分组标题行。
        /// 跳过的方向跟着 <paramref name="step"/> 走，所以 PageDown 落点靠后、PageUp 落点靠前，
        /// 不会把选择卡在分组标题上。
        /// </remarks>
        /// <param name="step">行数增量（±1 为单行，±5 为翻页）。</param>
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

        /// <summary>主题下标反查行号。</summary>
        /// <param name="topic">主题在 <c>_topics</c> 里的下标。</param>
        /// <returns>对应的行号；查不到时返回 0（当成首行，免得移动选择时被卡住）。</returns>
        private int RowOfTopic(int topic)
        {
            for (int i = 0; i < _rowTopic.Count; i++) if (_rowTopic[i] == topic) return i;
            return 0;
        }

        // 目录可视区（设计像素）：上边在章节头之下，下边给底部按钮行让位。
        /// <summary>目录列表的上边界（设计像素）。</summary>
        private int ListTop { get { return Theme.S(122); } }
        /// <summary>目录列表的下边界（设计像素）：按设计高算，窗口被压矮时可视区会小于它。</summary>
        private int ListBottom { get { return Theme.S(DesignH - 84); } }

        /// <summary>把某一行滚进可视区（只调滚动量，重绘由调用方负责）。</summary>
        /// <remarks>
        /// 只夹下界不夹上界：滚动量滚过头时下面的绘制端只画"完整落在面板内"的行，
        /// 最坏就是末尾留白，而上界由 <c>OnMouseWheel</c> 负责夹住。
        /// </remarks>
        /// <param name="row">目标行号。</param>
        private void EnsureVisible(int row)
        {
            int y = row * Theme.S(RowH) - _listScroll;
            int h = ListBottom - ListTop;
            if (y < 0) _listScroll += y;
            else if (y + Theme.S(RowH) > h) _listScroll += (y + Theme.S(RowH) - h);
            if (_listScroll < 0) _listScroll = 0;
        }

        /// <summary>滚轮滚动目录。</summary>
        /// <remarks>一格滚轮（Delta ±120）滚两行；上界在这里夹住，下界夹 0。</remarks>
        /// <param name="e">滚轮参数，只用 Delta。</param>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            _listScroll -= e.Delta / 120 * Theme.S(RowH * 2);
            if (_listScroll < 0) _listScroll = 0;
            int max = Math.Max(0, _rowTopic.Count * Theme.S(RowH) - (ListBottom - ListTop));
            if (_listScroll > max) _listScroll = max;
            Invalidate();
        }

        /// <summary>更新悬停行 —— 行号真的变了才重绘，鼠标在列表上滑动时不会每像素都刷一遍。</summary>
        /// <param name="e">鼠标参数，只用 Location。</param>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int row = RowAt(e.Location);
            if (row != _hoverRow) { _hoverRow = row; Invalidate(); }
        }

        /// <summary>鼠标离开窗体：清掉悬停行（不然会留着一行高亮）。</summary>
        /// <param name="e">事件参数，未使用。</param>
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverRow != -1) { _hoverRow = -1; Invalidate(); }
        }

        /// <summary>命中测试：屏幕坐标 → 行号。</summary>
        /// <remarks>先判是否落在目录面板的矩形内（横向按 PadX..PadX+ListW），再按 y 反算行号并加回滚动偏移。</remarks>
        /// <param name="p">客户区坐标。</param>
        /// <returns>行号；不在列表上或超出总行数时返回 -1。</returns>
        private int RowAt(Point p)
        {
            if (p.X < Theme.S(PadX) || p.X > Theme.S(PadX + ListW)) return -1;
            if (p.Y < ListTop || p.Y > ListBottom) return -1;
            int row = (p.Y - ListTop + _listScroll) / Theme.S(RowH);
            return (row >= 0 && row < _rowTopic.Count) ? row : -1;
        }

        /// <summary>点击：入场期间先跳过动画，否则按命中结果处理。</summary>
        /// <remarks>
        /// 命中主题行分两种：点当前选中项 = 直接进详情（省一次点击）；点别的行 = 只换选中。
        /// 都没命中、且落在标题栏高度内、且是左键，才当作拖动窗口 —— 顺序不能颠倒，
        /// 否则点目录也会把窗口拖走。
        /// </remarks>
        /// <param name="e">鼠标参数。</param>
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

        /// <summary>打开详情（三级界面），模态显示，关掉后回到本窗。</summary>
        /// <remarks>
        /// 完整的主题列表一并传下去：详情窗要支持"上一/下一主题"，得知道自己在列表里的位置。
        /// 用 <c>using</c> 是为了异常路径上也能释放窗口。
        /// 详情窗换主题时不刷新自身，而是以 <c>DialogResult.Retry</c> 关闭并把目标放进 <c>Tag</c>
        /// （见 HelpDetailForm.NextTopic），由上层决定要不要再开一窗；**本调用点没有接这个返回值**，
        /// 所以"下一主题"目前的效果只是退回目录。
        /// </remarks>
        private void OpenDetail()
        {
            if (_selected < 0 || _selected >= _topics.Count) return;
            // TODO(待确认): 详情窗的 DialogResult.Retry / NextTopic 是否要在这里接续成"换主题再开一窗"？
            using (HelpDetailForm d = new HelpDetailForm(_topics[_selected], _topics))
                d.ShowDialog(this);
        }

        // ---------------- 绘制 ----------------

        /// <summary>
        /// 画整套版式：底、极淡网格、绿顶边与标题栏、刻度带、标识、两部分内容、章节头、四角括号。
        /// </summary>
        /// <remarks>
        /// 绘制顺序就是叠放顺序，别随意调换（网格在底色之上、内容在网格之上）。
        /// 两处不显然的地方：
        /// ① 章节头用 <c>TranslateTransform</c> 跟着 <c>AutoScrollPosition</c> 平移 ——
        ///    AutoScroll 只会移动**子控件**，自绘内容框架不管；平移完必须立刻用反向位移还原，
        ///    否则后面的四角括号会跟着偏移。
        /// ② 标题的"擦入"用手动截断（<c>Theme.DrawTracked</c> 的 maxX）而不是 <c>SetClip</c>：
        ///    这套排字走 GDI，GDI 文字不认 GDI+ 的裁剪区。
        /// </remarks>
        /// <param name="e">绘制参数，本方法只用其中的 Graphics。</param>
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
            g.TranslateTransform(0, AutoScrollPosition.Y);   // 自绘内容跟随滚动（子控件会移，自绘不会 —— 框架不做这个平移）
            DrawSectionHeads(g);
            g.TranslateTransform(0, -AutoScrollPosition.Y);

            if (_reveal.WantsLayout)
                UiPaint.Brackets(g, Rectangle.Inflate(ClientRectangle, -Theme.S(2), -Theme.S(2)), Theme.Ink, _reveal.BracketPhases());

            base.OnPaint(e);
        }

        /// <summary>画两栏的章节头（近黑 chip + 中文标题 + 灰色英文小字）。</summary>
        /// <remarks>
        /// 标题的 x 由章节序号决定：第 0 节在左栏（目录上方），第 1 节在右栏（概览上方）。
        /// 每节各自有自己的显形进度，进度不足就直接跳过整块 —— 入场时它们是逐笔出现的。
        /// </remarks>
        /// <param name="g">目标画布。</param>
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

        /// <summary>画左侧主题目录：细线卡片、分组标题行、主题行（含选中/悬停态）、滚动条。</summary>
        /// <remarks>
        /// 两条硬约束，改动前先看清楚：
        /// ① **不能用 SetClip 裁行**：行文字走 TextRenderer（GDI），GDI 文字不认 GDI+ 的裁剪区，
        ///    越界的行会照画到面板外面 —— 所以这里只画"完整落在面板内"的行，靠整行滚动来对齐。
        /// ② 标题必须按可用宽度手动截断（<c>Theme.ClipTracked</c>），理由同上，
        ///    也免得它顶到行尾的 SECT. 编号上。
        /// 滚动条只在内容超出可视区时才出现，长度按内容比例算。
        /// </remarks>
        /// <param name="g">目标画布。</param>
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


        /// <summary>取某一行所属的分组名。</summary>
        /// <remarks>
        /// 行结构是"分组标题行 + 若干主题行"，所以从本行往上回溯，遇到的第一个标题行就是所属分组；
        /// 再数它是第几个标题行，用这个序号去 <c>_groups</c> 取名（序号与 <c>_groups</c> 的顺序
        /// 由 <c>BuildRows</c> 保证一致）。
        /// </remarks>
        /// <param name="row">行号。</param>
        /// <returns>分组名；回溯不到（理论上不会发生）时返回空串。</returns>
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

        /// <summary>画右侧概览：章节 chip、主题标题、摘要、要点清单、底部快捷键提示与署名。</summary>
        /// <remarks>
        /// 要点是"能塞多少塞多少"：剩余高度不足时直接跳出循环（<c>ClientSize.Height - 110</c> 那道闸），
        /// 保证不会压到底部的按钮行与提示行上。
        /// </remarks>
        /// <param name="g">目标画布。</param>
        /// <param name="right">内容右边界的设备像素 x（已扣掉右边距）。</param>
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
