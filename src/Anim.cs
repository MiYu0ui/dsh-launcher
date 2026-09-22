using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 全局动效时钟：所有会"活起来"的控件共用一条时间轴与一个节拍器，
    /// 这样各处的扫掠、脉冲、彗尾都同相位，不会各动各的。
    /// </summary>
    /// <remarks>
    /// 时钟从进程启动起跑、永不重置 —— 各控件只在自己 <c>OnPaint</c> 里按 <see cref="Now"/> 现算，
    /// 所以"暂停"是没有意义的状态，只需要挂起重绘。
    /// 登记表由 <c>Gate</c> 保护，可跨线程调用；真正的 <c>Invalidate</c> 一律发到 UI 线程
    /// （<c>System.Windows.Forms.Timer</c> 的 Tick 本就跑在创建它的消息循环线程上）。
    /// </remarks>
    internal static class Anim
    {
        /// <summary>一个被登记的控件：多久重绘一次、只重绘哪几块。</summary>
        private class Entry
        {
            public Control Target;
            public int Every;      // 每 N 拍刷新一次（给大控件降频）
            public Rectangle[] Regions;   // 只重绘这些区域（大窗体靠它避免全窗重画）
        }

        private static readonly Stopwatch Clock = Stopwatch.StartNew();
        private static readonly List<Entry> Items = new List<Entry>();
        private static readonly object Gate = new object();
        private static Timer _timer;
        private static int _tick;
        private static volatile bool _suspended;

        /// <summary>自进程启动起的秒数（所有动效的时间基准）。</summary>
        public static double Now { get { return Clock.Elapsed.TotalSeconds; } }

        /// <summary>
        /// 挂起 / 恢复全局刷新。开场·过渡卡片播放期间挂起：
        /// 那些"看不见但 WinForms 认为可见"（Opacity=0）的窗口不该跟卡片抢 UI 线程。
        /// </summary>
        /// <remarks>
        /// 挂起期间 <see cref="Now"/> 照常前进，所以恢复后动画会从时间轴上的"现在"接着走，
        /// 不会把停掉的那一段补播出来。可以随意重复调用，配对的 <see cref="Resume"/> 才解除。
        /// </remarks>
        public static void Suspend() { _suspended = true; }
        /// <summary>恢复刷新；恢复后下一个节拍（最多 33ms 后）才会重绘，不会立刻强制刷一次。</summary>
        public static void Resume() { _suspended = false; }

        /// <summary>按周期取 0→1 的循环进度（内部对周期取余并夹到 [0,1)，负数偏移也能正确回卷）。</summary>
        public static double Cycle(double period, double offset)
        {
            if (period <= 0.001) return 0;
            double p = (Now / period + offset) % 1.0;
            return p < 0 ? p + 1.0 : p;
        }

        /// <summary>登记一个控件，每 everyTicks 个节拍重绘一次（只重绘整个控件）。</summary>
        public static void Track(Control target, int everyTicks)
        {
            Track(target, everyTicks, null);
        }

        /// <summary>登记一个控件，并限定只重绘指定的几块区域。</summary>
        /// <param name="target">要登记 / 更新登记的控件；为 null 直接返回。</param>
        /// <param name="everyTicks">每几个节拍重绘一次（节拍为 33ms）。小于 1 按 1 处理，即约 30fps。</param>
        /// <param name="regions">只重绘这些矩形；为 null 表示整个控件都重绘。</param>
        /// <remarks>
        /// 同一个控件重复登记不会叠加：找到已有条目就改写它的频率与区域，登记顺序不变。
        /// 频率调低（<paramref name="everyTicks"/> 变大）只是少发几次重绘，动画本身仍按真实时间推进，
        /// 所以"呼吸"这类慢动效降到 10fps 也不会变慢。
        /// </remarks>
        public static void Track(Control target, int everyTicks, Rectangle[] regions)
        {
            if (target == null) return;
            if (everyTicks < 1) everyTicks = 1;
            lock (Gate)
            {
                foreach (Entry e in Items)
                {
                    if (ReferenceEquals(e.Target, target))
                    {
                        e.Every = everyTicks;
                        e.Regions = regions;
                        EnsureTimer();
                        return;
                    }
                }
                Entry n = new Entry();
                n.Target = target;
                n.Every = everyTicks;
                n.Regions = regions;
                Items.Add(n);
            }
            EnsureTimer();
        }

        /// <summary>注销控件：从登记表里移掉它的所有条目（重复登记过也一并清掉）。</summary>
        /// <remarks>
        /// 控件 <see cref="Control.Dispose"/> 或不再需要动效时必须调用，否则节拍器会一直白发重绘。
        /// 未登记过的控件调用它没有副作用。这里不会停掉节拍器 —— 表空时每次 Tick 什么都不做。
        /// </remarks>
        public static void Untrack(Control target)
        {
            lock (Gate)
            {
                for (int i = Items.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(Items[i].Target, target)) Items.RemoveAt(i);
            }
        }

        /// <summary>惰性启动节拍器：第一次有人登记时才建，之后一直跑（单实例）。</summary>
        private static void EnsureTimer()
        {
            if (_timer != null) return;
            _timer = new Timer();
            _timer.Interval = 33;      // ≈30fps
            _timer.Tick += OnTick;
            _timer.Start();
        }

        /// <summary>
        /// 一个节拍：把到点、可见且句柄已创建的控件挑出来发重绘。
        /// </summary>
        /// <remarks>
        /// 先在锁内做筛选并复制出快照，再在锁外逐个 <c>Invalidate</c> —— 避免在持锁期间回调到控件代码。
        /// 顺带清理已销毁的条目；不可见或没有句柄的只是这一拍跳过，不会从表里移掉。
        /// 单个控件的重绘失败被吞掉：界面刷新不该把异常抛进消息循环。
        /// </remarks>
        private static void OnTick(object sender, EventArgs e)
        {
            _tick++;
            if (_suspended) return;      // 卡片播放期间不刷新底层窗口
            List<Entry> dirty = new List<Entry>();
            lock (Gate)
            {
                for (int i = Items.Count - 1; i >= 0; i--)
                {
                    Control c = Items[i].Target;
                    if (c == null || c.IsDisposed) { Items.RemoveAt(i); continue; }
                    if (_tick % Items[i].Every != 0) continue;
                    if (!c.Visible || !c.IsHandleCreated) continue;
                    Entry copy = new Entry();
                    copy.Target = Items[i].Target;
                    copy.Every = Items[i].Every;
                    copy.Regions = Items[i].Regions;
                    dirty.Add(copy);
                }
            }
            foreach (Entry en in dirty)
            {
                try
                {
                    if (en.Regions != null && en.Regions.Length > 0)
                    {
                        foreach (Rectangle r in en.Regions) en.Target.Invalidate(r);
                    }
                    else en.Target.Invalidate();
                }
                catch { }
            }
        }
    }
}
