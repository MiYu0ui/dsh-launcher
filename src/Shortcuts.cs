using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace DshLauncher
{
    /// <summary>快捷方式创建 / 删除 / 有效性校验（通过 WScript.Shell 后期绑定，无需 COM 互操作声明）。</summary>
    /// <remarks>
    /// 归本启动器管的快捷方式只有三处：桌面、开始菜单、启动文件夹（自启），全部由下面的固定文件名标识。
    /// 这里的所有方法都不抛异常：失败以 false / "" / null 的形式返回，由调用方决定后果，
    /// 因为快捷方式建失败不该让部署或搬迁整个崩掉。
    /// </remarks>
    internal static class Shortcuts
    {
        public const string AppLinkName = "DSH 启动器.lnk";                 // 桌面与开始菜单共用这个名字
        public const string AutoStartLinkName = "DSH 启动器（后台自启）.lnk";   // 启动文件夹里的自启项

        /// <summary>旧版（≤1.7）用过的自启项文件名：可能指向 PowerShell 脚本，也可能是老名字的启动器快捷方式。</summary>
        public const string LegacyAutoStartLinkName = "DeepSeekHarness-AutoStart.lnk";

        // 三个落点都用"当前用户"的 SpecialFolder：都在用户配置里，不需要管理员权限。
        // StartupDir 就是「启动」文件夹 —— 开机自启靠它的快捷方式，不写注册表。
        public static string DesktopDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); } }
        public static string ProgramsDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.Programs); } }
        public static string StartupDir { get { return Environment.GetFolderPath(Environment.SpecialFolder.Startup); } }

        // 下面三个路径是"归我们管"的全部范围：BrokenLinks / ReconcileAutoStart 只认这三条，
        // 名字一改，旧文件就再也回收不掉了。
        public static string DesktopLinkPath { get { return Path.Combine(DesktopDir, AppLinkName); } }
        public static string StartMenuLinkPath { get { return Path.Combine(ProgramsDir, AppLinkName); } }
        public static string AutoStartLinkPath { get { return Path.Combine(StartupDir, AutoStartLinkName); } }

        /// <summary>旧版 PowerShell 自启项（会在开机时闪一个黑窗）。</summary>
        public static string LegacyAutoStartLinkPath
        {
            get { return Path.Combine(StartupDir, LegacyAutoStartLinkName); }
        }

        /// <summary>
        /// 写一个 .lnk。windowStyle 传 1（普通窗口）或 7（最小化，自启用）；图标取 iconPath 的第 0 号资源。
        /// 任何一步失败（COM 组件不可用、目录不可写、同名文件被占用）都只返回 false ——
        /// 搬迁流程正是靠这个返回值决定"先别清理旧目录"，否则会留下一堆点不开的图标。
        /// </summary>
        /// <param name="linkPath">.lnk 的完整路径；所在目录不存在会自动创建。</param>
        /// <param name="target">快捷方式指向的 exe。</param>
        /// <param name="arguments">附加命令行参数，不需要就传空串。</param>
        /// <param name="workingDir">起始目录。</param>
        /// <param name="iconPath">取图标的 exe/dll 路径（这里统一取它自己的第 0 号图标）。</param>
        /// <param name="windowStyle">窗口样式：1 = 普通，7 = 最小化（开机自启用）。</param>
        /// <returns>写入后 .lnk 确实存在才算成功。</returns>
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

        /// <summary>文件层面的存在性判断（不关心它指向的东西还在不在，那是 TargetValid 的事）。</summary>
        public static bool Exists(string linkPath) { return File.Exists(linkPath); }

        /// <summary>删掉 .lnk；不存在或删不掉都静默略过（清理动作不该抛异常）。</summary>
        public static void Delete(string linkPath)
        {
            try { if (File.Exists(linkPath)) File.Delete(linkPath); }
            catch { }
        }

        // ---------------- 校验 ----------------

        /// <summary>
        /// 读取 .lnk 的目标路径；读不到返回 ""。
        /// 注意：WScript.Shell 只接受 .lnk / .url 扩展名，对 "xxx.lnk.bak" 会直接抛
        /// 「快捷方式路径名称需以 .lnk 或 .url 结尾」，所以非 .lnk 先复制成临时 .lnk 再读。
        /// </summary>
        /// <remarks>读不到目标一律返回 "" 而不是抛异常，调用方据此当成"无效"处理即可。</remarks>
        public static string ReadTarget(string linkPath)
        {
            if (!File.Exists(linkPath)) return "";
            string probe = linkPath;
            bool temporary = false;
            try
            {
                bool isLnk = linkPath.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)
                          || linkPath.EndsWith(".url", StringComparison.OrdinalIgnoreCase);
                if (!isLnk)
                {
                    probe = Path.Combine(Path.GetTempPath(),
                        "dsh-link-probe-" + Guid.NewGuid().ToString("N") + ".lnk");
                    File.Copy(linkPath, probe, true);
                    temporary = true;
                }
            }
            catch { return ""; }

            try
            {
                Type shellType = Type.GetTypeFromProgID("WScript.Shell");
                if (shellType == null) return "";
                object shell = Activator.CreateInstance(shellType);
                object link = shellType.InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { probe });
                object target = link.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, link, null);
                return target == null ? "" : target.ToString();
            }
            catch { return ""; }
            finally
            {
                if (temporary) { try { File.Delete(probe); } catch { } }
            }
        }

        /// <summary>快捷方式存在、且它的目标文件真的还在。</summary>
        /// <remarks>目标路径读不出来、或读出来但文件已经不在，都算无效。</remarks>
        public static bool TargetValid(string linkPath)
        {
            string t = ReadTarget(linkPath);
            if (t.Length == 0) return false;
            try { return File.Exists(t); } catch { return false; }
        }

        /// <summary>三个快捷方式里「存在但目标已失效」的那些（用来提前发现安装目录被删/被挪）。</summary>
        /// <remarks>
        /// 刻意只报"文件在、目标没了"这一类 —— 快捷方式整个缺失是另一回事（由安装/搬迁流程负责补建）。
        /// </remarks>
        public static List<string> BrokenLinks()
        {
            List<string> broken = new List<string>();
            foreach (string p in new string[] { DesktopLinkPath, StartMenuLinkPath, AutoStartLinkPath })
            {
                try { if (File.Exists(p) && !TargetValid(p)) broken.Add(p); }
                catch { }
            }
            return broken;
        }

        // ---------------- 批量创建 ----------------

        /// <summary>建立（或修复）桌面 + 开始菜单两个启动快捷方式，返回 [桌面, 开始菜单] 是否成功。</summary>
        /// <remarks>窗口样式统一用 1（普通窗口）、参数为空 —— 双击快捷方式就是打开启动器界面。</remarks>
        public static bool[] CreateAppLinks(string exePath, string workDir)
        {
            return new bool[]
            {
                Create(DesktopLinkPath, exePath, "", workDir, exePath, 1),
                Create(StartMenuLinkPath, exePath, "", workDir, exePath, 1)
            };
        }

        /// <summary>建立开机自启快捷方式（--autostart，窗口最小化，静默）。</summary>
        /// <remarks>窗口样式 7 = 启动即最小化，配合 --autostart 做到"开机不闪窗、只进托盘"。</remarks>
        public static bool CreateAutoStartLink(string exePath, string workDir)
        {
            return Create(AutoStartLinkPath, exePath, "--autostart", workDir, exePath, 7);
        }

        /// <summary>
        /// 按配置把「开机自启」对齐到磁盘：开启却没快捷方式就补建，关闭却留着就删掉孤儿。
        /// 返回做了什么；无动作返回 null。
        /// </summary>
        /// <remarks>只处理"开关与磁盘不一致"的两种情形；一致时返回 null，不产生日志噪音。</remarks>
        public static string ReconcileAutoStart(bool enabled, string exePath, string workDir)
        {
            try
            {
                bool exists = File.Exists(AutoStartLinkPath);
                if (enabled && !exists)
                {
                    return CreateAutoStartLink(exePath, workDir)
                        ? "开机自启已开启但缺少快捷方式，已补建"
                        : "开机自启快捷方式补建失败（请检查启动文件夹写入权限）";
                }
                if (!enabled && exists)
                {
                    Delete(AutoStartLinkPath);
                    return "配置里开机自启为关闭，已清理启动文件夹里的孤儿快捷方式";
                }
            }
            catch { }
            return null;
        }

        /// <summary>
        /// 处理旧版自启项。关键：**按目标判断，不按文件名判断**。
        ///   · 指向别的东西（真正的旧 PowerShell 脚本）→ 改名 .bak 停用；
        ///   · 本来就指向我们自己的 exe → 那只是历史上用旧名字留下的启动器快捷方式，直接删掉；
        ///   · 已存在的 .lnk.bak 若指向本启动器 → 名字名不副实，删掉；指向别的 → 保留并如实说明。
        /// 返回一段说明；无动作返回 null。
        /// </summary>
        /// <remarks>
        /// 这个方法是幂等的：第二次调用会走到 .lnk.bak 那个分支，如实说明"保留着备份"，不会重复改名。
        /// </remarks>
        public static string RetireLegacyAutoStart(string exePath)
        {
            try
            {
                string bak = LegacyAutoStartLinkPath + ".bak";

                if (File.Exists(bak))
                {
                    if (SameTarget(bak, exePath))
                    {
                        try
                        {
                            File.Delete(bak);
                            return "已删除名不副实的旧自启备份 " + Path.GetFileName(bak) + "（它其实指向本启动器，不是旧方案）";
                        }
                        catch { }
                        return null;
                    }
                    string bt = ReadTarget(bak);
                    return "启动文件夹里保留着一个旧自启备份 " + Path.GetFileName(bak) +
                           (bt.Length > 0 ? "（指向 " + bt + "）" : "");
                }

                if (!File.Exists(LegacyAutoStartLinkPath)) return null;

                if (SameTarget(LegacyAutoStartLinkPath, exePath))
                {
                    Delete(LegacyAutoStartLinkPath);
                    return "已删除旧名字的启动器自启项 " + LegacyAutoStartLinkName + "（它指向本启动器，不是旧 PowerShell 方案）";
                }

                File.Move(LegacyAutoStartLinkPath, bak);
                return "已停用旧的 PowerShell 开机自启项（备份：" + Path.GetFileName(bak) + "）";
            }
            catch { return null; }
        }

        /// <summary>比较快捷方式目标与给定 exe 是否是同一个文件（取完整路径、忽略大小写）。</summary>
        private static bool SameTarget(string linkPath, string exePath)
        {
            try
            {
                string t = ReadTarget(linkPath);
                if (t.Length == 0) return false;
                return string.Equals(Path.GetFullPath(t), Path.GetFullPath(exePath), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
