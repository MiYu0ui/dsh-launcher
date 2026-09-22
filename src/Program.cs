using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DshLauncher
{
    /// <summary>
    /// 进程入口与启动顺序：解析参数 → 自检分流 → 设定界面缩放 → 抢占单实例 → 跑主消息循环。
    /// </summary>
    /// <remarks>
    /// 主循环里只有 <see cref="LauncherContext"/>；开启动画、主窗口、托盘都由它按时间轴驱动，
    /// 不另起 <c>Application.Run</c>（原因见 Main 里的注释）。
    /// </remarks>
    internal static class Program
    {
        /// <summary>
        /// 单实例互斥体。抢占成功才持有，退出前在 <c>finally</c> 里释放；
        /// 抢不到时会被置回 null，所以不能假设它一定非空。
        /// </summary>
        private static Mutex _instanceMutex;

        /// <summary>这次启动要不要放开场动画。</summary>
        /// <remarks>
        /// 自动启动、只进托盘、自检、显式 <c>--no-boot</c> 都不放 —— 这几种场合用户要么不在看，要么不该被打扰。
        /// 其余情况看配置项；配置读失败时按"放"处理，宁可多放一次动画也不要让界面显得没起来。
        /// </remarks>
        private static bool ShouldPlayBoot(Args args)
        {
            if (args.AutoStart || args.Minimized || args.SelfTest || args.NoBoot) return false;
            try { return AppConfig.Load().BootAnimation; }
            catch { return true; }
        }

        /// <summary>进程入口。</summary>
        /// <param name="argv">原始命令行（已去掉 exe 名），交给 <see cref="Args.Parse"/> 解析。</param>
        /// <returns>0 表示正常退出（含"已有实例在跑，本进程只负责把它的窗口叫出来"）；1 表示启动过程抛异常。</returns>
        /// <remarks>
        /// <c>--help</c> 与 <c>--selftest</c> 在创建任何界面之前就分流出去，包括不抢单实例锁 ——
        /// 自检要能在服务正跑着的时候执行。
        /// 单实例：默认只试一次，更新 / 迁移安装位置后的重启会重试 60 次（每 250ms 一次），
        /// 等旧实例把锁交出来。抢不到且不是自动启动时，就敲一下"显示窗口"事件把已有实例叫到前台。
        /// </remarks>
        [STAThread]
        private static int Main(string[] argv)
        {
            Args args = Args.Parse(argv);

            if (args.Help)
            {
                MessageBox.Show(Args.Usage(), "DSH 启动器", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return 0;
            }

            if (args.SelfTest) return SelfTest.Run(args);

            float scale = 1f;
            try
            {
                using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;
            }
            catch { }
            Theme.InitScale(scale);

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            bool created = false;
            int attempts = args.WaitForPreviousInstance ? 60 : 1;   // 更新 / 迁移安装位置后重启：等旧实例交出单实例锁
            for (int i = 0; i < attempts; i++)
            {
                _instanceMutex = new Mutex(true, Args.MutexName, out created);
                if (created) break;
                try { _instanceMutex.Close(); } catch { }
                _instanceMutex = null;
                Thread.Sleep(250);
            }
            if (!created)
            {
                // 已经有实例在跑：让它把窗口显示出来（开机自启模式不打扰用户）
                if (!args.AutoStart)
                {
                    try
                    {
                        EventWaitHandle signal = EventWaitHandle.OpenExisting(Args.ShowSignalName);
                        signal.Set();
                        signal.Close();
                    }
                    catch { }
                }
                return 0;
            }

            // ⚠️ 这里**不能**清 .old：新版本才刚进 Main，还没证明自己能起来。
            // 清理挪到主窗口真正显示出来之后（LauncherContext.RevealMainForm）——
            // 否则新版本自身有 bug 时，唯一的回退副本会被当场删掉，用户再也退不回去。
            FileLog.Write("=== 启动器启动 v" + BuildInfo.Version + " args=[" + string.Join(" ", argv) + "] dpi=" + scale.ToString("0.00") + " ===");

            try
            {
                // 开启动画由主消息循环驱动（非模态）：注意不能用 Application.Run(splash) 再 Run(ctx)，
                // 因为前一个循环结束时 ExitThread 会把这个线程上的所有窗口一并关掉（连主窗口一起没）。
                bool playBoot = ShouldPlayBoot(args);
                LauncherContext ctx = new LauncherContext(args, playBoot);
                Application.Run(ctx);
            }
            catch (Exception ex)
            {
                FileLog.Write("FATAL: " + ex);
                MessageBox.Show("启动器发生错误：\r\n" + ex.Message + "\r\n\r\n详细信息见日志：\r\n" + FileLog.Path,
                    "DSH 启动器", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return 1;
            }
            finally
            {
                try { if (_instanceMutex != null) _instanceMutex.ReleaseMutex(); } catch { }
            }
            return 0;
        }
    }
}
