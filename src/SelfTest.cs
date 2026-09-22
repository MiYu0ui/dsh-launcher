using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace DshLauncher
{
    /// <summary>
    /// 自检：真实启动一次服务，验证"全程没有任何黑色命令行窗口"。
    /// 只在临时工作目录 + 自选端口上运行（默认不碰配置里的 3080），不会影响正在使用中的服务。
    /// </summary>
    /// <remarks>
    /// 报告有两个去处：stdout（先挂上父进程的控制台才看得见）与 --selftest 指定路径的报告文件；
    /// 两者都会先过一遍 SecretMask，因为这份报告经常被直接贴出来。
    /// 退出码就是唯一判据：0 = PASS，1 = FAIL —— build.ps1 与 CI 都看它。
    /// </remarks>
    internal static class SelfTest
    {
        /// <summary>
        /// 把本进程挂到父进程的控制台上。本程序是 GUI 子系统，自身没有控制台，
        /// 不挂的话 Console.WriteLine 出来的报告在命令行里根本看不见。
        /// 挂不上（例如父进程也没有控制台）就忽略，报告文件仍然是可靠出路。
        /// </summary>
        [DllImport("kernel32.dll")]
        private static extern bool AttachConsole(int dwProcessId);

        // AttachConsole 的约定值：-1 表示"父进程的控制台"
        private const int AttachParentProcess = -1;

        /// <summary>
        /// 跑一次自检：拉起真实服务 → 等端口就绪 → 前后对比可见控制台窗口 → 干净停掉 → 汇总成报告。
        /// 全程只使用临时工作目录与自选端口，不碰用户正在用的那份服务。
        /// </summary>
        /// <param name="a">命令行参数：--port / --mode / --verify 会覆盖默认值，--selftest 后的路径决定报告写到哪。</param>
        /// <returns>进程退出码：0 = PASS，1 = FAIL。</returns>
        public static int Run(Args a)
        {
            try { AttachConsole(AttachParentProcess); } catch { }

            List<string> lines = new List<string>();
            bool pass = true;
            DshServer server = null;
            string workspace = Path.Combine(Path.GetTempPath(), "dsh-launcher-selftest");

            try
            {
                Directory.CreateDirectory(workspace);
                // 端口优先用 --port 指定的，否则现找一个空闲端口 —— 这样不会撞上本机正在运行的服务。
                int port = a.Port > 0 ? a.Port : FreePort();

                AppConfig cfg = new AppConfig();
                cfg.Workspace = workspace;
                cfg.Port = port;
                cfg.AutoOpenBrowser = false;
                // 只有显式 --verify 才开核验：自检要回答的是"服务能不能起来、有没有黑窗"，
                // 不该因为一次网络抖动把整个自检判成 FAIL。
                cfg.VerifyIntegrity = a.Verify;
                if (a.Mode == "npx") cfg.LaunchMode = "npx";
                else if (a.Mode == "direct") cfg.LaunchMode = "direct";
                // 没给 --mode 时按"本地有没有缓存入口"自动选，和主程序的默认策略保持一致。
                else cfg.LaunchMode = DshLocator.FindCachedEntry(cfg) != null ? "direct" : "npx";

                lines.Add("DSH 启动器自检报告");
                lines.Add("时间      : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
                lines.Add("程序      : " + AppPaths.ExePath);
                lines.Add("端口      : " + port);
                lines.Add("工作目录  : " + workspace);
                lines.Add("启动方式  : " + cfg.LaunchMode);
                lines.Add("完整性核验: " + (cfg.VerifyIntegrity ? "启用" : "关闭"));
                lines.Add("");

                List<ConsoleWindowInfo> before = WindowProbe.VisibleConsoleWindows();
                lines.Add("[1] 启动前可见的控制台窗口：" + before.Count + " 个");
                foreach (ConsoleWindowInfo w in before) lines.Add("      · " + w);

                server = new DshServer();
                StringBuilder serviceLog = new StringBuilder();
                // 服务的输出只保留最近一段：攒到 8000 字符就从**头部**丢 4000 字符，
                // 长时间跑也不会把内存吃掉；报告里只截尾部若干行。
                server.Log += delegate(string message, LogLevel level)
                {
                    lock (serviceLog)
                    {
                        serviceLog.AppendLine("      | " + message);
                        if (serviceLog.Length > 8000) serviceLog.Remove(0, 4000);
                    }
                };

                lines.Add("");
                lines.Add("[2] 启动服务（隐藏窗口）…");
                server.StartAsync(cfg);
                // 最多等 5 分钟：首次 npx 安装可能要下载很久。
                // 超时本身不算失败 —— 后面那次 HTTP 探测才是"服务到底起没起来"的判据。
                bool terminal = WaitTerminal(server, 300000);
                lines.Add("      最终状态：" + server.Status + "（等待 " + (terminal ? "结束" : "超时") + "）");

                string detail;
                ProbeState probe = DshServer.Probe(port, out detail);
                bool httpOk = probe == ProbeState.Dsh;
                lines.Add("      HTTP 探测：" + detail);
                if (!httpOk)
                {
                    pass = false;
                    lines.Add("      [失败] 服务未能在该端口就绪");
                }
                else
                {
                    lines.Add("      [通过] 服务可访问：" + DshServer.NormalizeUrl(port));
                }

                lines.Add("");
                lines.Add("[3] 启动后可见的控制台窗口检查");
                List<ConsoleWindowInfo> after = WindowProbe.VisibleConsoleWindows();
                List<ConsoleWindowInfo> added = WindowProbe.NewWindows(before, after);
                lines.Add("      启动过程中新增的可见控制台窗口：" + added.Count + " 个（期望 0）");
                foreach (ConsoleWindowInfo w in added) lines.Add("      [失败] 出现了黑窗 → " + w);
                if (added.Count > 0) pass = false;
                else lines.Add("      [通过] 没有任何黑色命令行窗口弹出");

                lines.Add("");
                lines.Add("[4] 孙进程检查：模拟 DSH 调用命令行工具（隐藏 node → powershell）");
                int grandchildWindows = GrandchildWindowTest(workspace);
                lines.Add("      子进程运行期间新增的可见控制台窗口：" + grandchildWindows + " 个（期望 0）");
                if (grandchildWindows > 0) { pass = false; lines.Add("      [失败] 工具调用会弹出黑窗"); }
                else lines.Add("      [通过] 工具调用同样不会弹出窗口");

                lines.Add("");
                lines.Add("[5] 服务输出（尾部）：");
                lock (serviceLog)
                {
                    string[] serviceLines = serviceLog.ToString().Split('\n');
                    int start = Math.Max(0, serviceLines.Length - 14);
                    for (int i = start; i < serviceLines.Length; i++)
                        if (serviceLines[i].Trim().Length > 0) lines.Add(serviceLines[i].TrimEnd());
                }

                // 只有服务确实活着才验证"停止 + 端口释放"；已经失败或端口冲突时这一项没有意义。
                if (server != null && (server.Status == ServerStatus.Running || server.Status == ServerStatus.External))
                {
                    lines.Add("");
                    lines.Add("[6] 停止服务…");
                    server.Stop();
                    Thread.Sleep(1200);
                    bool down = !DshServer.TcpAlive(port);
                    lines.Add("      端口已释放：" + (down ? "是" : "否"));
                    if (!down) { pass = false; lines.Add("      [失败] 服务没有完全停止"); }
                    else lines.Add("      [通过] 服务已干净退出");
                }

                lines.Add("");
                lines.Add(pass ? "结论：PASS —— 全程无黑色命令行窗口，服务可正常启动与停止。"
                              : "结论：FAIL —— 见上面的 [失败] 行。");
            }
            catch (Exception ex)
            {
                pass = false;
                lines.Add("");
                lines.Add("自检异常：" + ex);
            }
            finally
            {
                try { if (server != null && server.Owned) server.Stop(); } catch { }
            }

            string report = SecretMask.Apply(string.Join(Environment.NewLine, lines.ToArray()));   // 报告可能被贴出来，先脱敏
            Console.WriteLine(report);
            if (!string.IsNullOrEmpty(a.SelfTestResult))
            {
                try
                {
                    // 报告路径来自 --selftest 之后的第一个非 - 参数（build.ps1 传的是 build\selftest-report.txt）。
                    // 刻意写带 BOM 的 UTF-8：记事本一类工具据此认出编码，中文不会变乱码。
                    File.WriteAllText(a.SelfTestResult, report, new UTF8Encoding(true));
                    Console.WriteLine("报告已写入：" + a.SelfTestResult);
                }
                catch (Exception ex) { Console.WriteLine("写报告失败：" + ex.Message); }
            }
            return pass ? 0 : 1;
        }

        /// <summary>轮询到终态或超时；返回是否等到了终态（返回 false 只表示超时，不等于服务失败）。</summary>
        private static bool WaitTerminal(DshServer server, int timeoutMs)
        {
            DateTime deadline = DateTime.Now.AddMilliseconds(timeoutMs);
            while (DateTime.Now < deadline)
            {
                ServerStatus st = server.Status;
                if (st == ServerStatus.Running || st == ServerStatus.External ||
                    st == ServerStatus.Failed || st == ServerStatus.PortConflict) return true;
                Thread.Sleep(300);
            }
            return false;
        }

        /// <summary>用 node 拉起一个 powershell（与 DSH 调用工具的方式一致），检查是否冒出窗口。</summary>
        /// <remarks>
        /// 脚本故意用 stdio: inherit 让 powershell 继承 node 的控制台 —— 换成管道就复现不出黑窗了。
        /// node 找不到时直接返回 0（这一项不适用，不算失败）；等 700ms 让 powershell 真起来再数窗口，
        /// 进程最多再等 8 秒，之后强杀。
        /// </remarks>
        private static int GrandchildWindowTest(string workspace)
        {
            try
            {
                AppConfig cfg = new AppConfig();
                string node = DshLocator.FindNode(cfg);
                if (node == null) return 0;

                string script = Path.Combine(workspace, "child-window-test.js");
                File.WriteAllText(script,
                    "const { spawnSync } = require('child_process');\r\n" +
                    "spawnSync('powershell.exe', ['-NoProfile', '-Command', 'Start-Sleep -Milliseconds 1400'], { stdio: 'inherit' });\r\n",
                    new UTF8Encoding(false));

                List<ConsoleWindowInfo> before = WindowProbe.VisibleConsoleWindows();

                ProcessStartInfo psi = new ProcessStartInfo(node, "\"" + script + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                psi.WorkingDirectory = workspace;
                psi.RedirectStandardOutput = true;
                psi.RedirectStandardError = true;

                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.Start();
                    Thread.Sleep(700);                       // 等 powershell 真正起来
                    List<ConsoleWindowInfo> during = WindowProbe.VisibleConsoleWindows();
                    int count = WindowProbe.NewWindows(before, during).Count;
                    try { p.WaitForExit(8000); } catch { }
                    try { if (!p.HasExited) p.Kill(); } catch { }
                    return count;
                }
            }
            catch { return 0; }
        }

        /// <summary>
        /// 让系统分配一个空闲端口：绑 0 号端口问内核要，拿到端口号后立刻释放。
        /// 释放到别人占用之间有个理论上的竞争窗口，自检场景够用；连试 30 次仍拿不到就退回 3099。
        /// </summary>
        private static int FreePort()
        {
            for (int attempt = 0; attempt < 30; attempt++)
            {
                try
                {
                    TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
                    listener.Start();
                    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
                    listener.Stop();
                    if (port > 1024) return port;
                }
                catch { }
            }
            return 3099;
        }
    }
}
