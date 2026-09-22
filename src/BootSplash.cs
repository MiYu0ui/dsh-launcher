using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 开启动画：螺旋 DNA 环形加载器 + 中心莱茵生命标志。
    /// 版式沿用档案终端风格（暖灰白底、细线、宽字距排字、绿色顶边），
    /// 节奏参照站点开场：逐字输入 → 轮廓绘制 → 旋转加载 → 就绪 → 淡出。
    /// </summary>
    /// <remarks>
    /// 显示方式由 <see cref="LauncherContext"/> 决定：用 <c>Show()</c> 非模态显示（不能用 Application.Run，
    /// 否则结束时 ExitThread 会把主窗口一起关掉），关闭后由 FormClosed 回调把主窗口交还。
    /// 因此这里的 <c>Close()</c> 是安全的：它只关自己，不结束消息循环。
    /// 动画期间 <see cref="Anim"/> 是挂起的 —— 主窗口已经"可见但全透明"，不该再跟卡片抢 UI 线程。
    /// </remarks>
    internal class BootSplash : Form
    {
        // 版式基准（设计像素）：实际像素 = 设计值 × Theme 的缩放系数（Theme.S / Theme.SF）
        private const int DesignW = 620;
        private const int DesignH = 400;

        // 时间轴（秒）——完整档 / 精简档（精简档用于"已有实例时再次启动"）
        // 全部以 _clock 的读数为基准（而不是系统时钟），所以 Skip 只要拨 _timeShift 就能整段前移。
        private readonly double TDraw;        // 双螺旋绘制到位
        private readonly double TLogo;        // 中心标志开始出现
        private readonly double TFull;        // 加载完成：此点是分界，之前是入场淡入，之后是收尾
        private readonly double TFade;        // 收尾：卡片展开进主窗口并淡出
        private readonly double TitleStart, TitleDur, StatusStart, StatusDur, ProgStart;   // 标题擦入 / 状态字 / 进度条各自的起点与时长

        private const int Steps = 234;        // 螺旋采样段数（每圈 18 段，够顺滑又省一半绘制）
        private const double Turns = 13.0;    // 绕环圈数：螺距 ≈ 2.4×螺旋直径，才是 DNA 的比例
        private const int Rungs = 30;         // 碱基对数量

        private readonly Timer _timer;
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly Pen[] _strandInk = new Pen[6];
        private readonly Pen[] _strandAmber = new Pen[6];
        // 常驻画笔：构造函数里一次建好、Dispose 统一释放（OnPaint 里另有几支临时 Pen，用 using 就地释放）。
        // 注意 _tick / _tickLong / _rung 的 Color 是**每帧改写**的 —— 它们是可变状态，别在别处假设颜色固定。
        private readonly Pen _rung;           // 碱基对
        private readonly Pen _rungHot;        // 碱基对上沿环行进的高亮波
        private readonly Pen _tick;           // 刻度环短刻度
        private readonly Pen _tickLong;       // 刻度环长刻度
        // TODO(待确认): 下面这两支笔目前没有被用上（OnPaint 里画中心圆与绿弧用的都是局部 Pen），
        //               是预留给后续改动，还是可以删掉？
        private readonly Pen _centerRing;
        private readonly Pen _centerArc;
        private readonly Pen _barTrack;       // 底部进度条的底槽
        private readonly Pen _barFill;        // 底部进度条的填充

        private double _timeShift;            // 跳过用：叠加到时钟读数上的偏移（不重置 Stopwatch，所以跳过后曲线不会回跳）
        private bool _closing;                // 防重入：收尾只允许触发一次 Close
        private Bitmap _ringCache;            // 静态刻度环的缓存位图（本类唯一自建的 Bitmap，Dispose 里释放）
        private readonly GraphicsPath[] _bucketInk = new GraphicsPath[6];     // 按深度分桶的螺旋路径
        private readonly GraphicsPath[] _bucketAmber = new GraphicsPath[6];
        private Form _reveal;                 // 收尾时要淡入的主窗口
        private Rectangle _revealRect;        // 主窗口的边界（AttachReveal 时记下，作为插值终点）
        private Rectangle _startRect;         // 卡片自己的边界（OnShown 时记下，作为插值起点）

        /// <summary>
        /// 收尾衔接：主窗口先以全透明就位，动画最后 0.55 秒里卡片边界插值到主窗口边界、
        /// 自身淡出，同时主窗口淡入——两段之间不留硬切。
        /// </summary>
        /// <param name="target">接手的主窗口；为 null 时忽略（当作没有衔接，收尾只淡出）。</param>
        /// <remarks>
        /// 主窗口在这里被设成全透明，所以调用方**必须在它的句柄创建之前**打开 AllowTransparency：
        /// 否则这次赋值会抛（异常被吞掉），后果是 <c>_revealRect</c> 保持空矩形，
        /// Tick 里的边界插值被 <c>Width &gt; 0</c> 的判断挡掉，收尾只剩淡出、没有"卡片展开"那一段。
        /// </remarks>
        public void AttachReveal(Form target)
        {
            if (target == null) return;
            _reveal = target;
            try
            {
                target.Opacity = 0.0;
                _revealRect = target.Bounds;
            }
            catch { }
        }

        /// <summary>完整档开场（2.70 秒）：程序首次启动时用。</summary>
        public BootSplash() : this(false) { }

        /// <summary>建闪屏并立刻起表：时钟与 33ms 定时器都在构造函数里启动，没有单独的 Start。</summary>
        /// <remarks>
        /// 两件事必须在窗口句柄创建之前做完：<c>SetStyle</c>（全自绘 + 双缓冲）与 <c>AllowTransparency</c>
        /// （否则之后每帧改 Opacity 都会触发句柄重建，窗口会闪甚至消失）。
        /// 画笔与路径桶也在这里一次性建好，逐帧只改颜色与坐标，不再分配 GDI+ 对象。
        /// </remarks>
        /// <param name="compact">true = 精简档（约 1 秒），用于已有实例时再次启动的快速开场</param>
        public BootSplash(bool compact)
        {
            if (compact)
            {
                TDraw = 0.28; TLogo = 0.12; TFull = 0.70; TFade = 0.34;
                TitleStart = 0.14; TitleDur = 0.24;
                StatusStart = 0.20; StatusDur = 0.16;
                ProgStart = 0.10;
            }
            else
            {
                TDraw = 0.60; TLogo = 0.35; TFull = 2.15; TFade = 0.55;
                TitleStart = 0.35; TitleDur = 0.55;
                StatusStart = 0.55; StatusDur = 0.30;
                ProgStart = 0.25;
            }

            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            ClientSize = new Size(Theme.S(DesignW), Theme.S(DesignH));
            BackColor = Theme.Bg;
            KeyPreview = true;
            DoubleBuffered = true;
            AllowTransparency = true;   // 句柄创建前打开：避免淡入淡出时重建句柄

            // 按"深度"分 6 档预建画笔：越靠前越深越粗，形成体积感
            for (int i = 0; i < 6; i++)
            {
                int alpha = 44 + i * 36;
                float w = 1.0f + i * 0.24f;
                _strandInk[i] = new Pen(Color.FromArgb(alpha, Theme.Ink), w);
                _strandAmber[i] = new Pen(Color.FromArgb(alpha, Theme.Amber), w);
                _bucketInk[i] = new GraphicsPath();
                _bucketAmber[i] = new GraphicsPath();
            }
            _rung = new Pen(Theme.LineSoft, 1.2f);
            _rungHot = new Pen(Theme.Green, 1.6f);
            _tick = new Pen(Theme.LineSoft, 1f);
            _tickLong = new Pen(Theme.Line, 1f);
            _centerRing = new Pen(Theme.Line, 1f);
            _centerArc = new Pen(Theme.Green, 1.6f);
            _barTrack = new Pen(Theme.LineSoft, 2f);
            _barFill = new Pen(Theme.Green, 2f);

            _clock.Start();
            // 定时器与时钟是两回事：Skip 只拨 _timeShift，节拍始终按 33ms 走，跳过后曲线不会变形
            _timer = new Timer();
            _timer.Interval = 33;         // 30fps：够顺滑，比 40fps 省四分之一绘制
            _timer.Tick += delegate(object s, EventArgs e) { Tick(); };
            _timer.Start();
        }

        /// <summary>时间轴推进（定时器每 33ms 调一次）：改透明度与窗口边界，然后请求重绘。</summary>
        /// <remarks>
        /// 以 TFull 分两段：之前只做入场淡入；之后是收尾 —— 卡片自己淡出、边界插值到主窗口，
        /// 同时把主窗口的透明度从 0 推到 1（两段用同一条进度 p，同时到 0 / 1，中间不留硬切）。
        /// 到 TFull + TFade 时**先把主窗口的透明度顶到 1.0 再关自己**：这步是保险，
        /// 万一插值路径上出过异常，主窗口也不会永远停在透明态（那看起来就是"程序在跑但没有窗口"）。
        /// </remarks>
        private void Tick()
        {
            double now = _clock.Elapsed.TotalSeconds + _timeShift;
            if (now >= TFull + TFade)
            {
                if (_reveal != null && !_reveal.IsDisposed) { try { _reveal.Opacity = 1.0; } catch { } }
                if (!_closing) { _closing = true; Close(); }
                return;
            }
            if (now < TFull)
            {
                Opacity = Theme.Clamp01(now / 0.20);                    // 入场淡入
            }
            else
            {
                double p = Theme.Clamp01((now - TFull) / TFade);
                Opacity = 1.0 - p;
                if (_reveal != null && !_reveal.IsDisposed)
                {
                    try
                    {
                        _reveal.Opacity = Theme.Clamp01(p);                // 与卡片淡出对称：同时到 0 / 1
                        if (_startRect.Width > 0 && _revealRect.Width > 0)
                            Bounds = Theme.Lerp(_startRect, _revealRect, Theme.EaseInOutCubic(p));
                    }
                    catch { }
                }
            }
            Invalidate();
        }

        /// <summary>跳过开场：把时间轴直接拨到 TFull，而不是立刻关闭。</summary>
        /// <remarks>
        /// 不直接 Close 是因为收尾那一段（TFade）承担着"卡片展开进主窗口"的衔接，跳掉它就成了硬切。
        /// 只对 TFull 之前有效：已经进了收尾再点，时间轴不会再往前拨。
        /// </remarks>
        private void Skip()
        {
            double now = _clock.Elapsed.TotalSeconds + _timeShift;
            if (now < TFull) _timeShift += TFull - now;
        }

        /// <summary>窗口显示后记下自己的边界，并抢一次前台焦点。</summary>
        /// <remarks>
        /// <c>Bounds</c> 要在这里取：显示之前窗口位置还没定下来，而收尾的边界插值需要一个确定的起点。
        /// 抢焦点是为了让"按任意键跳过"真的能收到键 —— 闪屏不在任务栏上，不激活就收不到键盘。
        /// </remarks>
        /// <param name="e">事件参数，未使用。</param>
        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            _startRect = Bounds;                 // 收尾插值的起点
            Activate();
            BringToFront();
        }

        /// <summary>点一下就跳过开场（闪屏上没有别的可点，所以任意位置都算）。</summary>
        /// <param name="e">鼠标参数，未使用。</param>
        protected override void OnMouseDown(MouseEventArgs e) { Skip(); base.OnMouseDown(e); }
        /// <summary>按任意键跳过开场。</summary>
        /// <param name="e">键盘参数，未使用。</param>
        protected override void OnKeyDown(KeyEventArgs e) { Skip(); base.OnKeyDown(e); }

        /// <summary>
        /// 每帧从头画一遍：底板 → 刻度环 → 双螺旋 → 碱基对 → 中心标志 → 文案。
        /// </summary>
        /// <remarks>
        /// 绘制顺序就是遮挡顺序（螺旋压在刻度环上、中心圆盘再压住穿过标志的螺旋线），别随意调换。
        /// 几条 GDI+ 约定：
        /// ① 本方法里 new 出来的 Pen / SolidBrush 一律 <c>using</c> 就地释放；常驻画笔与路径桶归 Dispose 管；
        /// ② 图片来自 <c>Res.Mark()</c> 的**共享缓存实例**，只能画不能释放；
        /// ③ 刻度环在淡入结束后改用 <c>_ringCache</c> 位图整体贴上，省掉每帧 72 次 DrawLine ——
        ///    缓存是整窗尺寸且只建一次，闪屏不可缩放，所以不需要 Resize 失效逻辑。
        /// </remarks>
        /// <param name="e">绘制参数，本方法只用其中的 Graphics。</param>
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            double now = _clock.Elapsed.TotalSeconds + _timeShift;
            int w = ClientSize.Width;
            int h = ClientSize.Height;

            // ---- 底板 ----
            Theme.CardFrame(g, w, h);

            float cx = w / 2f;
            float cy = Theme.S(158);
            float R = Theme.SF(88);
            float A = Theme.SF(9f);           // 螺旋半径：直径 18，配 42 的螺距
            double sweep = 2.0 * Math.PI * Ease(Math.Min(1.0, now / TDraw));
            double phi = now * 0.85;

            // ---- 外圈刻度环 ----
            int tickCount = 72;
            float tickOuter = R + A + Theme.SF(18);
            // 刻度环是静态的：入场淡入结束后改用缓存位图，省掉每帧 72 次 DrawLine
            if (now > 0.65 && _ringCache != null)
            {
                g.DrawImageUnscaled(_ringCache, 0, 0);
            }
            else
            {
                for (int i = 0; i < tickCount; i++)
                {
                    double t = i * 2.0 * Math.PI / tickCount;
                    bool longTick = (i % 6) == 0;
                    float len = Theme.SF(longTick ? 7f : 3.4f);
                    double fade = Math.Min(1.0, Math.Max(0.0, (now - 0.10) / 0.5));
                    int a = (int)((longTick ? 170 : 110) * fade);
                    if (a <= 2) continue;
                    Pen p = longTick ? _tickLong : _tick;
                    p.Color = Color.FromArgb(a, longTick ? Theme.Line : Theme.LineSoft);
                    double ct = Math.Cos(t), st = Math.Sin(t);
                    g.DrawLine(p,
                        cx + (float)(ct * tickOuter), cy + (float)(st * tickOuter),
                        cx + (float)(ct * (tickOuter + len)), cy + (float)(st * (tickOuter + len)));
                }
                // 画完这一帧把刻度环缓存下来（整卡尺寸，后续直接贴图）
                // 缓存按整窗尺寸建、且只建一次：闪屏尺寸固定（无边框、不可缩放），所以不做 Resize 失效；
                // 将来若允许改尺寸，这里必须跟着加上"尺寸变了就重建"的逻辑
                if (now > 0.65 && _ringCache == null && Width > 0 && Height > 0)
                {
                    try
                    {
                        _ringCache = new Bitmap(Width, Height);
                        using (Graphics cg = Graphics.FromImage(_ringCache))
                        {
                            cg.SmoothingMode = SmoothingMode.AntiAlias;
                            for (int i = 0; i < tickCount; i++)
                            {
                                double t2 = i * 2.0 * Math.PI / tickCount;
                                bool longTick2 = (i % 6) == 0;
                                float len2 = Theme.SF(longTick2 ? 7f : 3.4f);
                                Pen p2 = longTick2 ? _tickLong : _tick;
                                p2.Color = Color.FromArgb(longTick2 ? 170 : 110, longTick2 ? Theme.Line : Theme.LineSoft);
                                double ct2 = Math.Cos(t2), st2 = Math.Sin(t2);
                                cg.DrawLine(p2,
                                    cx + (float)(ct2 * tickOuter), cy + (float)(st2 * tickOuter),
                                    cx + (float)(ct2 * (tickOuter + len2)), cy + (float)(st2 * (tickOuter + len2)));
                            }
                        }
                    }
                    catch { _ringCache = null; }
                }
            }

            // ---- 双螺旋：按深度分 6 桶攒成路径，每桶一次 DrawPath（原先每段一次 DrawLine，约 468 次）----
            for (int b = 0; b < 6; b++) { _bucketInk[b].Reset(); _bucketAmber[b].Reset(); }
            int lastInk = -1, lastAmber = -1;
            for (int i = 0; i < Steps; i++)
            {
                double t0 = i * 2.0 * Math.PI / Steps;
                if (t0 > sweep) break;
                double t1 = (i + 1) * 2.0 * Math.PI / Steps;
                double phase0 = Turns * t0 + phi;
                double z = Math.Cos(phase0);

                double phase1 = Turns * t1 + phi;
                double s0 = Math.Sin(phase0), s1 = Math.Sin(phase1);

                float x0 = cx + (float)(Math.Cos(t0) * (R + A * s0));
                float y0 = cy + (float)(Math.Sin(t0) * (R + A * s0));
                float x1 = cx + (float)(Math.Cos(t1) * (R + A * s1));
                float y1 = cy + (float)(Math.Sin(t1) * (R + A * s1));
                float x2 = cx + (float)(Math.Cos(t0) * (R - A * s0));
                float y2 = cy + (float)(Math.Sin(t0) * (R - A * s0));
                float x3 = cx + (float)(Math.Cos(t1) * (R - A * s1));
                float y3 = cy + (float)(Math.Sin(t1) * (R - A * s1));

                // z = cos(相位) ∈ [-1,1] 就是"朝向观察者的深度"，线性映射到 0..5 桶：0 最靠后、5 最靠前
                int bucket = (int)Math.Round((z + 1.0) * 0.5 * 5.0);
                if (bucket < 0) bucket = 0; else if (bucket > 5) bucket = 5;

                // 换桶就得起新图形，否则 AddLine 会把两段之间补一条连线
                if (bucket != lastInk) { _bucketInk[bucket].StartFigure(); lastInk = bucket; }
                _bucketInk[bucket].AddLine(x0, y0, x1, y1);
                if (bucket != lastAmber) { _bucketAmber[bucket].StartFigure(); lastAmber = bucket; }
                _bucketAmber[bucket].AddLine(x2, y2, x3, y3);
            }
            for (int b = 0; b < 6; b++)      // 0→5 即"由背到前"，与原先两趟绘制的遮挡关系一致
            {
                if (_bucketInk[b].PointCount > 1) g.DrawPath(_strandInk[b], _bucketInk[b]);
                if (_bucketAmber[b].PointCount > 1) g.DrawPath(_strandAmber[b], _bucketAmber[b]);
            }

            // ---- 碱基对（含沿环行进的高亮波）----
            double wave = (now * 0.5) % 1.0;
            for (int i = 0; i < Rungs; i++)
            {
                double t = i * 2.0 * Math.PI / Rungs;
                if (t > sweep) break;
                double phase = Turns * t + phi;
                double s = Math.Sin(phase);
                double z = Math.Cos(phase);
                float xa = cx + (float)(Math.Cos(t) * (R + A * s));
                float ya = cy + (float)(Math.Sin(t) * (R + A * s));
                float xb = cx + (float)(Math.Cos(t) * (R - A * s));
                float yb = cy + (float)(Math.Sin(t) * (R - A * s));

                // 环上的距离：把 (pos - wave) 规整到 [-0.5, 0.5] 再取绝对值，0 附近就是波头所在
                double pos = t / (2.0 * Math.PI);
                double dist = Math.Abs(((pos - wave + 1.5) % 1.0) - 0.5);
                if (dist < 0.05 && now > TDraw)
                {
                    g.DrawLine(_rungHot, xa, ya, xb, yb);
                }
                else
                {
                    int a = (int)(30 + 112 * (z + 1.0) * 0.5);
                    _rung.Color = Color.FromArgb(a, Theme.Line);
                    g.DrawLine(_rung, xa, ya, xb, yb);
                }
            }

            // ---- 中心：莱茵生命标志（先铺一块净空圆盘，避免螺旋线穿过标志）----
            float coreR = R * 0.46f;
            using (SolidBrush disc = new SolidBrush(Theme.Bg))
                g.FillEllipse(disc, cx - coreR, cy - coreR, coreR * 2, coreR * 2);
            using (Pen p = new Pen(Color.FromArgb(150, Theme.Line), 1f))
                g.DrawEllipse(p, cx - coreR, cy - coreR, coreR * 2, coreR * 2);

            // 中心外缓慢旋转的绿色弧（呼应顶边）
            using (Pen p = new Pen(Color.FromArgb(200, Theme.Green), Theme.SF(1.6f)))
                g.DrawArc(p, cx - coreR - Theme.SF(8), cy - coreR - Theme.SF(8),
                          (coreR + Theme.SF(8)) * 2, (coreR + Theme.SF(8)) * 2, (float)(-now * 42.0), 92f);

            double logoAge = now - TLogo;
            if (logoAge > 0)
            {
                double p = Math.Min(1.0, logoAge / 0.45);
                float scale = (float)EaseOutBack(p);
                float mw = Theme.SF(80) * scale;
                float mh = mw / 2.2f;
                Image mark = Res.Mark();
                if (mark != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(mark, new RectangleF(cx - mw / 2f, cy - mh / 2f, mw, mh));
                }
            }

            // ---- 文案 ----
            int pad = Theme.S(24);
            int right = w - pad;
            double typeP = Math.Min(1.0, now / TitleDur);

            string head = "BOOT SEQUENCE / 001";
            Theme.DrawTracked(g, Clip(head, typeP), Theme.FontMonoSmall, pad, Theme.S(16), Theme.Sub, Theme.SF(1.6f));
            Theme.DrawTrackedRight(g, "v" + LauncherContext.Version, Theme.FontMonoSmall, right, Theme.S(16), Theme.Sub, Theme.SF(1.6f));

            string title = "DSH LAUNCHER";
            double titleP = Math.Min(1.0, Math.Max(0.0, (now - TitleStart) / TitleDur));
            Theme.DrawTrackedCenter(g, Clip(title, titleP), Theme.FontTechBold,
                new Rectangle(0, Theme.S(288), w, Theme.S(24)), Theme.Ink, Theme.SF(3.0f));

            double prog = Math.Min(1.0, Math.Max(0.0, (now - ProgStart) / (TFull - ProgStart)));
            string status;
            // 状态字按进度分段（而不是按时间）：两档时间轴跑起来就是同一套节奏，改时间不影响文案切换点
            if (prog < 0.28) status = "INITIALIZING";
            else if (prog < 0.58) status = "LOADING ARCHIVE";
            else if (prog < 0.92) status = "AUTHORIZING";
            else status = "READY";
            Theme.DrawTrackedCenter(g, status, Theme.FontMonoSmall,
                new Rectangle(0, Theme.S(318), w, Theme.S(18)), Theme.Sub, Theme.SF(1.8f));

            int barX = Theme.S(76);
            int barW = w - barX * 2;
            int barY = Theme.S(354);
            g.DrawLine(_barTrack, barX, barY, barX + barW, barY);
            float fill = (float)(barW * Ease(prog));
            if (fill > 0.5f) g.DrawLine(_barFill, barX, barY, barX + fill, barY);
            Theme.DrawTrackedRight(g, ((int)Math.Round(prog * 100)) + "%", Theme.FontMonoSmall,
                barX + barW, barY - Theme.S(18), Theme.Sub, Theme.SF(1.4f));

            Theme.DrawTracked(g, "RHINE · LAB", Theme.FontMonoSmall, pad, Theme.S(376), Theme.Sub, Theme.SF(1.8f));
            Theme.DrawTrackedRight(g, "ARRAY / ACCESS", Theme.FontMonoSmall, right, Theme.S(376), Theme.Line, Theme.SF(1.4f));

            base.OnPaint(e);
        }

        /// <summary>按进度从尾部截断字符串，做出"逐字输入"的效果。</summary>
        /// <remarks>按**字符数**截而不是按像素截：这套文案是逐字宽字距排的，按字露出刚好。</remarks>
        /// <param name="text">完整文本。</param>
        /// <param name="p">0..1 的进度，越界会被夹住。</param>
        /// <returns>截断后的子串；进度为 0 时是空串。</returns>
        private static string Clip(string text, double p)
        {
            int n = (int)Math.Round(text.Length * Math.Min(1.0, Math.Max(0.0, p)));
            return text.Substring(0, n);
        }

        /// <summary>三次缓出：起步快、收尾慢。螺旋展开与进度条都用它。</summary>
        private static double Ease(double p) { return 1.0 - Math.Pow(1.0 - p, 3.0); }

        /// <summary>回弹缓出：越过 1 再退回来，用来做标志"弹一下"落位的缩放。</summary>
        /// <remarks>中途会短暂大于 1（形变用没问题），所以**不要**拿它去算透明度或长度。</remarks>
        /// <param name="p">0..1 的进度。</param>
        /// <returns>缩放系数（峰值约 1.1）。</returns>
        private static double EaseOutBack(double p)
        {
            const double c1 = 1.70158, c3 = c1 + 1.0;
            double q = p - 1.0;
            return 1.0 + c3 * q * q * q + c1 * q * q;
        }

        /// <summary>释放本类建的全部非托管绘制资源（定时器、画笔、路径桶、缓存位图）。</summary>
        /// <remarks>
        /// 定时器必须先 Stop 再 Dispose：它每 33ms 会回调 Tick 并重绘，释放过程中再进来一次就是访问已销毁对象。
        /// Stopwatch 不需要释放（它不持有句柄）。这些资源都是构造函数里建的，字段是 readonly，
        /// 所以这里只释放、不置 null（缓存位图除外，它是可空的，顺手置 null 免得重入）。
        /// </remarks>
        /// <param name="disposing">true = 走托管释放路径；只有这种情形才需要释放绘制资源。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (_timer != null) { _timer.Stop(); _timer.Dispose(); }
                if (_ringCache != null) { _ringCache.Dispose(); _ringCache = null; }
                for (int i = 0; i < 6; i++)
                {
                    if (_strandInk[i] != null) _strandInk[i].Dispose();
                    if (_strandAmber[i] != null) _strandAmber[i].Dispose();
                    if (_bucketInk[i] != null) _bucketInk[i].Dispose();
                    if (_bucketAmber[i] != null) _bucketAmber[i].Dispose();
                }
                Pen[] rest = new Pen[] { _rung, _rungHot, _tick, _tickLong, _centerRing, _centerArc, _barTrack, _barFill };
                foreach (Pen p in rest) { if (p != null) p.Dispose(); }
            }
            base.Dispose(disposing);
        }
    }
}
