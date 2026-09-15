using System;

namespace DshLauncher
{
    /// <summary>命令行参数。</summary>
    internal class Args
    {
        public const string MutexName = @"Local\DshLauncher.SingleInstance";
        public const string ShowSignalName = @"Local\DshLauncher.ShowWindow";

        public bool AutoStart;      // 开机自启：静默启动服务，只在托盘
        public bool Minimized;      // 只进托盘，不自动启动服务
        public bool SelfTest;       // 自检模式
        public bool Verify;         // 自检时启用下载源完整性核验
        public bool Settings;       // 启动后直接打开设置窗口
        public bool NoBoot;         // 跳过开启动画
        public bool AfterUpdate;    // 更新后重启：先等旧实例释放单实例锁
        public bool Help;
        public int Port;            // 覆盖端口（0 = 用配置）
        public string Dir = "";     // 覆盖工作目录
        public string Mode = "";    // 自检时的启动方式 direct/npx
        public string SelfTestResult = "";

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
            }
            return a;
        }

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
                "  DSH Launcher.exe --selftest 报告.txt [--mode direct|npx] [--verify] [--port 3099]\r\n" +
                "                                   自检：验证启动过程不出现任何黑色命令行窗口\r\n";
        }
    }
}
