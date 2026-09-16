using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace DshLauncher
{
    internal enum LogLevel { Info, Good, Warn, Bad, Dim }

    internal delegate void LogHandler(string message, LogLevel level);

    /// <summary>把启动器与服务的输出落盘，便于事后排查。</summary>
    internal static class FileLog
    {
        private static readonly object Gate = new object();
        private static string _path;

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
        public static string ExePath
        {
            get
            {
                try { return System.Reflection.Assembly.GetEntryAssembly().Location; }
                catch { return Process.GetCurrentProcess().MainModule.FileName; }
            }
        }

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

        public static string LogsDir { get { return System.IO.Path.Combine(DataDir, "logs"); } }

        public static string BackupDir { get { return System.IO.Path.Combine(DataDir, "backup"); } }
    }
}
