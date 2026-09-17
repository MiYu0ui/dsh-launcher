using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace DshLauncher
{
    internal class LogEntry
    {
        public DateTime Time;
        public string Text;
        public LogLevel Level;
    }

    internal class LogEventArgs : EventArgs
    {
        public LogEntry Entry;
        public LogEventArgs(LogEntry entry) { Entry = entry; }
    }

    /// <summary>应用主体：托盘、窗口、服务三者的协调者。</summary>
    internal class LauncherContext : ApplicationContext
    {
        public static string Version { get { return BuildInfo.Version; } }

        public AppConfig Config;
        public DshServer Server;

        private readonly List<LogEntry> _history = new List<LogEntry>();
        private readonly object _historyGate = new object();
        private NotifyIcon _tray;
        private LauncherForm _form;
        private System.Windows.Forms.Timer _watch;
        private bool _exiting;
        private bool _trayTipShown;
        private bool _settingsAfterReveal;
        private volatile bool _watchBusy;
        private EventWaitHandle _showSignal;
        private Thread _showThread;
        private Control _ui;          // UI 线程消息泵锚点（跨线程操作必须经它中转）
        private BootSplash _splash;
        private UpdateInfo _update;
        private DeployReport _envReport;
        private Deployer _deployer;
        private bool _deployCheckStarted;
        private bool _deployModeEntered;
        private bool _deployStarting;
        private bool _installAskedThisRun;

        public event EventHandler<LogEventArgs> LogProduced;

        /// <summary>更新检查结果（UI 的更新按钮据此变化）。</summary>
        public UpdateInfo Update { get { return _update; } }

        /// <summary>环境体检结果（是否需要自动部署）。</summary>
        public DeployReport EnvReport { get { return _envReport; } }

        /// <summary>自动部署引擎（未进入部署模式时为 null）。</summary>
        public Deployer Deployer { get { return _deployer; } }

        /// <summary>主窗口（开启动画收尾时要淡入它）。</summary>
        public Form MainWindow { get { return _form; } }

        public LauncherContext(Args args) : this(args, false) { }

        /// <param name="deferShow">true = 先以全透明就位，等开启动画收尾时再淡入</param>
        public LauncherContext(Args args, bool deferShow)
        {
            // 句柄必须在 UI 线程上创建：托盘线程要唤出窗口时，靠它把调用切回 UI 线程。
            // 否则会在后台线程上创建窗体（不绘制、不可用）。
            _ui = new Control();
            IntPtr anchor = _ui.Handle;

            bool configExisted = System.IO.File.Exists(AppConfig.ConfigPath);
            Config = AppConfig.Load();
            if (args.Port > 0) Config.Port = args.Port;
            if (!string.IsNullOrEmpty(args.Dir)) Config.Workspace = args.Dir;
            if (!configExisted) Config.Save();   // 命令行覆盖只在本次生效，不写回配置
            WindowReveal.LoadFrom(Config);       // 二级界面动效档位（对话框是静态入口，档位集中放那边）

            Server = new DshServer();
            Server.Log += delegate(string message, LogLevel level) { AddLog(message, level); };
            Server.StatusChanged += delegate(object s, EventArgs e) { UpdateTray(); };
            Server.Serving += delegate(object s, EventArgs e) { OnServiceServing(); };

            SetupTray();

            _showSignal = new EventWaitHandle(false, EventResetMode.AutoReset, Args.ShowSignalName);
            _showThread = new Thread(delegate()
            {
                while (true)
                {
                    try
                    {
                        if (!_showSignal.WaitOne()) continue;
                    }
                    catch { return; }
                    if (_exiting) return;
                    try { ShowFormFromSignal(); }
                    catch { }
                }
            });
            _showThread.IsBackground = true;
            _showThread.Start();

            _watch = new System.Windows.Forms.Timer();
            _watch.Interval = 4000;
            _watch.Tick += delegate(object s, EventArgs e) { WatchTick(); };
            _watch.Start();

            AddLog("DSH 启动器 v" + Version + " 已就绪（构建于 " + BuildInfo.BuildStamp + "）。", LogLevel.Dim);
            AddLog("工作目录：" + Config.Workspace, LogLevel.Dim);
            AddLog("配置：" + AppConfig.ConfigPath, LogLevel.Dim);
            AddLog("安装位置：" + (Config.IsPortable ? "绿色免安装（就地运行）" : Config.InstallDir), LogLevel.Dim);
            ReconcileShortcuts();

            // 版本检测：失败的更新候选也记一笔，便于排查
            _update = UpdateCheck.Check();
            AddLog(_update.Message, _update.Available ? LogLevel.Good : LogLevel.Dim);

            if (args.AutoStart)
            {
                AddLog("开机自启模式：静默启动服务，不显示窗口。", LogLevel.Dim);
                StartServer();
            }
            else if (!args.Minimized)
            {
                if (deferShow)
                {
                    // 开启动画期间：主窗口先以全透明就位，动画收尾时由卡片"展开"着淡入
                    ShowForm();
                    try { if (_form != null) _form.Opacity = 0.0; } catch { }
                    _settingsAfterReveal = args.Settings;
                    StartBootSplash();
                }
                else
                {
                    ShowForm();
                    if (args.Settings && _form != null && !_form.IsDisposed) OpenSettings(_form);
            try { UpdateCheck.CleanupLeftovers(); } catch { }   // 主窗真的起来了，才清上一轮的 .old
                    ScheduleInstallPrompt();
                }
            }

            // 启动后检测一次端口状态，便于界面如实显示
            if (!args.AutoStart) DetectExternal();
            if (!args.AutoStart) StartDeploymentCheck();
        }

        // ---------------- 安装位置 ----------------
        /// <summary>
        /// 配置与磁盘的一致性核对（只报告，不拦截启动）：
        /// 自启项按配置对齐；失效的快捷方式提前暴露出来，别等下次才发现"安装目录没了"。
        /// </summary>
        private void ReconcileShortcuts()
        {
            try
            {
                string action = Shortcuts.ReconcileAutoStart(Config.AutoStart, AppPaths.ExePath, AppPaths.InstallDir);
                if (!string.IsNullOrEmpty(action)) AddLog(action, LogLevel.Warn);

                string legacy = Shortcuts.RetireLegacyAutoStart(AppPaths.ExePath);
                if (!string.IsNullOrEmpty(legacy)) AddLog(legacy, LogLevel.Dim);

                // 上一次迁移安装位置留下的旧副本，由本实例（新位置）清理
                string cleaned = InstallLocation.CleanupPreviousInstall(Config);
                if (!string.IsNullOrEmpty(cleaned)) AddLog(cleaned, LogLevel.Dim);

                foreach (string p in Config.ConsistencyProblems()) AddLog(p, LogLevel.Warn);
            }
            catch { }
        }

        /// <summary>把「首次选择安装位置」排到消息循环启动之后（构造期不能直接弹模态框）。</summary>
        private void ScheduleInstallPrompt()
        {
            if (Config.InstallAsked || _installAskedThisRun) return;
            if (_form == null || _form.IsDisposed) return;
            try { _form.BeginInvoke(new MethodInvoker(MaybeAskInstallLocation)); }
            catch { }
        }

        /// <summary>
        /// 第一次运行由**用户**选择安装位置，绝不静默采用默认值。
        /// 用 ✕/Esc 关掉视为"还没决定"，下次启动接着问。
        /// </summary>
        public void MaybeAskInstallLocation()
        {
            if (_exiting || _installAskedThisRun || Config.InstallAsked) return;
            if (_ui != null && _ui.InvokeRequired)
            {
                try { _ui.BeginInvoke(new MethodInvoker(MaybeAskInstallLocation)); }
                catch { }
                return;
            }
            _installAskedThisRun = true;

            ConfirmDialog.Choice c = InstallLocation.Ask(DlgOwner);
            if (c == ConfirmDialog.Choice.Cancel) return;          // 没决定，下次再问

            if (c == ConfirmDialog.Choice.Alt)                     // 绿色免安装
            {
                InstallLocation.MarkPortable(Config);
                AddLog("安装位置：绿色免安装（就地运行）—— " + AppPaths.InstallDir, LogLevel.Good);
                return;
            }

            string target = c == ConfirmDialog.Choice.Confirm
                ? AppConfig.SuggestedInstallDir()
                : InstallLocation.PickFolder(DlgOwner, AppPaths.InstallDir);
            if (string.IsNullOrEmpty(target)) return;
            ApplyInstallLocation(DlgOwner, target);
        }

        /// <summary>
        /// 落实安装位置。target 为空串 = 绿色免安装。
        /// 需要换目录时会复制程序本体、重建快捷方式，再以新位置重启启动器（DSH 服务不受影响）。
        /// </summary>
        public void ApplyInstallLocation(IWin32Window owner, string target)
        {
            if (target != null && target.Length == 0)
            {
                InstallLocation.MarkPortable(Config);
                AddLog("安装位置：绿色免安装（就地运行）—— " + AppPaths.InstallDir, LogLevel.Good);
                return;
            }

            string error;
            bool restarting;
            error = InstallLocation.Apply(Config, target, out restarting);
            if (error != null)
            {
                AddLog("设置安装位置失败：" + error, LogLevel.Bad);
                ConfirmDialog.Info(owner, "INSTALL / 失败", "无法安置到该位置", error,
                    new string[] { "目标=" + target },
                    "换一个你有写入权限的目录；Program Files 之类的系统目录需要管理员权限。");
                return;
            }

            AddLog("安装位置：" + Config.InstallDir, LogLevel.Good);
            if (restarting)
            {
                AddLog("正在以新位置重启启动器…（DSH 服务不受影响）", LogLevel.Good);
                Shutdown(false);
            }
        }

        // ---------------- 部署检测与自动部署 ----------------
        /// <summary>环境体检：没部署过就进入自动部署（带进入动画）。</summary>
        private void StartDeploymentCheck()
        {
            if (_deployCheckStarted || _exiting) return;
            _deployCheckStarted = true;
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                DeployReport report;
                try { report = Deployment.Check(Config); }
                catch { return; }
                _envReport = report;
                AddLog(report.Summary, report.NeedsDeploy ? LogLevel.Warn : LogLevel.Dim);
                if (!report.NeedsDeploy) return;
                if (_ui != null && _ui.InvokeRequired)
                {
                    try { _ui.BeginInvoke(new MethodInvoker(EnterDeployMode)); }
                    catch { }
                }
                else EnterDeployMode();
            });
        }

        private void EnterDeployMode()
        {
            if (_exiting || _deployModeEntered) return;
            if (_form == null || _form.IsDisposed || !_form.Visible) return;   // 托盘状态下先不打扰
            _deployModeEntered = true;

            _deployer = new Deployer();
            _deployer.Log += delegate(string message, LogLevel level) { AddLog(message, level); };
            _deployer.Changed += delegate(object s, EventArgs e) { OnDeployChanged(); };

            AddLog("未检测到完整运行环境，进入自动部署流程。", LogLevel.Warn);

            // 进入部署状态的过渡动画
            try
            {
                using (LinkTransition card = LinkTransition.ForDeploy(
                           _envReport == null ? "" : _envReport.Summary, null))
                {
                    card.CoverOwner(_form);
                    card.ShowDialog(_form);
                }
            }
            catch { }

            if (_form != null && !_form.IsDisposed) _form.EnterDeployMode();
        }

        private void OnDeployChanged()
        {
            if (_form != null && !_form.IsDisposed) _form.SyncDeploy();
            if (_deployer == null || !_deployer.Finished || !_deployer.Succeeded || _deployStarting) return;
            _deployStarting = true;
            AddLog("部署完成，正在启动服务…", LogLevel.Good);
            if (_form != null && !_form.IsDisposed) _form.ExitDeployMode();
            StartServer();
        }

        /// <summary>点「一键部署」。</summary>
        public void StartDeployment()
        {
            if (_deployer == null) EnterDeployMode();
            if (_deployer == null || _deployer.Running) return;
            AddLog("开始自动部署。", LogLevel.Info);
            _deployer.Start(Config);
            if (_form != null && !_form.IsDisposed) _form.SyncDeploy();
        }

        /// <summary>
        /// 用户又启动了一次（已有实例在跑）。
        /// 窗口在托盘里 → 播一段**精简开场**（约 1 秒）再展开；
        /// 窗口本来就开着 → 直接置前，不打扰。
        /// </summary>
        private void ShowFormFromSignal()
        {
            if (_exiting) return;
            if (_ui != null && _ui.InvokeRequired)
            {
                try { _ui.BeginInvoke(new MethodInvoker(ShowFormFromSignal)); }
                catch { }
                return;
            }

            bool wasVisible = _form != null && !_form.IsDisposed && _form.Visible;
            if (wasVisible || !Config.BootAnimation)
            {
                ShowForm();
                return;
            }

            Anim.Suspend();
            ShowForm();
            try { if (_form != null) _form.Opacity = 0.0; } catch { }
            try
            {
                BootSplash splash = new BootSplash(true);
                if (_form != null && !_form.IsDisposed) splash.AttachReveal(_form);
                splash.FormClosed += delegate(object s, FormClosedEventArgs e) { RevealMainForm(); };
                _splash = splash;
                splash.Show();
            }
            catch
            {
                RevealMainForm();
            }
        }

        /// <summary>开启动画收尾：把主窗口恢复为不透明（并处理 --settings）。</summary>
        public void RevealMainForm()
        {
            if (_ui != null && _ui.InvokeRequired)
            {
                try { _ui.BeginInvoke(new MethodInvoker(RevealMainForm)); }
                catch { }
                return;
            }
            if (_form == null || _form.IsDisposed) return;
            Anim.Resume();                                 // 卡片结束，恢复底层动效
            try { _form.Opacity = 1.0; } catch { }
            if (_settingsAfterReveal)
            {
                _settingsAfterReveal = false;
                OpenSettings(_form);
            }
            try { UpdateCheck.CleanupLeftovers(); } catch { }   // 开场动画这条路径：主窗展开之后才清 .old
            ScheduleInstallPrompt();
            StartDeploymentCheck();
        }

        /// <summary>
        /// 开启动画：非模态窗口，由主消息循环驱动（不能用 Application.Run，否则结束时
        /// ExitThread 会把主窗口一起关掉）。收尾时卡片展开、主窗口淡入。
        /// </summary>
        private void StartBootSplash()
        {
            try
            {
                Anim.Suspend();          // 卡片播放期间别再刷新底层那个全透明的窗口
                BootSplash splash = new BootSplash();
                if (_form != null && !_form.IsDisposed) splash.AttachReveal(_form);
                splash.FormClosed += delegate(object s, FormClosedEventArgs e) { RevealMainForm(); };
                _splash = splash;
                splash.Show();
            }
            catch
            {
                RevealMainForm();
            }
        }

        // ---------------- 版本更新 ----------------
        /// <summary>更新按钮：有可用更新就直接问是否更新，否则重新检查一次。</summary>
        /// <summary>对话框 owner：主窗可见时挂主窗，否则屏幕居中。</summary>
        private IWin32Window DlgOwner
        {
            get { return (_form != null && !_form.IsDisposed && _form.Visible) ? (IWin32Window)_form : null; }
        }

        /// <summary>点「检查更新 / 更新」。</summary>
        public void UpdateButtonClicked()
        {
            _update = UpdateCheck.Check();
            if (_form != null && !_form.IsDisposed) _form.RefreshUpdateBadge();
            AddLog(_update.Message, _update.Available ? LogLevel.Good : LogLevel.Dim);

            if (!_update.Available)
            {
                ConfirmDialog.Info(DlgOwner, "UPDATE / 检查更新", "已是最新版本", _update.Message, null, null);
                return;
            }

            ConfirmDialog.Choice r = ConfirmDialog.Show(DlgOwner, "UPDATE / 安装更新", "发现新版本",
                "是否立即更新并重启启动器？正在运行的 DSH 服务不会被中断。",
                new string[]
                {
                    "当前版本=v" + BuildInfo.Version,
                    "新版本=v" + _update.LatestVersion,
                    "构建时间=" + _update.CandidateTime.ToString("yyyy-MM-dd HH:mm")
                },
                null, "立即更新", false);
            if (r != ConfirmDialog.Choice.Confirm) return;

            string error;
            if (!UpdateCheck.Apply(_update, out error))
            {
                AddLog("更新失败：" + error, LogLevel.Bad);
                ConfirmDialog.Info(DlgOwner, "UPDATE / 更新失败", "更新失败", error, null,
                    "可以改用「导出诊断日志」把现场信息发出来排查。");
                return;
            }
            AddLog("已更新到 v" + _update.LatestVersion + "，正在重启启动器…", LogLevel.Good);
            if (!UpdateCheck.RestartSelf("--after-update"))
            {
                ConfirmDialog.Info(DlgOwner, "UPDATE / 需要手动重启", "新版本已就位",
                    "请手动重新打开启动器以完成更新。", null, null);
            }
            Shutdown(false);      // 保持服务运行，只退出启动器
        }

        // ---------------- 诊断日志导出 ----------------
        public void ExportDiagnostics()
        {
            try
            {
                string path = Diagnostics.Export(Config, Server, _update);
                AddLog("诊断日志已导出：" + path, LogLevel.Good);
                System.Diagnostics.ProcessStartInfo psi =
                    new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"");
                psi.UseShellExecute = true;
                System.Diagnostics.Process.Start(psi);
            }
            catch (Exception ex)
            {
                AddLog("导出诊断日志失败：" + ex.Message, LogLevel.Bad);
                ConfirmDialog.Info(DlgOwner, "LOG / 导出失败", "导出诊断日志失败", ex.Message, null, null);
            }
        }

        public void OpenLogFolder()
        {
            try
            {
                string path = FileLog.Path;
                System.Diagnostics.ProcessStartInfo psi = System.IO.File.Exists(path)
                    ? new System.Diagnostics.ProcessStartInfo("explorer.exe", "/select,\"" + path + "\"")
                    : new System.Diagnostics.ProcessStartInfo("explorer.exe", "\"" + AppPaths.LogsDir + "\"");
                psi.UseShellExecute = true;
                System.Diagnostics.Process.Start(psi);
            }
            catch { }
        }

        // ---------------- 日志 ----------------
        public void AddLog(string message, LogLevel level)
        {
            LogEntry entry = new LogEntry();
            entry.Time = DateTime.Now;
            entry.Text = message;
            entry.Level = level;

            FileLog.Write("[" + level + "] " + message);   // 界面不显示日志，但日志文件必须完整

            lock (_historyGate)
            {
                _history.Add(entry);
                if (_history.Count > 500) _history.RemoveRange(0, 100);
            }

            EventHandler<LogEventArgs> h = LogProduced;
            if (h != null) { try { h(this, new LogEventArgs(entry)); } catch { } }
        }

        public List<LogEntry> LogHistory()
        {
            lock (_historyGate) { return new List<LogEntry>(_history); }
        }

        // ---------------- 托盘 ----------------
        private void SetupTray()
        {
            _tray = new NotifyIcon();
            _tray.Icon = Res.AppIcon(16);
            _tray.Text = "DSH 启动器";
            _tray.Visible = true;

            ContextMenuStrip menu = new ContextMenuStrip();
            menu.Renderer = new ArchiveMenuRenderer();
            menu.Font = Theme.FontUi;
            menu.Items.Add(MenuItem("显示主界面", delegate(object s, EventArgs e) { ShowForm(); }));
            menu.Items.Add(MenuItem("启动 DSH", delegate(object s, EventArgs e) { StartServer(); }));
            menu.Items.Add(MenuItem("打开界面", delegate(object s, EventArgs e) { OpenUi(); }));
            menu.Items.Add(MenuItem("停止服务", delegate(object s, EventArgs e) { StopServer(true); }));
            menu.Items.Add(MenuItem("重启服务（强制）", delegate(object s, EventArgs e) { ForceStopExternal(true); }));
            menu.Items.Add(MenuItem("清扫残留 DSH 进程", delegate(object s, EventArgs e) { CleanupResiduals(); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuItem("帮助…", delegate(object s, EventArgs e) { OpenHelp(DlgOwner); }));
            menu.Items.Add(MenuItem("卸载…", delegate(object s, EventArgs e) { OpenUninstall(DlgOwner); }));
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add(MenuItem("退出启动器", delegate(object s, EventArgs e) { ExitApp(); }));
            _tray.ContextMenuStrip = menu;
            _tray.DoubleClick += delegate(object s, EventArgs e) { ShowForm(); };
        }

        private static ToolStripMenuItem MenuItem(string text, EventHandler handler)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += handler;
            return item;
        }

        private void UpdateTray()
        {
            if (_tray == null) return;
            // StatusChanged 可能来自后台线程，托盘文本统一回 UI 线程更新
            if (_ui != null && _ui.InvokeRequired)
            {
                try { _ui.BeginInvoke(new MethodInvoker(UpdateTray)); }
                catch { }
                return;
            }
            string suffix;
            switch (Server.Status)
            {
                case ServerStatus.Starting: suffix = "正在启动…"; break;
                case ServerStatus.Running:
                case ServerStatus.External: suffix = "运行中"; break;
                case ServerStatus.Stopping: suffix = "正在停止…"; break;
                case ServerStatus.Failed: suffix = "启动失败"; break;
                case ServerStatus.PortConflict: suffix = "端口被占用"; break;
                default: suffix = "未运行"; break;
            }
            string text = "DSH 启动器 · " + suffix;
            if (text.Length > 62) text = text.Substring(0, 62);
            try { _tray.Text = text; } catch { }
        }

        public void NotifyHiddenToTray()
        {
            if (_exiting) return;
            if (_ui != null && _ui.InvokeRequired)
            {
                try { _ui.BeginInvoke(new MethodInvoker(NotifyHiddenToTray)); }
                catch { }
                return;
            }
            if (_trayTipShown || _tray == null) return;
            _trayTipShown = true;
            try
            {
                _tray.BalloonTipTitle = "DSH 启动器仍在运行";
                _tray.BalloonTipText = "服务在后台继续工作，双击托盘图标可以随时回到这个界面。";
                _tray.ShowBalloonTip(4000);
            }
            catch { }
        }

        // ---------------- 主窗口 ----------------
        public void ShowForm()
        {
            if (_exiting) return;
            // 关键：窗体只能在 UI 线程上创建/显示
            if (_ui != null && _ui.InvokeRequired)
            {
                try { _ui.BeginInvoke(new MethodInvoker(ShowForm)); }
                catch { }
                return;
            }
            if (_form == null || _form.IsDisposed)
            {
                _form = new LauncherForm(this);
            }
            _form.Show();
            if (_form.WindowState == FormWindowState.Minimized) _form.WindowState = FormWindowState.Normal;
            _form.Activate();
            try { _form.BringToFront(); } catch { }
        }

        // ---------------- 业务动作 ----------------
        public void StartServer()
        {
            if (Server.Status == ServerStatus.Starting || Server.Status == ServerStatus.Running) return;
            if (Server.Status == ServerStatus.Stopping) return;

            List<string> problems = Config.Validate();
            if (problems.Count > 0)
            {
                AddLog(problems[0], LogLevel.Bad);
                ConfirmDialog.Info(DlgOwner, "CONFIG / 配置有误", "无法启动", problems[0],
                    new string[] { "配置文件=" + AppConfig.ConfigPath }, null);
                DetectExternal();
                return;
            }
            Server.StartAsync(Config);
        }

        public void StopServer(bool confirm)
        {
            if (Server.Status == ServerStatus.External)
            {
                // 外部拉起：走"识别 + 强制终止"
                ForceStopExternal(false);
                return;
            }
            if (!Server.Owned) return;
            if (confirm)
            {
                ConfirmDialog.Choice r = ConfirmDialog.Show(DlgOwner, "CONFIRM / 停止服务", "停止服务",
                    "确定要停止 DeepSeek Harness 服务吗？",
                    new string[] { "端口=" + Config.Port, "工作目录=" + Config.Workspace },
                    "正在进行的会话会被中断，未保存的工作会丢失。", "停止服务", true);
                if (r != ConfirmDialog.Choice.Confirm) return;
            }
            Thread t = new Thread(delegate() { Server.Stop(); });
            t.IsBackground = true;
            t.Start();
        }

        public void OpenUi()
        {
            string url = Server.OpenUrl;
            if (string.IsNullOrEmpty(url)) url = DshServer.NormalizeUrl(Config.Port);
            if (Server.Status == ServerStatus.Stopped || Server.Status == ServerStatus.Failed)
            {
                string detail;
                if (DshServer.Probe(Config.Port, out detail) != ProbeState.Dsh)
                {
                    ConfirmDialog.Info(DlgOwner, "STATUS / 未运行", "服务还没有运行",
                        "请先点「启动 DSH」，等服务就绪后再打开界面。",
                        new string[] { "端口=" + Config.Port }, null);
                    return;
                }
            }
            AddLog("正在打开界面：" + url, LogLevel.Dim);
            OpenUiWithTransition(url);
        }

        /// <summary>
        /// 「打开界面」的统一出口：先播过渡动画（卡片铺满主窗口），
        /// 播完（或点击跳过）之后再真正拉起 Web UI。
        /// 窗口在托盘里 / 关掉了过渡开关时，直接打开，不播。
        /// </summary>
        private void OpenUiWithTransition(string url)
        {
            bool canAnimate = Config.TransitionAnimation && !_exiting
                              && _form != null && !_form.IsDisposed && _form.Visible;
            if (!canAnimate)
            {
                DshServer.OpenInBrowser(Config, url);
                return;
            }

            string shown = string.IsNullOrEmpty(Server.WebUrl)
                ? DshServer.NormalizeUrl(Config.Port) : Server.WebUrl;
            AddLog("播放接入过渡动画…", LogLevel.Dim);
            try
            {
                using (LinkTransition card = new LinkTransition(shown, null))
                {
                    card.CoverOwner(_form);
                    card.ShowDialog(_form);      // 阻塞到动画结束（点击可跳过）
                }
            }
            catch { }
            AddLog("过渡结束，正在打开界面：" + url, LogLevel.Dim);
            DshServer.OpenInBrowser(Config, url);
        }

        // ---------------- 强制终止 / 残留清扫 ----------------
        /// <summary>
        /// 识别并强制终止当前端口上的服务（可能是别人拉起的）。
        /// 识别 → 确认 → 从同族树根 taskkill /T /F → 复查 →（可选）立即重启。
        /// </summary>
        public void ForceStopExternal(bool preferRestart)
        {
            if (_ui != null && _ui.InvokeRequired)
            {
                try { _ui.BeginInvoke(new MethodInvoker(delegate() { ForceStopExternal(preferRestart); })); }
                catch { }
                return;
            }

            int port = Config.Port;
            AddLog("正在识别端口 " + port + " 上的进程…", LogLevel.Dim);
            ProcInfo owner = null;
            bool isDsh = false;
            try { owner = ProcessKiller.IdentifyPort(port, out isDsh); }
            catch (Exception ex) { AddLog("识别失败：" + ex.Message, LogLevel.Bad); }

            if (owner == null)
            {
                ConfirmDialog.Info(DlgOwner, "STOP / 无监听", "端口上没有监听进程",
                    "端口 " + port + " 上当前没有检测到监听进程。",
                    new string[] { "端口=" + port }, null);
                return;
            }

            if (!isDsh)
            {
                AddLog("端口 " + port + " 被非 DSH 程序占用：" + ProcessKiller.Describe(owner), LogLevel.Warn);
                ConfirmDialog.Info(DlgOwner, "STOP / 拒绝执行", "端口被其他程序占用",
                    "占用端口的程序不是 DeepSeek Harness，启动器不会去终止它（避免误杀）。",
                    new string[]
                    {
                        "监听进程=PID " + owner.Pid + "  " + owner.Name,
                        "命令行=" + owner.ShortCommand
                    },
                    "请在设置里换一个端口。");
                return;
            }

            int root = owner.RootPid > 0 ? owner.RootPid : owner.Pid;
            AddLog("识别结果：" + ProcessKiller.Describe(owner) + "；同族树根 PID " + root, LogLevel.Dim);

            List<string> fields = new List<string>();
            fields.Add("监听进程=PID " + owner.Pid + "  " + owner.Name);
            fields.Add("同族树根=PID " + root + (owner.RootName.Length > 0 ? "  " + owner.RootName : "") + "（含 cmd/npx 外壳）");
            fields.Add("命令行=" + owner.ShortCommand);
            if (owner.StartTime != DateTime.MinValue) fields.Add("启动于=" + owner.StartTime.ToString("MM-dd HH:mm:ss"));
            fields.Add("子进程=" + owner.ChildCount + " 个");

            ConfirmDialog.Choice r = ConfirmDialog.Show(DlgOwner,
                preferRestart ? "CONFIRM / 重启服务" : "CONFIRM / 强制终止",
                preferRestart ? "重启服务（强制）" : "强制终止服务",
                "检测到端口 " + port + " 上的 DeepSeek Harness 服务（不是本启动器拉起的）。",
                fields.ToArray(),
                "将强制终止整棵进程树；正在进行的会话会被立即中断，未保存的工作会丢失。" +
                (preferRestart ? " 终止后会立即重新启动服务。" : ""),
                preferRestart ? "终止并重启" : "强制终止", true);
            if (r != ConfirmDialog.Choice.Confirm) return;

            bool ok = KillAndVerify(root, port);
            if (!ok) return;

            if (preferRestart)
            {
                AddLog("正在重新启动服务…", LogLevel.Info);
                StartServer();
            }
        }

        /// <summary>杀树 + 复查；权限不足时询问是否提权重试。返回是否已终止。</summary>
        private bool KillAndVerify(int root, int port)
        {
            string error;
            bool denied;
            bool ok = ProcessKiller.KillTree(root, out error, out denied);

            if (!ok && (denied || !ProcessKiller.ProcessGone(root)))
            {
                AddLog("终止失败：" + error, LogLevel.Warn);
                ConfirmDialog.Choice r = ConfirmDialog.Show(DlgOwner, "CONFIRM / 提权重试", "终止失败",
                    "目标进程可能以管理员权限运行。" + error,
                    new string[] { "树根 PID=" + root },
                    "可以尝试以管理员身份重试（会弹出 UAC 授权）。",
                    "以管理员重试", true);
                if (r == ConfirmDialog.Choice.Confirm)
                {
                    ok = ProcessKiller.KillTreeElevated(root, out error);
                    if (!ok) AddLog("提权终止仍失败：" + error, LogLevel.Bad);
                }
            }

            // 复查：等进程消失 + 端口释放
            for (int i = 0; i < 40 && !ProcessKiller.ProcessGone(root); i++) System.Threading.Thread.Sleep(100);
            bool gone = ProcessKiller.ProcessGone(root);
            for (int i = 0; i < 20 && !ProcessKiller.PortFree(port); i++) System.Threading.Thread.Sleep(100);
            bool free = ProcessKiller.PortFree(port);

            if (gone && free)
            {
                AddLog("已强制终止（树根 PID " + root + "），端口 " + port + " 已释放。", LogLevel.Good);
                ConfirmDialog.Info(DlgOwner, "DONE / 已终止", "进程树已终止",
                    "已强制终止整棵进程树，端口已释放。",
                    new string[] { "树根 PID=" + root, "释放端口=" + port }, null);
                Server.Detach();
                return true;
            }

            AddLog("强制终止后复查未通过：进程" + (gone ? "已退出" : "仍在") + "，端口" + (free ? "已释放" : "仍被占用"), LogLevel.Bad);
            ConfirmDialog.Info(DlgOwner, "WARN / 复查未通过", "强制终止后复查未通过",
                "进程或端口没有完全清理干净。",
                new string[]
                {
                    "树根进程=" + (gone ? "已退出" : "仍在运行"),
                    "端口 " + port + "=" + (free ? "已释放" : "仍被占用")
                },
                "请用「清扫残留」或任务管理器处理。");
            return false;
        }

        /// <summary>清扫残留：列出本机所有 DSH 进程族，确认后全部强制终止。</summary>
        public void CleanupResiduals()
        {
            if (_ui != null && _ui.InvokeRequired)
            {
                try { _ui.BeginInvoke(new MethodInvoker(CleanupResiduals)); }
                catch { }
                return;
            }

            AddLog("正在扫描残留的 DSH 进程…", LogLevel.Dim);
            List<ProcInfo> roots = null;
            try
            {
                Dictionary<int, ProcInfo> map;
                roots = ProcessKiller.FindDshInstances(out map);
            }
            catch (Exception ex)
            {
                AddLog("扫描失败：" + ex.Message, LogLevel.Bad);
                return;
            }

            if (roots == null || roots.Count == 0)
            {
                AddLog("没有发现残留的 DSH 进程。", LogLevel.Dim);
                ConfirmDialog.Info(DlgOwner, "CLEAN / 清扫残留", "没有发现残留",
                    "本机没有扫描到残留的 DSH 进程。", null, null);
                return;
            }

            List<string> list = new List<string>();
            int shown = 0;
            foreach (ProcInfo p in roots)
            {
                if (shown >= 10) { list.Add("其他=（其余 " + (roots.Count - shown) + " 个略）"); break; }
                // 把**命令行**也给出来：只显示 PID/名字/端口，等于让用户在看不到"要杀的到底是什么"
                // 的情况下做决定 —— 而这是一次确认全杀。
                string cmdline = p.CommandLine == null ? "" : p.CommandLine.Trim();
                if (cmdline.Length > 110) cmdline = cmdline.Substring(0, 107) + "…";
                list.Add("进程 " + (shown + 1) + "=" + ProcessKiller.Describe(p) + (cmdline.Length > 0 ? "  ·  " + cmdline : ""));
                shown++;
            }

            AddLog("发现 " + roots.Count + " 个 DSH 进程族待清理。", LogLevel.Warn);
            ConfirmDialog.Choice r = ConfirmDialog.Show(DlgOwner, "CONFIRM / 清扫残留", "清扫残留 DSH 进程",
                "发现 " + roots.Count + " 个 DSH 进程族，将全部强制终止（含各自子进程）。",
                list.ToArray(),
                "正在进行的会话会被中断，未保存的工作会丢失。",
                "全部终止", true);
            if (r != ConfirmDialog.Choice.Confirm) return;

            int okCount = 0, failCount = 0;
            foreach (ProcInfo p in roots)
            {
                string error;
                bool denied;
                if (ProcessKiller.KillTree(p.Pid, out error, out denied)) okCount++;
                else
                {
                    if (denied)
                    {
                        string e2;
                        if (ProcessKiller.KillTreeElevated(p.Pid, out e2)) { okCount++; continue; }
                        error = e2;
                    }
                    failCount++;
                    AddLog("终止 PID " + p.Pid + " 失败：" + error, LogLevel.Warn);
                }
            }

            System.Threading.Thread.Sleep(600);
            string detail2;
            ProbeState st = DshServer.Probe(Config.Port, out detail2);
            AddLog("清扫完成：成功 " + okCount + " 个，失败 " + failCount + " 个；端口 " + Config.Port + " 探测：" + detail2, LogLevel.Good);
            ConfirmDialog.Info(DlgOwner, "DONE / 清扫完成", "清扫完成",
                "已按上面的范围清理残留进程。",
                new string[]
                {
                    "成功=" + okCount + " 个",
                    "失败=" + failCount + " 个",
                    "端口 " + Config.Port + "=" + detail2
                }, null);

            if (st != ProbeState.Dsh) Server.Detach();
            DetectExternal();
        }

        public void OpenSettings(IWin32Window owner)
        {
            string pending = null;
            bool uninstall = false;
            using (SettingsForm dlg = new SettingsForm(Config, Server))
            {
                DialogResult r = dlg.ShowDialog(owner);
                if (r == DialogResult.OK)
                {
                    bool cfgSaved = Config.Save();
                    WindowReveal.LoadFrom(Config);   // 档位改动从下一个二级界面开始生效
                    AddLog(cfgSaved ? "设置已保存。" : "配置写入失败：这次的改动只存在于本次运行，重启启动器后会回退（自启项尤其要注意）。", cfgSaved ? LogLevel.Good : LogLevel.Bad);
                    if (Server.Status == ServerStatus.Running || Server.Status == ServerStatus.Starting)
                        AddLog("（端口/工作目录的改动会在下次启动服务时生效）", LogLevel.Dim);
                    pending = dlg.PendingInstallDir;
                }
                else if (r == DialogResult.Abort) uninstall = true;   // 点了「卸载…」
            }
            if (uninstall) { OpenUninstall(owner); return; }
            // 搬迁必须在设置窗关闭之后执行：重启启动器时不能还压着一个模态窗
            if (pending != null) ApplyInstallLocation(owner, pending);
        }

        /// <summary>
        /// 打开帮助二级界面。反正在这里等它关掉即可 —— 帮助窗自己管三级详情。
        /// </summary>
        public void OpenHelp(IWin32Window owner)
        {
            using (HelpForm dlg = new HelpForm())
                dlg.ShowDialog(owner);
        }

        /// <summary>打开卸载二级界面；如果这次把启动器自己也卸了，收尾退出。</summary>
        public void OpenUninstall(IWin32Window owner)
        {
            bool removed = false;
            try
            {
                using (UninstallForm dlg = new UninstallForm(Config, Server))
                {
                    dlg.ShowDialog(owner);
                    removed = dlg.LauncherRemoved;
                }
            }
            catch (Exception ex)
            {
                AddLog("打开卸载界面失败：" + ex.Message, LogLevel.Bad);
                return;
            }
            if (removed)
            {
                AddLog("启动器本体已被卸载，正在退出…", LogLevel.Warn);
                Shutdown(false);      // 服务是否停止已由卸载界面按选项处理
            }
        }

        /// <summary>服务就绪：先播接入过渡动画（卡片铺满主窗口），再真正打开 Web 界面。</summary>
        private void OnServiceServing()
        {
            if (_exiting) return;
            if (_ui != null && _ui.InvokeRequired)
            {
                try { _ui.BeginInvoke(new MethodInvoker(OnServiceServing)); }
                catch { }
                return;
            }
            if (!Config.AutoOpenBrowser) return;

            string url = string.IsNullOrEmpty(Server.OpenUrl) ? DshServer.NormalizeUrl(Config.Port) : Server.OpenUrl;
            AddLog("接入成功，正在交接到 Web 界面…", LogLevel.Good);
            OpenUiWithTransition(url);
        }

        // ---------------- 端口状态巡检 ----------------
        /// <summary>
        /// 探测端口上是否已有 DSH 服务。
        /// **探测放后台线程**：HTTP 最长 2.5 秒，绝不能在 UI 线程上等——
        /// 启动时它正好卡在开场动画的第一帧前面，会把动画起手吃掉。
        /// </summary>
        private void DetectExternal()
        {
            if (Server.Status == ServerStatus.Running || Server.Status == ServerStatus.Starting) return;
            ThreadPool.QueueUserWorkItem(delegate(object probeState)
            {
                ProbeState found;
                try
                {
                    string detail;
                    found = DshServer.Probe(Config.Port, out detail);
                }
                catch { return; }
                if (found != ProbeState.Dsh) return;

                MethodInvoker apply = delegate()
                {
                    Server.WebUrl = DshServer.NormalizeUrl(Config.Port);
                    Server.OpenUrl = Server.WebUrl;
                    Server.MarkExternal();
                    AddLog("检测到已有 DeepSeek Harness 服务在 " + Config.Port + " 端口运行，已接管显示。", LogLevel.Good);
                };
                if (_ui != null && _ui.InvokeRequired)
                {
                    try { _ui.BeginInvoke(apply); } catch { }
                }
                else apply();
            });
        }

        private void WatchTick()
        {
            if (_exiting || _watchBusy) return;
            // 端口探测有网络等待，放到后台线程，别卡住界面
            _watchBusy = true;
            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                try
                {
                    ServerStatus st = Server.Status;
                    if (st == ServerStatus.External)
                    {
                        if (!DshServer.TcpAlive(Config.Port)) Server.Detach();
                    }
                    else if (st == ServerStatus.Stopped || st == ServerStatus.Failed)
                    {
                        if (DshServer.TcpAlive(Config.Port)) DetectExternal();
                    }
                }
                catch { }
                finally { _watchBusy = false; }
            });
        }

        // ---------------- 退出 ----------------
        public void ExitApp()
        {
            if (_exiting) return;
            bool stopService = false;

            if (Server.Status == ServerStatus.Running && Server.Owned)
            {
                ConfirmDialog.Choice r = ConfirmDialog.Show(DlgOwner, "CONFIRM / 退出启动器", "服务正在运行",
                    "要同时停止 DeepSeek Harness 服务吗？",
                    new string[] { "端口=" + Config.Port, "工作目录=" + Config.Workspace },
                    null, "停止服务并退出", "保持运行，只退出启动器", true);
                if (r == ConfirmDialog.Choice.Cancel) return;
                stopService = r == ConfirmDialog.Choice.Confirm;
            }
            Shutdown(stopService);
        }

        /// <summary>收尾退出；更新重启时走 stopService=false，服务不受影响。</summary>
        private void Shutdown(bool stopService)
        {
            if (_exiting) return;
            _exiting = true;
            if (stopService) { try { Server.Stop(); } catch { } }
            try { _watch.Stop(); } catch { }
            try { if (_tray != null) { _tray.Visible = false; _tray.Dispose(); _tray = null; } } catch { }
            try { if (_form != null && !_form.IsDisposed) { _form.AllowClose(); _form.Close(); } } catch { }
            try { if (_ui != null) { _ui.Dispose(); _ui = null; } } catch { }
            try { if (_showSignal != null) _showSignal.Set(); } catch { }
            try { if (_showSignal != null) _showSignal.Close(); } catch { }
            ExitThread();
        }
    }

    /// <summary>托盘菜单配色（档案终端：暖白底、细线、暖褐选中）。</summary>
    internal class ArchiveColorTable : ProfessionalColorTable
    {
        public override Color MenuItemSelected { get { return Theme.Panel; } }
        public override Color MenuItemBorder { get { return Theme.Amber; } }
        public override Color MenuItemSelectedGradientBegin { get { return Theme.Panel; } }
        public override Color MenuItemSelectedGradientEnd { get { return Theme.Panel; } }
        public override Color MenuItemPressedGradientBegin { get { return Theme.Bg; } }
        public override Color MenuItemPressedGradientEnd { get { return Theme.Bg; } }
        public override Color ToolStripDropDownBackground { get { return Theme.PanelHi; } }
        public override Color ImageMarginGradientBegin { get { return Theme.PanelHi; } }
        public override Color ImageMarginGradientMiddle { get { return Theme.PanelHi; } }
        public override Color ImageMarginGradientEnd { get { return Theme.PanelHi; } }
        public override Color SeparatorDark { get { return Theme.Line; } }
        public override Color SeparatorLight { get { return Theme.PanelHi; } }
    }

    internal class ArchiveMenuRenderer : ToolStripProfessionalRenderer
    {
        public ArchiveMenuRenderer() : base(new ArchiveColorTable()) { }
        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Item.Selected ? Theme.Amber : Theme.Ink;
            base.OnRenderItemText(e);
        }
    }
}
