using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DshLauncher
{
    /// <summary>日志级别：只决定界面上的配色，不参与过滤 —— 任何级别的日志都会照样落盘。</summary>
    internal enum LogLevel { Info, Good, Warn, Bad, Dim }

    /// <summary>日志回调：服务进程与部署 / 卸载流程用它在干活的线程上实时报进展。</summary>
    /// <param name="message">要显示的一行文本。</param>
    /// <param name="level">用于配色的级别。</param>
    /// <remarks>
    /// 回调可能来自后台线程，实现方必须自己处理跨线程（界面那边是转投到 UI 线程再加进列表）。
    /// </remarks>
    internal delegate void LogHandler(string message, LogLevel level);

    /// <summary>把启动器与服务的输出落盘，便于事后排查。</summary>
    internal static class FileLog
    {
        /// <summary>串行化写盘；日志可能从后台线程写入，而这个锁也保护着 <c>_path</c> 的惰性初始化。</summary>
        private static readonly object Gate = new object();
        /// <summary>本次运行的日志文件路径；为 null 表示还没算过（见 <see cref="Path"/>）。</summary>
        private static string _path;

        /// <summary>本次运行的日志文件路径：<c>&lt;数据目录&gt;\logs\launcher-yyyyMMdd.log</c>。</summary>
        /// <remarks>
        /// 第一次访问时才算出来，顺带创建目录；目录建不出来就退到系统临时目录，绝不抛异常
        /// （调用方可能在异常处理的路径上取它）。文件名只带日期，所以同一天多次启动是追加到同一份里。
        /// </remarks>
        public static string Path
        {
            get
            {
                if (_path == null)
                {
                    string dir = AppPaths.LogsDir;
                    try { Directory.CreateDirectory(dir); }
                    catch { dir = System.IO.Path.GetTempPath(); }
                    _path = System.IO.Path.Combine(dir, "launcher-" + DateTime.Now.ToString("yyyyMMdd") + ".log");
                }
                return _path;
            }
        }

        /// <summary>追加一行带时间戳的日志；本方法不抛异常。</summary>
        /// <param name="message">消息正文（时间戳由这里加，不用调用方带）。</param>
        /// <remarks>
        /// 写盘前统一脱敏 —— 日志是最常被分享出去的东西，绝不能带 <c>?token=</c>。
        /// 追加失败（磁盘满、文件被占）会被整个吞掉：日志写不进去也不该影响主流程。
        /// 文件固定用不带 BOM 的 UTF-8 追加，方便各种文本工具直接看。
        /// </remarks>
        public static void Write(string message)
        {
            try
            {
                // 统一脱敏：日志是最常被分享出去的东西，绝不能带 ?token=
                message = SecretMask.Apply(message);
                lock (Gate)
                {
                    File.AppendAllText(Path,
                        DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + "  " + message + Environment.NewLine,
                        new UTF8Encoding(false));
                }
            }
            catch { }
        }
    }

    /// <summary>
    /// 本程序自身的路径信息。这里有两个**不同**的目录，别混：
    ///   InstallDir = exe 所在目录（程序本体在哪，绿色版就是解压目录）
    ///   DataDir    = %LOCALAPPDATA%\DSH Launcher（运行期数据：日志、备份、迁移前的旧快捷方式）
    /// 运行期数据刻意不放在 exe 旁边：绿色分发包只打包 exe 目录，就不会把日志和备份一起带出去；
    /// 用户挪动 exe 或改安装位置时，日志也不会跟着丢。
    /// </summary>
    internal static class AppPaths
    {
        /// <summary>启动器 exe 的完整路径（本程序自己的位置）。</summary>
        /// <remarks>
        /// 优先问程序集位置，取不到才退回当前进程的主模块文件名。
        /// 两个分支都包着 try，且都可能失败 —— 调用方拿到的是"尽力而为"的路径。
        /// </remarks>
        public static string ExePath
        {
            get
            {
                try { return System.Reflection.Assembly.GetEntryAssembly().Location; }
                catch { return Process.GetCurrentProcess().MainModule.FileName; }
            }
        }

        /// <summary>程序本体所在目录 = <see cref="ExePath"/> 的目录名；绿色版就是解压出来的那个目录。</summary>
        public static string InstallDir { get { return System.IO.Path.GetDirectoryName(ExePath); } }

        /// <summary>运行期数据根目录：%LOCALAPPDATA%\DSH Launcher</summary>
        public static string DataDir
        {
            get
            {
                return System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DSH Launcher");
            }
        }

        /// <summary>日志目录：<c>&lt;数据目录&gt;\logs</c>。</summary>
        public static string LogsDir { get { return System.IO.Path.Combine(DataDir, "logs"); } }

        /// <summary>备份目录：<c>&lt;数据目录&gt;\backup</c>；覆盖更新前的旧版本放在这里，回退时要找它。</summary>
        public static string BackupDir { get { return System.IO.Path.Combine(DataDir, "backup"); } }
    }
}
