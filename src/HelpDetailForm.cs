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
        // 版式基准（设计像素）：实际像素 = 设计值 × Theme 的缩放系数（Theme.S / Theme.SF）
        private const int DesignW = 860;
        private const int DesignH = 700;
        private const int PadX = 34;
        private const int HeaderH = 76;
        private const int BodyTop = 96;

        private readonly WindowReveal _reveal;                        // 只管时间的入场/退场驱动器
        private readonly HelpTopic _topic;                            // 本窗要展开的主题
        private readonly List<HelpTopic> _all;                        // 完整主题列表，用于"上一/下一主题"
        private readonly HelpBody _body;                              // 正文滚动面板（自绘，见文件末尾的嵌套类）
        private readonly List<int> _subY = new List<int>();            // 各小节标题的设计 y，由 HelpBody 量高时回填
        private bool _sectionsRegistered;                             // 幂等开关：锚点只登记一次，重复登记会在导轨上叠出多个圆点

        /// <summary>点了「下一主题」时带出来的目标（DialogResult.Retry + Tag）。</summary>
        /// <remarks>
        /// 约定是：换主题时本窗不刷新内容，而是设 <c>Tag</c> + <c>DialogResult.Retry</c> 后关闭，
        /// 由上层读这个属性决定要不要再开一窗（见 <c>GotoSibling</c>）。
        /// 目前工程里没有任何调用点读它 —— HelpForm 忽略了 ShowDialog 的返回值，这个协议只写了一半。
        /// </remarks>
        public HelpTopic NextTopic { get { return Tag as HelpTopic; } }

        /// <summary>
        /// 建详情窗：先搭界面（客户区尺寸在这里定下来），再建正文面板并量一次高。
        /// </summary>
        /// <remarks>
        /// 几步的先后是有依赖的，别调换：
        /// ① <c>BuildUi</c> 先定下客户区高度；
        /// ② 正文面板的尺寸要用那个高度算（高度 = 客户区高 − 上边距 − 底部按钮行），
        ///    这里混用了两种单位：<c>ClientSize.Height</c> 已是设备像素，减去的两个边距是设计像素；
        /// ③ <c>MeasureNow</c> 必须在 OnShown 登记导轨锚点之前跑完，否则导轨拿不到小节的 y。
        /// </remarks>
        /// <param name="topic">要展开的主题。</param>
        /// <param name="all">完整主题列表，供前后翻页用；本窗只读不改。</param>
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

        /// <summary>搭界面：无边框固定尺寸，高度受屏幕工作区限制，底部一排「返回目录 / 上一主题 / 下一主题」。</summary>
        /// <remarks>
        /// 与二级界面不同，本窗**不开 AutoScroll**（正文自己带滚动条），所以高度只缩不滚：
        /// 屏幕装不下时按工作区收缩，正文面板跟着变矮。
        /// 三个按钮一律按设计高 <c>DesignH</c> 定位，与二级界面的做法保持一致。
        /// </remarks>
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
        /// <remarks>
        /// 目标通过 <c>Tag</c> 带出去，返回值固定是 <c>DialogResult.Retry</c>（与"取消/关闭"区分开）。
        /// 列表是环形的：到头就绕到另一端，所以这里不会出现"禁用上一主题"这种状态。
        /// </remarks>
        /// <param name="step">+1 = 下一主题，-1 = 上一主题。</param>
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

        /// <summary>窗口显示后再登记导轨锚点并起入场动画。</summary>
        /// <remarks>顺序不能反：<c>BeginEnter</c> 要拿窗口当前位置当位移基准，而它要等窗口显示后才定下来。</remarks>
        /// <param name="e">事件参数，未使用。</param>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            RegisterSubSections();
            if (_reveal != null) _reveal.BeginEnter();
        }

        /// <summary>把正文里的小节标题登记成导轨锚点（正文自己画标题，这里只提供圆点位置）。</summary>
        /// <remarks>
        /// 数据源是 <c>_subY</c>，由 <c>HelpBody</c> 量高时回填 —— 所以本方法只能放在 OnShown，
        /// 不能挪进构造函数（那时还没量）。锚点的 chip/中英文都是空的：这些圆点只需要位置。
        /// </remarks>
        private void RegisterSubSections()
        {
            if (_sectionsRegistered) return;
            _sectionsRegistered = true;
            for (int i = 0; i < _subY.Count; i++)
                _reveal.AddSection(_subY[i], "", "", "");
        }

        /// <summary>关闭前先让驱动器播完退场。</summary>
        /// <remarks>
        /// <c>InterceptClose</c> 只在第一次关闭时接管；退场途中再关一次就放行 ——
        /// 这个"只拦一次"是有意的，保证模态窗永远关得掉。
        /// 顺带说一句：被拦截的那次关闭会把 <c>DialogResult</c> 重置成 Cancel，
        /// 驱动器自己会把原值记下来再写回去（否则"下一主题"会静默失效）。
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

        /// <summary>本窗的键盘操作（窗口没有输入控件，统一在这里处理）。</summary>
        /// <remarks>
        /// 第一分支是"入场期间按任意键先跳过动画"：<c>Skip</c> 之后 <c>Busy</c> 立刻变 false，
        /// 所以只有入场中的第一次按键会被吃掉，不会把 Esc/方向键一直吞掉。
        /// 本窗不处理上下键与翻页键：正文的滚动交给面板自己的滚动条与滚轮，键盘不参与。
        /// </remarks>
        /// <param name="msg">消息引用，原样透传给基类。</param>
        /// <param name="keyData">按键；左右换主题，Esc 返回目录。</param>
        /// <returns>true = 已处理，不再往下传。</returns>
        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (_reveal != null && _reveal.Busy) { _reveal.Skip(); return true; }
            if (keyData == Keys.Escape) { Close(); return true; }
            if (keyData == Keys.Left) { GotoSibling(-1); return true; }
            if (keyData == Keys.Right) { GotoSibling(1); return true; }
            return base.ProcessCmdKey(ref msg, keyData);
        }

        /// <summary>鼠标按下：入场期间先跳过动画，否则在标题栏高度内按下即拖动窗口。</summary>
        /// <remarks>只认左键，且只在头部区域生效 —— 正文面板自己会吃掉落在它身上的点击。</remarks>
        /// <param name="e">鼠标参数。</param>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (_reveal != null && _reveal.Busy) { _reveal.Skip(); return; }
            if (e.Button == MouseButtons.Left && e.Y <= Theme.S(HeaderH)) WindowChrome.BeginDrag(this);
        }

        // ---------------- 绘制：头部 + 导轨 + 版式（正文在 HelpBody 里）----------------

        /// <summary>
        /// 画本窗自己的部分：头部（绿顶边、标题栏、刻度带、标识与署名）、左侧小节导轨、底部提示与按钮带。
        /// 正文不在这里 —— 它在 <c>HelpBody</c> 里自绘。
        /// </summary>
        /// <remarks>
        /// 两处不显然的地方：
        /// ① 所有入场进度都做了"驱动器缺席"的兜底（<c>_reveal == null</c> 时按终态 1.0 画），
        ///    这样在驱动器之外的地方预览这个窗也不会画出空白版式。
        /// ② 导轨圆点的 y 要把正文的滚动量减掉：圆点必须跟着正文一起动，
        ///    所以每帧都从 <c>_body.ScrollY</c> 现算，不能缓存。
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
        /// <remarks>
        /// 面板开了 <c>AllPaintingInWmPaint</c> + <c>OptimizedDoubleBuffer</c>：一帧里只走 OnPaint，
        /// 且整块面板双缓冲，滚动时不会闪。滚动条由 <c>AutoScrollMinSize</c> 驱动（在量高时设），
        /// 所以"量高"和"排版的绘制"是两套公式，**必须同步改**，否则滚动条长度会和内容对不上。
        /// </remarks>
        private class HelpBody : Panel
        {
            private readonly HelpTopic _topic;
            private readonly List<int> _subY;         // 宿主传进来的共享列表：量高时往里写小节锚点
            private int _contentH;                    // 量出的正文总高（喂给 AutoScrollMinSize）
            private int _measuredW = -1;              // 上次量高时的内容宽度；-1 = 还没量过

            /// <summary>建正文面板（只做初始化，量高交给 <c>MeasureNow</c>）。</summary>
            /// <param name="owner">宿主窗口。当前实现未用到它 —— 面板的尺寸与位置全由宿主在外部设好。</param>
            /// <param name="topic">要展开的主题（只读数据）。</param>
            /// <param name="subY">宿主提供的小节 y 列表，量高时会被清空并回填。</param>
            public HelpBody(Control owner, HelpTopic topic, List<int> subY)
            {
                _topic = topic;
                _subY = subY;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                         ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                BackColor = Theme.Bg;
                AutoScroll = true;
            }

            /// <summary>当前滚动量（正数）：宿主画导轨圆点时要用它把锚点跟正文对齐。</summary>
            /// <remarks>
            /// WinForms 的 <c>AutoScrollPosition</c> 在滚动后是**负值**，这里翻成正数再对外暴露。
            /// 等于 0 的判断只是为了跳过取负号的琐碎开销，语义上与 -AutoScrollPosition.Y 一致。
            /// </remarks>
            public int ScrollY { get { return AutoScrollPosition.Y == 0 ? 0 : -AutoScrollPosition.Y; } }

            /// <summary>对外量一次（构造后立刻调用，好让导轨锚点先就位）。</summary>
            /// <remarks>
            /// 这里临时借一个 Graphics 只为量文字，量完立刻释放（<c>using</c>）；
            /// 结果会缓存在 <c>_contentH</c> / <c>_measuredW</c> 里，OnPaint 不会重复量。
            /// </remarks>
            public void MeasureNow()
            {
                using (Graphics g = CreateGraphics()) EnsureMeasured(g);
            }

            /// <summary>按宽度量出一段文字的换行高度（绘制与量高共用它，保证两边算出来一致）。</summary>
            /// <remarks>
            /// 空串也返回一行的高度 —— 段落之间的节奏就靠它撑着，返回 0 会让相邻块贴在一起。
            /// 宽度下限取 40：面板极窄或还没布局完时宽度可能算出 0/负数，那会让 TextRenderer 的结果失去意义。
            /// </remarks>
            /// <param name="g">用于量字的 Graphics。</param>
            /// <param name="text">待量文本，可为 null/空。</param>
            /// <param name="f">字体。</param>
            /// <param name="width">可用宽度（设备像素）。</param>
            /// <returns>换行后的高度（设备像素）。</returns>
            private static int Wrapped(Graphics g, string text, Font f, int width)
            {
                if (string.IsNullOrEmpty(text)) return Theme.S(16);
                return TextRenderer.MeasureText(g, text, f, new Size(Math.Max(40, width), int.MaxValue),
                                                TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix).Height;
            }

            /// <summary>量总高与小节标题的位置。宽度变了才重量。</summary>
            /// <remarks>
            /// 两个副作用要记住：① 清空并回填 <c>_subY</c>（宿主靠它摆导轨圆点）；
            /// ② 设 <c>AutoScrollMinSize</c>（滚动条就是靠它出现的）。
            /// 下面的排版公式必须与 OnPaint 里那套一一对应 —— 少算或多算都会让滚动条与内容错位，
            /// 这是本类最容易改坏的地方。
            /// </remarks>
            /// <param name="g">用于量字的 Graphics。</param>
            private void EnsureMeasured(Graphics g)
            {
                int w = ClientSize.Width - Theme.S(10);
                if (w == _measuredW) return;
                _measuredW = w;
                _subY.Clear();

                int y = Theme.S(4);
                foreach (HelpBlock b in _topic.Blocks)
                {
                    // 小节标题单独处理：它既占版面又是导轨锚点（_subY 里每一项对应导轨上一个圆点）
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

            /// <summary>
            /// 按块顺序排完整篇正文 —— 每次重绘都从头排一遍，落在面板外的部分交给绘制裁剪区挡掉。
            /// </summary>
            /// <remarks>
            /// y 从 <c>-AutoScrollPosition.Y</c> 起算 —— 自绘内容要自己跟着滚动偏移走，框架只管子控件。
            /// 每种块的排版公式都要与 <c>EnsureMeasured</c> 保持一致（块间距、行高、内边距都是成对出现
            /// 的数字），改动时务必两处一起改。
            /// </remarks>
            /// <param name="e">绘制参数，本方法只用其中的 Graphics。</param>
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
                                // 只按**第一个**等号切：值里再有等号也不会被截断；没有等号就整行当键、值为空
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

            /// <summary>画一段会自动换行的文字，并返回它占掉的高度。</summary>
            /// <remarks>
            /// 返回值就是"下一个块该从哪继续"的 y 增量，调用方一律写成 <c>y += DrawWrapped(...) + 间距</c>。
            /// 量高与绘制都走 <c>Wrapped</c>，所以画出来的高度必然等于量出来的高度。
            /// </remarks>
            /// <param name="g">目标画布。</param>
            /// <param name="text">文本，可为 null/空（此时只占一行高度）。</param>
            /// <param name="f">字体。</param>
            /// <param name="c">颜色。</param>
            /// <param name="x">左边界（设备像素，相对本面板）。</param>
            /// <param name="y">上边界（已含滚动偏移）。</param>
            /// <param name="width">可用宽度。</param>
            /// <returns>占用的高度（设备像素）。</returns>
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
