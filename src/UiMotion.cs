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

        public static double EaseIn(double p) { return CubicBezier(EnterX1, EnterY1, EnterX2, EnterY2, p); }
        public static double EaseOut(double p) { return CubicBezier(ExitX1, ExitY1, ExitX2, ExitY2, p); }

        /// <summary>
        /// 三次贝塞尔求值：先解 x(t) = x 得到参数 t，再取 y(t)。
        /// 牛顿迭代为主、二分兜底（曲线可能有一段导数接近 0，纯牛顿会跑飞）。
        /// </summary>
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

        private static double Bez(double a, double b, double t)
        {
            double u = 1.0 - t;
            return 3.0 * u * u * t * a + 3.0 * u * t * t * b + t * t * t;
        }

        private static double BezSlope(double a, double b, double t)
        {
            double u = 1.0 - t;
            return 3.0 * u * u * a + 6.0 * u * t * (b - a) + 3.0 * t * t * (1.0 - b);
        }

        // ---------------- 曲线 ----------------

        public static double Clamp01(double p) { return p < 0.0 ? 0.0 : (p > 1.0 ? 1.0 : p); }

        /// <summary>smoothstep：t²(3−2t)。</summary>
        public static double Smooth(double p) { double t = Clamp01(p); return t * t * (3.0 - 2.0 * t); }

        /// <summary>smootherstep：t³(10 + t(−15 + 6t))。</summary>


        /// <summary>
        /// 把总时钟的某个区间归一化成 0..1。版式里每个元素都有自己的起笔时刻，全靠这个函数。
        /// </summary>
        public static double Window(double elapsedMs, double start, double span)
        {
            if (span <= 0.0) return elapsedMs >= start ? 1.0 : 0.0;
            return Clamp01((elapsedMs - start) / span);
        }

        /// <summary>同上，但过一遍入场缓动（元素位移 / 擦入用它，别用线性的）。</summary>
        public static double WindowEased(double elapsedMs, double start, double span)
        {
            return EaseIn(Window(elapsedMs, start, span));
        }

        // ---------------- 临界阻尼弹簧（帧率无关，原库 motion.ts 的 damp）----------------

        public struct Spring
        {
            public double Value;
            public double Velocity;
        }

        public const double DampReduced = 35.0;

        public static void Damp(ref Spring s, double target, double rate, double dt)
        {
            double delta = s.Value - target;
            double impulse = s.Velocity + rate * delta;
            double decay = Math.Exp(-rate * dt);
            s.Value = target + (delta + impulse * dt) * decay;
            s.Velocity = (s.Velocity - impulse * rate * dt) * decay;
        }

        // ---------------- 错峰 ----------------


        public const int StaggerCap = 600;

        public static int StaggerDelay(int index, int step)
        {
            int d = index * step;
            return d > StaggerCap ? StaggerCap : d;
        }

        public static double Cell(double elapsedMs, int index, int step, int spanMs)
        {
            double local = (elapsedMs - StaggerDelay(index, step)) / (double)Math.Max(1, spanMs);
            return Smooth(local);
        }

        // ---------------- 档位 ----------------

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

        public static string LevelName(MotionLevel lv)
        {
            switch (lv)
            {
                case MotionLevel.Off: return "关闭";
                case MotionLevel.Brief: return "精简";
                default: return "完整";
            }
        }

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

        public static int EnterMs(MotionLevel lv) { return lv == MotionLevel.Full ? EnterFull : EnterBrief; }
        public static int ExitMs(MotionLevel lv) { return lv == MotionLevel.Full ? ExitFull : ExitBrief; }
    }
}
