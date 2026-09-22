using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 圆形加载器：Rhine Lab 授权环。
    /// 依据设计基准实现——黑白双环由画面外收拢、内弧反向旋转并减速、
    /// 六颗琥珀节点沿轨道依次亮起、中央圆点闪变后收缩。
    /// 在暗色屏幕上绘制（#11181b），黑/白/琥珀三色才有对比。
    /// </summary>
    /// <remarks>
    /// 全自绘：画面都在 OnPaint 里，动画由一个 Timer 推进，Stopped / Failed / PortConflict 时定时器会停
    /// （见 SetStatus），画面就静静停住。所有尺寸都过 Theme.S / Theme.SF 换算，高 DPI 下比例不走样。
    /// </remarks>
    internal class RingIndicator : Control
    {
        private readonly Timer _timer;      // 推进动画；停掉即画面静止
        private readonly Stopwatch _clock = new Stopwatch();   // 单调计时，不受系统时间被改动的影响
        private ServerStatus _status = ServerStatus.Stopped;   // 决定配色、是否动画、下方状态词

        private double _angleOuter;      // 外环角度（顺时针）
        private double _angleInner;      // 内弧角度（逆时针）
        private double _nodePhase;       // 节点轨道相位
        private double _sweepOuter = 268.0;   // 平滑后的外弧弧长
        private double _sweepInner = 156.0;   // 平滑后的内弧弧长
        private double _lastMs;          // 上一帧时刻，用来算 dt
        // TODO(待确认): 这个字段只在 SetStatus 里被赋值，全文件没有任何地方读它 ——
        //               收拢入场现在按"控件创建时刻"算（见 OnPaint 里的 since）。
        //               是遗留的废字段、还是等着接"每次状态切换重播入场"，需要作者确认后再决定删或接。
        private double _statusSince;     // 进入当前状态的时刻（秒）

        // 轨道上的琥珀节点数（设计基准就是 6 颗，沿轨道依次亮起）
        private const int NodeCount = 6;

        /// <summary>
        /// 全自绘控件：开双缓冲消除闪烁；定时器先按 33ms（约 30fps）走，SetStatus 会按状态再调间隔。
        /// </summary>
        public RingIndicator()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Theme.Dark;
            _timer = new Timer();
            _timer.Interval = 33;
            _timer.Tick += delegate(object s, EventArgs e) { Tick(); };
            _clock.Start();
            _lastMs = _clock.Elapsed.TotalMilliseconds;
        }

        /// <summary>当前状态（只读）。改状态请走 SetStatus，那里会顺带重置计时并调整定时器。</summary>
        public ServerStatus Status { get { return _status; } }

        /// <summary>
        /// 切换状态。状态没变就直接返回 —— 既不重置计时也不重启动画，
        /// 这样面板频繁刷同一个状态时，正在播的入场动画不会被打断。
        /// </summary>
        public void SetStatus(ServerStatus status)
        {
            if (_status == status) return;
            _status = status;
            _statusSince = _clock.Elapsed.TotalSeconds;
            bool animate = status == ServerStatus.Starting || status == ServerStatus.Running ||
                           status == ServerStatus.External || status == ServerStatus.Stopping;
            // Starting 是"正在接入"，用 30fps 让动感连贯；Running / External 这类常态 110ms 足够，省 CPU。
            _timer.Interval = status == ServerStatus.Starting ? 33 : 110;
            if (animate) { if (!_timer.Enabled) _timer.Start(); }
            else _timer.Stop();
            Invalidate();
        }

        /// <summary>
        /// 推进一帧的角度与弧长。dt 被夹在 1ms..250ms：某一帧被拖长（拖动窗口、断点、休眠恢复）时，
        /// 角度不会一次跳出去很远。busy（接入/停止中）转得快并带缓慢呼吸，其余状态只做低速巡航。
        /// </summary>
        private void Tick()
        {
            double now = _clock.Elapsed.TotalMilliseconds;
            double dt = Math.Max(0.001, Math.Min(0.25, (now - _lastMs) / 1000.0));
            _lastMs = now;

            bool busy = _status == ServerStatus.Starting || _status == ServerStatus.Stopping;
            if (busy)
            {
                // "反向旋转并减速"：外环顺时针、内弧逆时针，速度带缓慢起伏
                double breathe = 0.55 + 0.45 * Math.Cos(now / 1150.0);
                _angleOuter += 155.0 * breathe * dt;
                _angleInner -= 118.0 * breathe * dt;
                _nodePhase += 62.0 * dt;
            }
            else
            {
                _angleOuter += 16.0 * dt;
                _angleInner -= 11.0 * dt;
                _nodePhase += 20.0 * dt;
            }

            // 弧长平滑过渡：状态切换时不会"跳"一下，而是缓缓收合/补全
            double k = Math.Min(1.0, dt * 2.6);
            _sweepOuter += ((busy ? 268.0 : 360.0) - _sweepOuter) * k;
            _sweepInner += ((busy ? 156.0 : 360.0) - _sweepInner) * k;
            Invalidate();
        }

        /// <summary>画一帧：巡行边框 → 轨道 → 双环/断环/虚环 → 节点 → 中央圆点 → 状态词。</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Theme.Fill(g, ClientRectangle, Theme.Dark);

            Rectangle frame = new Rectangle(0, 0, Width, Height);
            // 暗屏同样用"沿轮廓巡行"的动法，只是换成暗色系配色
            Theme.DrawLiveFrame(g, new Rectangle(0, 0, frame.Width - 1, frame.Height - 1),
                                Theme.DarkLine, Theme.AmberHi, false, 6.8, 0.6);
            Theme.CornerTicks(g, Rectangle.Inflate(frame, -4, -4), Theme.DarkLine, Theme.S(9));

            double now = _clock.Elapsed.TotalMilliseconds;
            double since = now / 1000.0;      // 自控件创建起算：收拢入场只播一次，不随状态切换重播

            // 入场：环从画面外收拢（0.7s）
            double intro = Math.Min(1.0, since / 0.7);
            // ease-out cubic：先快后慢地"收拢"到位，收尾不突兀。
            double ease = 1.0 - Math.Pow(1.0 - intro, 3.0);
            double gather = 1.0 + (1.0 - ease) * 0.42;

            float pad = Theme.S(26);
            // 取宽高中较小的一边当环的外接正方形，并额外预留底部 14 设计像素给状态词，
            // 于是环心比控件中心略高一点。
            float box = Math.Min(Width - pad * 2, Height - pad * 2 - Theme.S(14));
            float cx = Width / 2f;
            float cy = pad + box / 2f;
            float rOut = box / 2f * (float)gather;
            // 内弧与节点半径都由外环推出来，保证任何控件尺寸下三者的比例都一致。
            float rIn = rOut * 0.70f;
            float rNode = rOut * 0.46f;

            Color ink = Theme.DarkText;
            Color amber = Theme.AmberHi;
            Color dim = Theme.DarkLine;
            // 故障态整体换成警示色（暖褐红），与常态的黑白 + 琥珀一眼可分。
            if (_status == ServerStatus.Failed || _status == ServerStatus.PortConflict) { ink = Theme.SignalAlert; amber = Theme.SignalAlert; }

            bool busy = _status == ServerStatus.Starting || _status == ServerStatus.Stopping;
            bool live = busy || _status == ServerStatus.Running || _status == ServerStatus.External;

            // 轨道细线（始终存在的"刻度"）
            using (Pen track = new Pen(Color.FromArgb(70, dim), 1f))
            {
                g.DrawEllipse(track, cx - rOut, cy - rOut, rOut * 2, rOut * 2);
                g.DrawEllipse(track, cx - rIn, cy - rIn, rIn * 2, rIn * 2);
            }

            if (_status != ServerStatus.Stopped && _status != ServerStatus.Failed && _status != ServerStatus.PortConflict)
            {
                // 外环：黑/白双环中的亮环（弧长平滑变化，状态切换不跳变）
                using (Pen p = new Pen(ink, Theme.SF(2f)))
                {
                    p.StartCap = LineCap.Flat; p.EndCap = LineCap.Flat;
                    g.DrawArc(p, cx - rOut, cy - rOut, rOut * 2, rOut * 2, (float)_angleOuter, (float)_sweepOuter);
                }
                // 内弧：反向旋转的琥珀弧
                // 弧越长越亮（204 = 360 - 156）：转满整圈时最亮，暗示"链路完整"；
                // 收拢到 156 度时最暗，暗示"还在接"。
                int alphaInner = 150 + (int)(85 * ((_sweepInner - 156.0) / 204.0));
                if (alphaInner < 150) alphaInner = 150; else if (alphaInner > 235) alphaInner = 235;
                using (Pen p = new Pen(Color.FromArgb(alphaInner, amber), Theme.SF(2f)))
                {
                    p.StartCap = LineCap.Flat; p.EndCap = LineCap.Flat;
                    g.DrawArc(p, cx - rIn, cy - rIn, rIn * 2, rIn * 2, (float)_angleInner, (float)_sweepInner);
                }
            }
            else if (_status == ServerStatus.Failed || _status == ServerStatus.PortConflict)
            {
                // 断开的环：表示链路故障
                using (Pen p = new Pen(Color.FromArgb(210, ink), Theme.SF(2f)))
                {
                    g.DrawArc(p, cx - rOut, cy - rOut, rOut * 2, rOut * 2, 128f, 96f);
                    g.DrawArc(p, cx - rOut, cy - rOut, rOut * 2, rOut * 2, 286f, 74f);
                }
            }
            else
            {
                using (Pen p = new Pen(Color.FromArgb(120, dim), Theme.SF(1.6f)))
                    g.DrawArc(p, cx - rOut, cy - rOut, rOut * 2, rOut * 2, 0f, 360f);
            }

            // 六颗琥珀节点沿轨道依次进入
            if (live)
            {
                for (int i = 0; i < NodeCount; i++)
                {
                    double a = (_nodePhase + i * (360.0 / NodeCount)) * Math.PI / 180.0;
                    // 相邻节点错开 0.16 圈相位，看起来就是一颗接一颗沿轨道亮起。
                    double seq = (_nodePhase / 42.0 - i * 0.16);
                    double wave = 0.5 + 0.5 * Math.Cos(seq * Math.PI * 2.0);
                    int alpha = (int)(40 + 190 * wave);
                    float nr = Theme.SF(2.0f) + Theme.SF(1.1f) * (float)wave;
                    float nx = cx + (float)Math.Cos(a) * rNode;
                    float ny = cy + (float)Math.Sin(a) * rNode;
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(Math.Min(255, alpha), amber)))
                        g.FillEllipse(b, nx - nr, ny - nr, nr * 2, nr * 2);
                }
            }

            // 中央圆点：闪变并收缩
            // 闪变周期约 2.6 秒（2 × π × 420ms）。
            float pulse = (float)(0.5 + 0.5 * Math.Sin(now / 420.0));
            float core = Theme.SF(3.4f) + Theme.SF(2.0f) * pulse;
            if (_status == ServerStatus.Running || _status == ServerStatus.External) core = Theme.SF(4.2f);
            if (_status == ServerStatus.Stopped) core = Theme.SF(2.6f);
            Color coreColor = _status == ServerStatus.Stopped ? dim
                            : ((_status == ServerStatus.Failed || _status == ServerStatus.PortConflict) ? ink : amber);
            using (SolidBrush b = new SolidBrush(Color.FromArgb(_status == ServerStatus.Stopped ? 150 : 255, coreColor)))
                g.FillEllipse(b, cx - core, cy - core, core * 2, core * 2);
            using (Pen p = new Pen(Color.FromArgb(90, dim), 1f))
                g.DrawEllipse(p, cx - Theme.SF(9f), cy - Theme.SF(9f), Theme.SF(18f), Theme.SF(18f));

            // 屏幕下方状态词（对应站点的"授权提示"）
            string word;
            switch (_status)
            {
                case ServerStatus.Starting: word = "LINKING"; break;
                case ServerStatus.Stopping: word = "HALTING"; break;
                case ServerStatus.Running: word = "LINKED"; break;
                case ServerStatus.External: word = "EXTERNAL"; break;
                case ServerStatus.Failed: word = "FAULT"; break;
                case ServerStatus.PortConflict: word = "PORT BUSY"; break;
                default: word = "STANDBY"; break;
            }
            Rectangle labelRect = new Rectangle(Theme.S(12), Height - Theme.S(24), Width - Theme.S(24), Theme.S(16));
            Theme.DrawTrackedCenter(g, word, Theme.FontMonoSmall, labelRect,
                _status == ServerStatus.Stopped ? Theme.DarkSub : Theme.DarkText, Theme.SF(1.6f));

            base.OnPaint(e);
        }

        /// <summary>停掉并释放定时器；_clock 是 Stopwatch，没有需要释放的资源。</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing && _timer != null) { _timer.Stop(); _timer.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
