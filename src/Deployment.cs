using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;

namespace DshLauncher
{
    /// <summary>部署步骤状态。</summary>
    internal enum StepState { Pending, Running, Done, Failed, Skipped }

    internal class DeployStep
    {
        public string Title = "";
        public string Detail = "";
        public StepState State = StepState.Pending;
    }

    /// <summary>环境体检结果。</summary>
    internal class DeployReport
    {
        public bool NodeReady;        // node + npm + npx 齐全且版本达标
        public bool NodePresent;      // 有 node，但版本可能不达标
        public bool NpmReady;
        public bool NpxReady;
        public bool DshCached;        // 本地已有 DSH 缓存（= 部署过）
        public bool HomePresent;      // 存在 %USERPROFILE%\.dsh
        public string NodeVersion = "";
        public string NodePath = "";
        public string DshEntry = "";
        public string Workspace = "";
        public bool WorkspaceExists;

        public bool NeedsDeploy;      // 结论：需要进入自动部署
        public string Headline = "";
        public string Summary = "";
        public readonly List<string> Missing = new List<string>();

        // ---- 部署第 1 步的系统兼容性判定（对齐旧脚本 Test-WindowsCompatibility）----
        public bool SysSupported = true;
        public string SysProblem = "";
        public string SysWarning = "";
        // ---- 本地缓存里那份 DSH 的版本（固定版本模式下用来发现版本错配）----
        public string CachedVersion = "";
    }

    /// <summary>
    /// 部署检测：本机是否已经装好可用的 DSH。
    /// 判定标准对齐安装脚本 Test-NodeReady（node+npm+npx 且 22.19+ / 24+）与 DSH 缓存入口。
    /// </summary>
    internal static class Deployment
    {
        public static DeployReport Check(AppConfig cfg)
        {
            DeployReport r = new DeployReport();
            r.Workspace = cfg == null ? "" : cfg.Workspace;
            try { r.WorkspaceExists = !string.IsNullOrEmpty(r.Workspace) && Directory.Exists(r.Workspace); }
            catch { }

            // Node / npm / npx
            r.NodePath = DshLocator.FindNode(cfg);
            if (r.NodePath != null)
            {
                r.NodePresent = true;
                r.NodeVersion = Query(r.NodePath, "--version");
                if (r.NodeVersion.Length == 0) r.NodeVersion = Query(r.NodePath, "-v");
                r.NodeReady = NodeVersionOk(r.NodeVersion);
            }
            r.NpmReady = DshLocator.SearchPath("npm.cmd") != null || DshLocator.SearchPath("npm") != null;
            r.NpxReady = DshLocator.FindNpxCmd(cfg) != null || DshLocator.FindNpxCliJs(r.NodePath) != null;

            // DSH 本体
            r.DshEntry = DshLocator.FindCachedEntry(cfg);
            r.DshCached = !string.IsNullOrEmpty(r.DshEntry);
            try
            {
                string home = Environment.GetEnvironmentVariable("DSH_HOME");
                if (string.IsNullOrEmpty(home))
                    home = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
                r.HomePresent = Directory.Exists(home);
            }
            catch { }

            try { SysCheck.Supported(out r.SysProblem, out r.SysWarning); r.SysSupported = string.IsNullOrEmpty(r.SysProblem); }
            catch { }

            if (!r.NodePresent) r.Missing.Add("未安装 Node.js");
            else if (!r.NodeReady) r.Missing.Add("Node.js 版本过低（需要 22.19+ 或 24+，当前 " + r.NodeVersion + "）");
            if (r.NodeReady && !r.NpmReady) r.Missing.Add("缺少 npm");
            if (r.NodeReady && !r.NpxReady) r.Missing.Add("缺少 npx");
            if (!r.DshCached) r.Missing.Add("本地没有 DSH（@deepseek-ai/dsh）");
            if (r.DshCached)
            {
                // 固定版本模式下，缓存里那份必须就是目标版本；否则会误判成「无需部署」而跳过一键部署
                try { r.CachedVersion = VersionResolve.LocalCachedVersion(cfg) ?? ""; }
                catch { }
                if (string.Equals(cfg.VersionMode, "pinned", StringComparison.OrdinalIgnoreCase) &&
                    r.CachedVersion.Length > 0 &&
                    !string.Equals(r.CachedVersion, cfg.PinnedVersion, StringComparison.OrdinalIgnoreCase))
                    r.Missing.Add("本地 DSH 版本 " + r.CachedVersion + " 与固定版本 " + cfg.PinnedVersion + " 不一致");
            }
            if (!r.SysSupported) r.Missing.Add(r.SysProblem);

            r.NeedsDeploy = r.Missing.Count > 0;
            // 排障/演示用：DSH_LAUNCHER_FORCE_DEPLOY=1 时强制走一遍部署向导
            try
            {
                if (Environment.GetEnvironmentVariable("DSH_LAUNCHER_FORCE_DEPLOY") == "1")
                {
                    r.NeedsDeploy = true;
                    r.Missing.Add("（已强制进入部署向导）");
                }
            }
            catch { }

            if (!r.SysSupported)
            {
                r.Headline = "系统不受支持";
                r.Summary = r.SysProblem;
            }
            else if (!r.NeedsDeploy)
            {
                r.Headline = "环境就绪";
                r.Summary = "已检测到 Node.js " + r.NodeVersion + " 与本地 DSH，无需部署。";
            }
            else if (!r.NodeReady)
            {
                r.Headline = "需要部署运行环境";
                r.Summary = "本机未检测到可用的 Node.js，需要先自动安装。";
            }
            else
            {
                r.Headline = "需要部署 DSH";
                r.Summary = "Node.js 已就绪，但本地还没有 DeepSeek Harness。";
            }
            return r;
        }

        /// <summary>安装脚本的版本门槛：22.19+ 或 24+。</summary>
        public static bool NodeVersionOk(string raw)
        {
            try
            {
                string v = (raw ?? "").Trim().TrimStart('v', 'V');
                if (v.Length == 0) return false;
                string[] parts = v.Split('.');
                int major = int.Parse(parts[0], CultureInfo.InvariantCulture);
                int minor = parts.Length > 1 ? int.Parse(parts[1], CultureInfo.InvariantCulture) : 0;
                if (major >= 24) return true;
                return major == 22 && minor >= 19;
            }
            catch { return false; }
        }

        private static string Query(string exe, string args)
        {
            try
            {
                string stdout, stderr;
                if (HiddenRunner.Run(exe, args, null, null, 8000, out stdout, out stderr) != 0) return "";
                return (stdout + stderr).Trim();
            }
            catch { return ""; }
        }
    }

    /// <summary>
    /// 自动部署引擎：按安装脚本的顺序执行
    /// 环境检测 → 安装 Node.js（官方 MSI 校验 SHA-256 → npmmirror 镜像 → winget）→
    /// 准备工作目录 → 核验 DSH 版本与完整性 → 拉取到本地缓存 → 建快捷方式。
    /// </summary>
    internal class Deployer
    {
        private readonly List<DeployStep> _steps = new List<DeployStep>();
        private readonly object _gate = new object();
        private Thread _thread;
        private volatile bool _cancel;
        private volatile bool _running;

        public event LogHandler Log;
        public event EventHandler Changed;

        public Deployer()
        {
            Add("检查系统兼容性", "Windows 10/11 64 位");
            Add("检查运行环境", "node / npm / npx 与版本门槛");
            Add("安装 Node.js LTS", "官方 MSI（校验 SHA-256）→ npmmirror → winget");
            Add("解析版本与完整性", "取官方最新可安装版（或固定版）+ integrity");
            Add("准备工作目录", "创建 DSH 工作区");
            Add("拉取 DSH 到本地缓存", "npx 预下载（带实时进度）");
            Add("创建快捷方式", "桌面与开始菜单");
        }

        /// <summary>供安装器等外部步骤上报日志（事件本身不能在类外触发）。</summary>
        public void Report(string message, LogLevel level) { Emit(message, level); }

        public bool Running { get { return _running; } }
        public bool CancelRequested { get { return _cancel; } }

        public List<DeployStep> Steps { get { lock (_gate) { return new List<DeployStep>(_steps); } } }

        public string CurrentDetail { get { return _detail; } }
        private volatile string _detail = "";

        public double Progress
        {
            get
            {
                lock (_gate)
                {
                    int done = 0;
                    foreach (DeployStep s in _steps)
                    {
                        if (s.State == StepState.Done || s.State == StepState.Skipped) done++;
                    }
                    return (double)done / Math.Max(1, _steps.Count);
                }
            }
        }

        public bool Finished { get { return _finished; } }
        public bool Succeeded { get { return _succeeded; } }
        private volatile bool _finished;
        private volatile bool _succeeded;

        private void Add(string title, string detail)
        {
            DeployStep s = new DeployStep();
            s.Title = title;
            s.Detail = detail;
            _steps.Add(s);
        }

        private void SetStep(int index, StepState state, string detail)
        {
            lock (_gate)
            {
                if (index < 0 || index >= _steps.Count) return;
                _steps[index].State = state;
                if (!string.IsNullOrEmpty(detail)) _steps[index].Detail = detail;
                if (state == StepState.Running) _detail = _steps[index].Title + " …";
            }
            Raise();
        }

        private void Emit(string message, LogLevel level)
        {
            _detail = message;
            LogHandler h = Log;
            if (h != null) { try { h(message, level); } catch { } }
            Raise();
        }

        private void Raise()
        {
            EventHandler h = Changed;
            if (h != null) { try { h(this, EventArgs.Empty); } catch { } }
        }

        public void Start(AppConfig cfg)
        {
            if (_running) return;
            _cancel = false;
            _finished = false;
            _succeeded = false;
            lock (_gate) { foreach (DeployStep s in _steps) { s.State = StepState.Pending; } }
            _running = true;
            _thread = new Thread(delegate() { Run(cfg); });
            _thread.IsBackground = true;
            _thread.Name = "dsh-deploy";
            _thread.Start();
        }

        public void Cancel() { _cancel = true; }

        private void Run(AppConfig cfg)
        {
            try
            {
                bool ok = StepSystem(cfg) && StepEnvironment(cfg) && StepNode(cfg)
                          && StepVersion(cfg) && StepWorkspace(cfg) && StepFetch(cfg) && StepShortcuts(cfg);
                _succeeded = ok && !_cancel;
            }
            catch (Exception ex)
            {
                Emit("部署异常：" + ex.Message, LogLevel.Bad);
                _succeeded = false;
            }
            finally
            {
                _running = false;
                _finished = true;
                Emit(_succeeded ? "部署完成。" : (_cancel ? "部署已取消。" : "部署未完成。"),
                     _succeeded ? LogLevel.Good : LogLevel.Warn);
            }
        }

        // ---- 1. 系统兼容性 ----
        private bool StepSystem(AppConfig cfg)
        {
            SetStep(0, StepState.Running, null);
            string problem, warning;
            bool ok = SysCheck.Supported(out problem, out warning);
            if (!ok)
            {
                SetStep(0, StepState.Failed, problem);
                Emit("系统不受支持，已中止部署：" + problem, LogLevel.Bad);
                return false;
            }
            SetStep(0, StepState.Done, SysCheck.Describe());
            if (!string.IsNullOrEmpty(warning)) Emit(warning, LogLevel.Warn);
            return true;
        }

        // ---- 2. 环境检测 ----
        private bool StepEnvironment(AppConfig cfg)
        {
            SetStep(1, StepState.Running, null);
            DeployReport r = Deployment.Check(cfg);
            Emit("Node.js：" + (r.NodePresent ? r.NodeVersion + "（" + r.NodePath + "）" : "未找到"), LogLevel.Dim);
            Emit("npm：" + (r.NpmReady ? "已找到" : "未找到") + "   npx：" + (r.NpxReady ? "已找到" : "未找到"), LogLevel.Dim);
            if (r.NodeReady && r.NpmReady && r.NpxReady)
            {
                SetStep(1, StepState.Done, "Node.js " + r.NodeVersion + " 可用");
                return true;
            }
            SetStep(1, StepState.Done, r.NodePresent ? "Node.js " + r.NodeVersion + " 不达标" : "未找到 Node.js");
            return true;
        }

        // ---- 3. Node.js 安装 ----
        private bool StepNode(AppConfig cfg)
        {
            DeployReport r = Deployment.Check(cfg);
            if (r.NodeReady && r.NpmReady)
            {
                SetStep(2, StepState.Skipped, "已就绪，跳过安装");
                return true;
            }

            SetStep(2, StepState.Running, null);
            if (!NodeInstaller.InstallOfficial(cfg, this)) Emit("Node.js 官方源未完成，改用 npmmirror 镜像。", LogLevel.Warn);
            NodeRefreshPath();
            if (!NodeInstaller.IsReady(cfg)) { if (!NodeInstaller.InstallMirror(cfg, this)) Emit("npmmirror 镜像未完成，改用 winget。", LogLevel.Warn); }
            NodeRefreshPath();
            if (!NodeInstaller.IsReady(cfg)) { if (!NodeInstaller.InstallWinget(this)) Emit("winget 未完成。", LogLevel.Warn); }
            NodeRefreshPath();

            if (NodeInstaller.IsReady(cfg))
            {
                SetStep(2, StepState.Done, "Node.js 安装完成");
                return true;
            }
            SetStep(2, StepState.Failed, "需要手动安装 Node.js LTS");
            Emit("三个安装渠道都未成功，请手动安装 Node.js LTS 后重试：https://nodejs.org/en/download", LogLevel.Bad);
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("https://nodejs.org/en/download");
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch { }
            return false;
        }

        /// <summary>按安装脚本刷新 PATH：补上常见 Node 安装目录。</summary>
        private static void NodeRefreshPath()
        {
            try
            {
                string[] candidates = new string[]
                {
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs"),
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\nodejs")
                };
                string path = Environment.GetEnvironmentVariable("PATH") ?? "";
                foreach (string dir in candidates)
                {
                    if (!Directory.Exists(dir)) continue;
                    if (path.IndexOf(dir, StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    path = dir + ";" + path;
                }
                Environment.SetEnvironmentVariable("PATH", path);
            }
            catch { }
        }

        // ---- 5. 工作目录 ----
        private bool StepWorkspace(AppConfig cfg)
        {
            SetStep(4, StepState.Running, null);
            try
            {
                if (string.IsNullOrEmpty(cfg.Workspace))
                {
                    SetStep(4, StepState.Failed, "工作目录未设置");
                    return false;
                }
                if (!Directory.Exists(cfg.Workspace))
                {
                    Directory.CreateDirectory(cfg.Workspace);
                    Emit("已创建工作目录：" + cfg.Workspace, LogLevel.Good);
                }
                else Emit("工作目录已存在：" + cfg.Workspace, LogLevel.Dim);
                SetStep(4, StepState.Done, cfg.Workspace);
                return true;
            }
            catch (Exception ex)
            {
                SetStep(4, StepState.Failed, ex.Message);
                Emit("创建工作目录失败：" + ex.Message, LogLevel.Bad);
                return false;
            }
        }

        // ---- 4. 版本与完整性 ----
        private DshPackage _pkg;
        private string _registry;

        private bool StepVersion(AppConfig cfg)
        {
            SetStep(3, StepState.Running, null);
            DshPackage p = VersionResolve.Resolve(cfg, Emit);
            if (p == null || string.IsNullOrEmpty(p.Version))
            {
                SetStep(3, StepState.Failed, "无法确定要安装的版本");
                return false;
            }
            _pkg = p;
            _registry = p.Registry;

            Emit("目标版本：" + p.Version + "（" + p.Source + "）" +
                 (p.LocalVersion.Length > 0 ? "；本地已有 " + p.LocalVersion : ""), LogLevel.Dim);
            if (p.Downgrade)
                Emit("本地缓存的 " + p.LocalVersion + " 比目标 " + p.Version + " 新，本次不覆盖（防降级）。", LogLevel.Warn);

            if (cfg.VerifyIntegrity && p.Integrity.Length == 0)
            {
                SetStep(3, StepState.Failed, "该下载源未返回 integrity");
                Emit("下载源没有返回 integrity，出于安全考虑不再继续。可在设置里关闭完整性核验后重试。", LogLevel.Bad);
                return false;
            }
            SetStep(3, StepState.Done, p.Version + " · " + p.Source + " · " + p.Registry.Replace("https://", ""));

            // 把解析结果写回配置：否则会出现"部署时装最新版、启动时仍按旧 pin 启动"的两头不一致
            if (!p.Downgrade && (cfg.PinnedVersion != p.Version || cfg.PinnedIntegrity != p.Integrity))
            {
                cfg.PinnedVersion = p.Version;
                cfg.PinnedIntegrity = p.Integrity;
                cfg.Registry = p.Registry;
                try { cfg.Save(); } catch { }
                Emit("已把 " + p.Version + " 记为当前版本，之后启动都用它。", LogLevel.Dim);
            }
            return true;
        }

        // ---- 6. 预下载（带实时进度）----
        private bool StepFetch(AppConfig cfg)
        {
            SetStep(5, StepState.Running, null);
            AppConfig probe = Clone(cfg);
            if (!string.IsNullOrEmpty(_registry)) probe.Registry = _registry;
            probe.LaunchMode = "npx";

            string targetVersion = (_pkg != null && _pkg.Version.Length > 0) ? _pkg.Version : cfg.PinnedVersion;
            // 防降级：目标比本地旧时，改为就地校验本地那份，而不是把一个旧版本拉下来
            if (_pkg != null && _pkg.Downgrade && _pkg.LocalVersion.Length > 0)
            {
                Emit("目标版本 " + targetVersion + " 比本地 " + _pkg.LocalVersion + " 旧，改为校验本地这一份。", LogLevel.Warn);
                targetVersion = _pkg.LocalVersion;
            }

            string node = DshLocator.FindNode(probe);
            string npxCli = DshLocator.FindNpxCliJs(node);
            if (node == null || npxCli == null)
            {
                SetStep(5, StepState.Failed, "缺少 node / npx");
                return false;
            }

            Dictionary<string, string> env = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(_registry)) env["npm_config_registry"] = _registry;

            string args = "\"" + npxCli + "\" -y @deepseek-ai/dsh@" + targetVersion + " --version";
            Emit("正在把 DSH " + targetVersion + " 拉取到本地缓存（首次可能较慢）…", LogLevel.Info);

            // 真实进度：旧脚本同款 —— 每秒报 npm 缓存增量与近似写入速度，不编造百分比
            string cacheRoot = NpmCacheProbe.CacheRoot(probe);
            long baseBytes = NpmCacheProbe.SizeBytes(cacheRoot);
            bool stopTicker = false;
            Thread ticker = new Thread(delegate()
            {
                long prev = baseBytes;
                DateTime prevAt = DateTime.Now;
                DateTime t0 = DateTime.Now;
                while (!stopTicker)
                {
                    Thread.Sleep(1000);
                    if (stopTicker) break;
                    long now;
                    try { now = NpmCacheProbe.SizeBytes(cacheRoot); }
                    catch { now = prev; }
                    double span = Math.Max(0.1, (DateTime.Now - prevAt).TotalSeconds);
                    double speed = Math.Max(0d, (double)(now - prev) / 1048576d) / span;
                    double growth = Math.Max(0d, (double)(now - baseBytes) / 1048576d);
                    int el = (int)(DateTime.Now - t0).TotalSeconds;
                    Emit("已等待 " + (el / 60).ToString("00") + ":" + (el % 60).ToString("00") +
                         "｜npm 缓存增加 " + growth.ToString("0.0") + " MB" +
                         "｜近似写入 " + speed.ToString("0.0") + " MB/s（不是整机网速）", LogLevel.Dim);
                    prev = now;
                    prevAt = DateTime.Now;
                }
            });
            ticker.IsBackground = true;
            ticker.Start();

            DateTime started = DateTime.Now;
            string stdout, stderr;
            int code;
            try { code = HiddenRunner.Run(node, args, cfg.Workspace, env, 600000, out stdout, out stderr); }
            finally { stopTicker = true; }

            if (_cancel)
            {
                SetStep(5, StepState.Skipped, "已取消");
                return false;
            }
            string text = (stdout + " " + stderr).Trim();
            if (code != 0)
            {
                SetStep(5, StepState.Failed, text.Length > 0 ? Short(text) : ("退出码 " + code));
                Emit("拉取失败：" + Short(text), LogLevel.Bad);
                return false;
            }

            // 落地校验：命令成功了不算数，缓存里必须真的有一份可用的 DSH
            AppConfig scan = new AppConfig();
            scan.DshEntry = "";
            string cached = VersionResolve.LocalCachedVersion(scan);
            if (string.IsNullOrEmpty(cached))
            {
                SetStep(5, StepState.Failed, "拉取后仍找不到本地缓存");
                Emit("npx 报告成功，但本地缓存里找不到 DSH —— 部署没有真正完成。", LogLevel.Bad);
                return false;
            }

            int secs = (int)(DateTime.Now - started).TotalSeconds;
            Emit("已缓存 DSH " + cached + "（耗时 " + secs + " 秒）：" + Short(text), LogLevel.Good);
            SetStep(5, StepState.Done, "已缓存 " + cached + " · " + secs + " 秒");
            return true;
        }

        // ---- 7. 快捷方式 ----
        private bool StepShortcuts(AppConfig cfg)
        {
            SetStep(6, StepState.Running, null);
            bool[] ok;
            try { ok = Shortcuts.CreateAppLinks(AppPaths.ExePath, AppPaths.InstallDir); }
            catch (Exception ex)
            {
                SetStep(6, StepState.Failed, ex.Message);
                Emit("创建快捷方式失败：" + ex.Message, LogLevel.Bad);
                return false;      // 不再"失败也报成功"
            }

            bool a = ok[0], b = ok[1];
            if (a && b)
            {
                SetStep(6, StepState.Done, "桌面与开始菜单快捷方式已就绪");
                return true;
            }
            SetStep(6, StepState.Failed, a ? "桌面已就绪，开始菜单失败" : (b ? "开始菜单已就绪，桌面失败" : "两处都失败"));
            Emit("快捷方式没有全部创建成功（桌面=" + (a ? "OK" : "失败") + "，开始菜单=" + (b ? "OK" : "失败") +
                 "）。可在设置里点「创建 / 修复快捷方式」重试。", LogLevel.Warn);
            return false;
        }

        private static string Short(string s)
        {
            s = (s ?? "").Replace("\r", " ").Replace("\n", " ").Trim();
            return s.Length <= 120 ? s : s.Substring(0, 120) + "…";
        }

        private static AppConfig Clone(AppConfig src)
        {
            AppConfig c = new AppConfig();
            c.Workspace = src.Workspace; c.Port = src.Port; c.LaunchMode = src.LaunchMode;
            c.VerifyIntegrity = src.VerifyIntegrity; c.PinnedVersion = src.PinnedVersion;
            c.PinnedIntegrity = src.PinnedIntegrity; c.Registry = src.Registry;
            c.AutoOpenBrowser = src.AutoOpenBrowser; c.EdgeAppMode = src.EdgeAppMode;
            c.CloseToTray = src.CloseToTray; c.AutoStart = src.AutoStart;
            c.NodePath = src.NodePath; c.DshEntry = src.DshEntry;
            c.VersionMode = src.VersionMode; c.CleanupDir = src.CleanupDir;
            return c;
        }
    }

    /// <summary>
    /// Node.js 安装器：官方 MSI（校验 SHA-256）→ npmmirror 镜像 → winget。
    /// 与安装脚本的三级降级链保持一致。
    /// </summary>
    internal static class NodeInstaller
    {
        private const string OfficialIndex = "https://nodejs.org/dist/index.json";
        private const string MirrorIndex = "https://registry.npmmirror.com/-/binary/node/index.json";

        /// <summary>Node 是否就绪。注意必须带上 cfg —— 用户可能把 node 装在非 PATH 的自定义位置。</summary>
        public static bool IsReady(AppConfig cfg)
        {
            if (cfg == null) cfg = new AppConfig();
            string node = DshLocator.FindNode(cfg);
            if (node == null) return false;
            DeployReport r = Deployment.Check(cfg);
            return r.NodeReady && r.NpmReady;
        }

        public static bool InstallOfficial(AppConfig cfg, Deployer d)
        {
            return InstallFrom(cfg, d, OfficialIndex, "nodejs.org", new string[] { "https://nodejs.org/dist/" });
        }

        public static bool InstallMirror(AppConfig cfg, Deployer d)
        {
            // 镜像下 MSI；校验值**优先取镜像自己那份 SHASUMS256**（实测与官方逐字节一致），
            // 取不到才回落官方 —— 否则 nodejs.org 不可达时这条降级链等于不存在。
            return InstallFrom(cfg, d, MirrorIndex, "npmmirror", new string[]
            {
                "https://registry.npmmirror.com/-/binary/node/",
                "https://nodejs.org/dist/"
            });
        }

        private static bool InstallFrom(AppConfig cfg, Deployer d, string indexUrl, string sourceName, string[] checksumBases)
        {
            try
            {
                d.Report("正在从 " + sourceName + " 查询 Node.js LTS 版本…", LogLevel.Info);
                string index = NetFetch.GetString(cfg, indexUrl, 20000);
                string version = PickLtsVersion(index);
                if (version == null)
                {
                    d.Report(sourceName + " 未返回可用的 LTS 版本。", LogLevel.Warn);
                    return false;
                }
                string file = "node-" + version + "-x64.msi";
                string baseUrl = indexUrl.StartsWith("https://registry.npmmirror.com")
                    ? "https://registry.npmmirror.com/-/binary/node/" + version + "/"
                    : "https://nodejs.org/dist/" + version + "/";

                string msi = Path.Combine(Path.GetTempPath(), file);
                try { if (File.Exists(msi)) File.Delete(msi); } catch { }
                d.Report("正在下载 Node.js " + version + "（约 30 MB）…", LogLevel.Info);
                if (!NetFetch.Download(cfg, baseUrl + file, msi, d.Report, 300000))
                {
                    d.Report(sourceName + " 的安装包下载失败（.NET 与 node 两条路径都没成功）。", LogLevel.Warn);
                    return false;
                }

                d.Report("正在校验安装包 SHA-256…", LogLevel.Dim);
                string sums = null;
                foreach (string cb in checksumBases)
                {
                    try { sums = NetFetch.GetString(cfg, cb + version + "/SHASUMS256.txt", 30000); }
                    catch { sums = null; }
                    if (!string.IsNullOrEmpty(sums)) break;
                }
                string expected = FindChecksum(sums, file);
                if (expected == null) { d.Report("未找到校验值（镜像与官方都没取到）。", LogLevel.Warn); return false; }
                string actual = Sha256(msi);
                if (!string.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                {
                    d.Report("SHA-256 校验不一致，已放弃该安装包。", LogLevel.Bad);
                    try { File.Delete(msi); } catch { }
                    return false;
                }
                d.Report("安装包校验通过，正在静默安装（可能弹出管理员授权）…", LogLevel.Good);
                return RunInstaller(msi, d);
            }
            catch (Exception ex)
            {
                d.Report(sourceName + " 安装失败：" + ex.Message, LogLevel.Warn);
                return false;
            }
        }

        public static bool InstallWinget(Deployer d)
        {
            try
            {
                string winget = DshLocator.SearchPath("winget.exe");
                if (winget == null) { d.Report("未找到 winget。", LogLevel.Warn); return false; }
                d.Report("正在通过 winget 安装 Node.js LTS（可能弹出管理员授权）…", LogLevel.Info);
                ProcessStartInfo psi = new ProcessStartInfo(winget,
                    "install --id OpenJS.NodeJS.LTS --exact --source winget --accept-package-agreements --accept-source-agreements --silent");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(600000)) { d.Report("winget 安装超时。", LogLevel.Warn); return false; }
                    return p.ExitCode == 0;
                }
            }
            catch (Exception ex)
            {
                d.Report("winget 安装失败：" + ex.Message, LogLevel.Warn);
                return false;
            }
        }

        private static bool RunInstaller(string msiPath, Deployer d)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("msiexec.exe",
                    "/i \"" + msiPath + "\" /passive /norestart");
                psi.UseShellExecute = true;
                psi.Verb = "runas";                 // 触发 UAC
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(900000)) { d.Report("安装超时。", LogLevel.Warn); return false; }
                    int[] okCodes = new int[] { 0, 1641, 3010 };
                    bool ok = Array.IndexOf(okCodes, p.ExitCode) >= 0;
                    if (!ok) d.Report("msiexec 退出码 " + p.ExitCode, LogLevel.Warn);
                    return ok;
                }
            }
            catch (Exception ex)
            {
                d.Report("调用安装程序失败：" + ex.Message, LogLevel.Warn);
                return false;
            }
        }

        /// <summary>从 Node 官方 index.json 里挑最新的 LTS（要求 major ≥ 24 且有 win-x64-msi）。</summary>
        public static string PickLtsVersion(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            string best = null;
            int bestMajor = 0, bestMinor = 0, bestPatch = 0;
            foreach (Match m in Regex.Matches(json, "\\{[^{}]*\"version\"\\s*:\\s*\"v(\\d+)\\.(\\d+)\\.(\\d+)\"[^{}]*\\}"))
            {
                string chunk = m.Value;
                if (chunk.IndexOf("\"lts\":false", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (chunk.IndexOf("win-x64-msi", StringComparison.OrdinalIgnoreCase) < 0) continue;
                int major = int.Parse(m.Groups[1].Value), minor = int.Parse(m.Groups[2].Value), patch = int.Parse(m.Groups[3].Value);
                if (major < 24) continue;
                if (major > bestMajor || (major == bestMajor && (minor > bestMinor || (minor == bestMinor && patch > bestPatch))))
                {
                    bestMajor = major; bestMinor = minor; bestPatch = patch;
                    best = "v" + major + "." + minor + "." + patch;
                }
            }
            return best;
        }

        public static string FindChecksum(string shasums, string fileName)
        {
            if (string.IsNullOrEmpty(shasums)) return null;
            foreach (string line in shasums.Split('\n'))
            {
                string t = line.Trim();
                if (t.EndsWith(fileName, StringComparison.OrdinalIgnoreCase))
                {
                    int sp = t.IndexOf(' ');
                    if (sp > 0) return t.Substring(0, sp).Trim();
                }
            }
            return null;
        }

        private static string HttpGetString(string url, int timeoutMs)
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

        private static void DownloadFile(string url, string dest, Deployer d)
        {
            using (WebClient wc = new WebClient())
            {
                wc.Headers.Add("User-Agent", "DSH-Launcher");
                DateTime last = DateTime.MinValue;
                wc.DownloadProgressChanged += delegate(object s, DownloadProgressChangedEventArgs e)
                {
                    if ((DateTime.Now - last).TotalMilliseconds < 400) return;
                    last = DateTime.Now;
                    d.Report("下载中 " + (e.BytesReceived / 1048576.0).ToString("0.0") + " MB"
                          + (e.TotalBytesToReceive > 0 ? " / " + (e.TotalBytesToReceive / 1048576.0).ToString("0.0") + " MB" : ""), LogLevel.Dim);
                };
                wc.DownloadFile(url, dest);
            }
        }

        private static string Sha256(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
            {
                byte[] hash = sha.ComputeHash(fs);
                StringBuilder sb = new StringBuilder();
                foreach (byte b in hash) sb.Append(b.ToString("X2"));
                return sb.ToString();
            }
        }
    }

    /// <summary>下载源核验：npm view 指定版本并比对固定 integrity（安装脚本同款做法）。</summary>
    internal static class SourceVerify
    {
        public static bool Resolve(AppConfig cfg, LogHandler log, out string registry)
        {
            registry = cfg.Registry;
            string node = DshLocator.FindNode(cfg);
            string npmCli = null;
            if (node != null)
            {
                try
                {
                    string c = Path.Combine(Path.GetDirectoryName(node), @"node_modules\npm\bin\npm-cli.js");
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
                if (log != null) log("正在核对 " + cfg.PinnedVersion + " 的下载源完整性（" + reg + "）…", LogLevel.Dim);

                string file, args;
                if (node != null && npmCli != null)
                {
                    file = node;
                    args = "\"" + npmCli + "\" view @deepseek-ai/dsh@" + cfg.PinnedVersion +
                           " version dist.integrity --json --registry=" + reg + " --fetch-timeout=8000 --fetch-retries=0";
                }
                else
                {
                    string npm = DshLocator.SearchPath("npm.cmd");
                    if (npm == null)
                    {
                        // 找不到 npm 时不再"静默认为通过"：这一步是完整性核验，跳过就等于没核验
                        if (log != null) log("找不到 npm，无法完成完整性核验；请在设置里关闭核验或先装好 npm。", LogLevel.Bad);
                        return false;
                    }
                    file = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe");
                    args = "/d /s /c \"\"" + npm + "\" view @deepseek-ai/dsh@" + cfg.PinnedVersion +
                           " version dist.integrity --json --registry=" + reg + " --fetch-timeout=8000 --fetch-retries=0\"";
                }

                string stdout, stderr;
                int code = HiddenRunner.Run(file, args, cfg.Workspace, null, 25000, out stdout, out stderr);
                if (code != 0) continue;

                string version = JsonField(stdout, "version");
                string integrity = JsonField(stdout, "dist.integrity");
                if (version == cfg.PinnedVersion && integrity == cfg.PinnedIntegrity)
                {
                    registry = reg;
                    if (log != null) log("完整性核验通过，使用下载源：" + reg, LogLevel.Good);
                    return true;
                }
                if (log != null) log("源 " + reg + " 返回的版本/完整性不匹配，换下一个源。", LogLevel.Dim);
            }
            if (log != null) log("所有下载源都未通过完整性核验。", LogLevel.Bad);
            return false;
        }

        public static string JsonField(string json, string key)
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
    }
}
