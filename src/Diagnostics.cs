using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 诊断日志导出：把「环境 + 服务状态 + 配置 + 启动器日志」打包成一个 txt，
    /// 方便排查启动失败、端口占用、下载源核验失败等问题。
    /// </summary>
    internal static class Diagnostics
    {
        public static string Export(AppConfig cfg, DshServer server, UpdateInfo update)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("DSH 启动器 · 诊断日志");
            sb.AppendLine("生成时间      : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            sb.AppendLine("启动器版本    : v" + BuildInfo.Version + "（构建于 " + BuildInfo.BuildStamp + "）");
            sb.AppendLine("程序路径      : " + AppPaths.ExePath);
            sb.AppendLine("程序所在目录  : " + AppPaths.InstallDir);
            sb.AppendLine("安装位置(配置): " + (cfg.IsPortable ? "绿色免安装（就地运行）" : cfg.InstallDir));
            sb.AppendLine("运行数据目录  : " + AppPaths.DataDir);
            sb.AppendLine();

            sb.AppendLine("== 运行环境 ==");
            try
            {
                sb.AppendLine("操作系统      : " + Environment.OSVersion.VersionString +
                              (Environment.Is64BitOperatingSystem ? " / 64 位" : " / 32 位"));
                sb.AppendLine("CLR           : " + Environment.Version);
                sb.AppendLine("界面缩放      : " + Theme.Scale.ToString("0.##") + "x");
                foreach (Screen s in Screen.AllScreens)
                    sb.AppendLine("显示器        : " + s.DeviceName + " " + s.Bounds.Width + "x" + s.Bounds.Height +
                                  (s.Primary ? "（主）" : ""));
            }
            catch { }
            sb.AppendLine();

            sb.AppendLine("== DSH 服务 ==");
            sb.AppendLine("工作目录      : " + cfg.Workspace + (Directory.Exists(cfg.Workspace) ? "" : "   【不存在】"));
            sb.AppendLine("端口          : " + cfg.Port);
            sb.AppendLine("启动方式      : " + (cfg.LaunchMode == "direct" ? "极速模式（本地缓存）" : "npm 方式"));
            sb.AppendLine("固定版本      : " + cfg.PinnedVersion);
            sb.AppendLine("完整性核验    : " + (cfg.VerifyIntegrity ? "开" : "关") + "  首选源：" + cfg.Registry);
            sb.AppendLine("当前状态      : " + server.Status + (server.Owned ? "（本启动器拉起）" : "（外部启动）"));
            if (!string.IsNullOrEmpty(server.LastError)) sb.AppendLine("最近错误      : " + server.LastError);
            try
            {
                string detail;
                ProbeState st = DshServer.Probe(cfg.Port, out detail);
                sb.AppendLine("端口探测      : " + st + " — " + detail);
            }
            catch { }
            try
            {
                string node = DshLocator.FindNode(cfg);
                sb.AppendLine("node          : " + (node == null ? "未找到" : node + "  " + Capture(node, "--version")));
                string npx = DshLocator.FindNpxCmd(cfg);
                sb.AppendLine("npx           : " + (npx == null ? "未找到" : npx));
                string entry = DshLocator.FindCachedEntry(cfg);
                sb.AppendLine("DSH 缓存入口  : " + (entry == null ? "未找到" : entry));
                string home = Environment.GetEnvironmentVariable("DSH_HOME");
                sb.AppendLine("DSH_HOME      : " + (string.IsNullOrEmpty(home) ? "(默认 %USERPROFILE%\\.dsh)" : home));
            }
            catch { }
            sb.AppendLine();

            sb.AppendLine("== 更新检查 ==");
            sb.AppendLine(update == null ? "(本次未检查)" : update.Message);
            sb.AppendLine();

            sb.AppendLine("== 配置 " + AppConfig.ConfigPath + " ==");
            sb.AppendLine(ReadText(AppConfig.ConfigPath));
            sb.AppendLine();

            sb.AppendLine("== 启动器日志 ==");
            sb.AppendLine(CollectLogs());

            string name = "DSH启动器-诊断日志-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt";
            string dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string path = Path.Combine(dir, name);
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(true));
            return path;
        }

        private static string ReadText(string path)
        {
            try
            {
                if (!File.Exists(path)) return "(文件不存在)";
                // 脱敏兜底：磁盘上可能还留着修复之前写下的老日志
                return SecretMask.Apply(File.ReadAllText(path, Encoding.UTF8).TrimEnd());
            }
            catch (Exception ex) { return "(读取失败：" + ex.Message + ")"; }
        }

        private static string Capture(string exe, string args)
        {
            try
            {
                string stdout, stderr;
                int code = HiddenRunner.Run(exe, args, null, null, 6000, out stdout, out stderr);
                string text = (stdout + " " + stderr).Trim();
                return code == 0 && text.Length > 0 ? text : "";
            }
            catch { return ""; }
        }

        private static string CollectLogs()
        {
            StringBuilder sb = new StringBuilder();
            try
            {
                string dir = AppPaths.LogsDir;
                if (!Directory.Exists(dir)) return "(日志目录不存在：" + dir + ")";

                List<string> files = new List<string>(Directory.GetFiles(dir, "launcher-*.log"));
                files.Sort();
                files.Reverse();
                if (files.Count == 0) return "(暂无日志文件)";

                int take = Math.Min(2, files.Count);
                for (int i = 0; i < take; i++)
                {
                    string f = files[i];
                    sb.AppendLine("---- " + Path.GetFileName(f) + " ----");
                    string[] lines = File.ReadAllLines(f, Encoding.UTF8);
                    int limit = i == 0 ? 1200 : 300;          // 当天全量（截断），前一天只留尾部
                    int start = Math.Max(0, lines.Length - limit);
                    if (start > 0) sb.AppendLine("（省略前 " + start + " 行）");
                    for (int k = start; k < lines.Length; k++) sb.AppendLine(lines[k]);
                    sb.AppendLine();
                }
            }
            catch (Exception ex) { sb.AppendLine("(日志收集失败：" + ex.Message + ")"); }
            return sb.ToString();
        }
    }
}
