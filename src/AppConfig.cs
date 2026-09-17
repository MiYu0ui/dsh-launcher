using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;

namespace DshLauncher
{
    /// <summary>启动器配置，保存为 %APPDATA%\DSH Launcher\config.ini（人类可读、可手改）。</summary>
    internal class AppConfig
    {
        public string Workspace = "";
        public int Port = 3080;
        public string LaunchMode = "npx";          // npx | direct
        public bool VerifyIntegrity = true;
        public string PinnedVersion = "0.1.5-rc.2";
        public string PinnedIntegrity = "sha512-8Xc8hCQHcIWRmTCVU/xZdp6/qMsWMeAd2ObChKDEsfhUPJFXx6H0lgeb1DxUMD86HZrrVN+1bCvn1ppjZ/fOxw==";
        public string Registry = "https://registry.npmmirror.com";
        public bool AutoOpenBrowser = true;
        public bool EdgeAppMode = false;
        public bool CloseToTray = true;
        public bool AutoStart = false;
        public string NodePath = "";
        public string DshEntry = "";
        public bool BootAnimation = true;
        public bool TransitionAnimation = true;

        /// <summary>
        /// DSH 版本策略：latest = 动态取官方最新可安装版（默认，对齐旧安装脚本）；
        /// pinned = 固定 PinnedVersion + PinnedIntegrity（可复现）。
        /// </summary>
        public string VersionMode = "latest";

        /// <summary>
        /// 二级界面的过渡动画档位：full（默认，含盖板解密）/ brief（只留整窗淡入）/ off（瞬时）。
        /// 取值与解析见 UiMotion.ParseLevel —— 关闭档等价于原库的 reduced-motion 硬分支。
        /// </summary>
        public string Motion = "full";

        /// <summary>
        /// 迁移安装位置后要清理的旧目录。由**新实例**在下次启动时删除本体的旧副本
        /// （迁移当下旧 exe 还在运行，删不掉）。
        /// </summary>
        public string CleanupDir = "";

        /// <summary>
        /// 启动器安装位置。空 = 绿色免安装（就地运行）。
        /// 这是**用户的选择**，不是代码里的默认值：首次运行必须由用户点一下才算数。
        /// </summary>
        public string InstallDir = "";

        /// <summary>是否已经问过用户「装到哪里」。false 时下次启动会弹选择框（不会静默采用默认值）。</summary>
        public bool InstallAsked = false;

        /// <summary>绿色免安装：程序就在自己所在目录里跑，不做任何安装动作。</summary>
        public bool IsPortable { get { return string.IsNullOrEmpty(InstallDir); } }

        public static string ConfigDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DSH Launcher"); }
        }

        public static string ConfigPath { get { return Path.Combine(ConfigDir, "config.ini"); } }

        /// <summary>建议的安装位置（只在选择框里作为「使用默认位置」这一项出现，绝不静默使用）。</summary>
        public static string SuggestedInstallDir()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "DSH Launcher");
        }

        public static string DefaultWorkspace()
        {
            string[] candidates = new string[]
            {
                @"D:\项目\004",
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "DeepSeekHarnessDemo"),
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)
            };
            foreach (string c in candidates)
            {
                try { if (!string.IsNullOrEmpty(c) && Directory.Exists(c)) return c; }
                catch { }
            }
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        public static AppConfig Load()
        {
            AppConfig cfg = new AppConfig();
            cfg.Workspace = DefaultWorkspace();
            if (!File.Exists(ConfigPath)) return cfg;

            try
            {
                foreach (string raw in File.ReadAllLines(ConfigPath, Encoding.UTF8))
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";")) continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0) continue;
                    string key = line.Substring(0, eq).Trim().ToLowerInvariant();
                    string val = line.Substring(eq + 1).Trim();
                    switch (key)
                    {
                        case "workspace": if (val.Length > 0) cfg.Workspace = val; break;
                        case "port": cfg.Port = ParseInt(val, cfg.Port); break;
                        case "launchmode": cfg.LaunchMode = val.ToLowerInvariant() == "direct" ? "direct" : "npx"; break;
                        case "verifyintegrity": cfg.VerifyIntegrity = ParseBool(val, cfg.VerifyIntegrity); break;
                        case "pinnedversion": if (val.Length > 0) cfg.PinnedVersion = val; break;
                        case "pinnedintegrity": if (val.Length > 0) cfg.PinnedIntegrity = val; break;
                        case "registry": if (val.Length > 0) cfg.Registry = val; break;
                        case "autoopenbrowser": cfg.AutoOpenBrowser = ParseBool(val, cfg.AutoOpenBrowser); break;
                        case "edgeappmode": cfg.EdgeAppMode = ParseBool(val, cfg.EdgeAppMode); break;
                        case "closetotray": cfg.CloseToTray = ParseBool(val, cfg.CloseToTray); break;
                        case "autostart": cfg.AutoStart = ParseBool(val, cfg.AutoStart); break;
                        case "nodepath": cfg.NodePath = val; break;
                        case "dshentry": cfg.DshEntry = val; break;
                        case "bootanimation": cfg.BootAnimation = ParseBool(val, cfg.BootAnimation); break;
                        case "transitionanimation": cfg.TransitionAnimation = ParseBool(val, cfg.TransitionAnimation); break;
                        case "installdir": cfg.InstallDir = val; break;
                        case "installasked": cfg.InstallAsked = ParseBool(val, cfg.InstallAsked); break;
                        case "versionmode": cfg.VersionMode = val.ToLowerInvariant() == "pinned" ? "pinned" : "latest"; break;
                        case "motion": cfg.Motion = UiMotion.LevelKey(UiMotion.ParseLevel(val)); break;
                        case "cleanupdir": cfg.CleanupDir = val; break;
                    }
                }
            }
            catch { /* 配置损坏时用默认值，不影响启动 */ }
            return cfg;
        }

        /// <summary>
        /// 写盘。**返回是否真的写成功** —— 以前是 void + 裸 catch，于是配置写失败时
        /// 调用方照样打「设置已保存」，用户关掉设置窗、下次启动却读回旧值（设置凭空消失）。
        /// </summary>
        public bool Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# DSH 启动器配置（改动后重启启动器生效）");
                sb.AppendLine("# workspace   DSH 打开的工作目录");
                sb.AppendLine("# launchmode  npx = 经 npm 启动（可校验完整性，推荐）；direct = 直接启动本地已缓存版本（最快）");
                sb.AppendLine("# installdir  启动器安装位置；留空 = 绿色免安装（就地运行）");
                sb.AppendLine("# installasked 是否已经问过用户安装位置（0 = 下次运行会弹选择框）");
                sb.AppendLine("# versionmode latest = 自动解析官方最新可安装版（推荐）；pinned = 固定用 pinnedversion");
                sb.AppendLine("# motion      二级界面过渡动画：full = 完整（含盖板解密，默认）；brief = 只留整窗淡入；off = 关闭");
                sb.AppendLine("# cleanupdir  迁移安装位置后待清理的旧目录（由新实例自动清空，一般不用手改）");
                sb.AppendLine("workspace=" + Workspace);
                sb.AppendLine("port=" + Port.ToString(CultureInfo.InvariantCulture));
                sb.AppendLine("launchmode=" + LaunchMode);
                sb.AppendLine("verifyintegrity=" + (VerifyIntegrity ? "1" : "0"));
                sb.AppendLine("pinnedversion=" + PinnedVersion);
                sb.AppendLine("pinnedintegrity=" + PinnedIntegrity);
                sb.AppendLine("registry=" + Registry);
                sb.AppendLine("autoopenbrowser=" + (AutoOpenBrowser ? "1" : "0"));
                sb.AppendLine("edgeappmode=" + (EdgeAppMode ? "1" : "0"));
                sb.AppendLine("closetotray=" + (CloseToTray ? "1" : "0"));
                sb.AppendLine("autostart=" + (AutoStart ? "1" : "0"));
                sb.AppendLine("nodepath=" + NodePath);
                sb.AppendLine("dshentry=" + DshEntry);
                sb.AppendLine("bootanimation=" + (BootAnimation ? "1" : "0"));
                sb.AppendLine("transitionanimation=" + (TransitionAnimation ? "1" : "0"));
                sb.AppendLine("installdir=" + InstallDir);
                sb.AppendLine("installasked=" + (InstallAsked ? "1" : "0"));
                sb.AppendLine("versionmode=" + VersionMode);
                sb.AppendLine("motion=" + UiMotion.LevelKey(UiMotion.ParseLevel(Motion)));
                sb.AppendLine("cleanupdir=" + CleanupDir);
                // 原子写：先写同目录临时文件，再整体替换 —— 直接 WriteAllText 在磁盘满/被中断时
                // 会留下**截断的 config.ini**，而 Load() 的 catch 会静默退回默认值（用户设置全没）。
                string tmp = ConfigPath + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), new UTF8Encoding(false));
                if (File.Exists(ConfigPath)) File.Replace(tmp, ConfigPath, null);
                else File.Move(tmp, ConfigPath);
                return true;
            }
            catch (Exception ex)
            {
                FileLog.Write("[Warn] 配置写入失败：" + ex.Message + "（路径 " + ConfigPath + "）");
                return false;
            }
        }

        private static int ParseInt(string s, int fallback)
        {
            int v;
            if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out v)) return v;
            return fallback;
        }

        private static bool ParseBool(string s, bool fallback)
        {
            string v = s.Trim().ToLowerInvariant();
            if (v == "1" || v == "true" || v == "yes" || v == "on") return true;
            if (v == "0" || v == "false" || v == "no" || v == "off") return false;
            return fallback;
        }

        public List<string> Validate()
        {
            List<string> problems = new List<string>();
            if (string.IsNullOrEmpty(Workspace) || !Directory.Exists(Workspace))
                problems.Add("工作目录不存在：" + Workspace);
            if (Port < 1 || Port > 65535)
                problems.Add("端口不合法：" + Port);
            return problems;
        }

        /// <summary>
        /// 配置与磁盘的一致性核对（只报告，不拦截启动）。
        /// 覆盖两类真实踩过的坑：安装目录被删/被挪之后快捷方式全失效、自启项变孤儿。
        /// </summary>
        public List<string> ConsistencyProblems()
        {
            List<string> problems = new List<string>();
            try
            {
                if (!string.IsNullOrEmpty(InstallDir) && !Directory.Exists(InstallDir))
                    problems.Add("配置的安装位置不存在：" + InstallDir);

                if (!string.IsNullOrEmpty(InstallDir))
                {
                    string exe = AppPaths.ExePath;
                    string dir = AppPaths.InstallDir;
                    bool same = false;
                    try
                    {
                        same = string.Equals(Path.GetFullPath(dir).TrimEnd('\\'),
                                             Path.GetFullPath(InstallDir).TrimEnd('\\'),
                                             StringComparison.OrdinalIgnoreCase);
                    }
                    catch { }
                    if (!same)
                        problems.Add("当前程序不在配置的安装位置里运行（现在：" + dir + "）");
                }

                foreach (string link in Shortcuts.BrokenLinks())
                    problems.Add("快捷方式目标已失效：" + link);
            }
            catch { }
            return problems;
        }
    }
}
