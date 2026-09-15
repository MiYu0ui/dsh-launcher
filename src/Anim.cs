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
    internal static class Anim
    {
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
        public static void Suspend() { _suspended = true; }
        public static void Resume() { _suspended = false; }

        /// <summary>按周期取 0→1 的循环进度。</summary>
        public static double Cycle(double period, double offset)
        {
            if (period <= 0.001) return 0;
            double p = (Now / period + offset) % 1.0;
            return p < 0 ? p + 1.0 : p;
        }

        public static void Track(Control target, int everyTicks)
        {
            Track(target, everyTicks, null);
        }

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

        public static void Untrack(Control target)
        {
            lock (Gate)
            {
                for (int i = Items.Count - 1; i >= 0; i--)
                    if (ReferenceEquals(Items[i].Target, target)) Items.RemoveAt(i);
            }
        }

        private static void EnsureTimer()
        {
            if (_timer != null) return;
            _timer = new Timer();
            _timer.Interval = 33;      // ≈30fps
            _timer.Tick += OnTick;
            _timer.Start();
        }

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
