using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 二级界面的入场 / 退场驱动器。**只管时间**：它把 0..600ms 的时钟翻译成各个版式元素的进度，
    /// 具体怎么画在 <see cref="UiPaint"/> 里，由各窗的 OnPaint 取用。
    ///
    /// 三条从原库学来的规矩：
    ///   ① **退场比入场更短更急**（200 vs 600），且退场换一条加速曲线；
    ///   ② **可打断续算**——中途再次触发不重置回起点；
    ///   ③ **「减少动态效果」是硬分支**：关闭档直接瞬移到终态，不是把动画调快。
    ///
    /// 关于「取消关闭会重置 DialogResult」那个坑：见 <see cref="InterceptClose"/> 的注释 ——
    /// 不处理它，「保存设置」会静默变成「取消」。
    /// </summary>
    internal class WindowReveal
    {
        /// <summary>一个章节锚点：导轨上落一个圆点，右侧排「chip + 中文标题 + 灰色英文」。</summary>
        internal class Section
        {
            public int Y;             // 圆点所在的设计 y（与标题同一行）
            public string Index = ""; // chip 文字，例如 "SECT. 01"
            public string Cn = "";    // 中文标题
            public string En = "";    // 灰色大写英文小字
        }

        /// <summary>宿主窗口（动画就是拨它的 Opacity 与 Location）。</summary>
        private readonly Form _form;
        /// <summary>本窗的动效档位：关闭 / 精简 / 完整，决定时长与是否做版式。</summary>
        private readonly MotionLevel _level;
        /// <summary>
        /// 宿主是不是对话框。**这一条不是分类癖好，是两条时间轴的分水岭**：
        /// 对话框 300ms 入场且不做版式，大窗 600ms 且逐笔就位；混用会踩到构造函数注释里那个关不掉窗的坑。
        /// </summary>
        private readonly bool _dialog;

        private readonly Timer _timer;              // 入场期间 16ms（≈60fps），结束即停 —— 不常驻
        private readonly Stopwatch _clock = new Stopwatch();
        private readonly List<Section> _sections = new List<Section>();

        private Point _baseLocation;
        private bool _baseCaptured;
        /// <summary>退场播完后、真正 Close 之前要执行的动作；本类同样不负责给 _afterExit 赋值（由调用方在需要时注入）。</summary>
        private Action _afterExit;
        private DialogResult _pendingResult = DialogResult.None;

        private bool _running;
        private bool _exiting;
        private bool _allowClose;
        private bool _entered;
        private double _elapsed;
        private double _exitElapsed;

        /// <summary>
        /// 全局动效档位。由 LauncherContext 在启动时与设置保存后刷新 ——
        /// 对话框是静态入口（ConfirmDialog.Show / TypeConfirm.Ask），没有 config 可传，
        /// 所以档位集中放在这里，避免每次弹框都去读一遍 ini。
        /// </summary>
        public static MotionLevel Level = MotionLevel.Full;

        public static void LoadFrom(AppConfig cfg)
        {
            Level = UiMotion.ParseLevel(cfg == null ? "full" : cfg.Motion);
        }

        /// <param name="form">宿主窗口。</param>
        /// <param name="level">动效档位。</param>
        /// <param name="dialog">
        /// true = 对话框（确认框 / 打字确认框）：用原库的 300/200ms，且**不做版式**（它没有章节）。
        /// false = 大窗（设置 / 卸载）：600/200ms + 整套版式逐笔就位。
        /// 这两条时间轴必须分开 —— 实测踩过：对话框套用 600ms 后，「入场期间按 Esc 先跳过动画」的
        /// 保护会把 Esc 吃掉，而驱动它的定时器早已停掉，于是**窗口再也关不掉**（回归用例直接挂死）。
        /// </param>
        public WindowReveal(Form form, MotionLevel level, bool dialog)
        {
            _form = form;
            _level = level;
            _dialog = dialog;

            // ⚠️ 必须在本窗句柄创建之前设置：否则动画里第一次改 Opacity 会触发句柄重建。
            if (_level != MotionLevel.Off)
            {
                try
                {
                    _form.AllowTransparency = true;
                    _form.Opacity = 0.0;      // 先隐形就位，避免首帧闪一下全亮的窗口
                }
                catch { }
            }

            _timer = new Timer();
            _timer.Interval = 16;
            _timer.Tick += delegate(object s, EventArgs e) { Tick(); };
        }

        // ---------------- 章节登记 ----------------

        /// <summary>登记一个章节锚点（在 BuildUi 里调用，位置用设计像素）。</summary>
        public void AddSection(int y, string index, string cn, string en)
        {
            Section s = new Section();
            s.Y = y; s.Index = index; s.Cn = cn; s.En = en;
            _sections.Add(s);
        }

        public List<Section> Sections { get { return _sections; } }

        // 章节标题的起始 x（导轨右侧）。
        // TODO(待确认): 该说明原本属于某个成员（疑似「章节标题 x」常量），但对应成员已不存在；
        // 若保留为 /// 文档注释，编译器会把它顺延挂到下面的 Busy 属性上，使 Busy 的文档
        // 变成一句与它无关的话。故降级为普通注释，并另行为 Busy 补一条准确的文档。


        // ---------------- 对外状态（供 OnPaint 用）----------------

        /// <summary>是否正在播放且尚未退场：入场进行中为 true，退场开始后即变为 false。</summary>
        public bool Busy { get { return _running && !_exiting; } }
        public int ElapsedMs { get { return (int)_elapsed; } }
        public bool WantsLayout { get { return !_dialog && UiMotion.WantsLayout(_level); } }

        /// <summary>本窗的入场时长（对话框 300ms / 大窗 600ms）。</summary>
        public int EnterDuration
        {
            get
            {
                if (!_dialog) return UiMotion.EnterMs(_level);
                return _level == MotionLevel.Brief ? 120 : UiMotion.DialogIn;
            }
        }

        /// <summary>本窗的退场时长（都是 200ms；精简档更短）。</summary>
        public int ExitDuration
        {
            get
            {
                if (_level == MotionLevel.Brief) return UiMotion.ExitBrief;
                return _dialog ? UiMotion.DialogOut : UiMotion.ExitFull;
            }
        }

        /// <summary>整窗淡入进度（已过缓动）。</summary>
        public double Enter { get { return UiMotion.EaseIn(_elapsed / Math.Max(1, EnterDuration)); } }

        /// <summary>
        /// 第 i 支四角括号的进度（左上 → 右上 → 左下 → 右下），错峰由 <see cref="UiMotion.BracketStep"/> 给。
        /// </summary>
        /// <remarks>
        /// 不参与版式或正在退场时直接返回 1 —— 让元素瞬移到终态，绝不留下半截版式。
        /// </remarks>
        public double Bracket(int i)
        {
            if (!WantsLayout || _exiting) return _exiting ? 1.0 : (_level == MotionLevel.Off ? 1.0 : Enter);
            return UiMotion.WindowEased(_elapsed, UiMotion.BracketStart + i * UiMotion.BracketStep, UiMotion.BracketSpan);
        }

        /// <summary>极淡网格的浮现进度。</summary>
        public double GridP
        {
            get
            {
                if (!WantsLayout || _exiting) return _exiting ? 1.0 : Enter;
                return UiMotion.WindowEased(_elapsed, UiMotion.GridStart, UiMotion.GridSpan);
            }
        }

        /// <summary>标题背后那条高亮条的刷入进度。</summary>
        public double BarP
        {
            get
            {
                if (!WantsLayout || _exiting) return _exiting ? 1.0 : Enter;
                return UiMotion.WindowEased(_elapsed, UiMotion.BarStart, UiMotion.BarSpan);
            }
        }

        /// <summary>标题文字本身的擦入进度（比高亮条晚一点，形成"条先到位、字再落上去"）。</summary>
        public double TitleP
        {
            get
            {
                if (!WantsLayout || _exiting) return _exiting ? 1.0 : Enter;
                return UiMotion.WindowEased(_elapsed, UiMotion.TitleStart, UiMotion.TitleSpan);
            }
        }

        /// <summary>窗口标签块（SECT.）的刷入进度。</summary>
        public double ChipP
        {
            get
            {
                if (!WantsLayout || _exiting) return _exiting ? 1.0 : Enter;
                return UiMotion.WindowEased(_elapsed, UiMotion.ChipStart, UiMotion.ChipSpan);
            }
        }

        /// <summary>章节导轨自上而下的画出进度。</summary>
        public double RailP
        {
            get
            {
                if (!WantsLayout || _exiting) return _exiting ? 1.0 : Enter;
                return UiMotion.WindowEased(_elapsed, UiMotion.RailStart, UiMotion.RailSpan);
            }
        }

        /// <summary>第 i 个章节（圆点 + 引线 + 标题）的显形进度。</summary>
        public double SectionP(int i)
        {
            if (!WantsLayout || _exiting) return _exiting ? 1.0 : Enter;
            return UiMotion.WindowEased(_elapsed, UiMotion.RailStart + i * UiMotion.SectionStep, UiMotion.SectionSpan);
        }

        /// <summary>刻度尺这类"多根小元素依次点亮"的进度（步长 / 单项时长由调用方给）。</summary>
        public double Tick(int index, int step, int span)
        {
            if (!WantsLayout || _exiting) return _exiting ? 1.0 : Enter;
            return UiMotion.Cell(_elapsed, index, step, span);
        }

        /// <summary>四角括号的进度数组（给 <see cref="UiPaint.Brackets"/> 用）。</summary>
        public double[] BracketPhases()
        {
            return new double[] { Bracket(0), Bracket(1), Bracket(2), Bracket(3) };
        }

        /// <summary>所有章节的圆点进度数组。</summary>
        public double[] SectionPhases()
        {
            double[] a = new double[_sections.Count];
            for (int i = 0; i < a.Length; i++) a[i] = SectionP(i);
            return a;
        }

        /// <summary>所有章节的圆点 y（设计像素）。</summary>
        public int[] SectionYs()
        {
            int[] a = new int[_sections.Count];
            for (int i = 0; i < a.Length; i++) a[i] = Theme.S(_sections[i].Y);
            return a;
        }

        // ---------------- 入场 ----------------

        /// <summary>
        /// 在 OnShown 里调用：先起后台活儿，再播动画 —— 动画不能挡住真正要跑的初始化。
        /// 关闭档直接 <see cref="SnapToEnd"/> 瞬移，不建任何时间轴。
        /// </summary>
        public void BeginEnter()
        {
            _entered = true;
            if (_level == MotionLevel.Off) { SnapToEnd(); return; }

            _baseLocation = _form.Location;
            _baseCaptured = true;
            ShiftWindow(Theme.S(UiMotion.OffsetIn));

            _elapsed = 0.0;
            _running = true;
            _exiting = false;
            _clock.Reset();
            _clock.Start();
            _timer.Start();
        }

        // ---------------- 退场 ----------------

        /// <summary>
        /// 拦截关闭：先播 200ms 退场再真关。返回 true 表示"本次关闭已被接管"（调用方直接 return）。
        ///
        /// 只接管用户主动发起的关闭；系统关机 / 任务管理器结束 / 应用整体退出这些
        /// <c>CloseReason</c> 一律放行 —— 那些场合系统在等我们，绝不能拖 200ms。
        /// 有意做成**只有第一次关闭会被拦**：退场途中再点一次就立即关掉，绝不把窗口卡住。
        /// </summary>
        public bool InterceptClose(FormClosingEventArgs e)
        {
            if (_allowClose || _exiting) return false;
            if (_level == MotionLevel.Off) return false;
            if (!_entered) return false;
            if (e.CloseReason == CloseReason.WindowsShutDown ||
                e.CloseReason == CloseReason.TaskManagerClosing ||
                e.CloseReason == CloseReason.ApplicationExitCall ||
                e.CloseReason == CloseReason.FormOwnerClosing) return false;

            e.Cancel = true;
            // ⚠️ 关键：模态窗上「取消关闭」会把 DialogResult 重置成 Cancel（WinForms 的行为），
            //    于是"保存设置"那条路会静默变成"取消" —— 实测抓到的。先记下来，真关之前再写回去。
            _pendingResult = _form.DialogResult;

            _running = true;
            _exiting = true;
            _exitElapsed = 0.0;
            _clock.Reset();
            _clock.Start();
            _timer.Start();
            return true;
        }

        // ---------------- 内部推进 ----------------

        private void Tick()
        {
            if (_form == null || _form.IsDisposed) { _timer.Stop(); return; }   // 窗体已销毁：静默停表
            if (_exiting) { TickExit(); return; }
            if (!_running) { _timer.Stop(); return; }

            _elapsed = _clock.Elapsed.TotalMilliseconds;

            double fade = UiMotion.EaseIn(_elapsed / UiMotion.FadeSpan);
            SetOpacity(fade);
            ShiftWindow((int)Math.Round(Theme.S(UiMotion.OffsetIn) * (1.0 - UiMotion.EaseIn(
                _elapsed / Math.Max(1, EnterDuration)))));

            _form.Invalidate();     // 版式每帧重画

            if (_elapsed >= EnterDuration)
            {
                ShiftWindow(0);
                SetOpacity(1.0);
                _running = false;
                _timer.Stop();
                _form.Invalidate();
            }
        }

        private void TickExit()
        {
            _exitElapsed = _clock.Elapsed.TotalMilliseconds;
            double ms = ExitDuration;
            double p = ms <= 0.0 ? 1.0 : UiMotion.Clamp01(_exitElapsed / ms);
            double e = UiMotion.EaseOut(p);      // 退场用另一条曲线：加速离场

            // 位移与透明度都由同一条进度 e 驱动：位置往下沉多少，画面就变透明多少
            ShiftWindow((int)Math.Round(Theme.S(UiMotion.OffsetOut) * e));
            SetOpacity(1.0 - e);

            // 看门狗：任何意外都不能让窗口关不掉
            if (p >= 1.0 || _exitElapsed > ms + 1500.0)
            {
                _timer.Stop();
                SetOpacity(0.0);
                _allowClose = true;
                try { if (_afterExit != null) _afterExit(); } catch { }
                // 还原被"取消关闭"抹掉的那个返回值；对模态窗来说这一步本身就会触发关闭
                try { if (_pendingResult != DialogResult.None) _form.DialogResult = _pendingResult; } catch { }
                try { _form.Close(); } catch { }
            }
        }

        /// <summary>
        /// 把窗口整体位移（设计像素）：入场从 +OffsetIn 升上来，退场再往下沉。
        /// </summary>
        /// <remarks>
        /// 位移是**相对 _baseLocation 的绝对偏移**，不是每帧累加 —— 这样中途打断也能直接跳到位。
        /// 基准位置在第一次调用时抓取。
        /// </remarks>
        private void ShiftWindow(int dy)
        {
            try
            {
                if (!_baseCaptured) { _baseLocation = _form.Location; _baseCaptured = true; }
                _form.Location = new Point(_baseLocation.X, _baseLocation.Y + dy);
            }
            catch { }
        }

        /// <summary>设置整窗不透明度（内部再钳一次 0..1，防调用方给越界值）。</summary>
        private void SetOpacity(double v)
        {
            try { _form.Opacity = UiMotion.Clamp01(v); } catch { }
        }

        /// <summary>跳过入场：直接落到终态（点击 / 按键跳过用）。</summary>
        public void Skip()
        {
            if (!_running || _exiting) return;
            ShiftWindow(0);
            SetOpacity(1.0);
            _running = false;
            _timer.Stop();
            _form.Invalidate();
        }

        /// <summary>瞬移到终态（关闭档 / 异常兜底）。</summary>
        public void SnapToEnd()
        {
            if (_baseCaptured) ShiftWindow(0);
            SetOpacity(1.0);
            _running = false;
            _exiting = false;
            _timer.Stop();
        }

        /// <summary>只负责停表并释放定时器（本类不是 <c>IDisposable</c> 实现，仅按约定命名）。</summary>
        public void Dispose()
        {
            try { _timer.Stop(); _timer.Dispose(); } catch { }
        }
    }
}
