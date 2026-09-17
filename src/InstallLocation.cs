using System;
using System.Diagnostics;
using System.IO;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 「启动器装到哪里」由**用户**决定，不静默采用默认值。
    /// 三种结果：默认位置 / 自定义位置 / 绿色免安装（就地运行）。
    /// 首次运行弹一次选择框；之后可在「设置 → 安装位置」里改。
    /// </summary>
    internal static class InstallLocation
    {
        /// <summary>
        /// 弹一次安装位置选择框。
        /// 返回 Choice.Cancel = 用户关掉了框（还没决定，下次启动再问）。
        /// </summary>
        public static ConfirmDialog.Choice Ask(IWin32Window owner)
        {
            string suggested = AppConfig.SuggestedInstallDir();
            return ConfirmDialog.Show(owner,
                "INSTALL / 选择安装位置",
                "启动器要装到哪里？",
                "第一次运行需要你决定程序放在哪里。这个选择之后可以在「设置 → 安装位置」里随时更改；" +
                "选「绿色免安装」则程序就在当前目录运行，不往系统里装任何东西。",
                new string[]
                {
                    "当前位置=" + AppPaths.InstallDir,
                    "默认位置=" + suggested,
                    "运行数据=" + AppPaths.DataDir + "（日志、备份）"
                },
                null,
                "使用默认位置",   // 最右 → Choice.Confirm
                "绿色免安装",     // 最左 → Choice.Alt
                "自定义…",        // 中间 → Choice.Alt2
                false);
        }

        /// <summary>挑一个自定义安装目录；取消返回 null。</summary>
        public static string PickFolder(IWin32Window owner, string start)
        {
            using (FolderBrowserDialog dlg = new FolderBrowserDialog())
            {
                dlg.Description = "选择启动器的安装位置";
                dlg.ShowNewFolderButton = true;
                try { if (!string.IsNullOrEmpty(start) && Directory.Exists(start)) dlg.SelectedPath = start; } catch { }
                return dlg.ShowDialog(owner) == DialogResult.OK ? dlg.SelectedPath : null;
            }
        }

        /// <summary>绿色免安装：记住"不要再问了"，程序继续就地运行。</summary>
        public static void MarkPortable(AppConfig cfg)
        {
            cfg.InstallDir = "";
            cfg.InstallAsked = true;
            cfg.Save();
        }

        /// <summary>
        /// 把程序安置到 targetDir 并重建快捷方式。
        /// 返回 null 表示成功；否则返回错误说明。
        /// restarting = true 时调用方必须立刻 Shutdown(false)（服务保持运行），新实例已在等单实例锁。
        /// </summary>
        public static string Apply(AppConfig cfg, string targetDir, out bool restarting)
        {
            restarting = false;          // 必须在 try 之外：catch 分支也要求 out 参数已赋值
            string previous = cfg.InstallDir;
            try
            {
                targetDir = (targetDir ?? "").Trim().Trim('"');
                if (targetDir.Length == 0) return "安装位置为空。";
                targetDir = Path.GetFullPath(targetDir);

                string exeName = Path.GetFileName(AppPaths.ExePath);
                bool sameDir = SameDir(AppPaths.InstallDir, targetDir);
                string target = sameDir ? AppPaths.ExePath : Path.Combine(targetDir, exeName);

                if (!sameDir)
                {
                    Directory.CreateDirectory(targetDir);
                    if (File.Exists(target)) { try { File.Delete(target); } catch { } }
                    File.Copy(AppPaths.ExePath, target, true);
                }

                cfg.InstallDir = targetDir;
                cfg.InstallAsked = true;
                // 旧位置交给新实例去清：此刻旧 exe 还在运行，删不掉
                if (!sameDir && !string.IsNullOrEmpty(previous) &&
                    !SameDir(previous, targetDir) && Directory.Exists(previous))
                    cfg.CleanupDir = previous;
                cfg.Save();

                // 快捷方式一律指向最终位置
                bool[] app = Shortcuts.CreateAppLinks(target, targetDir);
                if (cfg.AutoStart) Shortcuts.CreateAutoStartLink(target, targetDir);

                // ⚠️ 快捷方式没全部重建成功，就**别清旧目录**：旧 exe 一被删，桌面/开始菜单上那些
                //    没重建成功的 .lnk 立刻变成死链，用户只会看到一个点不开的图标。旧目录留着不影响
                //    使用（设置里还有「创建 / 修复快捷方式」可以补）。
                if (!app[0] || !app[1])
                {
                    cfg.CleanupDir = "";
                    cfg.Save();
                    FileLog.Write("[Warn] 快捷方式未能全部重建（桌面=" + (app[0] ? "OK" : "失败") +
                                  " 开始菜单=" + (app[1] ? "OK" : "失败") + "），已取消清理旧安装位置以免留下死链。");
                }

                FileLog.Write("[Info] 安装位置设为 " + targetDir +
                              (sameDir ? "（程序本就在此，未复制）" : "（程序本体已复制到该目录）") +
                              "；快捷方式 桌面=" + (app[0] ? "OK" : "失败") + " 开始菜单=" + (app[1] ? "OK" : "失败") +
                              (cfg.CleanupDir.Length > 0 ? "；旧位置待清理=" + cfg.CleanupDir : ""));

                if (!sameDir)
                {
                    ProcessStartInfo psi = new ProcessStartInfo(target, "--after-install");
                    psi.UseShellExecute = false;
                    psi.WorkingDirectory = targetDir;
                    Process.Start(psi);
                    restarting = true;
                }
                return null;
            }
            catch (Exception ex)
            {
                return Explain(ex, targetDir);
            }
        }

        /// <summary>把 Win32 的原始报错翻译成用户能照着做的说明。</summary>
        private static string Explain(Exception ex, string targetDir)
        {
            string raw = ex.Message;
            if (ex is UnauthorizedAccessException ||
                raw.IndexOf("拒绝访问", StringComparison.Ordinal) >= 0 ||
                raw.IndexOf("Access is denied", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "没有写入该目录的权限。" + raw + "\r\n" +
                       "该目录可能受系统保护（如 Program Files）或所在盘只读；换一个普通目录再试，" +
                       "例如 D:\\Apps\\DSH Launcher。";
            }
            if (raw.IndexOf("另一个程序正在使用", StringComparison.Ordinal) >= 0 ||
                raw.IndexOf("being used by another process", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return "目标文件正被占用（那个位置可能已有一份正在运行的启动器）。" +
                       "请先退出那一份，或换一个目录。";
            }
            return raw;
        }

        /// <summary>
        /// 清理上一次迁移留下的旧安装位置 —— 由**新实例**启动时调用（那时旧 exe 已退出）。
        /// 只删我们自己复制过去的那个同名 exe；目录里还有别的东西就不删目录。
        /// </summary>
        public static string CleanupPreviousInstall(AppConfig cfg)
        {
            try
            {
                string dir = cfg.CleanupDir;
                if (string.IsNullOrEmpty(dir)) return null;

                // ⚠️ 以前这里先 `cfg.CleanupDir = ""; cfg.Save();` 再删 —— 删除失败（旧 exe 还在跑）
                //    待办就永久丢了，旧副本永远残留、再也不会重试。现在**删成功之后才清标记**。
                bool done = false;
                try
                {
                    if (!Directory.Exists(dir)) { done = true; return null; }
                    if (SameDir(dir, AppPaths.InstallDir)) { done = true; return null; }   // 已经就地，别动自己

                    string exe = Path.Combine(dir, Path.GetFileName(AppPaths.ExePath));
                    if (File.Exists(exe))
                    {
                        try { File.Delete(exe); }
                        catch { return "旧安装位置的 exe 暂时删不掉（可能仍在运行）：" + exe + "　下次启动会再试。"; }
                    }
                    try
                    {
                        if (Directory.GetFileSystemEntries(dir).Length == 0) Directory.Delete(dir, false);
                        else { done = true; return "已移除旧位置的程序本体，但该目录还有其它文件，保留：" + dir; }
                    }
                    catch { }
                    done = true;
                    return "已清理旧安装位置：" + dir;
                }
                finally
                {
                    // 只有"确实没有待办"时才清标记；失败路径保留，等下次启动重试
                    if (done) { cfg.CleanupDir = ""; cfg.Save(); }
                }
            }
            catch { return null; }
        }

        private static bool SameDir(string a, string b)
        {
            try
            {
                return string.Equals(Path.GetFullPath(a).TrimEnd('\\'),
                                     Path.GetFullPath(b).TrimEnd('\\'),
                                     StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }
    }
}
