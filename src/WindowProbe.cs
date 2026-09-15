using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace DshLauncher
{
    internal class ConsoleWindowInfo
    {
        public IntPtr Handle;
        public string ClassName = "";
        public string Title = "";

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
        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        private static readonly string[] ConsoleClasses = new string[]
        {
            "ConsoleWindowClass",              // conhost / cmd / powershell
            "CASCADIA_HOSTING_WINDOW_CLASS",   // Windows Terminal
            "PseudoConsoleWindow"
        };

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
