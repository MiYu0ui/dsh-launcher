using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DshLauncher
{
    /// <summary>一个可见控制台窗口的快照：句柄、窗口类名、标题。</summary>
    /// <remarks>
    /// 这是**取样那一刻**的信息。句柄可能被系统复用，所以两次取样之间不能只看句柄是否相等。
    /// </remarks>
    internal class ConsoleWindowInfo
    {
        public IntPtr Handle;
        public string ClassName = "";
        public string Title = "";

        /// <summary>排成一行写进自检报告，便于人工核对"冒出来的是哪个窗"。</summary>
        public override string ToString()
        {
            return "hwnd=0x" + Handle.ToInt64().ToString("X") + " class=" + ClassName + " title=\"" + Title + "\"";
        }
    }

    /// <summary>
    /// 枚举可见的控制台窗口。仅用于自检：验证启动服务的过程中没有任何"黑窗"冒出来。
    /// </summary>
    internal static class WindowProbe
    {
        /// <summary>EnumWindows 的回调签名；返回 true 表示继续枚举下一个窗口。</summary>
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        /// <summary>算作"控制台窗口"的窗口类名，比对时忽略大小写。</summary>
        /// <remarks>
        /// <c>ConsoleWindowClass</c> 是经典 conhost（cmd / powershell）；
        /// <c>CASCADIA_HOSTING_WINDOW_CLASS</c> 是 Windows Terminal；
        /// <c>PseudoConsoleWindow</c> 是伪控制台宿主。<c>GetClassName</c> 声明成 Unicode，
        /// 所以这里写的字面量能直接与之比对。
        /// </remarks>
        private static readonly string[] ConsoleClasses = new string[]
        {
            "ConsoleWindowClass",              // conhost / cmd / powershell
            "CASCADIA_HOSTING_WINDOW_CLASS",   // Windows Terminal
            "PseudoConsoleWindow"
        };

        /// <summary>枚举当前所有可见的控制台窗口。</summary>
        /// <returns>此刻的快照；没有则返回空列表（不会返回 null）。</returns>
        /// <remarks>
        /// 只收可见窗口：隐藏的 conhost 无处不在（服务、后台任务都可能有），把它们算进来就没有判据了。
        /// 回调里整段包了 try，并且恒返回 true —— 枚举期间抛异常会中断整轮枚举，反而漏掉后面的窗口。
        /// </remarks>
        public static List<ConsoleWindowInfo> VisibleConsoleWindows()
        {
            List<ConsoleWindowInfo> found = new List<ConsoleWindowInfo>();
            EnumWindows(delegate(IntPtr hWnd, IntPtr lParam)
            {
                try
                {
                    if (!IsWindowVisible(hWnd)) return true;
                    StringBuilder cls = new StringBuilder(256);
                    GetClassName(hWnd, cls, cls.Capacity);
                    string name = cls.ToString();
                    foreach (string target in ConsoleClasses)
                    {
                        if (string.Equals(name, target, StringComparison.OrdinalIgnoreCase))
                        {
                            StringBuilder title = new StringBuilder(512);
                            GetWindowText(hWnd, title, title.Capacity);
                            ConsoleWindowInfo info = new ConsoleWindowInfo();
                            info.Handle = hWnd;
                            info.ClassName = name;
                            info.Title = title.ToString();
                            found.Add(info);
                            break;
                        }
                    }
                }
                catch { }
                return true;
            }, IntPtr.Zero);
            return found;
        }

        /// <summary>求两次取样之间"新出现的"控制台窗口（按句柄判断）。</summary>
        /// <param name="before">操作前的快照。</param>
        /// <param name="after">操作后的快照。</param>
        /// <returns>只在 <paramref name="after"/> 里出现的窗口；自检据此判定"跑出了黑窗"。</returns>
        /// <remarks>
        /// 按句柄判定，所以只增不减也会被当成新窗口 —— 这正是要的效果：
        /// 服务的日志窗哪怕一闪而过，两次取样之间被看到就算数。
        /// </remarks>
        public static List<ConsoleWindowInfo> NewWindows(List<ConsoleWindowInfo> before, List<ConsoleWindowInfo> after)
        {
            List<ConsoleWindowInfo> added = new List<ConsoleWindowInfo>();
            foreach (ConsoleWindowInfo w in after)
            {
                bool known = false;
                foreach (ConsoleWindowInfo b in before)
                {
                    if (b.Handle == w.Handle) { known = true; break; }
                }
                if (!known) added.Add(w);
            }
            return added;
        }
    }
}
