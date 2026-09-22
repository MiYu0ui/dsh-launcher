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
        /// <summary>
        /// 收集信息并写出诊断 txt，返回该文件的完整路径。
        /// </summary>
        /// <param name="cfg">当前配置；只读取，不修改。</param>
        /// <param name="server">当前服务实例；<c>Status</c> / <c>Owned</c> / <c>LastError</c> 会被写入报告。</param>
        /// <param name="update">本次更新检查的结果；null 表示还没检查过，报告里写"(本次未检查)"。</param>
        /// <returns>写好的诊断日志文件路径（在桌面上）。</returns>
        /// <remarks>
        /// 内容分六段：基本信息、运行环境、DSH 服务、更新检查、配置文件原文、启动器日志。
        /// 服务与环境那几段各自包了 try：探不出来的项宁可整段缺失，也不让导出整体失败。
        /// 配置原文与日志都会过一遍 <see cref="SecretMask"/>，token 不会出现在导出文件里。
        /// 写盘用带 BOM 的 UTF-8，方便直接双击用记事本看清中文。
        /// </remarks>
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

        /// <summary>读一个文本文件并脱敏，供报告原文引用；任何失败都变成一句可读的说明而不是异常。</summary>
        /// <returns>文件内容（去掉尾部空白并已脱敏）、"(文件不存在)" 或 "(读取失败：…)"。</returns>
        /// <remarks>
        /// 这里仍要脱敏的原因：磁盘上可能还留着"修复之前"写下的老配置 / 老日志，不能只指望写入侧已经洗过。
        /// </remarks>
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

        /// <summary>静默执行一条命令并把 stdout / stderr 合成一行，用于在报告里带上版本号这类信息。</summary>
        /// <returns>命令成功（退出码 0）且有输出时的输出文本；否则返回空串 —— 失败信息不值得占报告篇幅。</returns>
        /// <remarks>超时 6 秒、不显示窗口（走 <see cref="HiddenRunner"/>），所以诊断导出期间不会闪黑窗。</remarks>
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

        /// <summary>收集启动器日志：取最近的两个日志文件拼进报告。</summary>
        /// <returns>拼好的日志文本（每份带文件名分隔行）；目录或文件有问题时返回一句说明。</returns>
        /// <remarks>
        /// 截断策略按新旧不同：最新那份留尾部 1200 行、上一份只留 300 行，被砍掉的行数会写在报告里。
        /// 日志按文件名排序后倒序，而文件名带日期，所以"第一个"就是最新那份。
        /// 整段包在 try 里：日志收集失败不该让导出失败，失败原因只写进报告正文。
        /// </remarks>
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
