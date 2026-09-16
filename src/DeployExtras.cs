using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Threading;

namespace DshLauncher
{
    /// <summary>
    /// 部署第 1 步：系统兼容性门。对齐旧安装脚本的 Test-WindowsCompatibility
    /// （只支持 Windows 10/11 的 64 位系统；build &lt; 22000 给一条提示）。
    /// </summary>
    internal static class SysCheck
    {
        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        private struct OSVERSIONINFO
        {
            public int dwOSVersionInfoSize;
            public int dwMajorVersion;
            public int dwMinorVersion;
            public int dwBuildNumber;
            public int dwPlatformId;
            [System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szCSDVersion;
        }

        [System.Runtime.InteropServices.DllImport("ntdll.dll")]
        private static extern int RtlGetVersion(ref OSVERSIONINFO info);

        /// <summary>
        /// 取真实的系统版本号。
        /// **不能用 Environment.OSVersion**：进程清单里没有 supportedOS 声明时，Windows 会对
        /// 新系统谎报 6.2（实测本机 Win11 被报成 "Microsoft Windows NT 6.2.9200.0"）。
        /// RtlGetVersion 不看清单，读注册表作兜底。
        /// </summary>
        private static bool RealVersion(out int major, out int minor, out int build, out bool reliable)
        {
            major = minor = build = 0;
            reliable = false;
            try
            {
                OSVERSIONINFO v = new OSVERSIONINFO();
                v.dwOSVersionInfoSize = System.Runtime.InteropServices.Marshal.SizeOf(typeof(OSVERSIONINFO));
                if (RtlGetVersion(ref v) == 0 && v.dwMajorVersion > 0)
                {
                    major = v.dwMajorVersion; minor = v.dwMinorVersion; build = v.dwBuildNumber;
                    reliable = true;
                    return true;
                }
            }
            catch { }
            try
            {
                using (Microsoft.Win32.RegistryKey k = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                           @"SOFTWARE\Microsoft\Windows NT\CurrentVersion"))
                {
                    if (k != null)
                    {
                        object mj = k.GetValue("CurrentMajorVersionNumber");
                        object bd = k.GetValue("CurrentBuildNumber");
                        if (mj != null) major = Convert.ToInt32(mj);
                        if (bd != null) build = Convert.ToInt32(bd.ToString());
                        if (major > 0) { reliable = true; return true; }
                    }
                }
            }
            catch { }
            try
            {
                major = Environment.OSVersion.Version.Major;
                minor = Environment.OSVersion.Version.Minor;
                build = Environment.OSVersion.Version.Build;
                return true;      // reliable 仍为 false：这个来源会受清单影响
            }
            catch { }
            return false;
        }

        /// <summary>给日志/面板用的一行系统描述（用真实版本号，不读会撒谎的 Environment.OSVersion）。</summary>
        public static string Describe()
        {
            int major, minor, build;
            bool reliable;
            string arch = Environment.Is64BitOperatingSystem ? " / 64 位" : " / 32 位";
            if (!RealVersion(out major, out minor, out build, out reliable) || !reliable)
                return Environment.OSVersion.VersionString + arch;
            string name = major >= 10 ? (build >= 22000 ? "Windows 11" : "Windows 10") : "Windows " + major + "." + minor;
            return name + " " + major + "." + minor + "." + build + arch;
        }

        public static bool Supported(out string problem, out string warning)
        {
            problem = null;
            warning = null;
            try
            {
                if (Environment.OSVersion.Platform != PlatformID.Win32NT)
                {
                    problem = "本工具只支持 Windows。";
                    return false;
                }

                int major, minor, build;
                bool reliable;
                if (!RealVersion(out major, out minor, out build, out reliable))
                {
                    warning = "无法判定系统版本，已按兼容方式继续。";
                    return true;
                }

                if (!Environment.Is64BitOperatingSystem)
                {
                    problem = "本工具只支持 64 位系统；自动部署要装的 Node.js 安装包是 x64 版本。";
                    return false;
                }

                if (major < 10)
                {
                    if (!reliable)
                    {
                        // 进程没有 supportedOS 清单时 Windows 会把新系统谎报成 6.2；
                        // 版本读不准就别拦人，只提示。
                        warning = "系统版本无法准确判定（当前读到 " + major + "." + minor + "." + build +
                                  "），已按 Windows 10/11 兼容方式继续。";
                        return true;
                    }
                    problem = "本工具只支持 Windows 10 或 Windows 11（当前 " + major + "." + minor + "." + build + "）。";
                    return false;
                }

                if (build < 22000)
                    warning = "Windows 10 的多数常规版本已结束标准支持；仍按 x64 兼容方式运行，建议使用仍在支持期内并已打安全更新的系统。";
                return true;
            }
            catch (Exception ex)
            {
                problem = "无法判定系统版本：" + ex.Message;
                return false;
            }
        }
    }

    /// <summary>
    /// 网络取数：**优先交给 node 去下载**。
    /// 原因：本机的 .NET/Schannel 会按主机抽风（实测过 nodejs.org 通、npmjs.org 不通的情形），
    /// 而 node 自带 OpenSSL，不受 Windows 证书栈影响。node 不可用时才退回 .NET。
    /// </summary>
    internal static class NetFetch
    {
        private const string ScriptName = "dsh-netfetch.js";
        private static string _scriptPath;

        /// <summary>node 是否可用（决定走哪条取数路径）。</summary>
        public static bool NodeUsable(AppConfig cfg)
        {
            try { return DshLocator.FindNode(cfg) != null; }
            catch { return false; }
        }

        /// <summary>取一个文本内容（索引 / 校验文件 / npm view 之外的网页）。</summary>
        public static string GetString(AppConfig cfg, string url, int timeoutMs)
        {
            if (NodeUsable(cfg))
            {
                try
                {
                    string node = DshLocator.FindNode(cfg);
                    string script = EnsureScript();
                    string stdout, stderr;
                    int code = HiddenRunner.Run(node, "\"" + script + "\" \"" + url + "\"",
                                                null, null, timeoutMs, out stdout, out stderr);
                    if (code == 0 && stdout.Length > 0) return stdout;
                }
                catch { }
            }
            return HttpGetString(url, timeoutMs);
        }

        /// <summary>
        /// 下载到文件。默认用 .NET 的 WebClient（有字节级进度回调）；
        /// 失败时再用 node 重试一遍 —— 这条兜底正是为了绕开 Schannel 抽风。
        /// </summary>
        public static bool Download(AppConfig cfg, string url, string dest, LogHandler progress, int timeoutMs)
        {
            try
            {
                using (WebClient wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "DSH-Launcher");
                    DateTime last = DateTime.MinValue;
                    wc.DownloadProgressChanged += delegate(object s, DownloadProgressChangedEventArgs e)
                    {
                        if (progress == null) return;
                        if ((DateTime.Now - last).TotalMilliseconds < 400) return;
                        last = DateTime.Now;
                        progress("下载中 " + (e.BytesReceived / 1048576.0).ToString("0.0") + " MB"
                              + (e.TotalBytesToReceive > 0 ? " / " + (e.TotalBytesToReceive / 1048576.0).ToString("0.0") + " MB" : ""),
                              LogLevel.Dim);
                    };
                    wc.DownloadFile(url, dest);
                }
                if (File.Exists(dest) && new FileInfo(dest).Length > 0) return true;
            }
            catch (Exception ex)
            {
                if (progress != null) progress(".NET 下载失败（" + ex.Message + "），改用 node 重试…", LogLevel.Warn);
            }

            if (!NodeUsable(cfg)) return false;
            try
            {
                string node = DshLocator.FindNode(cfg);
                string script = EnsureScript();
                string stdout, stderr;
                int code = HiddenRunner.Run(node, "\"" + script + "\" \"" + url + "\" \"" + dest + "\"",
                                            null, null, timeoutMs, out stdout, out stderr);
                return code == 0 && File.Exists(dest) && new FileInfo(dest).Length > 0;
            }
            catch { return false; }
        }

        /// <summary>把取数脚本落到临时目录（只写一次），避免把带引号的 JS 塞进命令行。</summary>
        private static string EnsureScript()
        {
            if (!string.IsNullOrEmpty(_scriptPath) && File.Exists(_scriptPath)) return _scriptPath;
            string path = Path.Combine(Path.GetTempPath(), ScriptName);
            const string js =
                "const https=require('https'),http=require('http'),fs=require('fs');\n" +
                "const url=process.argv[2], out=process.argv[3];\n" +
                "function go(u,d){\n" +
                "  if(d>5){console.error('too many redirects');process.exit(3);}\n" +
                "  const m=/^http:/i.test(u)?http:https;\n" +
                "  const req=m.get(u,{headers:{'User-Agent':'DSH-Launcher'}},r=>{\n" +
                "    if(r.statusCode>=300&&r.statusCode<400&&r.headers.location){r.resume();return go(new URL(r.headers.location,u).toString(),d+1);}\n" +
                "    if(r.statusCode!==200){console.error('HTTP '+r.statusCode);process.exit(4);}\n" +
                "    if(out){const f=fs.createWriteStream(out);r.pipe(f);f.on('finish',()=>process.exit(0));f.on('error',e=>{console.error(e.message);process.exit(5);});}\n" +
                "    else{let b='';r.setEncoding('utf8');r.on('data',c=>b+=c);r.on('end',()=>process.stdout.write(b));}\n" +
                "  });\n" +
                "  req.setTimeout(120000,()=>{req.destroy(new Error('timeout'));});\n" +
                "  req.on('error',e=>{console.error(e.message);process.exit(6);});\n" +
                "}\n" +
                "go(url,0);\n";
            File.WriteAllText(path, js, new UTF8Encoding(false));
            _scriptPath = path;
            return path;
        }

        public static string HttpGetString(string url, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.UserAgent = "DSH-Launcher";
            using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
            using (Stream s = resp.GetResponseStream())
            using (StreamReader sr = new StreamReader(s, Encoding.UTF8))
                return sr.ReadToEnd();
        }
    }

    /// <summary>解析出来的 DSH 目标包（版本 + 完整性 + 来源）。</summary>
    internal class DshPackage
    {
        public string Version = "";
        public string Integrity = "";
        public string Registry = "";
        public string Source = "";          // 「最新」或「固定」
        public string LocalVersion = "";    // 本地缓存里已有的版本
        public bool Downgrade;              // 本地比目标更新 → 拒绝覆盖
    }

    /// <summary>语义化版本比较（主.次.修订 + 预发布段的数字感知比较）。</summary>
    internal static class SemVer
    {
        public static int Compare(string a, string b)
        {
            string[] pa = Split(a), pb = Split(b);
            for (int i = 0; i < 3; i++)
            {
                int va = i < pa.Length ? Num(pa[i]) : 0;
                int vb = i < pb.Length ? Num(pb[i]) : 0;
                if (va != vb) return va < vb ? -1 : 1;
            }
            // 主版本相同：没有预发布段的更新（1.0.0 > 1.0.0-rc.1）
            int da = a == null ? -1 : a.IndexOf('-');
            int db = b == null ? -1 : b.IndexOf('-');
            string ra = da >= 0 ? a.Substring(da + 1) : "";
            string rb = db >= 0 ? b.Substring(db + 1) : "";
            if (ra.Length == 0 && rb.Length == 0) return 0;
            if (ra.Length == 0) return 1;
            if (rb.Length == 0) return -1;

            string[] sa = ra.Split('.'), sb = rb.Split('.');
            int n = Math.Max(sa.Length, sb.Length);
            for (int i = 0; i < n; i++)
            {
                string x = i < sa.Length ? sa[i] : "";
                string y = i < sb.Length ? sb[i] : "";
                if (x == y) continue;
                bool xn = IsDigits(x), yn = IsDigits(y);
                if (xn && yn) { int ix = int.Parse(x), iy = int.Parse(y); return ix < iy ? -1 : 1; }
                if (xn) return -1;                 // 数字段 < 字母段（semver 规则）
                if (yn) return 1;
                return string.Compare(x, y, StringComparison.OrdinalIgnoreCase) < 0 ? -1 : 1;
            }
            return 0;
        }

        private static string[] Split(string v)
        {
            if (string.IsNullOrEmpty(v)) return new string[0];
            string core = v.Trim().TrimStart('v', 'V');
            int dash = core.IndexOf('-');
            if (dash >= 0) core = core.Substring(0, dash);
            return core.Split('.');
        }

        private static int Num(string s) { int n; return int.TryParse(s, out n) ? n : 0; }

        private static bool IsDigits(string s)
        {
            if (string.IsNullOrEmpty(s)) return false;
            foreach (char c in s) if (c < '0' || c > '9') return false;
            return true;
        }
    }

    /// <summary>
    /// 版本解析：默认动态取「官方最新可安装版 + 其完整 integrity」（对齐旧脚本的
    /// Resolve-LatestOfficialDshPackage），也可以切回固定版本（config 的 versionmode=pinned）。
    /// 通过 node 跑 npm view，因此不受 Schannel 影响。
    /// </summary>
    internal static class VersionResolve
    {
        public static DshPackage Resolve(AppConfig cfg, LogHandler log)
        {
            DshPackage p = new DshPackage();
            p.LocalVersion = LocalCachedVersion(cfg) ?? "";

            if (string.Equals(cfg.VersionMode, "pinned", StringComparison.OrdinalIgnoreCase))
            {
                p.Version = cfg.PinnedVersion;
                p.Integrity = cfg.PinnedIntegrity;
                p.Registry = cfg.Registry;
                p.Source = "固定";
            }
            else
            {
                // ① 先按旧脚本的路子：只认「有官方 immutable GitHub Release」的版本
                string ov, oi, orr;
                if (QueryOfficial(cfg, out ov, out oi, out orr, log))
                {
                    p.Version = ov; p.Integrity = oi; p.Registry = orr;
                    p.Source = "官方最新可安装版";
                }
                else
                {
                    // ② GitHub 不可用（或没有对应的 npm 包）时，退回「npm 上已发布版本里最大的」
                    string[] regs = new string[] { cfg.Registry, "https://registry.npmjs.org" };
                    foreach (string reg in regs)
                    {
                        if (string.IsNullOrEmpty(reg)) continue;
                        string version, integrity;
                        if (QueryNpm(cfg, reg, out version, out integrity))
                        {
                            p.Version = version;
                            p.Integrity = integrity;
                            p.Registry = reg;
                            p.Source = "最新（GitHub 发布列表不可用，取自 npm）";
                            break;
                        }
                        if (log != null) log("源 " + reg + " 没能返回版本信息，换下一个源。", LogLevel.Dim);
                    }
                }
                if (p.Version.Length == 0)
                {
                    // 全部源都问不到 → 退回固定版本，保证还能装出一个可用的东西
                    p.Version = cfg.PinnedVersion;
                    p.Integrity = cfg.PinnedIntegrity;
                    p.Registry = cfg.Registry;
                    p.Source = "固定（动态解析失败后的回退）";
                    if (log != null) log("无法动态解析最新版本，回退到固定版本 " + cfg.PinnedVersion + "。", LogLevel.Warn);
                }
            }

            // 防降级：本地缓存比目标新时不覆盖
            if (p.LocalVersion.Length > 0 && p.Version.Length > 0 &&
                !string.Equals(p.LocalVersion, p.Version, StringComparison.OrdinalIgnoreCase) &&
                SemVer.Compare(p.LocalVersion, p.Version) > 0)
            {
                p.Downgrade = true;
            }
            return p;
        }

        /// <summary>
        /// 官方发布优先路径（对齐旧脚本 Get-LatestOfficialDshRelease + Resolve-LatestOfficialDshPackage）：
        /// 翻 GitHub Release 列表，只认「非 draft + tag 形如 dsh-v&lt;semver&gt; + immutable=true」的条目，
        /// 按 GitHub 给出的顺序（新→旧）逐个确认 npm 上确实有该版本，取第一个能装的。
        /// </summary>
        private static bool QueryOfficial(AppConfig cfg, out string version, out string integrity, out string registry, LogHandler log)
        {
            version = ""; integrity = ""; registry = "";
            try
            {
                List<string> candidates = new List<string>();
                for (int page = 1; page <= 2 && candidates.Count < 12; page++)
                {
                    string url = "https://api.github.com/repos/deepseek-ai/deepseek-harness/releases?per_page=100&page=" + page;
                    string json = null;
                    try { json = NetFetch.GetString(cfg, url, 30000); }
                    catch { json = null; }
                    if (string.IsNullOrEmpty(json)) break;

                    int at = 0;
                    while (candidates.Count < 12)
                    {
                        int i = json.IndexOf("\"tag_name\"", at, StringComparison.Ordinal);
                        if (i < 0) break;
                        at = i + 10;
                        string tag = PickOne(json.Substring(i), "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
                        System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(
                            tag, "^dsh-v(\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z.\\-]+)?)$");
                        if (!m.Success) continue;

                        // 就近窗口里查 draft / immutable（沿用工程里既有的"定位 + 就近回溯"风格）
                        int from = Math.Max(0, i - 2500);
                        int to = Math.Min(json.Length, i + 2500);
                        string win = json.Substring(from, to - from);
                        if (win.IndexOf("\"draft\":true", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        if (win.IndexOf("\"immutable\":false", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                        candidates.Add(m.Groups[1].Value);
                    }
                }

                if (candidates.Count == 0)
                {
                    if (log != null) log("GitHub 上没有取到「正式且不可变」的 DSH 发布，改用 npm 版本列表。", LogLevel.Dim);
                    return false;
                }

                foreach (string cand in candidates)
                {
                    string[] regs = new string[] { cfg.Registry, "https://registry.npmjs.org" };
                    foreach (string reg in regs)
                    {
                        if (string.IsNullOrEmpty(reg)) continue;
                        string iv;
                        if (QueryNpmExact(cfg, reg, cand, out iv))
                        {
                            version = cand; integrity = iv; registry = reg;
                            if (log != null) log("官方最新可安装版：" + cand + "（" + reg.Replace("https://", "") + "）", LogLevel.Dim);
                            return true;
                        }
                    }
                    if (log != null) log("GitHub 发布 " + cand + " 在 npm 上还取不到，继续看下一个。", LogLevel.Dim);
                }
                return false;
            }
            catch { return false; }
        }

        /// <summary>确认某个确切版本在指定源上存在，并取它的 integrity。</summary>
        private static bool QueryNpmExact(AppConfig cfg, string registry, string ver, out string integrity)
        {
            integrity = "";
            try
            {
                string node = DshLocator.FindNode(cfg);
                if (node == null) return false;
                string npmCli = Path.Combine(Path.GetDirectoryName(node), @"node_modules\npm\bin\npm-cli.js");
                if (!File.Exists(npmCli)) npmCli = Path.Combine(Path.GetDirectoryName(node), @"lib\node_modules\npm\bin\npm-cli.js");
                if (!File.Exists(npmCli)) return false;

                string stdout, stderr;
                int code = HiddenRunner.Run(node,
                    "\"" + npmCli + "\" view @deepseek-ai/dsh@" + ver + " version dist.integrity --json" +
                    " --registry=" + registry + " --fetch-timeout=20000 --fetch-retries=1",
                    null, null, 45000, out stdout, out stderr);
                if (code != 0) return false;

                string vv = SourceVerify.JsonField(stdout, "version");
                if (string.IsNullOrEmpty(vv)) return false;
                if (!string.Equals(vv.Trim('"', ' '), ver, StringComparison.OrdinalIgnoreCase)) return false;
                string ii = SourceVerify.JsonField(stdout, "dist.integrity");
                integrity = string.IsNullOrEmpty(ii) ? "" : ii.Trim('"', ' ');
                return true;
            }
            catch { return false; }
        }

        private static string PickOne(string text, string pattern)
        {
            try
            {
                System.Text.RegularExpressions.Match m = System.Text.RegularExpressions.Regex.Match(text, pattern);
                return m.Success ? m.Groups[1].Value : "";
            }
            catch { return ""; }
        }

        /// <summary>取指定源上「已发布版本里最大的那个」及其 integrity（经 node，不经 Schannel）。</summary>
        private static bool QueryNpm(AppConfig cfg, string registry, out string version, out string integrity)
        {
            version = "";
            integrity = "";
            try
            {
                string node = DshLocator.FindNode(cfg);
                if (node == null) return false;
                string npmCli = Path.Combine(Path.GetDirectoryName(node), @"node_modules\npm\bin\npm-cli.js");
                if (!File.Exists(npmCli)) npmCli = Path.Combine(Path.GetDirectoryName(node), @"lib\node_modules\npm\bin\npm-cli.js");
                if (!File.Exists(npmCli)) return false;

                // ① 取全部已发布版本，自己挑最大的。
                //    不能只看 dist-tag 的 latest —— 实测 @deepseek-ai/dsh 的 latest 指向 0.1.5-rc.1，
                //    而用户机器上跑的 0.1.5-rc.2 比它新，照 latest 走会变成降级。
                string stdout, stderr;
                int code = HiddenRunner.Run(node,
                    "\"" + npmCli + "\" view @deepseek-ai/dsh versions --json" +
                    " --registry=" + registry + " --fetch-timeout=20000 --fetch-retries=1",
                    null, null, 45000, out stdout, out stderr);
                if (code != 0) return false;

                string best = null;
                foreach (System.Text.RegularExpressions.Match m in
                         System.Text.RegularExpressions.Regex.Matches(
                             stdout, "\"(\\d+\\.\\d+\\.\\d+(?:-[0-9A-Za-z.\\-]+)?)\""))
                {
                    string v = m.Groups[1].Value;
                    if (best == null || SemVer.Compare(v, best) > 0) best = v;
                }
                if (best == null) return false;

                // ② 再取这个确定版本的 integrity
                code = HiddenRunner.Run(node,
                    "\"" + npmCli + "\" view @deepseek-ai/dsh@" + best + " version dist.integrity --json" +
                    " --registry=" + registry + " --fetch-timeout=20000 --fetch-retries=1",
                    null, null, 45000, out stdout, out stderr);
                if (code != 0) return false;

                string vv = SourceVerify.JsonField(stdout, "version");
                string ii = SourceVerify.JsonField(stdout, "dist.integrity");
                version = string.IsNullOrEmpty(vv) ? best : vv.Trim('"', ' ');
                integrity = string.IsNullOrEmpty(ii) ? "" : ii.Trim('"', ' ');
                return version.Length > 0;
            }
            catch { return false; }
        }

        /// <summary>本地 npx / 全局缓存里那份 DSH 的版本号（读它自己的 package.json）。</summary>
        public static string LocalCachedVersion(AppConfig cfg)
        {
            try
            {
                string entry = DshLocator.FindCachedEntry(cfg);
                if (string.IsNullOrEmpty(entry)) return null;
                string dir = Path.GetDirectoryName(Path.GetDirectoryName(entry));   // …\@deepseek-ai\dsh
                string pkg = Path.Combine(dir, "package.json");
                if (!File.Exists(pkg)) return null;
                string json = File.ReadAllText(pkg, Encoding.UTF8);
                string v = SourceVerify.JsonField(json, "version");
                return string.IsNullOrEmpty(v) ? null : v.Trim('"', ' ');
            }
            catch { return null; }
        }
    }

    /// <summary>npm 缓存体积探针：拉取阶段用它把"真实进度"报出来（旧脚本同款做法）。</summary>
    internal static class NpmCacheProbe
    {
        /// <summary>npm 缓存根目录（经 node 问 npm config get cache；失败则用默认位置）。</summary>
        public static string CacheRoot(AppConfig cfg)
        {
            try
            {
                string node = DshLocator.FindNode(cfg);
                if (node != null)
                {
                    string npmCli = Path.Combine(Path.GetDirectoryName(node), @"node_modules\npm\bin\npm-cli.js");
                    if (File.Exists(npmCli))
                    {
                        string stdout, stderr;
                        int code = HiddenRunner.Run(node, "\"" + npmCli + "\" config get cache",
                                                    null, null, 15000, out stdout, out stderr);
                        if (code == 0)
                        {
                            string[] lines = stdout.Split('\n');
                            for (int i = lines.Length - 1; i >= 0; i--)
                            {
                                string t = lines[i].Trim();
                                if (t.Length > 0) return t;
                            }
                        }
                    }
                }
            }
            catch { }
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            return Path.Combine(local, "npm-cache");
        }

        /// <summary>缓存里 _cacache 与 _npx 两个子目录的总字节数。</summary>
        public static long SizeBytes(string cacheRoot)
        {
            if (string.IsNullOrEmpty(cacheRoot) || !Directory.Exists(cacheRoot)) return 0L;
            long total = 0L;
            foreach (string name in new string[] { "_cacache", "_npx" })
            {
                string dir = Path.Combine(cacheRoot, name);
                if (!Directory.Exists(dir)) continue;
                try
                {
                    foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    {
                        try { total += new FileInfo(f).Length; } catch { }
                    }
                }
                catch { }
            }
            return total;
        }
    }
}
