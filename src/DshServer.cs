using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DshLauncher
{
    /// <summary>服务状态机。External = 端口上已有 DSH，但不是本启动器拉起的（只接管显示，不拥有进程）。</summary>
    /// <remarks>
    /// PortConflict 目前没有任何赋值点（端口被别人占用时走 Failed，靠 LastError 说明原因），
    /// 但托盘文案、状态指示灯与表单已经在读它，保留这个取值。
    /// </remarks>
    internal enum ServerStatus { Stopped, Starting, Running, Stopping, External, PortConflict, Failed }

    /// <summary>端口探测结论：没人监听 / 已是 DSH（可能需要令牌）/ 被别的程序占用。</summary>
    internal enum ProbeState { Down, Dsh, Other }

    /// <summary>定位 node / npx / dsh 入口。</summary>
    internal static class DshLocator
    {
        /// <summary>在 PATH 里逐目录找文件。自己扫而不用 where.exe：省一次进程启动，也不会闪窗口。</summary>
        /// <returns>命中返回完整路径；PATH 为空或都没找到时返回 null。</returns>
        /// <remarks>PATH 里带引号的项（例如 "C:\Program Files\nodejs"）会先去掉两端引号再拼路径。</remarks>
        public static string SearchPath(string fileName)
        {
            try
            {
                string path = Environment.GetEnvironmentVariable("PATH");
                if (string.IsNullOrEmpty(path)) return null;
                foreach (string raw in path.Split(';'))
                {
                    string dir = raw.Trim().Trim('"');
                    if (dir.Length == 0) continue;
                    try
                    {
                        string full = Path.Combine(dir, fileName);
                        if (File.Exists(full)) return full;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        /// <summary>定位 node.exe：配置里写死的路径 → PATH → 两个最常见的安装目录。</summary>
        /// <returns>找不到返回 null（调用方据此提示"请安装 Node.js"）。</returns>
        public static string FindNode(AppConfig cfg)
        {
            if (!string.IsNullOrEmpty(cfg.NodePath) && File.Exists(cfg.NodePath)) return cfg.NodePath;
            string found = SearchPath("node.exe");
            if (found != null) return found;
            string[] guesses = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"nodejs\node.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"nodejs\node.exe")
            };
            foreach (string g in guesses) { try { if (File.Exists(g)) return g; } catch { } }
            return null;
        }

        /// <summary>定位 npx.cmd。注意 .cmd 必须经 cmd.exe 才能起来，能不折腾就优先用下面的 npx-cli.js。</summary>
        /// <returns>找不到返回 null。</returns>
        public static string FindNpxCmd(AppConfig cfg)
        {
            return SearchPath("npx.cmd");
        }

        /// <summary>node 自带的 npx-cli.js（用它可完全绕开 cmd.exe，少一层窗口风险）。</summary>
        /// <returns>找到返回完整路径；nodeExe 为空或两处都没有时返回 null。</returns>
        /// <remarks>两个候选位置对应 npm 的两种安装布局：与 node.exe 同级，或包在 lib\ 目录下。</remarks>
        public static string FindNpxCliJs(string nodeExe)
        {
            if (string.IsNullOrEmpty(nodeExe)) return null;
            try
            {
                string dir = Path.GetDirectoryName(nodeExe);
                if (string.IsNullOrEmpty(dir)) return null;
                string candidate = Path.Combine(dir, @"node_modules\npm\bin\npx-cli.js");
                if (File.Exists(candidate)) return candidate;
                candidate = Path.Combine(dir, @"lib\node_modules\npm\bin\npx-cli.js");
                if (File.Exists(candidate)) return candidate;
            }
            catch { }
            return null;
        }

        /// <summary>已缓存的 @deepseek-ai/dsh 入口 bin.js（极速模式用）。</summary>
        /// <returns>找到返回完整路径；都没有时返回 null（调用方据此回落到 npx 在线启动）。</returns>
        /// <remarks>
        /// 扫 npx 缓存（%LocalAppData%\npm-cache\_npx 下的哈希目录）与 %AppData%\npm，取其中
        /// node_modules\@deepseek-ai\dsh\lib\bin.js 写入时间最新的一份 —— 缓存目录名是哈希，没法直接拼出来。
        /// </remarks>
        public static string FindCachedEntry(AppConfig cfg)
        {
            if (!string.IsNullOrEmpty(cfg.DshEntry) && File.Exists(cfg.DshEntry)) return cfg.DshEntry;

            List<string> roots = new List<string>();
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string npxRoot = Path.Combine(local, @"npm-cache\_npx");
            try
            {
                if (Directory.Exists(npxRoot))
                {
                    foreach (string dir in Directory.GetDirectories(npxRoot)) roots.Add(dir);
                }
            }
            catch { }

            string roaming = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            roots.Add(Path.Combine(roaming, "npm"));

            string best = null;
            DateTime bestTime = DateTime.MinValue;
            foreach (string root in roots)
            {
                string candidate = Path.Combine(root, @"node_modules\@deepseek-ai\dsh\lib\bin.js");
                try
                {
                    if (File.Exists(candidate))
                    {
                        DateTime t = File.GetLastWriteTimeUtc(candidate);
                        if (t > bestTime) { bestTime = t; best = candidate; }
                    }
                }
                catch { }
            }
            return best;
        }
    }

    /// <summary>在后台静默执行一个子进程并取得输出（绝不产生窗口）。</summary>
    internal static class HiddenRunner
    {
        /// <summary>在后台静默执行一个子进程并取回输出，全程不产生窗口。</summary>
        /// <param name="fileName">可执行文件（node.exe / cmd.exe / taskkill.exe 等）。</param>
        /// <param name="arguments">命令行参数，按原样传给子进程。</param>
        /// <param name="workingDir">子进程的工作目录；为空表示沿用当前目录。</param>
        /// <param name="environment">额外注入的环境变量（例如 npm_config_registry）；可为 null。</param>
        /// <param name="timeoutMs">超时毫秒数；超时会结束该进程。</param>
        /// <param name="stdout">子进程的标准输出（按 UTF-8 解码）。</param>
        /// <param name="stderr">子进程的标准错误；进程没能起来时里面是异常信息。</param>
        /// <returns>子进程退出码；超时返回 -1，启动/读取抛异常返回 -2（stderr 里带异常信息）。</returns>
        /// <remarks>
        /// stdout 与 stderr 各用一个线程读到底：只读一路时，另一路管道写满会把子进程卡死。
        /// 输出按不带 BOM 的 UTF-8 解码，与 DSH / npm 的输出保持一致。
        /// </remarks>
        public static int Run(string fileName, string arguments, string workingDir,
                              Dictionary<string, string> environment, int timeoutMs,
                              out string stdout, out string stderr)
        {
            stdout = "";
            stderr = "";
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(fileName, arguments);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;
                psi.StandardOutputEncoding = new UTF8Encoding(false);
                psi.StandardErrorEncoding = new UTF8Encoding(false);
                if (!string.IsNullOrEmpty(workingDir)) psi.WorkingDirectory = workingDir;
                if (environment != null)
                {
                    foreach (KeyValuePair<string, string> kv in environment) psi.EnvironmentVariables[kv.Key] = kv.Value;
                }

                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.Start();

                    string outText = "";
                    string errText = "";
                    Thread t1 = new Thread(delegate() { try { outText = p.StandardOutput.ReadToEnd(); } catch { } });
                    Thread t2 = new Thread(delegate() { try { errText = p.StandardError.ReadToEnd(); } catch { } });
                    t1.IsBackground = true; t2.IsBackground = true;
                    t1.Start(); t2.Start();

                    bool exited = p.WaitForExit(timeoutMs);
                    if (!exited)
                    {
                        try { p.Kill(); } catch { }
                        try { p.WaitForExit(3000); } catch { }
                    }
                    t1.Join(2000); t2.Join(2000);
                    stdout = outText;
                    stderr = errText;
                    if (!exited) return -1;
                    return p.ExitCode;
                }
            }
            catch (Exception ex)
            {
                stderr = ex.Message;
                return -2;
            }
        }
    }

    /// <summary>DeepSeek Harness 服务进程的启动、就绪探测与停止。</summary>
    internal class DshServer
    {
        private readonly object _gate = new object();   // 保护 _proc 与 _status
        private Process _proc;                          // 非 null 即"这个进程归我们管"（Owned）
        private ServerStatus _status = ServerStatus.Stopped;
        private string _lastError = "";
        private volatile bool _cancel;                  // 置位表示"这次退出是我们主动停的"，不要报成崩溃
        // DSH 就绪时会把带令牌的地址打在输出里，形状固定为 "dsh web: http://…"（token 每次都不同）
        private readonly Regex _urlPattern = new Regex(@"dsh web:\s*(http://\S+)", RegexOptions.Compiled);

        /// <summary>日志事件（可能在启动线程或进程回调线程上触发，订阅方需自行切回界面线程）。</summary>
        public event LogHandler Log;
        /// <summary>状态变化事件（同样：SetStatus 会在任意线程上触发）。</summary>
        public event EventHandler StatusChanged;
        /// <summary>本启动器拉起的服务刚刚就绪（用于播放接入过渡动画并交接界面）。</summary>
        public event EventHandler Serving;

        /// <summary>触发 Serving 事件；订阅方抛异常只吞掉，不允许影响服务本身。</summary>
        private void RaiseServing()
        {
            EventHandler h = Serving;
            if (h != null) { try { h(this, EventArgs.Empty); } catch { } }
        }

        public string WebUrl;      // 规范地址 http://127.0.0.1:port/（不带令牌；PortFromWebUrl 靠它反查端口）
        public string OpenUrl;     // 实际打开地址（优先用 DSH 打印出来的带 token 地址）

        /// <summary>当前状态（加锁读，任意线程可调）。</summary>
        public ServerStatus Status
        {
            get { lock (_gate) { return _status; } }
        }

        /// <summary>最近一次失败原因（给界面与诊断用；每次启动前清空）。</summary>
        public string LastError { get { return _lastError; } }

        /// <summary>进程是否由本启动器拉起。External（别人起的服务）时为 false，此时不该去 Stop 它。</summary>
        public bool Owned
        {
            get { lock (_gate) { return _proc != null; } }
        }

        /// <summary>我们拉起的那个进程是否还活着（_proc 为 null 或已退出都算 false）。</summary>
        public bool IsProcessAlive
        {
            get
            {
                lock (_gate)
                {
                    if (_proc == null) return false;
                    try { return !_proc.HasExited; }
                    catch { return false; }
                }
            }
        }

        /// <summary>改状态并去重：只有真的变了才落日志、发事件，避免界面被无意义的刷新淹没。</summary>
        private void SetStatus(ServerStatus s)
        {
            bool changed;
            lock (_gate)
            {
                changed = _status != s;
                _status = s;
            }
            if (changed)
            {
                FileLog.Write("status -> " + s);
                EventHandler h = StatusChanged;
                if (h != null) { try { h(this, EventArgs.Empty); } catch { } }
            }
        }

        /// <summary>上报日志：有订阅方时交给它统一落盘，没有才自己写文件，避免同一条写两遍。</summary>
        private void Emit(string message, LogLevel level)
        {
            LogHandler h = Log;
            if (h != null) { try { h(message, level); } catch { } }   // 有订阅方时由它统一落盘，避免重复写
            else FileLog.Write("[" + level + "] " + message);
        }

        /// <summary>拼规范地址：回环地址 + 十进制端口 + 结尾斜杠。</summary>
        public static string NormalizeUrl(int port) { return "http://127.0.0.1:" + port.ToString(CultureInfo.InvariantCulture) + "/"; }

        /// <summary>探测端口：空闲 / 已是 DSH / 被其他程序占用。</summary>
        /// <param name="port">要探测的本机端口。</param>
        /// <param name="detail">给界面看的中文结论（"端口空闲"/"已是 DeepSeek Harness 服务"…）。</param>
        /// <returns>Down = 没人监听；Dsh = 已是 DSH；Other = 被别的程序占用。</returns>
        /// <remarks>
        /// 最长等 2.5 秒。DSH 首页需要访问令牌，无令牌访问会拿到 401 且正文里有 "dsh web" 提示，
        /// 这种响应同样算 Dsh —— 否则会把一个正在跑的服务误判成"端口被占用"。
        /// </remarks>
        public static ProbeState Probe(int port, out string detail)
        {
            detail = "";
            string url = NormalizeUrl(port);
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 2500;
                req.ReadWriteTimeout = 2500;
                req.AllowAutoRedirect = false;
                req.UserAgent = "DSH-Launcher";
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    string body = ReadLimited(resp, 16384);
                    if (resp.StatusCode == HttpStatusCode.OK && body.IndexOf("DeepSeek Harness", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        detail = "已是 DeepSeek Harness 服务";
                        return ProbeState.Dsh;
                    }
                    detail = "端口有响应但不是 DSH（HTTP " + (int)resp.StatusCode + "）";
                    return ProbeState.Other;
                }
            }
            catch (WebException we)
            {
                if (we.Response != null)
                {
                    HttpWebResponse resp = we.Response as HttpWebResponse;
                    if (resp != null)
                    {
                        string body = ReadLimited(resp, 16384);
                        // DSH 的网页要带令牌访问，无令牌时它会明确回一句提示
                        if ((int)resp.StatusCode == 401 &&
                            body.IndexOf("dsh web", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            detail = "DSH 服务已在运行（网页需要访问令牌）";
                            return ProbeState.Dsh;
                        }
                        detail = "端口被其他程序占用（HTTP " + (int)resp.StatusCode + "）";
                        return ProbeState.Other;
                    }
                }
                detail = "端口空闲";
                return ProbeState.Down;
            }
            catch (Exception ex)
            {
                detail = "探测失败：" + ex.Message;
                return ProbeState.Down;
            }
        }

        /// <summary>带令牌访问服务首页：返回状态码（200 表示服务已能正常提供界面）。</summary>
        /// <param name="url">要访问的完整地址（通常是 DSH 打印出来的带 token 地址）。</param>
        /// <param name="body">响应正文（最多 16 KB）。</param>
        /// <returns>HTTP 状态码；连不上、超时或拿不到响应时返回 0（401 等错误码也会如实返回）。</returns>
        public static int HttpStatus(string url, out string body)
        {
            body = "";
            try
            {
                HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
                req.Method = "GET";
                req.Timeout = 3000;
                req.ReadWriteTimeout = 3000;
                req.AllowAutoRedirect = false;
                req.UserAgent = "DSH-Launcher";
                using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                {
                    body = ReadLimited(resp, 16384);
                    return (int)resp.StatusCode;
                }
            }
            catch (WebException we)
            {
                if (we.Response != null)
                {
                    HttpWebResponse resp = we.Response as HttpWebResponse;
                    if (resp != null)
                    {
                        body = ReadLimited(resp, 16384);
                        return (int)resp.StatusCode;
                    }
                }
                return 0;
            }
            catch { return 0; }
        }

        /// <summary>最多读 max 字节正文（判断"是不是 DSH"只看开头，不必整页读下来）。</summary>
        /// <returns>按 UTF-8 解码的正文；读失败返回空串。</returns>
        private static string ReadLimited(WebResponse resp, int max)
        {
            try
            {
                using (Stream s = resp.GetResponseStream())
                {
                    if (s == null) return "";
                    byte[] buf = new byte[max];
                    int total = 0;
                    int read;
                    while (total < max && (read = s.Read(buf, total, max - total)) > 0) total += read;
                    return Encoding.UTF8.GetString(buf, 0, total);
                }
            }
            catch { return ""; }
        }

        /// <summary>在后台线程里完成：校验 -> 启动 -> 等待就绪。</summary>
        /// <remarks>立即返回：真正的启动、探测与等待都在后台线程 "dsh-start" 上做，结果通过 StatusChanged 通知。</remarks>
        public void StartAsync(AppConfig cfg)
        {
            _cancel = false;
            SetStatus(ServerStatus.Starting);
            _lastError = "";
            Thread t = new Thread(delegate() { StartWorker(cfg); });
            t.IsBackground = true;
            t.Name = "dsh-start";
            t.Start();
        }

        /// <summary>
        /// 启动主流程：先探端口，再按 LaunchMode 选启动方式 —— 极速模式直接跑缓存里的 bin.js，
        /// 否则用 node + npx-cli.js；两条都不行才回落到经 cmd.exe 调 npx.cmd。最后统一等就绪。
        /// </summary>
        private void StartWorker(AppConfig cfg)
        {
            try
            {
                WebUrl = NormalizeUrl(cfg.Port);
                OpenUrl = WebUrl;

                List<string> problems = cfg.Validate();
                if (problems.Count > 0)
                {
                    Fail(problems[0]);
                    return;
                }

                Emit("检查端口 " + cfg.Port + " …", LogLevel.Dim);
                string detail;
                ProbeState probe = Probe(cfg.Port, out detail);
                if (probe == ProbeState.Dsh)
                {
                    Emit("检测到 DeepSeek Harness 已在运行（" + WebUrl + "），直接接管。", LogLevel.Good);
                    SetStatus(ServerStatus.External);
                    if (cfg.AutoOpenBrowser) OpenInBrowser(cfg, WebUrl);
                    return;
                }
                if (probe == ProbeState.Other)
                {
                    Fail("端口 " + cfg.Port + " 已被其他程序占用，请在设置里换一个端口。");
                    return;
                }

                Dictionary<string, string> env = new Dictionary<string, string>();
                string file;
                string args;

                if (cfg.LaunchMode == "direct")
                {
                    string node = DshLocator.FindNode(cfg);
                    string entry = DshLocator.FindCachedEntry(cfg);
                    if (node == null) { Fail("找不到 node.exe，请安装 Node.js 或在设置里指定路径。"); return; }
                    if (entry == null)
                    {
                        Emit("本地还没有 DSH 的缓存版本，本次自动改用 npm 方式启动。", LogLevel.Warn);
                        cfg.LaunchMode = "npx";
                    }
                    else
                    {
                        Emit("极速模式：直接启动 " + entry, LogLevel.Dim);
                        file = node;
                        args = Quote(entry) + " web --port " + cfg.Port + " --no-open";
                        Launch(file, args, cfg, env);
                        WaitReady(cfg);
                        return;
                    }
                }

                if (cfg.VerifyIntegrity)
                {
                    string registry = VerifySource(cfg);
                    if (registry == null) return;   // VerifySource 内部已报错
                    env["npm_config_registry"] = registry;
                }

                string nodeExe = DshLocator.FindNode(cfg);
                string npxCli = DshLocator.FindNpxCliJs(nodeExe);
                string package = "@deepseek-ai/dsh@" + cfg.PinnedVersion;

                if (nodeExe != null && npxCli != null)
                {
                    file = nodeExe;
                    args = Quote(npxCli) + " -y " + package + " web --port " + cfg.Port + " --no-open";
                }
                else
                {
                    string npxCmd = DshLocator.FindNpxCmd(cfg);
                    if (npxCmd == null) { Fail("找不到 npx，请安装 Node.js（含 npm）。"); return; }
                    file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                    string inner = Quote(npxCmd) + " -y " + package + " web --port " + cfg.Port + " --no-open";
                    args = "/d /s /c \"" + inner + "\"";
                }

                Emit("正在启动 DeepSeek Harness（端口 " + cfg.Port + "，工作目录 " + cfg.Workspace + "）…", LogLevel.Info);
                Launch(file, args, cfg, env);
                WaitReady(cfg);
            }
            catch (Exception ex)
            {
                Fail("启动异常：" + ex.Message);
            }
        }

        /// <summary>给路径或参数加双引号（Node 常装在带空格的目录下，不加引号会被拆成多个参数）。</summary>
        private static string Quote(string s) { return "\"" + s + "\""; }

        /// <summary>拉起服务进程并把输出接到日志上；进程对象在锁内记入 _proc，之后才算"归我们管"。</summary>
        /// <remarks>工作目录即 DSH 的工作区；全程 CreateNoWindow，不闪黑框。</remarks>
        private void Launch(string file, string args, AppConfig cfg, Dictionary<string, string> env)
        {
            ProcessStartInfo psi = new ProcessStartInfo(file, args);
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;                 // 关键：CREATE_NO_WINDOW，全程无黑窗
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;
            psi.StandardOutputEncoding = new UTF8Encoding(false);
            psi.StandardErrorEncoding = new UTF8Encoding(false);
            psi.WorkingDirectory = cfg.Workspace;      // DSH 以工作目录作为工作区
            foreach (KeyValuePair<string, string> kv in env) psi.EnvironmentVariables[kv.Key] = kv.Value;

            Process p = new Process();
            p.StartInfo = psi;
            p.EnableRaisingEvents = true;
            p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e) { OnServiceLine(e.Data, false); };
            p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e) { OnServiceLine(e.Data, true); };
            p.Exited += delegate(object s, EventArgs e) { OnServiceExited(); };

            if (!p.Start())
            {
                Fail("无法启动进程：" + file);
                return;
            }
            lock (_gate) { _proc = p; }
            p.BeginOutputReadLine();
            p.BeginErrorReadLine();
            Emit("进程已启动（PID " + p.Id + "），等待服务就绪…", LogLevel.Dim);
        }

        /// <summary>
        /// 服务进程的每一行输出：优先从里面抓 DSH 打印的带令牌地址（抓到就更新 OpenUrl）；
        /// 其余按来源分级 —— stderr 记 Warn，npm 的 warn 压成 Dim，免得刷屏。
        /// </summary>
        private void OnServiceLine(string line, bool isError)
        {
            if (line == null) return;
            string text = line.TrimEnd();
            if (text.Length == 0) return;

            Match m = _urlPattern.Match(text);
            if (m.Success)
            {
                OpenUrl = m.Groups[1].Value;
                Emit("服务地址：" + OpenUrl, LogLevel.Good);
                return;
            }

            LogLevel level = LogLevel.Info;
            if (isError) level = LogLevel.Warn;
            if (text.StartsWith("npm warn", StringComparison.OrdinalIgnoreCase)) level = LogLevel.Dim;
            Emit(text, level);
        }

        /// <summary>
        /// 进程退出回调：先摘掉 _proc（此后不再算我们拥有它）。
        /// _cancel 为真说明是 Stop() 主动停的，按"已停止"处理；仍停在 Starting 则说明是启动失败。
        /// </summary>
        private void OnServiceExited()
        {
            int code = -1;
            lock (_gate)
            {
                if (_proc != null)
                {
                    try { code = _proc.ExitCode; } catch { }
                    _proc.Dispose();
                    _proc = null;
                }
            }
            if (_cancel)
            {
                SetStatus(ServerStatus.Stopped);
                Emit("服务已停止。", LogLevel.Info);
                return;
            }
            if (Status == ServerStatus.Starting)
            {
                Fail("服务进程已退出（退出码 " + code + "），请查看日志。");
            }
            else
            {
                Emit("服务进程已退出（退出码 " + code + "）。", LogLevel.Warn);
                SetStatus(ServerStatus.Stopped);
            }
        }

        /// <summary>服务是否已经可以打开界面。优先用 dsh 打印出来的带令牌地址判断。</summary>
        /// <param name="cfg">当前配置，端口从它取（回环地址由 NormalizeUrl 拼出）。</param>
        /// <param name="how">判定依据（写进日志，便于区分是哪条通道先通过的）。</param>
        /// <returns>能打开界面即为 true。</returns>
        private bool IsReady(AppConfig cfg, out string how)
        {
            how = "";
            if (!string.IsNullOrEmpty(OpenUrl) && OpenUrl.IndexOf("token=", StringComparison.OrdinalIgnoreCase) > 0)
            {
                string body;
                int code = HttpStatus(OpenUrl, out body);
                if (code == 200)
                {
                    how = "界面地址已返回 200";
                    return true;
                }
            }
            string detail;
            if (Probe(cfg.Port, out detail) == ProbeState.Dsh)
            {
                how = "端口探测通过";
                return true;
            }
            return false;
        }

        /// <summary>
        /// 轮询等待服务就绪，最长 5 分钟（每 20 秒一条心跳，免得看起来像卡死）。
        /// 请求取消或进程提前退出都会立即返回；只有超时才判失败。
        /// </summary>
        private void WaitReady(AppConfig cfg)
        {
            DateTime started = DateTime.Now;
            DateTime deadline = started.AddSeconds(300);
            DateTime nextBeat = started.AddSeconds(20);
            while (DateTime.Now < deadline)
            {
                if (_cancel) return;
                if (!IsProcessAlive && Status == ServerStatus.Starting)
                {
                    // 进程已退出，OnServiceExited 会给出原因
                    return;
                }
                string how;
                if (IsReady(cfg, out how))
                {
                    Emit("服务已就绪（" + how + "）：" + WebUrl, LogLevel.Good);
                    SetStatus(ServerStatus.Running);
                    RaiseServing();   // 打开界面交给启动器：先播接入过渡动画
                    return;
                }
                if (DateTime.Now >= nextBeat)
                {
                    Emit("仍在启动中…（已等待 " + (int)(DateTime.Now - started).TotalSeconds + " 秒）", LogLevel.Dim);
                    nextBeat = DateTime.Now.AddSeconds(20);
                }
                Thread.Sleep(500);
            }
            Fail("等待服务就绪超时（5 分钟），请查看日志。");
        }

        /// <summary>端口上是否有程序在监听（轻量 TCP 探测，不产生 HTTP 请求）。</summary>
        /// <returns>300ms 内能连上回环端口即为 true。</returns>
        /// <remarks>只做 TCP 连接、不发 HTTP 请求 —— 它供端口巡检使用，代价必须足够低。</remarks>
        public static bool TcpAlive(int port)
        {
            try
            {
                using (System.Net.Sockets.TcpClient client = new System.Net.Sockets.TcpClient())
                {
                    IAsyncResult ar = client.BeginConnect(IPAddress.Loopback, port, null, null);
                    bool ok = ar.AsyncWaitHandle.WaitOne(300);
                    if (!ok) return false;
                    try { client.EndConnect(ar); } catch { return false; }
                    return client.Connected;
                }
            }
            catch { return false; }
        }

        /// <summary>把状态标记为"外部已运行"（服务不是本启动器拉起来的）。</summary>
        public void MarkExternal()
        {
            lock (_gate) { _proc = null; }
            SetStatus(ServerStatus.External);
        }

        /// <summary>统一的失败出口：记下原因、报日志、置为 Failed（界面据此显示红色提示）。</summary>
        private void Fail(string message)
        {
            _lastError = message;
            Emit(message, LogLevel.Bad);
            SetStatus(ServerStatus.Failed);
        }

        /// <summary>核对下载源与固定版本的完整性（沿用原有安装脚本的安全策略）。</summary>
        /// <returns>通过核验的下载源；找不到 npm 时跳过核验、直接返回配置里的源。</returns>
        /// <remarks>
        /// 失败时内部已经报过错，调用方直接 return 即可，不要重复报。
        /// 与部署期的 Deployment.SourceVerify 是同策略的两份实现，差别就在"找不到 npm"时这里选择放行。
        /// </remarks>
        private string VerifySource(AppConfig cfg)
        {
            Emit("正在核对 " + cfg.PinnedVersion + " 的下载源完整性…", LogLevel.Dim);
            string nodeExe = DshLocator.FindNode(cfg);
            string npmCli = null;
            if (nodeExe != null)
            {
                try
                {
                    string dir = Path.GetDirectoryName(nodeExe);
                    string c = Path.Combine(dir, @"node_modules\npm\bin\npm-cli.js");
                    if (File.Exists(c)) npmCli = c;
                }
                catch { }
            }

            string[] registries = new string[] { cfg.Registry, "https://registry.npmjs.org" };
            List<string> tried = new List<string>();
            foreach (string reg in registries)
            {
                if (string.IsNullOrEmpty(reg) || tried.Contains(reg)) continue;
                tried.Add(reg);

                string file;
                string args;
                if (nodeExe != null && npmCli != null)
                {
                    file = nodeExe;
                    args = Quote(npmCli) + " view @deepseek-ai/dsh@" + cfg.PinnedVersion + " version dist.integrity --json --registry=" + reg + " --fetch-timeout=8000 --fetch-retries=0";
                }
                else
                {
                    string npm = DshLocator.SearchPath("npm.cmd");
                    if (npm == null) { Emit("找不到 npm，跳过完整性核验。", LogLevel.Warn); return cfg.Registry; }
                    file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                    args = "/d /s /c \"\"" + npm + "\" view @deepseek-ai/dsh@" + cfg.PinnedVersion + " version dist.integrity --json --registry=" + reg + " --fetch-timeout=8000 --fetch-retries=0\"";
                }

                string stdout, stderr;
                int code = HiddenRunner.Run(file, args, cfg.Workspace, null, 20000, out stdout, out stderr);
                if (code != 0) { Emit("源 " + reg + " 核验未通过，换下一个源。", LogLevel.Dim); continue; }

                string version = ExtractJsonString(stdout, "version");
                string integrity = ExtractJsonString(stdout, "dist.integrity");
                if (version == cfg.PinnedVersion && integrity == cfg.PinnedIntegrity)
                {
                    Emit("完整性核验通过，使用下载源：" + reg, LogLevel.Good);
                    return reg;
                }
                Emit("源 " + reg + " 返回的版本/完整性不匹配，换下一个源。", LogLevel.Dim);
            }

            Fail("国内源和 npm 官方源都未通过固定版本的完整性核验，已停止启动。可在设置里关闭核验，或改用极速模式。");
            return null;
        }

        /// <summary>从 npm --json 输出里取字段（避免额外依赖，做个够用的解析）。</summary>
        /// <returns>字符串值去掉引号后返回，标量原样返回；键不存在时返回 null。</returns>
        private static string ExtractJsonString(string json, string key)
        {
            if (string.IsNullOrEmpty(json)) return null;
            int at = json.IndexOf("\"" + key + "\"", StringComparison.Ordinal);
            if (at < 0) return null;
            int colon = json.IndexOf(':', at);
            if (colon < 0) return null;
            int i = colon + 1;
            while (i < json.Length && char.IsWhiteSpace(json[i])) i++;
            if (i >= json.Length) return null;
            if (json[i] == '"')
            {
                i++;
                StringBuilder sb = new StringBuilder();
                while (i < json.Length)
                {
                    char c = json[i];
                    if (c == '\\' && i + 1 < json.Length) { sb.Append(json[i + 1]); i += 2; continue; }
                    if (c == '"') break;
                    sb.Append(c);
                    i++;
                }
                return sb.ToString();
            }
            int start = i;
            while (i < json.Length && json[i] != ',' && json[i] != '}' && !char.IsWhiteSpace(json[i])) i++;
            return json.Substring(start, i - start);
        }

        /// <summary>打开 Web 界面：开了 Edge 应用模式就尽量用它（像原生窗口、没有地址栏），否则回落系统默认浏览器。</summary>
        /// <remarks>失败只写日志、不弹窗 —— 这个动作可能是服务就绪后自动触发的，不能打断用户。</remarks>
        public static void OpenInBrowser(AppConfig cfg, string url)
        {
            try
            {
                if (cfg.EdgeAppMode)
                {
                    string edge = FindEdge();
                    if (edge != null)
                    {
                        ProcessStartInfo psi = new ProcessStartInfo(edge, "--app=" + url);
                        psi.UseShellExecute = false;
                        Process.Start(psi);
                        return;
                    }
                }
                ProcessStartInfo shell = new ProcessStartInfo(url);
                shell.UseShellExecute = true;
                Process.Start(shell);
            }
            catch (Exception ex)
            {
                FileLog.Write("打开浏览器失败：" + ex.Message);
            }
        }

        /// <summary>定位 msedge.exe（三个常见位置：Program Files (x86)、Program Files、用户级安装）。</summary>
        /// <returns>找不到返回 null（调用方回落到系统默认浏览器）。</returns>
        private static string FindEdge()
        {
            string[] candidates = new string[]
            {
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), @"Microsoft\Edge\Application\msedge.exe"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Microsoft\Edge\Application\msedge.exe")
            };
            foreach (string c in candidates) { try { if (File.Exists(c)) return c; } catch { } }
            return null;
        }

        /// <summary>停止服务（连同其子进程一起结束，避免残留）。</summary>
        /// <remarks>
        /// 先用 taskkill /T /F 结束整棵进程树（npx 还会再起子进程，只杀父进程会留残留），
        /// 再等进程消失，最后**复验端口**：端口没释放就绝不宣告"已停止"。
        /// </remarks>
        public void Stop()
        {
            int pid;
            lock (_gate)
            {
                if (_proc == null) { SetStatus(ServerStatus.Stopped); return; }
                try { pid = _proc.Id; }
                catch { _proc = null; SetStatus(ServerStatus.Stopped); return; }
            }
            _cancel = true;
            SetStatus(ServerStatus.Stopping);
            Emit("正在停止服务（PID " + pid + "）…", LogLevel.Info);

            string stdout, stderr;
            string taskkill = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe");
            HiddenRunner.Run(taskkill, "/PID " + pid + " /T /F", null, null, 15000, out stdout, out stderr);

            for (int i = 0; i < 40 && IsProcessAlive; i++) Thread.Sleep(100);

            lock (_gate)
            {
                if (_proc != null && !IsProcessAlive)
                {
                    try { _proc.Dispose(); } catch { }
                    _proc = null;
                }
            }
            if (IsProcessAlive)
            {
                Emit("进程未能正常结束，请手动检查任务管理器。", LogLevel.Warn);
                return;                       // 进程还在，绝不宣告"已停止"
            }

            // ⚠️ **端口复验**：taskkill 的退出码没看，"进程没了"也不等于端口真的释放。
            //    以前这里无条件宣告"服务已停止"并置成 Stopped —— 用户据此以为可以安全地动数据目录了。
            //    现在只有端口确实空闲才敢这么说；否则如实告警，状态宁可停在"运行中"也不撒谎。
            int stopPort = PortFromWebUrl();
            bool portFree = stopPort <= 0 || ProcessKiller.PortFree(stopPort);
            for (int i = 0; i < 20 && !portFree; i++) { Thread.Sleep(100); portFree = ProcessKiller.PortFree(stopPort); }

            if (portFree)
            {
                Emit("服务已停止。", LogLevel.Good);
                SetStatus(ServerStatus.Stopped);
            }
            else
            {
                Emit("进程已终止，但端口 " + stopPort + " 仍被占用：可能有残留进程，先别动数据目录。", LogLevel.Warn);
                SetStatus(ServerStatus.Running);
            }
        }

        /// <summary>从 WebUrl 里取端口（DshServer 不单独存端口，启动时把规范地址存进 WebUrl）。</summary>
        private int PortFromWebUrl()
        {
            try
            {
                string u = WebUrl == null ? "" : WebUrl;
                int colon = u.LastIndexOf(':');
                if (colon <= 0) return 0;
                int p;
                return int.TryParse(u.Substring(colon + 1).TrimEnd('/'), out p) ? p : 0;
            }
            catch { return 0; }
        }

        /// <summary>
        /// 本启动器**自己拉起的** DSH 服务进程 PID（0 = 不是我们拉起的，或已停止）。
        /// 「清扫残留」用它把自己正在跑的服务排除在外 —— 否则一键全杀会把自己的服务杀掉。
        /// </summary>
        public int OwnPid
        {
            get
            {
                lock (_gate)
                {
                    try { return _proc != null && !_proc.HasExited ? _proc.Id : 0; }
                    catch { return 0; }
                }
            }
        }
        /// <summary>不拥有进程时（外部启动），仅重置界面状态。</summary>
        public void Detach()
        {
            lock (_gate) { _proc = null; }
            SetStatus(ServerStatus.Stopped);
        }
    }
}
