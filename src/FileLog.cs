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
                    string dir = System.IO.Path.Combine(AppPaths.InstallDir, "logs");
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

    /// <summary>本程序自身的路径信息。</summary>
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
    }
}
