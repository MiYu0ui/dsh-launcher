using System;
using System.Diagnostics;
using System.IO;

namespace DshLauncher
{
    internal class UpdateInfo
    {
        public bool Available;
        public string CurrentVersion = "";
        public string LatestVersion = "";
        public string CandidatePath = "";
        public string Message = "";
        public DateTime CandidateTime;
        public string DownloadUrl = "";     // 非空表示走 GitHub Releases 下载
        public string DownloadSha256 = "";
    }

    /// <summary>
    /// 版本检测与更新。
    /// 版本号来自构建时写入的 BuildInfo（同时写进 exe 的文件版本，便于比对）。
    /// 更新来源：源码目录里的构建产物（重新构建后启动器即可一键升级到该版本）。
    /// </summary>
    internal static class UpdateCheck
    {
        public static string CurrentVersion { get { return BuildInfo.Version; } }
        public static string CurrentStamp { get { return BuildInfo.BuildStamp; } }

        public static string CandidateExePath
        {
            get { return Path.Combine(BuildInfo.SourceDir, @"build\DSH Launcher.exe"); }
        }

        public static UpdateInfo Check()
        {
            UpdateInfo info = new UpdateInfo();
            info.CurrentVersion = BuildInfo.Version;
            info.LatestVersion = BuildInfo.Version;
            info.Message = "已是最新版本 v" + BuildInfo.Version + "（构建于 " + BuildInfo.BuildStamp + "）";

            // ① 配了 repo.txt → 优先查 GitHub Releases
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
                        info.Message = "发现新版本 v" + rel.Version + "（GitHub Releases · " +
                                       BuildInfo.RepoSlug + "）";
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

        private static int ParseInt(string s)
        {
            int v;
            return int.TryParse((s ?? "").Trim(), out v) ? v : 0;
        }

        private static DateTime ParseStamp(string s)
        {
            DateTime t;
            return DateTime.TryParse(s, out t) ? t : DateTime.MinValue;
        }

        /// <summary>
        /// 用新版本替换当前 exe。运行中的 exe 允许改名，改名后把新文件放到原路径，
        /// 旧文件（正在运行的那个）留待下次启动清理。
        /// </summary>
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
                rel.ShaUrl = DeriveShaUrl(info.DownloadUrl);
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
            try
            {
                if (File.Exists(old)) { try { File.Delete(old); } catch { } }
                File.Move(installed, old);
            }
            catch (Exception ex)
            {
                error = "无法重命名当前程序：" + ex.Message;
                return false;
            }

            try
            {
                File.Copy(info.CandidatePath, installed, true);
            }
            catch (Exception ex)
            {
                error = "复制新版本失败：" + ex.Message;
                try { if (!File.Exists(installed)) File.Move(old, installed); } catch { }
                return false;
            }
            return true;
        }

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
