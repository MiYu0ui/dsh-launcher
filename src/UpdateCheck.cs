using System;
using System.Diagnostics;
using System.IO;

namespace DshLauncher
{
    /// <summary>
    /// 一次版本检测的结果：结论（Available / Message）加下载线索。
    /// 线索按来源只填其中一路：
    ///   · 走 GitHub Releases —— DownloadUrl / DownloadSha256 / DownloadShaUrl 非空，CandidatePath 由 Apply 下载落地后回填；
    ///   · 走本地构建产物   —— CandidatePath 指向源码目录 build\ 下的 exe，下载三兄弟留空。
    /// Available=false 时其余字段只用来拼 Message。
    /// </summary>
    internal class UpdateInfo
    {
        public bool Available;         // 有比当前更新的版本可以装
        public string CurrentVersion = "";      // 当前 exe 的版本（照抄 BuildInfo.Version）
        public string LatestVersion = "";       // 检测到的版本；没有更新时等于当前版本
        public string CandidatePath = "";       // 候选 exe 的落地路径（本地回退命中，或 GitHub 下载后的临时文件）
        public string Message = "";             // 给用户看的一句话结论；无更新时也要能读
        public DateTime CandidateTime;          // 候选版本的构建/发布时间：版本号相同时靠它判断谁更新
        public string DownloadUrl = "";     // 非空表示走 GitHub Releases 下载
        public string DownloadSha256 = "";  // Release 的 digest 字段（sha256:...），在 .sha256 资产缺失时当兜底校验值
        public string DownloadShaUrl = "";  // .sha256 资产的真实地址（比按名字猜更可靠）
    }

    /// <summary>
    /// 版本检测与更新。
    /// 版本号来自构建时写入的 BuildInfo（同时写进 exe 的文件版本，便于比对）。
    /// 更新来源有两条，Check() 里按优先级串行尝试：
    ///   ① GitHub Releases —— 只在有仓库坐标（BuildInfo.RepoSlug 或 exe 旁的 repo.txt）时启用；
    ///   ② 本地构建产物   —— 源码目录 build\ 下的 exe，重新构建后启动器即可一键升级到该版本。
    /// </summary>
    internal static class UpdateCheck
    {
        /// <summary>当前运行的这个 exe 的版本号（编译期常量）。</summary>
        public static string CurrentVersion { get { return BuildInfo.Version; } }
        /// <summary>当前 exe 的构建时间戳（编译期常量），用于"版本号相同但重建过"的比较。</summary>
        public static string CurrentStamp { get { return BuildInfo.BuildStamp; } }

        /// <summary>本地回退时的候选位置：源码目录里的 build\DSH Launcher.exe（只有开发机才有这条路径）。</summary>
        public static string CandidateExePath
        {
            get { return Path.Combine(BuildInfo.SourceDir, @"build\DSH Launcher.exe"); }
        }

        /// <summary>
        /// 检测有没有更新的版本。两次来源尝试**都不抛异常**，失败只体现为 Message 里的说明：
        ///   ① GitHub 那条路查得到仓库却查不通时，会记一笔再继续走 ②，不是直接放弃；
        ///   ② 本地那条路文件不在、读不出文件版本，都静默按"没有更新"处理。
        /// 返回值一定非 null；Available=false 也不代表出错，"已是最新"就是这种形态。
        /// </summary>
        public static UpdateInfo Check()
        {
            UpdateInfo info = new UpdateInfo();
            info.CurrentVersion = BuildInfo.Version;
            info.LatestVersion = BuildInfo.Version;
            info.Message = "已是最新版本 v" + BuildInfo.Version + "（构建于 " + BuildInfo.BuildStamp + "）";

            // ① 有仓库坐标（构建时注入的 BuildInfo.RepoSlug，或 exe 旁的 repo.txt）→ 优先查 GitHub Releases
            if (GitHubRelease.Enabled)
            {
                string gerr;
                GitHubRelease.Release rel = GitHubRelease.FetchLatest(out gerr);
                if (rel != null && rel.AssetUrl.Length > 0)
                {
                    info.LatestVersion = rel.Version;
                    info.CandidateTime = ParseStamp(rel.PublishedAt);
                    if (CompareVersion(rel.Version, BuildInfo.Version) > 0)
                    {
                        info.Available = true;
                        info.DownloadUrl = rel.AssetUrl;
                        info.DownloadSha256 = rel.Digest;
                        info.DownloadShaUrl = rel.ShaUrl;
                        info.Message = "发现新版本 v" + rel.Version + "（GitHub Releases · " +
                                       GitHubRelease.ResolveSlug() + "）";
                    }
                    else
                    {
                        info.Message = "已是最新版本 v" + BuildInfo.Version +
                                       "（GitHub 上为 v" + rel.Version + "，构建于 " + BuildInfo.BuildStamp + "）";
                    }
                    return info;
                }
                // 查得到仓库但没查通 → 记一笔，然后继续本地回退
                info.Message = "已是最新版本 v" + BuildInfo.Version +
                               "（GitHub 检查未完成：" + gerr + "）";
            }

            try
            {
                // ② 本地回退：源码目录里的 build\ 产物。这条路上任何一步不成立都只是"没有更新"，
                //    不写 Message、不报错 —— 普通用户机器上这个路径本来就不存在。
                string candidate = CandidateExePath;
                if (!File.Exists(candidate)) return info;

                info.CandidateTime = File.GetLastWriteTime(candidate);
                string ver = ReadExeVersion(candidate);
                if (ver.Length == 0) return info;

                int cmp = CompareVersion(ver, BuildInfo.Version);
                if (cmp > 0)
                {
                    info.Available = true;
                    info.LatestVersion = ver;
                    info.CandidatePath = candidate;
                    info.Message = "发现新版本 v" + ver + "（本地构建于 " + info.CandidateTime.ToString("yyyy-MM-dd HH:mm") + "）";
                }
                // 版本号一样但文件更新（改了代码没改版本号的重建）也算有更新。
                // 5 秒余量是给"同一次构建"留的：BuildStamp 取的是构建开始的时刻，
                // 而 exe 的写入时间总在编译结束之后，一点余量都不留会把当前这个 exe 自己判成新版。
                else if (cmp == 0)
                {
                    DateTime mine = ParseStamp(BuildInfo.BuildStamp);
                    if (mine != DateTime.MinValue && info.CandidateTime > mine.AddSeconds(5))
                    {
                        info.Available = true;
                        info.LatestVersion = ver;
                        info.CandidatePath = candidate;
                        info.Message = "本地有更新的构建 v" + ver + "（" + info.CandidateTime.ToString("yyyy-MM-dd HH:mm") + "）";
                    }
                }
            }
            catch { }
            return info;
        }

        /// <summary>读 exe 里的文件版本；先 FileVersion 后 ProductVersion，读不到返回 ""（不抛异常）。</summary>
        public static string ReadExeVersion(string path)
        {
            try
            {
                FileVersionInfo vi = FileVersionInfo.GetVersionInfo(path);
                string v = null;
                if (!string.IsNullOrEmpty(vi.FileVersion)) v = vi.FileVersion.Trim();
                else if (!string.IsNullOrEmpty(vi.ProductVersion)) v = vi.ProductVersion.Trim();
                if (string.IsNullOrEmpty(v)) return "";
                // "1.1.3.0" 这种末尾补零去掉，显示与比对都更干净
                while (v.EndsWith(".0")) v = v.Substring(0, v.Length - 2);
                return v;
            }
            catch { }
            return "";
        }

        /// <summary>
        /// 按点分段比较版本号：a 旧返回 -1、相同返回 0、a 新返回 1。缺的段按 0 补。
        /// 注意这里**只比数字段**：带预发布后缀的版本（如 1.0.0-rc.1）后缀会被当成 0 而忽略，
        /// 需要完整 SemVer 语义的地方用的是 DeployExtras.SemVer.Compare。
        /// </summary>
        public static int CompareVersion(string a, string b)
        {
            string[] pa = (a ?? "").Split('.');
            string[] pb = (b ?? "").Split('.');
            int n = Math.Max(pa.Length, pb.Length);
            for (int i = 0; i < n; i++)
            {
                int va = i < pa.Length ? ParseInt(pa[i]) : 0;
                int vb = i < pb.Length ? ParseInt(pb[i]) : 0;
                if (va != vb) return va < vb ? -1 : 1;
            }
            return 0;
        }

        /// <summary>release 资产里 .sha256 的地址就是 exe 地址后面加 .sha256（Actions 工作流按此命名上传）。</summary>
        private static string DeriveShaUrl(string assetUrl)
        {
            return string.IsNullOrEmpty(assetUrl) ? "" : assetUrl + ".sha256";
        }

        /// <summary>取版本号里的一段数字；不是数字（预发布后缀等）一律当 0。</summary>
        private static int ParseInt(string s)
        {
            int v;
            return int.TryParse((s ?? "").Trim(), out v) ? v : 0;
        }

        /// <summary>解析构建时间戳；解析不了返回 DateTime.MinValue，调用方据此跳过时间比较。</summary>
        private static DateTime ParseStamp(string s)
        {
            DateTime t;
            return DateTime.TryParse(s, out t) ? t : DateTime.MinValue;
        }

        /// <summary>
        /// 用新版本替换当前 exe。运行中的 exe 允许改名，改名后把新文件放到原路径，
        /// 旧文件（正在运行的那个）留待下次启动清理。
        /// </summary>
        /// <remarks>
        /// 顺序是刻意的：先把新版本**完整**复制成 .new（失败零损失），再让当前 exe 让位成 .old，
        /// 最后才把 .new 顶到原位。任何一步失败都会尽力把磁盘恢复成"旧版本仍可启动"的状态；
        /// 只有最后一步连回滚都失败时，才会在错误信息里给出需要手工改名的两个路径。
        /// </remarks>
        /// <param name="info">Check() 的结果，必须 Available 且至少有一路线索。</param>
        /// <param name="error">失败原因，直接可以展示给用户；成功时为空串。</param>
        /// <returns>替换成功返回 true —— 调用方接下来应当重启自己（见 RestartSelf）。</returns>
        public static bool Apply(UpdateInfo info, out string error)
        {
            error = "";
            if (info == null || !info.Available || (info.CandidatePath.Length == 0 && info.DownloadUrl.Length == 0))
            {
                error = "没有可用的更新。";
                return false;
            }

            // 走 GitHub：先下载到临时文件并校验 SHA-256，再沿用下面的替换流程
            if (info.DownloadUrl.Length > 0)
            {
                GitHubRelease.Release rel = new GitHubRelease.Release();
                rel.AssetUrl = info.DownloadUrl;
                rel.Digest = info.DownloadSha256;
                rel.ShaUrl = info.DownloadShaUrl.Length > 0 ? info.DownloadShaUrl : DeriveShaUrl(info.DownloadUrl);
                string target = Path.Combine(Path.GetTempPath(),
                    "DSH Launcher-" + info.LatestVersion + ".exe");
                string derr;
                string got = GitHubRelease.Download(rel, target, out derr);
                if (got.Length == 0)
                {
                    error = "下载新版本失败：" + derr;
                    return false;
                }
                info.CandidatePath = got;
            }

            string installed = AppPaths.ExePath;
            string old = installed + ".old";
            string staged = installed + ".new";

            // ① 先把新版本**完整**复制成 .new —— 这一步不碰现有 exe，失败零损失
            try
            {
                if (File.Exists(staged)) { try { File.Delete(staged); } catch { } }
                File.Copy(info.CandidatePath, staged, true);
            }
            catch (Exception ex)
            {
                error = "复制新版本失败（现有程序未受影响）：" + ex.Message;
                try { if (File.Exists(staged)) File.Delete(staged); } catch { }
                return false;
            }

            // ② 新版本已完整落地，再把当前 exe 让位成 .old
            try
            {
                if (File.Exists(old)) { try { File.Delete(old); } catch { } }
                File.Move(installed, old);
            }
            catch (Exception ex)
            {
                error = "无法重命名当前程序：" + ex.Message;
                try { if (File.Exists(staged)) File.Delete(staged); } catch { }
                return false;
            }

            // ③ 把 .new 顶到原位。失败时**无条件回滚** ——
            //    原来那句 `if (!File.Exists(installed)) File.Move(old, installed)` 在
            //    File.Copy 半途失败（磁盘满）留下半截 exe 时永远为假，结果安装路径上
            //    一个可执行文件都没有，而提示只说"复制失败"，用户以为程序还在。
            try
            {
                File.Move(staged, installed);
            }
            catch (Exception ex)
            {
                error = "替换新版本失败：" + ex.Message;
                try { if (File.Exists(installed)) File.Delete(installed); } catch { }
                try { File.Move(old, installed); }
                catch { error += "（回滚也失败：请手工把 " + old + " 改回 " + installed + "）"; }
                try { if (File.Exists(staged)) File.Delete(staged); } catch { }
                return false;
            }
            return true;
        }

        /// <summary>
        /// 用给定参数重启启动器本体。extraArgs 传 --after-update / --after-install 时，
        /// 新实例会先等旧实例交出单实例锁（见 Args.WaitForPreviousInstance）。
        /// 只报告有没有启动成功，不等待新实例、也不退出当前进程。
        /// </summary>
        public static bool RestartSelf(string extraArgs)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(AppPaths.ExePath, extraArgs);
                psi.UseShellExecute = false;
                psi.WorkingDirectory = AppPaths.InstallDir;
                Process.Start(psi);
                return true;
            }
            catch { return false; }
        }

        /// <summary>启动时清理上一轮更新留下的 .old 文件（那时它已不再被占用）。</summary>
        public static void CleanupLeftovers()
        {
            try
            {
                string old = AppPaths.ExePath + ".old";
                if (File.Exists(old)) File.Delete(old);
            }
            catch { }
        }
    }
}
