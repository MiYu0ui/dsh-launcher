using System;
using System.IO;
using System.Reflection;

namespace DshLauncher
{
    /// <summary>快捷方式创建/删除（通过 WScript.Shell 后期绑定，无需 COM 互操作声明）。</summary>
    internal static class Shortcuts
    {
        public static string DesktopDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } }
        public static string ProgramsDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.Programs); } }
        public static string StartupDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.Startup); } }

        public static string AutoStartLinkPath
        {
            get { return Path.Combine(StartupDir, "DSH 启动器（后台自启）.lnk"); }
        }

        /// <summary>旧版 PowerShell 自启项（会在开机时闪一个黑窗）。</summary>
        public static string LegacyAutoStartLinkPath
        {
            get { return Path.Combine(StartupDir, "DeepSeekHarness-AutoStart.lnk"); }
        }

        public static bool Create(string linkPath, string target, string arguments, string workingDir, string iconPath, int windowStyle)
        {
            try
            {
                string dir = Path.GetDirectoryName(linkPath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return false;
                object shell = Activator.CreateInstance(shellType);
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { linkPath });
                Type linkType = link.GetType();

                linkType.InvokeMember("TargetPath", BindingFlags.SetProperty, null, link, new object[] { target });
                linkType.InvokeMember("Arguments", BindingFlags.SetProperty, null, link, new object[] { arguments });
                linkType.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, link, new object[] { workingDir });
                linkType.InvokeMember("IconLocation", BindingFlags.SetProperty, null, link, new object[] { iconPath + ",0" });
                linkType.InvokeMember("Description", BindingFlags.SetProperty, null, link, new object[] { "DeepSeek Harness 启动器" });
                linkType.InvokeMember("WindowStyle", BindingFlags.SetProperty, null, link, new object[] { windowStyle });
                linkType.InvokeMember("Save", BindingFlags.InvokeMethod, null, link, null);
                return File.Exists(linkPath);
            }
            catch
            {
                return false;
            }
        }

        public static bool Exists(string linkPath) { return File.Exists(linkPath); }

        public static void Delete(string linkPath)
        {
            try { if (File.Exists(linkPath)) File.Delete(linkPath); }
            catch { }
        }

        /// <summary>把旧的开机自启项（PowerShell 版）改名备份，避免它继续闪黑窗。</summary>
        public static string DisableLegacyAutoStart()
        {
            try
            {
                if (!File.Exists(LegacyAutoStartLinkPath)) return null;
                string backup = LegacyAutoStartLinkPath + ".bak";
                if (File.Exists(backup)) File.Delete(backup);
                File.Move(LegacyAutoStartLinkPath, backup);
                return backup;
            }
            catch { return null; }
        }
    }
}
