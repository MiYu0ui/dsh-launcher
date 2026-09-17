using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace DshLauncher
{
    internal static class Program
    {
        private static Mutex _instanceMutex;

        private static bool ShouldPlayBoot(Args args)
        {
            if (args.AutoStart || args.Minimized || args.SelfTest || args.NoBoot) return false;
            try { return AppConfig.Load().BootAnimation; }
            catch { return true; }
        }

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
