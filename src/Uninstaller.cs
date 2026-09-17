using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace DshLauncher
{
    /// <summary>卸载范围。</summary>
    internal enum UninstallMode
    {
        DshKeepData,        // 卸载 DSH，保留用户数据
        DshEverything,      // 彻底卸载 DSH（含凭据 / 会话 / 插件数据）
        LauncherKeepData,   // 卸载启动器，保留配置与日志
        LauncherEverything, // 彻底卸载启动器（含配置 / 日志 / 残留副本）
        All                 // 彻底卸载全部（DSH + 启动器）
    }

    /// <summary>计划里每一项的处置。</summary>
    internal enum TargetAction
    {
        Delete,   // 会删除
        Keep,     // 会保留（属于本次范围但不删）
        Skip      // 不会动（识别为非 DSH 的东西，列出来给用户看个安心）
    }

    internal enum TargetGroup
    {
        DshProgram, DshData, DshPlugin,
        LauncherProgram, LauncherData, Shortcut, Legacy
    }

    /// <summary>一个待处置目标。</summary>
    internal class UninstallTarget
    {
        public string Label = "";
        public string Path = "";
        public bool IsDirectory;
        public long Bytes;
        public int Files;
        public bool Exists;
        public TargetAction Action = TargetAction.Delete;
        public TargetGroup Group = TargetGroup.DshProgram;
        public bool Private;          // 含私密数据（凭据 / 会话 / 配置）
        public string Note = "";
        public bool ByToggle;         // 受某个可选开关控制

        public string SizeText
        {
            get
            {
                if (!Exists) return "不存在";
                if (Bytes <= 0) return "0 B";
                if (Bytes >= 1073741824L) return (Bytes / 1073741824.0).ToString("0.00") + " GB";
                if (Bytes >= 1048576L) return (Bytes / 1048576.0).ToString("0.0") + " MB";
                if (Bytes >= 1024L) return (Bytes / 1024.0).ToString("0.0") + " KB";
                return Bytes + " B";
            }
        }
    }

    /// <summary>一份完整的卸载计划（先算清楚，再动手）。</summary>
    internal class UninstallPlan
    {
        public UninstallMode Mode;
        public string Headline = "";
        public string Summary = "";
        public readonly List<UninstallTarget> Targets = new List<UninstallTarget>();
        public readonly List<string> Warnings = new List<string>();
        public bool IsDangerous;      // 涉及私密数据 → 需要打字确认
        public string ConfirmWord = "";
        public bool AffectsRunningLauncher;

        public long DeleteBytes;
        public int DeleteFileCount;

        public void Recalc()
        {
            DeleteBytes = 0;
            DeleteFileCount = 0;
            foreach (UninstallTarget t in Targets)
            {
                if (t.Action != TargetAction.Delete || !t.Exists) continue;
                DeleteBytes += t.Bytes;
                DeleteFileCount += t.Files;
            }
        }

        public string DeleteSizeText
        {
            get
            {
                if (DeleteBytes >= 1073741824L) return (DeleteBytes / 1073741824.0).ToString("0.00") + " GB";
                if (DeleteBytes >= 1048576L) return (DeleteBytes / 1048576.0).ToString("0.0") + " MB";
                if (DeleteBytes >= 1024L) return (DeleteBytes / 1024.0).ToString("0.0") + " KB";
                return DeleteBytes + " B";
            }
        }
    }

    /// <summary>删除动作的可选项。</summary>
    internal class UninstallOptions
    {
        public bool IncludeWallpaper = true;    // .dsh-wallpaper-engine（插件数据，默认删）
        public bool IncludeMemory = true;       // .mnemon / .hindsight（插件记忆，默认删）
        public bool IncludeShortcuts = true;    // 三处快捷方式
        public bool IncludeLegacy = true;       // 旧 PowerShell 方案残留 + 早期安装残留
        public bool StopService = true;         // 先停 DSH 服务
        public bool BackupPrivate = true;       // 卸载前把凭据与配置备份到桌面
        public bool Permanent;                  // true = 不送回收站
    }

    /// <summary>
    /// 卸载进度快照。每次回调都新建一个实例，避免工作线程与 UI 线程共享可变对象。
    /// </summary>
    internal class UninstallProgress
    {
        public int Done;              // 已处理（删除）的项数
        public int Total;             // 待删除项总数
        public long BytesDone;
        public long BytesTotal;
        public string CurrentLabel = "";   // 当前项的名字
        public string CurrentPath = "";    // 当前项的路径

        public double Fraction
        {
            get { return Total <= 0 ? 0.0 : Math.Min(1.0, (double)Done / Total); }
        }
    }

    /// <summary>
    /// 卸载引擎。
    /// **安全底座**：只处理本类自己识别出来的白名单路径；任何外部传入路径都不会被删。
    /// 另外有一组硬护栏（工作区、非 DSH 的 npx 缓存、%APPDATA%\npm、Node 安装目录）永远只列不动。
    /// </summary>
    internal static class Uninstaller
    {
        // ---------------- 路径 ----------------

        public static string DshHome
        {
            get
            {
                string env = Environment.GetEnvironmentVariable("DSH_HOME");
                if (!string.IsNullOrEmpty(env)) return env;
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh");
            }
        }

        public static string NpxCacheRoot
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"npm-cache\_npx");
            }
        }

        public static string NpmGlobalRoot
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "npm");
            }
        }

        public static string WallpaperData
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dsh-wallpaper-engine"); }
        }

        public static string MnemonData
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".mnemon"); }
        }

        public static string HindsightData
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".hindsight"); }
        }

        public static string LauncherConfigDir { get { return AppConfig.ConfigDir; } }
        public static string LauncherDataDir { get { return AppPaths.DataDir; } }

        public static string LauncherOldProgramsDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    @"Programs\DSH Launcher");
            }
        }

        public static string DesktopDir
        {
            get { return Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory); }
        }

        // ---------------- 识别：哪些 npx 缓存目录是 DSH 的 ----------------

        /// <summary>
        /// _npx 下每个子目录逐个查身份，只认含 @deepseek-ai/dsh 的那些。
        /// **绝不能整目录删** —— 实测本机 4 个目录里只有 2 个是 DSH，另两个共 489 MB 是别人的包。
        /// </summary>
        public static List<string> FindDshNpxDirs(out List<string> foreignDirs)
        {
            List<string> dsh = new List<string>();
            foreignDirs = new List<string>();
            try
            {
                if (!Directory.Exists(NpxCacheRoot)) return dsh;
                foreach (string dir in Directory.GetDirectories(NpxCacheRoot))
                {
                    if (IsDshNpxDir(dir)) dsh.Add(dir);
                    else foreignDirs.Add(dir);
                }
            }
            catch { }
            return dsh;
        }

        private static bool IsDshNpxDir(string dir)
        {
            try
            {
                string pkg = Path.Combine(dir, @"node_modules\@deepseek-ai\dsh\package.json");
                if (File.Exists(pkg)) return true;
                string bin = Path.Combine(dir, @"node_modules\@deepseek-ai\dsh\lib\bin.js");
                return File.Exists(bin);
            }
            catch { return false; }
        }

        // ---------------- 硬护栏：这些永远不删 ----------------

        private static readonly List<string> _protected = new List<string>();

        /// <summary>记录"不会动"的东西（只用于展示与自检，删除路径根本不会经过它们）。</summary>
        public static List<UninstallTarget> Protected(AppConfig cfg)
        {
            List<UninstallTarget> list = new List<UninstallTarget>();
            List<string> foreign;
            FindDshNpxDirs(out foreign);
            foreach (string d in foreign)
            {
                UninstallTarget t = Make(d, "非 DSH 的 npx 缓存目录", TargetGroup.DshProgram, false);
                t.Action = TargetAction.Skip;
                t.Note = "内容不是 @deepseek-ai/dsh，不会动";
                list.Add(t);
            }
            AddSkip(list, NpmGlobalRoot, "npm 全局包目录（claude-code / pnpm 等）", "与 DSH 无关，不会动");
            if (cfg != null && !string.IsNullOrEmpty(cfg.Workspace))
                AddSkip(list, cfg.Workspace, "你的工作区", "永远不碰你的工程文件");
            string node = null;
            try { node = DshLocator.FindNode(cfg ?? new AppConfig()); } catch { }
            if (!string.IsNullOrEmpty(node))
            {
                string nd = Path.GetDirectoryName(node);
                if (!string.IsNullOrEmpty(nd))
                    AddSkip(list, nd, "Node.js 安装目录" + (IsOurNodeDir(nd) ? "" : "（你自己装的）"),
                            "卸载启动器 / DSH 都不会卸载 Node.js");
            }
            return list;
        }

        /// <summary>Node 是否装在"我们的部署会用的位置"（用于判断这一项能不能提供给你勾）。</summary>
        public static bool IsOurNodeDir(string dir)
        {
            try
            {
                string a = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "nodejs");
                string b = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), @"Programs\nodejs");
                return string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), Path.GetFullPath(a).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)
                    || string.Equals(Path.GetFullPath(dir).TrimEnd('\\'), Path.GetFullPath(b).TrimEnd('\\'), StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
        }

        private static void AddSkip(List<UninstallTarget> list, string path, string label, string note)
        {
            UninstallTarget t = Make(path, label, TargetGroup.DshProgram, false);
            t.Action = TargetAction.Skip;
            t.Note = note;
            list.Add(t);
        }

        // ---------------- 计划 ----------------

        /// <summary>生成计划（会清点体积，可能耗时几秒，请放在后台线程调用）。</summary>
        public static UninstallPlan Build(AppConfig cfg, UninstallMode mode, UninstallOptions opt, LogHandler log)
        {
            UninstallPlan p = new UninstallPlan();
            p.Mode = mode;

            bool dsh = mode == UninstallMode.DshKeepData || mode == UninstallMode.DshEverything || mode == UninstallMode.All;
            bool dshAll = mode == UninstallMode.DshEverything || mode == UninstallMode.All;
            bool launcher = mode == UninstallMode.LauncherKeepData || mode == UninstallMode.LauncherEverything || mode == UninstallMode.All;
            bool launcherAll = mode == UninstallMode.LauncherEverything || mode == UninstallMode.All;

            switch (mode)
            {
                case UninstallMode.DshKeepData:
                    p.Headline = "卸载 DSH（保留用户数据）";
                    p.Summary = "只删 DSH 程序包，会话 / 插件 / 凭据全部保留，随时可以再装回来。";
                    p.ConfirmWord = "";
                    p.IsDangerous = false;
                    break;
                case UninstallMode.DshEverything:
                    p.Headline = "彻底卸载 DSH（连同所有相关文件）";
                    p.Summary = "删除程序包与全部用户数据，等于把 DSH 从这台机器上清空，可以从头重新部署。";
                    p.ConfirmWord = "UNINSTALL DSH";
                    p.IsDangerous = true;
                    break;
                case UninstallMode.LauncherKeepData:
                    p.Headline = "卸载启动器（保留配置与日志）";
                    p.Summary = "只删启动器程序本体与快捷方式，配置和日志留下，重装后设置照旧。";
                    p.ConfirmWord = "";
                    p.IsDangerous = false;
                    break;
                case UninstallMode.LauncherEverything:
                    p.Headline = "彻底卸载启动器（连同所有相关文件）";
                    p.Summary = "删除启动器本体、快捷方式、配置、日志与所有残留副本。DSH 本身不受影响。";
                    p.ConfirmWord = "UNINSTALL LAUNCHER";
                    p.IsDangerous = true;
                    break;
                default:
                    p.Headline = "彻底卸载全部（DSH + 启动器）";
                    p.Summary = "把 DSH 与启动器一并清空，机器回到没装过之前的状态，可以从头重新部署。";
                    p.ConfirmWord = "UNINSTALL ALL";
                    p.IsDangerous = true;
                    break;
            }
            if (dshAll && launcherAll) p.AffectsRunningLauncher = true;
            else if (launcher) p.AffectsRunningLauncher = true;

            if (log != null) log("正在清点磁盘占用…", LogLevel.Dim);

            // ---- DSH 程序包（npx 缓存里按内容识别出来的那些）----
            if (dsh)
            {
                List<string> foreign;
                List<string> dirs = FindDshNpxDirs(out foreign);
                if (dirs.Count == 0)
                {
                    UninstallTarget none = Make(Path.Combine(NpxCacheRoot, "@deepseek-ai/dsh"), "DSH 程序包（npx 缓存）", TargetGroup.DshProgram, true);
                    none.Action = TargetAction.Keep;
                    none.Note = "没有找到已缓存的 DSH 包";
                    p.Targets.Add(none);
                }
                foreach (string d in dirs)
                {
                    UninstallTarget t = Make(d, "DSH 程序包（npx 缓存）", TargetGroup.DshProgram, true);
                    p.Targets.Add(t);
                }
                // 全局安装的 DSH（若有）
                string g = Path.Combine(NpmGlobalRoot, @"node_modules\@deepseek-ai");
                UninstallTarget gt = Make(g, "全局安装的 DSH（若有）", TargetGroup.DshProgram, true);
                if (gt.Exists) p.Targets.Add(gt);
            }

            // ---- DSH 用户数据 ----
            if (dsh)
            {
                UninstallTarget home = Make(DshHome, "DSH 用户数据（配置 / 会话 / 插件 / 凭据）", TargetGroup.DshData, false);
                home.Private = true;
                home.Note = "含 .credentials.yaml 与全部会话";
                home.Action = dshAll ? TargetAction.Delete : TargetAction.Keep;
                p.Targets.Add(home);
            }

            // ---- 插件数据（可选开关）----
            if (dshAll)
            {
                UninstallTarget wp = Make(WallpaperData, "壁纸引擎插件数据", TargetGroup.DshPlugin, false);
                wp.ByToggle = true;
                wp.Note = "cache / ffmpeg / uploads";
                wp.Action = opt.IncludeWallpaper ? TargetAction.Delete : TargetAction.Keep;
                p.Targets.Add(wp);

                UninstallTarget mn = Make(MnemonData, "Mnemon 插件记忆数据", TargetGroup.DshPlugin, false);
                mn.ByToggle = true;
                mn.Private = true;
                mn.Note = "含文档与运行时记忆；Hindsight 等其它工具可能共用同一目录结构";
                mn.Action = opt.IncludeMemory ? TargetAction.Delete : TargetAction.Keep;
                p.Targets.Add(mn);

                UninstallTarget hs = Make(HindsightData, "Hindsight 插件配置与日志", TargetGroup.DshPlugin, false);
                hs.ByToggle = true;
                hs.Private = true;
                hs.Note = "hindsight-coding-agents 插件数据";
                hs.Action = opt.IncludeMemory ? TargetAction.Delete : TargetAction.Keep;
                p.Targets.Add(hs);
            }

            // ---- 启动器本体 ----
            if (launcher)
            {
                string dir = string.IsNullOrEmpty(cfg.InstallDir) ? AppPaths.InstallDir : cfg.InstallDir;
                UninstallTarget t = Make(dir, "启动器安装目录", TargetGroup.LauncherProgram, true);
                t.Note = "正在运行的 DSH Launcher.exe 会改名后删除";
                p.Targets.Add(t);
            }

            // ---- 启动器配置与日志 ----
            if (launcher)
            {
                UninstallTarget c = Make(LauncherConfigDir, "启动器配置（config.ini）", TargetGroup.LauncherData, false);
                c.Private = true;
                c.Action = launcherAll ? TargetAction.Delete : TargetAction.Keep;
                p.Targets.Add(c);

                UninstallTarget l = Make(LauncherDataDir, "启动器日志与备份", TargetGroup.LauncherData, false);
                l.Action = launcherAll ? TargetAction.Delete : TargetAction.Keep;
                p.Targets.Add(l);
            }

            // ---- 残留副本 / 旧方案（可选开关）----
            if (launcherAll)
            {
                UninstallTarget op = Make(LauncherOldProgramsDir, "早期安装残留副本", TargetGroup.Legacy, true);
                op.ByToggle = true;
                op.Action = opt.IncludeLegacy ? TargetAction.Delete : TargetAction.Keep;
                p.Targets.Add(op);

                string[] desktopJunk = new string[]
                {
                    Path.Combine(DesktopDir, "DSH Launcher.exe"),
                    Path.Combine(DesktopDir, "DSH Launcher.zip"),
                    Path.Combine(DesktopDir, "2026-08-29_install-and-run-deepseek-harness.bat"),
                    Path.Combine(DesktopDir, "Uninstall_DSH_Desktop.exe")
                };
                foreach (string f in desktopJunk)
                {
                    UninstallTarget t = Make(f, "桌面上的旧文件", TargetGroup.Legacy, false);
                    t.ByToggle = true;
                    t.Action = opt.IncludeLegacy ? TargetAction.Delete : TargetAction.Keep;
                    if (t.Exists) p.Targets.Add(t);
                }
            }

            // ---- 快捷方式 ----
            if (launcher && opt.IncludeShortcuts)
            {
                string[] links = new string[]
                {
                    Shortcuts.DesktopLinkPath,
                    Shortcuts.StartMenuLinkPath,
                    Shortcuts.AutoStartLinkPath,
                    Shortcuts.LegacyAutoStartLinkPath + ".bak"
                };
                foreach (string l in links)
                {
                    UninstallTarget t = Make(l, "快捷方式 / 自启项", TargetGroup.Shortcut, false);
                    if (t.Exists) p.Targets.Add(t);
                }
            }

            // ---- 警告 ----
            if (dshAll)
            {
                p.Warnings.Add("将删除 DSH 用户数据，其中包含 .credentials.yaml（API 凭据）与全部会话记录。");
                if (dshAll && (mode == UninstallMode.DshEverything || mode == UninstallMode.All))
                    p.Warnings.Add("删除后无法恢复会话；如需保留请先勾选「卸载前备份凭据与配置」。");
            }
            if (p.AffectsRunningLauncher)
                p.Warnings.Add("卸载启动器会结束当前正在运行的启动器，本窗口关闭后无法再显示结果（报告会存到桌面）。");
            if (opt.StopService && dsh)
                p.Warnings.Add("将先停止正在运行的 DSH 服务；正在进行的会话会被中断。");
            if (!opt.Permanent)
            {
                string rw = RecycleCapacityWarning(p);
                if (!string.IsNullOrEmpty(rw)) p.Warnings.Add(rw);
            }

            p.Recalc();
            return p;
        }

        // ---------------- 体积清点（带缓存：5 个方案只扫一遍）----------------

        internal class PathStats
        {
            public bool Exists;
            public bool IsDir;
            public long Bytes;
            public int Files;
        }

        private static readonly Dictionary<string, PathStats> _stats =
            new Dictionary<string, PathStats>(StringComparer.OrdinalIgnoreCase);

        public static void ClearStats() { _stats.Clear(); }

        /// <summary>清点一个路径（结果缓存，避免切换方案时反复扫盘）。</summary>
        public static PathStats Stat(string path)
        {
            PathStats s;
            if (_stats.TryGetValue(path, out s)) return s;
            s = new PathStats();
            try
            {
                if (Directory.Exists(path))
                {
                    s.Exists = true; s.IsDir = true;
                    foreach (string f in Directory.GetFiles(path, "*", SearchOption.AllDirectories))
                    {
                        try { s.Bytes += new FileInfo(f).Length; s.Files++; } catch { }
                    }
                }
                else if (File.Exists(path))
                {
                    s.Exists = true; s.IsDir = false;
                    s.Bytes = new FileInfo(path).Length;
                    s.Files = 1;
                }
            }
            catch { }
            _stats[path] = s;
            return s;
        }

        private static UninstallTarget Make(string path, string label, TargetGroup group, bool isDir)
        {
            UninstallTarget t = new UninstallTarget();
            t.Path = path;
            t.Label = label;
            t.Group = group;
            t.IsDirectory = isDir;
            PathStats s = Stat(path);
            t.Exists = s.Exists;
            if (s.Exists) { t.IsDirectory = s.IsDir; t.Bytes = s.Bytes; t.Files = s.Files; }
            return t;
        }

        // ---------------- 执行 ----------------

        /// <summary>
        /// 执行计划。返回报告文本；同时给出成功/失败计数。
        /// onProgress 在「每一项开始前」与「每一项结束后」各回调一次，**可能在工作线程上** ——
        /// 调用方负责 marshal 回 UI 线程。传 null 表示不需要进度。
        /// </summary>
        public static string Execute(UninstallPlan plan, UninstallOptions opt, LogHandler log,
                                     Action<UninstallProgress> onProgress,
                                     out int okCount, out int failCount)
        {
            okCount = 0; failCount = 0;
            StringBuilder report = new StringBuilder();
            report.AppendLine("DSH 启动器 · 卸载报告");
            report.AppendLine("时间      : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            report.AppendLine("方案      : " + plan.Headline);
            report.AppendLine("删除方式  : " + (opt.Permanent ? "永久删除（不送回收站）" : "送入回收站（可还原）"));
            report.AppendLine();

            // 卸载前备份私密小文件
            if (opt.BackupPrivate)
            {
                string bak = BackupPrivate(plan);
                if (!string.IsNullOrEmpty(bak))
                {
                    report.AppendLine("== 已备份 ==");
                    report.AppendLine(bak);
                    report.AppendLine();
                    if (log != null) log("已备份凭据与配置：" + bak, LogLevel.Good);
                }
            }

            // 先算清楚要处理多少项 / 多少字节，进度才有分母
            int total = 0;
            long bytesTotal = 0;
            foreach (UninstallTarget t0 in plan.Targets)
            {
                if (t0.Action != TargetAction.Delete || !t0.Exists) continue;
                total++;
                bytesTotal += t0.Bytes;
            }
            int done = 0;
            long bytesDone = 0;

            report.AppendLine("== 已删除 ==");
            foreach (UninstallTarget t in plan.Targets)
            {
                if (t.Action != TargetAction.Delete) continue;
                if (!t.Exists) continue;

                if (onProgress != null)
                {
                    UninstallProgress pre = new UninstallProgress();
                    pre.Done = done; pre.Total = total;
                    pre.BytesDone = bytesDone; pre.BytesTotal = bytesTotal;
                    pre.CurrentLabel = t.Label;
                    pre.CurrentPath = t.Path;
                    onProgress(pre);
                }

                string err;
                bool deleted = DeleteTarget(t, opt.Permanent, out err);
                if (deleted)
                {
                    okCount++;
                    report.AppendLine("  [OK]   " + t.Path + "   (" + t.SizeText + ")");
                    if (log != null) log("已删除：" + t.Path, LogLevel.Good);
                }
                else
                {
                    failCount++;
                    report.AppendLine("  [失败] " + t.Path);
                    report.AppendLine("         " + err);
                    if (log != null) log("删除失败：" + t.Path + " —— " + err, LogLevel.Bad);
                }
                done++;
                bytesDone += t.Bytes;

                if (onProgress != null)
                {
                    UninstallProgress post = new UninstallProgress();
                    post.Done = done; post.Total = total;
                    post.BytesDone = bytesDone; post.BytesTotal = bytesTotal;
                    post.CurrentLabel = t.Label;
                    post.CurrentPath = t.Path;
                    onProgress(post);
                }
            }

            report.AppendLine();
            report.AppendLine("== 已保留 ==");
            foreach (UninstallTarget t in plan.Targets)
            {
                if (t.Action != TargetAction.Keep) continue;
                report.AppendLine("  [保留] " + t.Path + (t.Exists ? "   (" + t.SizeText + ")" : "   (不存在)"));
            }

            report.AppendLine();
            report.AppendLine("== 没有动过的东西（已识别为非 DSH 或属于你的个人文件）==");
            foreach (UninstallTarget t in plan.Targets)
            {
                if (t.Action != TargetAction.Skip) continue;
                report.AppendLine("  [未动] " + t.Path + (t.Note.Length > 0 ? "   —— " + t.Note : ""));
            }
            AppConfig cfgNow = null;
            try { cfgNow = AppConfig.Load(); } catch { }
            foreach (UninstallTarget t in Protected(cfgNow))
                report.AppendLine("  [未动] " + t.Path + (t.Note.Length > 0 ? "   —— " + t.Note : ""));

            report.AppendLine();
            report.AppendLine("成功 " + okCount + " 项，失败 " + failCount + " 项。");
            return report.ToString();
        }

        private static bool DeleteTarget(UninstallTarget t, bool permanent, out string error)
        {
            error = "";
            if (!IsAllowed(t.Path)) { error = "内部护栏拒绝删除该路径（不在白名单内）。"; return false; }
            try
            {
                if (t.IsDirectory && Directory.Exists(t.Path))
                {
                    // 特判：安装目录里可能有"正在运行的自己"，那是删不掉的 →
                    // 其余先删，本进程退出后再由一个隐藏 cmd 收尾。
                    string exe = AppPaths.ExePath;
                    string dirFull = Path.GetFullPath(t.Path).TrimEnd('\\');
                    string exeFull = Path.GetFullPath(exe);
                    if (exeFull.StartsWith(dirFull + "\\", StringComparison.OrdinalIgnoreCase))
                    {
                        foreach (string entry in Directory.GetFileSystemEntries(t.Path))
                        {
                            if (string.Equals(Path.GetFullPath(entry), exeFull, StringComparison.OrdinalIgnoreCase)) continue;
                            string e2;
                            if (!ShellDelete(entry, permanent, out e2)) error += (error.Length > 0 ? "；" : "") + Path.GetFileName(entry) + ": " + e2;
                        }
                        ScheduleSelfDelete(exeFull, dirFull);
                        return error.Length == 0;
                    }
                    string derr;
                    if (ShellDelete(t.Path, permanent, out derr)) return true;
                    return DeleteDirFallback(t.Path, permanent, derr, out error);
                }
                if (File.Exists(t.Path)) return ShellDelete(t.Path, permanent, out error);
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        /// <summary>
        /// 整目录删除的兜底：直接删目录失败时（目录里有文件被占用），退化成逐项删除，
        /// 把清不掉的那些如实报出来 —— 不能让"有文件在跑"导致整批一个都没删。
        /// </summary>
        private static bool DeleteDirFallback(string dir, bool permanent, string firstError, out string error)
        {
            error = "";
            StringBuilder left = new StringBuilder();
            int failed = 0;
            try
            {
                foreach (string entry in Directory.GetFileSystemEntries(dir))
                {
                    string e2;
                    if (!ShellDelete(entry, permanent, out e2))
                    {
                        failed++;
                        left.Append((left.Length > 0 ? "、" : "") + Path.GetFileName(entry));
                    }
                }
            }
            catch (Exception ex)
            {
                error = firstError + "；逐项删除也失败：" + ex.Message;
                return false;
            }
            try
            {
                if (Directory.Exists(dir) && Directory.GetFileSystemEntries(dir).Length == 0)
                    Directory.Delete(dir, false);
            }
            catch { }

            if (failed == 0 && !Directory.Exists(dir)) return true;
            error = firstError + "；逐项删除后还剩 " + failed + " 项未删掉"
                  + (left.Length > 0 ? "（" + left.ToString() + "，通常是被占用的可执行文件）" : "");
            return false;
        }

        /// <summary>
        /// 回收站配额预警：Windows 的回收站有容量上限，装不下时带 FOF_ALLOWUNDO 的删除
        /// 会被**静默改成永久删除**。这里按各盘总量的 5%（Windows 默认配额）估算。
        /// </summary>
        public static string RecycleCapacityWarning(UninstallPlan plan)
        {
            try
            {
                Dictionary<string, long> perDrive = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
                foreach (UninstallTarget t in plan.Targets)
                {
                    if (t.Action != TargetAction.Delete || !t.Exists || t.Bytes <= 0) continue;
                    string root = Path.GetPathRoot(Path.GetFullPath(t.Path));
                    if (string.IsNullOrEmpty(root)) continue;
                    long cur;
                    perDrive.TryGetValue(root, out cur);
                    perDrive[root] = cur + t.Bytes;
                }
                List<string> over = new List<string>();
                foreach (KeyValuePair<string, long> kv in perDrive)
                {
                    try
                    {
                        DriveInfo di = new DriveInfo(kv.Key);
                        long cap = (long)(di.TotalSize * 0.05);
                        if (kv.Value > cap)
                            over.Add(kv.Key + " 要删 " + Human(kv.Value) + "，超过回收站估计容量 " + Human(cap));
                    }
                    catch { }
                }
                if (over.Count == 0) return null;
                return "回收站可能装不下（" + string.Join("；", over.ToArray()) + "）。" +
                       "装不下时 Windows 会静默改为永久删除；如要确保可还原，请先清空回收站或改用「永久删除」。";
            }
            catch { return null; }
        }

        internal static string Human(long b)   // 卸载窗也用这一份，别再各写一遍
        {
            if (b >= 1073741824L) return (b / 1073741824.0).ToString("0.00") + " GB";
            if (b >= 1048576L) return (b / 1048576.0).ToString("0.0") + " MB";
            if (b >= 1024L) return (b / 1024.0).ToString("0.0") + " KB";
            return b + " B";
        }

        /// <summary>
        /// 白名单护栏：只允许删本类识别出来的那些位置的子路径。
        /// 任何"意外传入"的路径（比如工作区、盘根、用户目录本身）都会被拒绝。
        /// </summary>
        private static bool IsAllowed(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path) || path.Length < 8) return false;
                string full = Path.GetFullPath(path).TrimEnd('\\');
                string root = Path.GetPathRoot(full);
                if (string.IsNullOrEmpty(root)) return false;
                if (string.Equals(full, root.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase)) return false;

                string profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile).TrimEnd('\\');
                if (string.Equals(full, profile, StringComparison.OrdinalIgnoreCase)) return false;

                // 工作区永远不碰
                try
                {
                    AppConfig cfg = AppConfig.Load();
                    if (cfg != null && !string.IsNullOrEmpty(cfg.Workspace))
                    {
                        string ws = Path.GetFullPath(cfg.Workspace).TrimEnd('\\');
                        if (string.Equals(full, ws, StringComparison.OrdinalIgnoreCase)) return false;
                        if (full.StartsWith(ws + "\\", StringComparison.OrdinalIgnoreCase)) return false;
                    }
                }
                catch { }

                // npm 全局包目录只在"就是 @deepseek-ai 这一层"时才允许
                string gRoot = NpmGlobalRoot.TrimEnd('\\');
                if (full.StartsWith(gRoot + "\\", StringComparison.OrdinalIgnoreCase))
                {
                    string rel = full.Substring(gRoot.Length + 1);
                    return rel.StartsWith(@"node_modules\@deepseek-ai", StringComparison.OrdinalIgnoreCase);
                }

                return true;
            }
            catch { return false; }
        }

        // ---------------- 送回收站 / 永久删除 ----------------

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct SHFILEOPSTRUCT
        {
            public IntPtr hwnd;
            public uint wFunc;
            public string pFrom;
            public string pTo;
            public ushort fFlags;
            public bool fAnyOperationsAborted;
            public IntPtr hNameMappings;
            public string lpszProgressTitle;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern int SHFileOperation(ref SHFILEOPSTRUCT lpFileOp);

        private const uint FO_DELETE = 0x0003;
        private const ushort FOF_SILENT = 0x0004;
        private const ushort FOF_NOCONFIRMATION = 0x0010;
        private const ushort FOF_ALLOWUNDO = 0x0040;
        private const ushort FOF_NOERRORUI = 0x0400;

        /// <summary>用 shell 删除（默认进回收站；permanent=true 时永久删除）。</summary>
        private static bool ShellDelete(string path, bool permanent, out string error)
        {
            error = "";
            try
            {
                SHFILEOPSTRUCT op = new SHFILEOPSTRUCT();
                op.wFunc = FO_DELETE;
                op.pFrom = path + "\0\0";          // 需要双 NUL 结尾
                op.fFlags = (ushort)(FOF_NOCONFIRMATION | FOF_SILENT | FOF_NOERRORUI | (permanent ? 0 : FOF_ALLOWUNDO));
                int rc = SHFileOperation(ref op);
                if (rc != 0) { error = "SHFileOperation 返回 " + rc; return false; }
                if (op.fAnyOperationsAborted) { error = "操作被中止。"; return false; }
                // 复查
                if (Directory.Exists(path) || File.Exists(path))
                {
                    // 目录可能只剩空壳
                    if (Directory.Exists(path))
                    {
                        try
                        {
                            if (Directory.GetFileSystemEntries(path).Length == 0) { Directory.Delete(path, false); return true; }
                        }
                        catch { }
                        error = "删除后路径仍然存在（可能被占用）。";
                        return false;
                    }
                }
                return true;
            }
            catch (Exception ex) { error = ex.Message; return false; }
        }

        // ---------------- 备份 ----------------

        /// <summary>把凭据与配置这类小文件复制到桌面一个带时间戳的文件夹里。</summary>
        public static string BackupPrivate(UninstallPlan plan)
        {
            try
            {
                string dir = Path.Combine(DesktopDir, "DSH卸载备份-" + DateTime.Now.ToString("yyyyMMdd-HHmmss"));
                Directory.CreateDirectory(dir);
                StringBuilder sb = new StringBuilder();
                sb.AppendLine("备份目录：" + dir);
                sb.AppendLine();

                string cred = Path.Combine(DshHome, ".credentials.yaml");
                if (File.Exists(cred))
                {
                    File.Copy(cred, Path.Combine(dir, "credentials.yaml"), true);
                    sb.AppendLine("  已备份 " + cred);
                }
                string cfg = AppConfig.ConfigPath;
                if (File.Exists(cfg))
                {
                    File.Copy(cfg, Path.Combine(dir, "config.ini"), true);
                    sb.AppendLine("  已备份 " + cfg);
                }
                foreach (string extra in new string[] { ".dsh-usage-ledger.json", ".dsh-usage-stats.json", ".anonymous-user-id" })
                {
                    string f = Path.Combine(DshHome, extra);
                    if (File.Exists(f))
                    {
                        File.Copy(f, Path.Combine(dir, extra.TrimStart('.')), true);
                        sb.AppendLine("  已备份 " + f);
                    }
                }
                if (Directory.GetFiles(dir).Length == 0) return null;
                return sb.ToString().TrimEnd();
            }
            catch { return null; }
        }

        // ---------------- 报告与自我卸载 ----------------

        public static string WriteReport(string text)
        {
            try
            {
                string path = Path.Combine(DesktopDir, "DSH卸载报告-" + DateTime.Now.ToString("yyyyMMdd-HHmmss") + ".txt");
                File.WriteAllText(path, text, new UTF8Encoding(true));
                return path;
            }
            catch { return null; }
        }

        /// <summary>在资源管理器里定位文件（与「导出诊断日志」同一手法）。</summary>
        public static void RevealInExplorer(string filePath)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("explorer.exe", "/select,\"" + filePath + "\"");
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch { }
        }

        /// <summary>打开任务管理器（让用户自己核对还有没有残留进程）。</summary>
        public static void OpenTaskManager()
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo("taskmgr.exe");
                psi.UseShellExecute = true;
                Process.Start(psi);
            }
            catch { }
        }

        /// <summary>
        /// 延迟删除"正在运行的自己"：起一个隐藏的 cmd，等本进程退出后删掉 exe 与空目录。
        /// 卸载启动器时必须用它 —— 运行中的 exe 是删不掉的。
        /// </summary>
        public static void ScheduleSelfDelete(string exePath, string dir)
        {
            try
            {
                string cmd = "/c ping -n 4 127.0.0.1 > nul & del /f /q \"" + exePath + "\"";
                if (!string.IsNullOrEmpty(dir)) cmd += " & rmdir \"" + dir + "\" 2>nul";
                ProcessStartInfo psi = new ProcessStartInfo(
                    Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System), "cmd.exe"), cmd);
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WindowStyle = ProcessWindowStyle.Hidden;
                Process.Start(psi);
            }
            catch { }
        }
    }
}
