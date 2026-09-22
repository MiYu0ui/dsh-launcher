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
        /// <summary>
        /// RtlGetVersion 的输出结构（ntdll 的 RTL_OSVERSIONINFOW）。
        /// </summary>
        /// <remarks>
        /// 布局必须与 ntdll 的定义逐字段对齐：<c>dwOSVersionInfoSize</c> 要在调用前填成结构体字节数，
        /// 否则 RtlGetVersion 直接失败。CharSet 只能是 Unicode —— 原生侧的 <c>szCSDVersion</c>
        /// 是定长宽字符数组，按 ANSI 封送会读成乱码。
        /// </remarks>
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

        /// <summary>
        /// 向 ntdll 直接问系统版本。返回 0（STATUS_SUCCESS）才表示 <paramref name="info"/> 被填好。
        /// </summary>
        /// <remarks>
        /// 之所以不用 GetVersionEx / VerifyVersionInfo：它们会被进程清单里的 supportedOS 声明左右，
        /// 而 RtlGetVersion 只看内核真实版本（详见 <see cref="RealVersion"/> 的说明）。
        /// 它不是 Win32 API 的正式成员（文档归在 WDK 一侧），但 ntdll.dll 一直导出它，
        /// 本工具只支持 Windows 10/11，不需要为更老的系统留后路。
        /// </remarks>
        /// <param name="info">调用前必须填好 <c>dwOSVersionInfoSize</c> 的结构体。</param>
        /// <returns>NTSTATUS；非 0 视为取版本失败，调用方要退回注册表或受清单影响的来源。</returns>
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

        /// <summary>
        /// 部署第 1 步的系统门：判定当前机器能否继续自动部署。
        /// </summary>
        /// <remarks>
        /// 两个出参的分工是调用方的分支依据，不是"级别高低"：
        /// <paramref name="problem"/> 非空 = 硬性不支持，必须中止（非 Windows、32 位、明确低于 Win10）；
        /// <paramref name="warning"/> 非空 = 放行但要在面板/日志里提一句（版本读不准、Win10 已过支持期）。
        /// 两者都为 null 是唯一"完全没问题"的情形；判定不出问题时一律偏向放行 ——
        /// 读不准版本不该拦住用户，真正的失败留给后续步骤去报。
        /// 本方法自己吞掉所有异常（异常也转成 problem），调用方不必再包 try。
        /// </remarks>
        /// <param name="problem">不为 null 时表示必须中止的原因（面向用户的中文句子）。</param>
        /// <param name="warning">不为 null 时表示"可以继续，但值得提醒"的说明。</param>
        /// <returns>true = 可以继续部署（可能带 warning）；false = 应当中止。</returns>
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
        private static string _scriptPath;      // 已落盘的脚本路径（进程内缓存；%TEMP% 里的文件不主动删除）

        /// <summary>node 是否可用（决定走哪条取数路径）。</summary>
        /// <param name="cfg">用于定位 node 的配置（含自定义安装位置）。</param>
        /// <returns>true = 已找到 node 可执行文件；查找本身抛异常时也按 false 处理。</returns>
        public static bool NodeUsable(AppConfig cfg)
        {
            try { return DshLocator.FindNode(cfg) != null; }
            catch { return false; }
        }

        /// <summary>取一个文本内容（索引 / 校验文件 / npm view 之外的网页）。</summary>
        /// <remarks>
        /// node 优先、.NET 兜底：node 那条路返回空串或非 0 退出码（含超时的 -1）时静默降级到
        /// <see cref="HttpGetString"/>。node 那条路自己包了 catch，.NET 这条会抛 ——
        /// 所以本方法仍可能抛，由调用方决定"取不到就算了"还是"必须报错"。
        /// </remarks>
        /// <param name="cfg">用于定位 node 的配置。</param>
        /// <param name="url">完整 URL，会原样作为脚本的第 1 个参数传给 node（不经 shell 解析）。</param>
        /// <param name="timeoutMs">超时上限；node 路径交给 HiddenRunner，.NET 路径设给 HttpWebRequest。</param>
        /// <returns>正文文本。</returns>
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
        /// <remarks>
        /// 与 <see cref="GetString"/> 的优先顺序相反：这里 .NET 优先，因为只有 WebClient 才给得出
        /// 字节级进度。<paramref name="dest"/> 已存在时直接覆盖。判定成功的标准不是"没抛异常"，
        /// 而是"文件存在且长度大于 0" —— 传输中断会留下 0 字节的残留文件，必须当成失败。
        /// 进度回调按 400ms 节流（简单的时间戳比较，不保证每次进度变化都报一次）。
        /// </remarks>
        /// <param name="cfg">用于定位 node（兜底路径要用）。</param>
        /// <param name="url">下载地址。</param>
        /// <param name="dest">目标文件路径，父目录必须已存在。</param>
        /// <param name="progress">进度/降级提示回调，可为 null。</param>
        /// <param name="timeoutMs">node 兜底路径的超时；.NET 那条走 WebClient 的默认超时。</param>
        /// <returns>true = 目标文件已存在且非空。</returns>
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
        /// <remarks>
        /// 写文件用**不带 BOM** 的 UTF-8（脚本内容全是 ASCII，无 BOM 更省事）。
        /// 文件名固定，多实例并发启动时会互相覆盖同名文件 —— 理论上存在"读到半截脚本"的窗口，
        /// 但脚本只有几百字节且内容恒定，工程里接受这个风险，不加锁也不校验。
        /// </remarks>
        /// <returns>脚本的绝对路径。</returns>
        private static string EnsureScript()
        {
            if (!string.IsNullOrEmpty(_scriptPath) && File.Exists(_scriptPath)) return _scriptPath;
            string path = Path.Combine(Path.GetTempPath(), ScriptName);
            // 退出码即错误分类：3=重定向超过 5 跳、4=HTTP 非 200、5=写文件失败、6=网络错误；
            // 调用方只区分 0 / 非 0，细节要看 stderr。脚本内的 120s 是自保，
            // 真正的上限由 HiddenRunner 的 timeoutMs 决定（超时它返回 -1 并杀掉 node）。
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

        /// <summary>
        /// .NET 侧的纯文本 GET：node 不可用、或 node 那条路失败时的兜底。
        /// </summary>
        /// <remarks>
        /// 走的是系统 Schannel 证书栈 —— 正是 NetFetch 类型注释里说的"会按主机抽风"的那条路，
        /// 所以它只配当兜底。<paramref name="timeoutMs"/> 同时设给 Timeout 与 ReadWriteTimeout
        /// （连接与读写各算各的）。响应按 UTF-8 解码（StreamReader 默认还会识别并跳过 BOM）；
        /// 非 2xx 会在 GetResponse 处抛 WebException，重定向交给 HttpWebRequest 的默认行为
        /// （最多 50 跳）。
        /// </remarks>
        /// <param name="url">完整 URL。</param>
        /// <param name="timeoutMs">连接与读写的超时上限（毫秒）。</param>
        /// <returns>响应正文。</returns>
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
    /// <remarks>
    /// 纯数据载体，字段全部由 <c>VersionResolve</c> 填充。
    /// <c>Integrity</c> 是 npm 的 dist.integrity（SRI 字符串），可能为空 —— 那只表示"这个源没给"，
    /// 不代表校验可以跳过。<c>Downgrade</c> 不是解析结果，而是"本地缓存比目标新"的结论，
    /// 由调用方决定要不要据此拒绝安装。
    /// </remarks>
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
        /// <summary>
        /// 比较两个版本号：a 小于 b 返回 -1，相等返回 0，a 大于 b 返回 1。
        /// </summary>
        /// <remarks>
        /// 只比较"主.次.修订 + 预发布段"，不处理 +build 元数据（在本工具的用途里它不影响"要不要升级"，
        /// 实际会被当成无法解析的段而归 0）。缺段按 0 补齐；两边的 v 前缀与首尾空白都会被吃掉。
        /// 预发布段按 '.' 再切分：全数字段按数值比，其余按序数（忽略大小写）比，
        /// 且数字段一律小于字母段 —— 这是 semver 的既定规则，别改成"按字符串比"。
        /// null / 空串等同于 0.0.0（最低版本）。
        /// </remarks>
        /// <param name="a">左版本号，可带 v 前缀。</param>
        /// <param name="b">右版本号，可带 v 前缀。</param>
        /// <returns>-1 / 0 / 1。</returns>
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

        /// <summary>取版本号的"数字核心"：去掉 v 前缀与首尾空白、砍掉 '-' 之后的预发布段，再按 '.' 切分。</summary>
        /// <param name="v">原始版本串，可为 null。</param>
        /// <returns>主/次/修订数字段（长度不定，缺段由调用方按 0 补）。</returns>
        private static string[] Split(string v)
        {
            if (string.IsNullOrEmpty(v)) return new string[0];
            string core = v.Trim().TrimStart('v', 'V');
            int dash = core.IndexOf('-');
            if (dash >= 0) core = core.Substring(0, dash);
            return core.Split('.');
        }

        /// <summary>把一段文本当数字读，读不出就是 0（空段、带 +build 的段都归到 0）。</summary>
        private static int Num(string s) { int n; return int.TryParse(s, out n) ? n : 0; }

        /// <summary>整段是否全为 ASCII 数字（空串与 null 都不算）—— 决定该段按数值比还是按序数比。</summary>
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
        /// <summary>
        /// 解析出本次要安装的目标包：动态优先，固定版本兜底。
        /// </summary>
        /// <remarks>
        /// 三条路径按顺序退让，任何一条成功就立刻停止：
        /// ① 官方 GitHub Release（<c>QueryOfficial</c>）；
        /// ② npm 上已发布版本里最大的那个（<c>QueryNpm</c>）；
        /// ③ 全问不到就退回 config 里的固定版本，保证"总还能装出一个能用的东西"。
        /// 走 ③ 会在日志里留一条 Warn —— 它意味着版本可能不是最新的，是排查问题的第一线索。
        /// 解析完还会做一次**防降级判定**：本地缓存版本比目标新时打上 <c>Downgrade</c> 标记；
        /// 本方法只判定不阻断，是否真的拒绝安装由调用方决定。
        /// 不抛异常：网络侧的问题都被各个 Query 吞成 false。
        /// </remarks>
        /// <param name="cfg">配置（版本模式、固定版本、registry 都从这里取）。</param>
        /// <param name="log">日志回调，可为 null。</param>
        /// <returns>已填充的目标包；调用方要检查 <c>Downgrade</c>，以及三个字符串字段是否为空。</returns>
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
        /// <remarks>
        /// 只认 npm 明确回报的版本号：<c>version</c> 字段必须与请求的版本一致（忽略引号与大小写），
        /// 否则视为"这个源上没有" —— registry 镜像落后时正是这种情况，靠这一步把候选筛掉。
        /// 走 npm-cli.js 而不是 .NET 的理由见 <see cref="NetFetch"/>。npm-cli.js 的位置按两种
        /// npm 布局先后探测（独立安装 / node 自带），都不在就放弃。
        /// </remarks>
        /// <param name="cfg">用于定位 node。</param>
        /// <param name="registry">registry 地址（以 --registry 传给 npm）。</param>
        /// <param name="ver">要确认的确切版本号。</param>
        /// <param name="integrity">该版本的 dist.integrity；源没给时为空串。</param>
        /// <returns>true = 该源上确实有这个版本。</returns>
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

        /// <summary>单次正则匹配并取第 1 个捕获组；不匹配、无捕获组或正则本身出错都返回空串。</summary>
        /// <remarks>
        /// 这里是有意"拿不准就当没有"：上游 JSON 的结构一旦变化，宁可让上层回退到别的取数路径，
        /// 也不要因为一个正则异常把整条解析链炸掉。调用方拿到空串要当成"没找到"。
        /// </remarks>
        /// <param name="text">待匹配文本。</param>
        /// <param name="pattern">正则（本工程解析 JSON 一律用这种"定位 + 就近回溯"的做法）。</param>
        /// <returns>第 1 个捕获组，或空串。</returns>
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
        /// <remarks>
        /// 要跑两次 npm：先 <c>view versions</c> 拿到全部版本号自己挑最大，再对选中的版本取 dist.integrity。
        /// 两次调用之间源上的内容理论上可能变，所以第二次回报的 <c>version</c> 优先于本地挑出的 best ——
        /// 以 npm 说的为准，避免"挑的和取到的是两个版本"。任一步失败都返回 false，不做重试。
        /// </remarks>
        /// <param name="cfg">用于定位 node。</param>
        /// <param name="registry">registry 地址。</param>
        /// <param name="version">挑出的版本号（以 npm 回报为准）；失败时为空串。</param>
        /// <param name="integrity">该版本的 dist.integrity；源没给时为空串。</param>
        /// <returns>true = 取到了可用版本。</returns>
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
        /// <remarks>
        /// 纯只读探测：缓存入口找不到、package.json 不在、字段缺失、读取异常，一律返回 null，
        /// 由调用方按"本地没有版本"处理（本工程里 null 与空串同义）。它不触发任何下载。
        /// </remarks>
        /// <param name="cfg">用于定位缓存目录的配置。</param>
        /// <returns>版本号，或 null。</returns>
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
        /// <remarks>
        /// 取输出的**最后一行非空文本**：npm 会先在 stdout 打警告，把真正的路径挤到后面。
        /// 问不到（node 不在、npm-cli.js 不在、退出码非 0）就退回 %LOCALAPPDATA%\npm-cache，
        /// 那是 npm 在 Windows 上的默认位置。本方法只报路径，不保证目录存在。
        /// </remarks>
        /// <param name="cfg">用于定位 node。</param>
        /// <returns>缓存根目录的绝对路径（不保证存在）。</returns>
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
        /// <remarks>
        /// 只算这两个目录：npm 拉包落在 _cacache，npx 的临时安装落在 _npx，其余子目录与拉取无关。
        /// 单个文件读长度失败就跳过它（枚举与读取之间文件被 npm 挪走时会出现），
        /// 所以这个数字是下限估计而不是精确值 —— 它只用来把拉取进度画得动起来。
        /// </remarks>
        /// <param name="cacheRoot">缓存根目录；为空或不存在时返回 0。</param>
        /// <returns>总字节数。</returns>
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
