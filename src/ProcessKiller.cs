using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Management;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Text;

namespace DshLauncher
{
    /// <summary>一个进程的快照信息（含 DSH 归属判定）。</summary>
    internal class ProcInfo
    {
        public int Pid;
        public int ParentPid;
        public string Name = "";
        public string CommandLine = "";
        public DateTime StartTime = DateTime.MinValue;
        public int Port;              // 关联端口（0 = 未知）
        public int ChildCount;        // 直接子进程数
        public int RootPid;           // 同族树根（含 cmd/npx 外壳）
        public string RootName = "";

        public bool IsDshLike
        {
            get { return ProcessKiller.IsDshCommandLine(CommandLine); }
        }

        public string ShortCommand
        {
            get
            {
                string c = CommandLine == null ? "" : CommandLine;
                return c.Length <= 150 ? c : c.Substring(0, 150) + "…";
            }
        }
    }

    /// <summary>
    /// 进程识别与强制终止。
    /// 识别：端口 → PID（iphlpapi，IPv4/IPv6）→ 身份校验（命令行 + HTTP 特征）→ 往上找同族树根。
    /// 终止：从树根 taskkill /T /F，复查端口与进程；权限不足可降级到 UAC 提权重试。
    /// </summary>
    internal static class ProcessKiller
    {
        // ---- 端口 → PID ----
        // GetExtendedTcpTable 的三个入参常量：两个地址族 + 只取「监听态并按 PID 归属」的那张表
        /// <summary>IPv4 地址族（<c>AF_INET</c>）。</summary>
        private const int AF_INET = 2;
        /// <summary>IPv6 地址族（<c>AF_INET6</c>）。</summary>
        private const int AF_INET6 = 23;
        /// <summary>表类型：TCP 监听表，每行带 owning PID（<c>TCP_TABLE_OWNER_PID_LISTENER</c>）。</summary>
        private const int TCP_TABLE_OWNER_PID_LISTENER = 3;

        /// <summary>查询 TCP 表（iphlpapi）。首次传空指针只为拿所需缓冲区大小，返回 0 才是成功。</summary>
        [DllImport("iphlpapi.dll", SetLastError = true)]
        private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int pdwSize, bool bOrder,
                                                       int ulAf, int tableClass, int reserved);

        /// <summary>找出监听指定端口的进程 PID（先 IPv4 再 IPv6），没有则 0。</summary>
        public static int FindListenerPid(int port)
        {
            int pid = FindListenerPidInFamily(port, AF_INET, 24, 8, 20);
            if (pid != 0) return pid;
            return FindListenerPidInFamily(port, AF_INET6, 56, 20, 52);
        }

        private static int FindListenerPidInFamily(int port, int family, int rowSize, int portOffset, int pidOffset)
        {
            int size = 0;
            uint ret = GetExtendedTcpTable(IntPtr.Zero, ref size, false, family, TCP_TABLE_OWNER_PID_LISTENER, 0);
            if (ret != 0 && size <= 0) return 0;

            IntPtr buf = Marshal.AllocHGlobal(size);
            try
            {
                ret = GetExtendedTcpTable(buf, ref size, false, family, TCP_TABLE_OWNER_PID_LISTENER, 0);
                if (ret != 0) return 0;
                int count = Marshal.ReadInt32(buf);
                IntPtr row = (IntPtr)((long)buf + 4);
                for (int i = 0; i < count; i++)
                {
                    IntPtr p = (IntPtr)((long)row + (long)i * rowSize);
                    // 表里的端口是网络字节序（大端），x86 上按小端读进来后要手工交换高低字节
                    int rawPort = Marshal.ReadInt32(p, portOffset);
                    int p2 = ((rawPort & 0xFF) << 8) | ((rawPort >> 8) & 0xFF);   // 网络字节序
                    if (p2 == port)
                    {
                        int owner = Marshal.ReadInt32(p, pidOffset);
                        if (owner > 0) return owner;
                    }
                }
            }
            catch { }
            finally { Marshal.FreeHGlobal(buf); }
            return 0;
        }

        /// <summary>全量进程快照（WMI 一次查询拿全，避免反复开连接）。</summary>
        /// <remarks>
        /// 权限不足（例如 32 位进程读 64 位进程）或进程刚好退出时，个别字段可能取不到，
        /// 那种行按默认值留在列表里 —— 判身份时宁可漏判，也不要在这里抛异常。
        /// </remarks>
        public static List<ProcInfo> Snapshot()
        {
            List<ProcInfo> list = new List<ProcInfo>();
            try
            {
                using (ManagementObjectSearcher searcher =
                       new ManagementObjectSearcher("SELECT ProcessId,ParentProcessId,Name,CommandLine,CreationDate FROM Win32_Process"))
                using (ManagementObjectCollection rows = searcher.Get())
                {
                    foreach (ManagementBaseObject mo in rows)
                    {
                        ProcInfo p = new ProcInfo();
                        try
                        {
                            p.Pid = Convert.ToInt32(mo["ProcessId"]);
                            p.ParentPid = Convert.ToInt32(mo["ParentProcessId"]);
                            p.Name = Convert.ToString(mo["Name"]);
                            p.CommandLine = Convert.ToString(mo["CommandLine"]);
                            string created = Convert.ToString(mo["CreationDate"]);
                            if (!string.IsNullOrEmpty(created) && created.Length >= 14)
                                p.StartTime = ManagementDateTimeConverter.ToDateTime(created);
                        }
                        catch { }
                        list.Add(p);
                    }
                }
            }
            catch { }
            return list;
        }

        /// <summary>
        /// 命令行是否像 DSH 服务（判定用，宁可放过不可错杀）。
        ///
        /// ⚠️ 早先是"同时含子串 dsh 和 web"—— 任何路径里带 dsh 的 node/web 工具都会被命中，
        /// 再配合"一次确认全杀"就会误杀用户其它 node 进程。现在改成**锚定**：要么是
        /// @deepseek-ai/dsh 的包路径，要么是 `dsh web` 这种把 dsh 当命令名调用的形态。
        /// </summary>
        public static bool IsDshCommandLine(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return false;
            string c = cmd.ToLowerInvariant();

            // ① 包路径（正反斜杠两种写法都算）
            if (c.IndexOf("@deepseek-ai/dsh", StringComparison.Ordinal) >= 0) return true;
            if (c.IndexOf("@deepseek-ai\\dsh", StringComparison.Ordinal) >= 0) return true;
            // ② dsh 的入口脚本
            if (c.IndexOf("\\dsh\\lib\\bin.js", StringComparison.Ordinal) >= 0) return true;
            if (c.IndexOf("/dsh/lib/bin.js", StringComparison.Ordinal) >= 0) return true;
            // ③ 把 dsh 当命令名调用：dsh web / dsh.cmd" web / \dsh web（web 必须是独立参数）
            return System.Text.RegularExpressions.Regex.IsMatch(
                c, "(^|[\\s\"'\\\\(/])dsh(\\.(cmd|exe|ps1))?[\"']?\\s+web(\\s|$)");
        }

        /// <summary>往上找同族树根：父进程必须也是 DSH 族且是壳层进程，遇到终端/系统进程立即停。</summary>
        public static int FindTreeRoot(int pid, Dictionary<int, ProcInfo> map)
        {
            int cur = pid;
            for (int i = 0; i < 8; i++)
            {
                ProcInfo p;
                if (!map.TryGetValue(cur, out p)) break;
                ProcInfo parent;
                if (!map.TryGetValue(p.ParentPid, out parent)) break;
                if (!IsShellName(parent.Name)) break;
                if (!parent.IsDshLike) break;
                cur = parent.Pid;
            }
            return cur;
        }

        private static bool IsShellName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string n = name.ToLowerInvariant();
            return n == "node.exe" || n == "cmd.exe" || n == "conhost.exe" || n == "npx.exe" || n == "npx.cmd";
        }

        /// <summary>把一组进程按"同族树根"归并成若干实例。</summary>
        public static List<ProcInfo> FindDshInstances(out Dictionary<int, ProcInfo> map)
        {
            List<ProcInfo> all = Snapshot();
            map = new Dictionary<int, ProcInfo>();
            foreach (ProcInfo p in all) { try { map[p.Pid] = p; } catch { } }

            // 关联端口：把监听端口的 PID 标出来（只查几个常见端口，避免全表扫描）
            foreach (ProcInfo p in all)
            {
                if (!p.IsDshLike) continue;
                int port = PortFromCommandLine(p.CommandLine);
                if (port > 0) p.Port = port;
            }

            List<ProcInfo> roots = new List<ProcInfo>();
            HashSet<int> seen = new HashSet<int>();
            foreach (ProcInfo p in all)
            {
                if (!p.IsDshLike) continue;
                int root = FindTreeRoot(p.Pid, map);
                if (root == p.Pid && !seen.Contains(root))
                {
                    seen.Add(root);
                    ProcInfo rp;
                    if (map.TryGetValue(root, out rp))
                    {
                        // 端口从族里任意成员身上取（树根往往是 npx 包装，端口写在下层）
                        if (rp.Port == 0) rp.Port = PortOfFamily(root, map);
                        CountChildren(root, map, ref rp.ChildCount, 0);
                        roots.Add(rp);
                    }
                }
            }
            // 如果某族的根已被合并进别的族（理论上不会），兜底：把所有监听 3080 的也算上
            return roots;
        }

        /// <summary>在族里找一个带端口的成员反查是哪个族（树根自己没端口时用）。</summary>
        private static int PortOfFamily(int rootPid, Dictionary<int, ProcInfo> map)
        {
            foreach (KeyValuePair<int, ProcInfo> kv in map)
            {
                if (kv.Value.Port <= 0) continue;
                if (FindTreeRoot(kv.Key, map) == rootPid) return kv.Value.Port;
            }
            return 0;
        }

        /// <summary>递归统计整棵子树的进程数，累计到 <paramref name="count"/>（包含各级孙进程）。</summary>
        private static void CountChildren(int pid, Dictionary<int, ProcInfo> map, ref int count, int depth)
        {
            if (depth > 6) return;   // 深度上限：防环形父子关系把递归拖死
            foreach (KeyValuePair<int, ProcInfo> kv in map)
            {
                if (kv.Value.ParentPid == pid)
                {
                    count++;
                    CountChildren(kv.Key, map, ref count, depth + 1);
                }
            }
        }

        public static int PortFromCommandLine(string cmd)
        {
            if (string.IsNullOrEmpty(cmd)) return 0;
            int at = cmd.IndexOf("--port", StringComparison.OrdinalIgnoreCase);
            if (at < 0) return 0;
            int i = at + 6;
            while (i < cmd.Length && cmd[i] == ' ') i++;
            int start = i;
            while (i < cmd.Length && char.IsDigit(cmd[i])) i++;
            if (i <= start) return 0;
            int port;
            return int.TryParse(cmd.Substring(start, i - start), out port) ? port : 0;
        }

        /// <summary>识别某端口上是谁：返回进程信息，isDsh 表示身份校验是否通过。</summary>
        public static ProcInfo IdentifyPort(int port, out bool isDsh)
        {
            isDsh = false;
            int pid = FindListenerPid(port);
            if (pid <= 0) return null;

            ProcInfo found = null;
            List<ProcInfo> all = Snapshot();
            Dictionary<int, ProcInfo> map = new Dictionary<int, ProcInfo>();
            foreach (ProcInfo p in all) { try { map[p.Pid] = p; } catch { } }
            if (!map.TryGetValue(pid, out found)) return null;

            found.Port = port;
            CountChildren(pid, map, ref found.ChildCount, 0);
            found.RootPid = FindTreeRoot(pid, map);
            ProcInfo rp;
            if (map.TryGetValue(found.RootPid, out rp)) found.RootName = rp.Name;

            // 身份校验：命令行像 DSH，或 HTTP 探测返回 DSH 特征（两者任一即可）。
            // 之所以要 HTTP 兜底：命令行可能读不到（权限 / 已退出），那时端口仍在，探测就成了唯一线索。
            bool byCmd = found.IsDshLike;
            bool byHttp = false;
            try
            {
                string detail;
                byHttp = DshServer.Probe(port, out detail) == ProbeState.Dsh;
            }
            catch { }
            isDsh = byCmd || byHttp;
            return found;
        }

        /// <summary>
        /// 强制终止整棵进程树。
        /// </summary>
        /// <remarks>
        /// 用系统自带 taskkill 的 <c>/T /F</c>：<c>/T</c> 连子进程一起收，<c>/F</c> 不给优雅退出机会
        /// （DSH 服务常挂住不响应关闭消息）。<c>accessDenied</c> 只表示"输出里认出了拒绝访问"，
        /// 用来决定是否值得再走一次 UAC 提权，不代表真实错误码。
        /// </remarks>
        public static bool KillTree(int rootPid, out string error, out bool accessDenied)
        {
            error = "";
            accessDenied = false;
            try
            {
                string stdout, stderr;
                string taskkill = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe");
                int code = HiddenRunner.Run(taskkill, "/PID " + rootPid + " /T /F", null, null, 30000, out stdout, out stderr);
                string text = (stdout + " " + stderr).Trim();
                if (code == 0) return true;
                string lower = text.ToLowerInvariant();
                if (lower.Contains("access is denied") || lower.Contains("拒绝访问") || lower.Contains("access denied"))
                    accessDenied = true;
                error = text.Length > 0 ? text : ("taskkill 退出码 " + code);
                return false;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>
        /// 权限不足时用管理员权限重试（弹 UAC）。
        /// </summary>
        /// <remarks>这次走 ShellExecute + <c>runas</c>，拿不到子进程的 stdout / stderr，只能靠退出码判断。</remarks>
        public static bool KillTreeElevated(int rootPid, out string error)
        {
            error = "";
            try
            {
                string taskkill = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.System), "taskkill.exe");
                ProcessStartInfo psi = new ProcessStartInfo(taskkill, "/PID " + rootPid + " /T /F");
                psi.UseShellExecute = true;
                psi.Verb = "runas";
                using (Process p = Process.Start(psi))
                {
                    if (!p.WaitForExit(60000)) { error = "提权执行超时"; return false; }
                    if (p.ExitCode == 0) return true;
                    error = "提权 taskkill 退出码 " + p.ExitCode;
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = ex.Message;   // 用户在 UAC 上点了"否"也会走到这里
                return false;
            }
        }

        /// <summary>
        /// 进程是否已经不在。
        /// </summary>
        /// <remarks>
        /// 用"构造函数抛异常"当判据：PID 已消失时 <c>GetProcessById</c> 必然抛
        /// <c>ArgumentException</c>。反过来说，任何异常都当成"已退出"——终止流程里
        /// 这个方向的误判是安全的（顶多复查一次端口）。
        /// </remarks>
        public static bool ProcessGone(int pid)
        {
            try { Process.GetProcessById(pid); return false; }
            catch { return true; }
        }

        /// <summary>
        /// 端口是否已经空出来。
        /// </summary>
        /// <remarks>
        /// 只探测回环地址：这是给"等服务让出端口"用的，别人占着本机 127.0.0.1 也算没让。
        /// 300ms 上限是刻意的 —— 等待方在轮询，这里绝不能长时间阻塞；连不上 / 超时都算空闲。
        /// </remarks>
        public static bool PortFree(int port)
        {
            try
            {
                using (TcpClient c = new TcpClient())
                {
                    IAsyncResult ar = c.BeginConnect(IPAddress.Loopback, port, null, null);
                    bool ok = ar.AsyncWaitHandle.WaitOne(300);
                    if (!ok) return true;
                    try { c.EndConnect(ar); } catch { return true; }
                    return !c.Connected;
                }
            }
            catch { return true; }
        }

        /// <summary>拼一行给人看的进程摘要（PID / 名字 / 端口 / 子进程数 / 启动时间，取不到的项直接省略）。</summary>
        public static string Describe(ProcInfo p)
        {
            if (p == null) return "(未识别到进程)";
            StringBuilder sb = new StringBuilder();
            sb.Append("PID ").Append(p.Pid).Append("  ").Append(p.Name);
            if (p.Port > 0) sb.Append("  端口 ").Append(p.Port);
            if (p.ChildCount > 0) sb.Append("  子进程 ").Append(p.ChildCount).Append(" 个");
            if (p.StartTime != DateTime.MinValue) sb.Append("  启动于 ").Append(p.StartTime.ToString("MM-dd HH:mm:ss"));
            return sb.ToString();
        }
    }
}
