using System;

namespace DshLauncher
{
    /// <summary>二级界面过渡动画的档位。</summary>
    internal enum MotionLevel
    {
        Off = 0,     // 关闭：瞬时到位
        Brief = 1,   // 精简：只保留整窗淡入淡出，跳过自绘版式
        Full = 2     // 完整：淡入 + 版式逐笔就位（默认）
    }

    /// <summary>
    /// 界面动效层：只管**时间**（时长、曲线、错峰、阻尼），不含绘制代码，也不含业务判断。
    ///
    /// 设计来源两份：
    ///   ① LBEILC/RhineLabUI 的 <c>src/ui-transitions.ts</c> / <c>motion.ts</c> —— 两条全局缓动与临界阻尼弹簧；
    ///   ② 《莱茵生命 PPT 模板》—— 版式语言，以及它全程使用的**「平滑」(Morph) 转场**思路：
    ///      元素**位移就位**，而不是盖住内容再揭开。二级界面入场按这个思路重做过一版。
    ///
    /// 绘制在 <see cref="UiPaint"/>，时序驱动在 <see cref="WindowReveal"/>。
    /// </summary>
    internal static class UiMotion
    {
        // ---------------- 时长（毫秒）----------------

        /// <summary>大窗（设置 / 卸载）入场总时长。600ms 是定下的量级：看得清，又不拖。</summary>
        public const int EnterFull = 600;
        /// <summary>精简档：只留整窗淡入。</summary>
        public const int EnterBrief = 150;

        /// <summary>对话框入场 / 退场（原样取自 RhineLabUI 的 ui-transitions.ts）。</summary>
        public const int DialogIn = 300;
        public const int DialogOut = 200;

        /// <summary>退场：一律比入场更短更急（原库的规矩），且换加速曲线。</summary>
        public const int ExitFull = 200;
        public const int ExitBrief = 120;

        // ---------------- 位移（设计像素）----------------

        /// <summary>大窗入场的微升。早先版本是 12px，现在压到 6px —— 大位移会让人以为"窗口在飞"。</summary>
        public const int OffsetIn = 6;
        /// <summary>对话框仍按原库的 12px：它体量小，位移看得清。</summary>
        public const int DialogOffsetIn = 12;
        /// <summary>退场下沉。</summary>
        public const int OffsetOut = 8;

        // ---------------- 入场时间轴（相对入场起点，毫秒）----------------
        //
        // 0 ──── 100 ──── 200 ──── 300 ──── 400 ──── 500 ──── 600
        // ├ 淡入 + 微升：0–100
        // ├ 四角括号：0 / 30 / 60 / 90 起笔，每支 70ms
        // ├ 极淡网格：60–260
        // ├ 标题高亮条：80–280，标题擦入 220–380
        // ├ 窗口 chip（SECT.）：140–320
        // └ 章节导轨：160 起画，每落一节 +60ms，单节 180ms

        public const int FadeSpan = 100;
        public const int BracketStart = 0, BracketStep = 30, BracketSpan = 70;
        public const int GridStart = 60, GridSpan = 200;
        public const int BarStart = 60, BarSpan = 160;
        public const int TitleStart = 140, TitleSpan = 160;
        public const int ChipStart = 120, ChipSpan = 170;
        public const int RailStart = 180, RailSpan = 380;
        public const int SectionStep = 60, SectionSpan = 180;

        // ---------------- 缓动 ----------------
        //
        // enter = cubic-bezier(0.22, 1, 0.36, 1)   强减速（入场）
        // exit  = cubic-bezier(0.4, 0, 1, 1)       加速（退场）

        private const double EnterX1 = 0.22, EnterY1 = 1.0, EnterX2 = 0.36, EnterY2 = 1.0;
        private const double ExitX1 = 0.40, ExitY1 = 0.0, ExitX2 = 1.0, ExitY2 = 1.0;

        /// <summary>入场缓动：强减速（曲线见上方注释）。<paramref name="p"/> 是 0..1 的线性时间进度。</summary>
        public static double EaseIn(double p) { return CubicBezier(EnterX1, EnterY1, EnterX2, EnterY2, p); }
        /// <summary>退场缓动：加速离场，比入场更急。</summary>
        public static double EaseOut(double p) { return CubicBezier(ExitX1, ExitY1, ExitX2, ExitY2, p); }

        /// <summary>
        /// 三次贝塞尔求值：先解 x(t) = x 得到参数 t，再取 y(t)。
        /// 牛顿迭代为主、二分兜底（曲线可能有一段导数接近 0，纯牛顿会跑飞）。
        /// </summary>
        /// <param name="x1">第一个控制点的 x（须在 0..1 内，否则曲线在 x 上不单调）。</param>
        /// <param name="y1">第一个控制点的 y，可以超出 0..1（回弹 / 过冲效果就靠这个）。</param>
        /// <param name="x2">第二个控制点的 x。</param>
        /// <param name="y2">第二个控制点的 y。</param>
        /// <param name="x">横轴上的取样点。</param>
        /// <returns>该横坐标处的曲线值 y(t)。</returns>
        /// <remarks>
        /// 只接受 0..1 的 <paramref name="x"/>：越界直接返回 0.0 / 1.0，函数内部不做夹紧。
        /// 调用方通常传 <c>elapsed/span</c>，所以这个边界就是"动画尚未开始 / 已经结束"。
        /// 牛顿迭代最多 8 次；残差仍大于 1e-4 时改用 24 次二分，保证任何形状的曲线都能解出来。
        /// </remarks>
        public static double CubicBezier(double x1, double y1, double x2, double y2, double x)
        {
            if (x <= 0.0) return 0.0;
            if (x >= 1.0) return 1.0;

            double t = x;
            for (int i = 0; i < 8; i++)
            {
                double err = Bez(x1, x2, t) - x;
                if (Math.Abs(err) < 1e-6) return Bez(y1, y2, t);
                double d = BezSlope(x1, x2, t);
                if (Math.Abs(d) < 1e-9) break;
                t -= err / d;
                if (t < 0.0) t = 0.0;
                if (t > 1.0) t = 1.0;
            }
            if (Math.Abs(Bez(x1, x2, t) - x) < 1e-4) return Bez(y1, y2, t);

            double lo = 0.0, hi = 1.0;
            for (int i = 0; i < 24; i++)
            {
                t = (lo + hi) * 0.5;
                if (Bez(x1, x2, t) < x) lo = t; else hi = t;
            }
            return Bez(y1, y2, (lo + hi) * 0.5);
        }

        /// <summary>三次贝塞尔在单轴上的取值：固定端点 0 与 1，<paramref name="a"/> / <paramref name="b"/> 是两个控制点。</summary>
        private static double Bez(double a, double b, double t)
        {
            double u = 1.0 - t;
            return 3.0 * u * u * t * a + 3.0 * u * t * t * b + t * t * t;
        }

        /// <summary><see cref="Bez"/> 对 <paramref name="t"/> 的导数，供牛顿迭代用。</summary>
        private static double BezSlope(double a, double b, double t)
        {
            double u = 1.0 - t;
            return 3.0 * u * u * a + 6.0 * u * t * (b - a) + 3.0 * t * t * (1.0 - b);
        }

        // ---------------- 曲线 ----------------

        /// <summary>把进度夹到 0..1；NaN 会原样穿过（比较恒为 false），调用方不必指望这里兜住 NaN。</summary>
        public static double Clamp01(double p) { return p < 0.0 ? 0.0 : (p > 1.0 ? 1.0 : p); }

        /// <summary>smoothstep：t²(3−2t)。两端导数为 0，用来做"起步和收尾都不生硬"的显形。</summary>
        public static double Smooth(double p) { double t = Clamp01(p); return t * t * (3.0 - 2.0 * t); }

        // smootherstep：t³(10 + t(−15 + 6t))。
        // TODO(待确认): 这原本是对某个 smootherstep 实现的文档注释，但该实现已不存在；
        // 因后面紧接另一段 /// 文档块，编译器判定本段无主并报 CS1587，故降级为普通注释。
        // 若确认该实现是有意删除，可直接删掉这四行。


        /// <summary>
        /// 把总时钟的某个区间归一化成 0..1。版式里每个元素都有自己的起笔时刻，全靠这个函数。
        /// </summary>
        /// <param name="elapsedMs">本窗入场开始至今的毫秒数。</param>
        /// <param name="start">该元素的起笔时刻（毫秒，相对入场起点）。</param>
        /// <param name="span">该元素从起笔到就位的时长（毫秒）。</param>
        /// <returns>0..1；<paramref name="span"/> 不为正时退化成阶跃：到点即 1，否则 0。</returns>
        public static double Window(double elapsedMs, double start, double span)
        {
            if (span <= 0.0) return elapsedMs >= start ? 1.0 : 0.0;
            return Clamp01((elapsedMs - start) / span);
        }

        /// <summary>同上，但过一遍入场缓动（元素位移 / 擦入用它，别用线性的）。</summary>
        /// <returns>缓动后的 0..1，返回值与线性的 <see cref="Window"/> 一一对应。</returns>
        public static double WindowEased(double elapsedMs, double start, double span)
        {
            return EaseIn(Window(elapsedMs, start, span));
        }

        // ---------------- 临界阻尼弹簧（帧率无关，原库 motion.ts 的 damp）----------------

        // ---------------- 错峰 ----------------


        /// <summary>错峰延迟的上限（毫秒）：元素再多也不让排在后面的等到 600ms 以上。</summary>
        public const int StaggerCap = 600;

        /// <summary>第 <paramref name="index"/> 个元素该晚多少毫秒起笔。</summary>
        /// <returns><c>index * step</c>，但不超过 <see cref="StaggerCap"/>。</returns>
        public static int StaggerDelay(int index, int step)
        {
            int d = index * step;
            return d > StaggerCap ? StaggerCap : d;
        }

        /// <summary>错峰网格里第 <paramref name="index"/> 格的显形进度（已经过 <see cref="Smooth"/>）。</summary>
        /// <param name="elapsedMs">本窗入场开始至今的毫秒数。</param>
        /// <param name="index">元素序号，决定它排在第几个起笔。</param>
        /// <param name="step">相邻元素之间的起笔间隔（毫秒）。</param>
        /// <param name="spanMs">单个元素自己的显形时长（毫秒）。</param>
        /// <returns>0..1；起笔前（local 为负）由 <see cref="Smooth"/> 夹紧成 0，不会回卷。</returns>
        public static double Cell(double elapsedMs, int index, int step, int spanMs)
        {
            double local = (elapsedMs - StaggerDelay(index, step)) / (double)Math.Max(1, spanMs);
            return Smooth(local);
        }

        // ---------------- 档位 ----------------

        /// <summary>把配置里的档位字符串解析成枚举。</summary>
        /// <returns>
        /// 空、null 或认不出来的值一律返回 <see cref="MotionLevel.Full"/>（认得出的只有 "off" / "brief" / "full"，
        /// 比对前会 Trim 并转小写），所以档位解析不会失败、也不会让界面没有动效。
        /// </returns>
        public static MotionLevel ParseLevel(string s)
        {
            if (string.IsNullOrEmpty(s)) return MotionLevel.Full;
            switch (s.Trim().ToLowerInvariant())
            {
                case "off": return MotionLevel.Off;
                case "brief": return MotionLevel.Brief;
                case "full": return MotionLevel.Full;
                default: return MotionLevel.Full;
            }
        }

        /// <summary><see cref="ParseLevel"/> 的逆运算：写回配置文件用的字符串键。</summary>
        /// <returns>"off" / "brief" / "full"（除 Off、Brief 之外都归到 "full"）。</returns>
        public static string LevelKey(MotionLevel lv)
        {
            switch (lv)
            {
                case MotionLevel.Off: return "off";
                case MotionLevel.Brief: return "brief";
                default: return "full";
            }
        }

        /// <summary>该档位是否要画版式（四角括号 / 章节导轨 / 标签块）。只有完整档。</summary>
        public static bool WantsLayout(MotionLevel lv) { return lv == MotionLevel.Full; }

        /// <summary>该档位下大窗的入场时长（毫秒）。</summary>
        public static int EnterMs(MotionLevel lv) { return lv == MotionLevel.Full ? EnterFull : EnterBrief; }
    }
}
