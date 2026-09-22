using System;

namespace DshLauncher
{
    /// <summary>
    /// 命令行参数。用法文本见 <see cref="Usage"/>，这份清单就是启动器的对外命令行契约。
    /// </summary>
    /// <remarks>
    /// 解析在 <see cref="Parse"/>：选项名与短名一律忽略大小写；带值的选项写在下一个 argv 里。
    /// 认不出来的参数被静默忽略，不会报错也不会提示 —— 老版本启动器收到新参数时应当照常启动。
    /// </remarks>
    internal class Args
    {
        /// <summary>
        /// 单实例互斥体名。带 <c>Local\</c> 前缀：作用域是当前登录会话，
        /// 这样不同用户各自开一份互不干扰（也意味着切换用户后可以再开一个）。
        /// </summary>
        public const string MutexName = @"Local\DshLauncher.SingleInstance";
        /// <summary>
        /// "把已有实例的窗口显示出来"事件名。第二个实例抢不到互斥体时打开并 Set 这个事件，
        /// 已有实例在后台线程上等它（<see cref="Program"/> 里用它来决定"再开一个"还是"叫醒原来那个"）。
        /// </summary>
        public const string ShowSignalName = @"Local\DshLauncher.ShowWindow";

        public bool AutoStart;      // 开机自启：静默启动服务，只在托盘
        public bool Minimized;      // 只进托盘，不自动启动服务
        public bool SelfTest;       // 自检模式
        public bool Verify;         // 自检时启用下载源完整性核验
        public bool Settings;       // 启动后直接打开设置窗口
        public bool NoBoot;         // 跳过开启动画
        public bool AfterUpdate;    // 更新后重启：先等旧实例释放单实例锁
        public bool AfterInstall;   // 迁移安装位置后重启：同样要等旧实例交出单实例锁
        public bool Help;
        public int Port;            // 覆盖端口（0 = 用配置）
        public string Dir = "";     // 覆盖工作目录
        public string Mode = "";    // 自检时的启动方式 direct/npx
        /// <summary>自检报告的输出路径（<c>--selftest</c> 后面那个可选的位置参数）；空串表示只打印不落盘。</summary>
        public string SelfTestResult = "";

        /// <summary>启动时要不要先等旧实例让出单实例锁（更新 / 迁移安装位置之后）。</summary>
        public bool WaitForPreviousInstance { get { return AfterUpdate || AfterInstall; } }

        /// <summary>解析命令行。</summary>
        /// <param name="argv">原始参数数组（已去掉 exe 名）；不会被修改。为 null 会抛 <see cref="NullReferenceException"/>。</param>
        /// <returns>解析结果；未出现的开关保持默认（false / 0 / 空串）。</returns>
        /// <remarks>
        /// 认得出的写法：<c>--autostart|-a</c>、<c>--minimized|-m</c>、<c>--help|-h|/?</c>、
        /// <c>--selftest|--self-test [报告路径]</c>、<c>--port N</c>、<c>--dir|--workspace 路径</c>、
        /// <c>--mode direct|npx</c>、<c>--verify</c>、<c>--settings</c>、<c>--no-boot</c>、
        /// <c>--after-update</c>、<c>--after-install</c>。
        /// 带值的选项只在后面确实还有参数时才取值；<c>--selftest</c> 的报告路径要求不以 <c>-</c> 开头，
        /// 否则视为没写（于是可以安全地写 <c>--selftest --verify</c> 而不吃掉后面的开关）。
        /// <c>--port</c> 的值解析失败时保持 0（用配置里的端口）。
        /// TODO(待确认): <c>--dir --verify</c> 这种写法会把 <c>--verify</c> 当成目录名（带值的选项不做
        /// "下一个是不是开关"的检查），当前调用方都没有这种用法。
        /// </remarks>
        public static Args Parse(string[] argv)
        {
            Args a = new Args();
            for (int i = 0; i < argv.Length; i++)
            {
                string s = argv[i];
                string lower = s.ToLowerInvariant();
                if (lower == "--autostart" || lower == "-a") a.AutoStart = true;
                else if (lower == "--minimized" || lower == "-m") a.Minimized = true;
                else if (lower == "--help" || lower == "-h" || lower == "/?") a.Help = true;
                else if (lower == "--selftest" || lower == "--self-test")
                {
                    a.SelfTest = true;
                    if (i + 1 < argv.Length && !argv[i + 1].StartsWith("-")) { a.SelfTestResult = argv[++i]; }
                }
                else if (lower == "--port" && i + 1 < argv.Length)
                {
                    int p;
                    if (int.TryParse(argv[++i], out p)) a.Port = p;
                }
                else if ((lower == "--dir" || lower == "--workspace") && i + 1 < argv.Length) a.Dir = argv[++i];
                else if (lower == "--mode" && i + 1 < argv.Length) a.Mode = argv[++i].ToLowerInvariant();
                else if (lower == "--verify") a.Verify = true;
                else if (lower == "--settings") a.Settings = true;
                else if (lower == "--no-boot") a.NoBoot = true;
                else if (lower == "--after-update") a.AfterUpdate = true;
                else if (lower == "--after-install") a.AfterInstall = true;
            }
            return a;
        }

        /// <summary>命令行帮助文本，由 <c>--help</c> 弹出。</summary>
        /// <returns>含用法与各开关说明的多行文本（换行用 <c>\r\n</c>，直接喂给 MessageBox）。</returns>
        public static string Usage()
        {
            return
                "DSH 启动器 —— DeepSeek Harness 图形化启动工具（无控制台窗口）\r\n\r\n" +
                "用法：\r\n" +
                "  DSH Launcher.exe                 打开启动器界面\r\n" +
                "  DSH Launcher.exe --autostart     后台静默启动服务（供开机自启使用，不弹窗）\r\n" +
                "  DSH Launcher.exe --minimized     只放进系统托盘，不显示窗口\r\n" +
                "  DSH Launcher.exe --settings      打开界面并直接进入设置\r\n" +
                "  DSH Launcher.exe --no-boot       跳过开启动画\r\n" +
                "  DSH Launcher.exe --port 3080     临时指定端口\r\n" +
                "  DSH Launcher.exe --dir D:\\项目   临时指定工作目录\r\n" +
                "  DSH Launcher.exe --after-update  更新后重启（先等旧实例交出单实例锁）\r\n" +
                "  DSH Launcher.exe --after-install 迁移安装位置后重启（同上）\r\n" +
                "  DSH Launcher.exe --selftest 报告.txt [--mode direct|npx] [--verify] [--port 3099]\r\n" +
                "                                   自检：验证启动过程不出现任何黑色命令行窗口\r\n";
        }
    }
}
