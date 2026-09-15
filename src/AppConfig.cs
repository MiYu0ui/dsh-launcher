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

        public static string ConfigDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DSH Launcher"); }
        }

        public static string ConfigPath { get { return Path.Combine(ConfigDir, "config.ini"); } }

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
                    }
                }
            }
            catch { /* 配置损坏时用默认值，不影响启动 */ }
            return cfg;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(ConfigDir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("# DSH 启动器配置（改动后重启启动器生效）");
                sb.AppendLine("# workspace   DSH 打开的工作目录");
                sb.AppendLine("# launchmode  npx = 经 npm 启动（可校验完整性，推荐）；direct = 直接启动本地已缓存版本（最快）");
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
                File.WriteAllText(ConfigPath, sb.ToString(), new UTF8Encoding(false));
            }
            catch { }
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
    }
}
