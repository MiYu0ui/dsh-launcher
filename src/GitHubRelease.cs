using System;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace DshLauncher
{
    /// <summary>
    /// GitHub Releases 更新源：查最新 release → 找 exe 资产与校验值 → 下载并校验 SHA-256。
    /// 不引第三方库（C# 5 / .NET Framework 4.8）：JSON 用"定位 + 就近回溯"的定向提取，
    /// 因为 release 资产的字段顺序是稳定的（name 在 browser_download_url 之前）。
    /// </summary>
    internal static class GitHubRelease
    {
        internal class Release
        {
            public string Tag = "";
            public string Version = "";        // tag 去掉前导 v
            public string PublishedAt = "";
            public string AssetName = "";
            public string AssetUrl = "";
            public string ShaUrl = "";         // 可选的 .sha256 资产
            public string Digest = "";         // API 的 digest 字段（sha256:...）
            public string Notes = "";
        }

        private const string ApiBase = "https://api.github.com/repos/";
        private const string ExeAsset = "DSH Launcher.exe";
        private const string ShaAsset = "DSH Launcher.exe.sha256";

        public static bool Enabled { get { return ResolveSlug().Length > 0; } }

        private static string _cachedSlug;

        /// <summary>
        /// 仓库坐标：优先取构建时注入的（CI 里由 github.repository 自动写好）；
        /// 为空时读 exe 同目录的 repo.txt —— 这样手动下载的 Release 包也能指向仓库，
        /// 不用为了改个坐标重新编译。
        /// </summary>
        public static string ResolveSlug()
        {
            if (!string.IsNullOrEmpty(BuildInfo.RepoSlug)) return BuildInfo.RepoSlug;
            if (_cachedSlug != null) return _cachedSlug;
            _cachedSlug = "";
            try
            {
                string beside = Path.Combine(AppPaths.InstallDir, "repo.txt");
                if (File.Exists(beside))
                {
                    string s = File.ReadAllText(beside).Trim();
                    if (s.Length > 0 && s.IndexOf('/') > 0) _cachedSlug = s;
                }
            }
            catch { }
            return _cachedSlug;
        }

        /// <summary>查询最新 release；失败返回 null 并通过 error 说明原因。</summary>
        public static Release FetchLatest(out string error)
        {
            error = "";
            if (!Enabled) { error = "未配置仓库"; return null; }

            string json;
            string slug = ResolveSlug();
            try { json = HttpGet(ApiBase + slug + "/releases/latest", 8000); }
            catch (WebException wex)
            {
                HttpWebResponse r = wex.Response as HttpWebResponse;
                if (r != null && (int)r.StatusCode == 404) { error = "仓库或 Release 不存在（404）"; return null; }
                error = "网络错误：" + wex.Message;
                return null;
            }
            catch (Exception ex) { error = ex.Message; return null; }

            Release rel = new Release();
            rel.Tag = Pick(json, "\"tag_name\"\\s*:\\s*\"([^\"]+)\"");
            rel.Version = rel.Tag.TrimStart('v', 'V');
            rel.PublishedAt = Pick(json, "\"published_at\"\\s*:\\s*\"([^\"]+)\"");
            rel.Notes = Pick(json, "\"body\"\\s*:\\s*\"([^\"]{0,400})");

            // 资产：每个资产的 name 出现在 browser_download_url 之前，据此就近配对
            FindAsset(json, ExeAsset, rel, false);
            FindAsset(json, ShaAsset, rel, true);
            if (rel.AssetUrl.Length == 0) error = "该 Release 里没有 " + ExeAsset + " 资产";
            return rel;
        }

        private static void FindAsset(string json, string wantName, Release rel, bool isSha)
        {
            int at = 0;
            while (true)
            {
                int u = json.IndexOf("\"browser_download_url\"", at, StringComparison.Ordinal);
                if (u < 0) return;
                int namePos = json.LastIndexOf("\"name\"", u, Math.Min(u, 700), StringComparison.Ordinal);
                if (namePos >= 0)
                {
                    string name = Pick(json.Substring(namePos, u - namePos), "\"name\"\\s*:\\s*\"([^\"]+)\"");
                    if (string.Equals(name, wantName, StringComparison.OrdinalIgnoreCase))
                    {
                        string url = Pick(json.Substring(u), "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"");
                        int d = json.IndexOf("\"digest\"", u, StringComparison.Ordinal);
                        string digest = "";
                        if (d > 0 && d - u < 900) digest = Pick(json.Substring(d), "\"digest\"\\s*:\\s*\"([^\"]+)\"");
                        if (isSha) { rel.ShaUrl = url; }
                        else { rel.AssetUrl = url; rel.AssetName = name; rel.Digest = digest; }
                        return;
                    }
                }
                at = u + 24;
            }
        }

        /// <summary>下载并校验，返回落地文件路径；任何一步失败都返回 "" 并说明原因。</summary>
        public static string Download(Release rel, string targetPath, out string error)
        {
            error = "";
            try
            {
                byte[] data = HttpGetBytes(rel.AssetUrl, 120000);
                if (data == null || data.Length < 1024) { error = "下载内容异常（过小）"; return ""; }

                string want = "";
                if (rel.ShaUrl.Length > 0)
                {
                    string txt = HttpGet(rel.ShaUrl, 15000);
                    if (!string.IsNullOrEmpty(txt))
                    {
                        string[] parts = txt.Trim().Split(new char[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length > 0) want = parts[0].Trim().ToLowerInvariant();
                    }
                }
                if (want.Length == 0 && rel.Digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                    want = rel.Digest.Substring(7).Trim().ToLowerInvariant();

                if (want.Length == 0)
                {
                    error = "该 Release 没有提供 SHA-256 校验值，出于安全考虑不予安装";
                    return "";
                }

                string got;
                using (SHA256 sha = SHA256.Create())
                using (MemoryStream ms = new MemoryStream(data))
                {
                    byte[] h = sha.ComputeHash(ms);
                    StringBuilder sb = new StringBuilder();
                    foreach (byte b in h) sb.Append(b.ToString("x2"));
                    got = sb.ToString();
                }
                if (!string.Equals(got, want, StringComparison.OrdinalIgnoreCase))
                {
                    error = "校验失败：期望 " + want.Substring(0, Math.Min(16, want.Length)) +
                            "…，实际 " + got.Substring(0, 16) + "…";
                    return "";
                }

                string dir = Path.GetDirectoryName(targetPath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllBytes(targetPath, data);
                return targetPath;
            }
            catch (Exception ex) { error = ex.Message; return ""; }
        }

        // ---------------- HTTP ----------------
        private static string HttpGet(string url, int timeoutMs)
        {
            byte[] b = HttpGetBytes(url, timeoutMs);
            return b == null ? null : Encoding.UTF8.GetString(b);
        }

        private static byte[] HttpGetBytes(string url, int timeoutMs)
        {
            HttpWebRequest req = (HttpWebRequest)WebRequest.Create(url);
            req.UserAgent = "DSH-Launcher/" + BuildInfo.Version;   // GitHub API 强制要求 UA
            req.Accept = "application/vnd.github+json";
            req.Timeout = timeoutMs;
            req.ReadWriteTimeout = timeoutMs;
            req.AllowAutoRedirect = true;
            using (HttpWebResponse res = (HttpWebResponse)req.GetResponse())
            using (Stream s = res.GetResponseStream())
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buf = new byte[65536];
                int n;
                while ((n = s.Read(buf, 0, buf.Length)) > 0) ms.Write(buf, 0, n);
                return ms.ToArray();
            }
        }

        private static string Pick(string text, string pattern)
        {
            try
            {
                System.Text.RegularExpressions.Match m =
                    System.Text.RegularExpressions.Regex.Match(text, pattern,
                        System.Text.RegularExpressions.RegexOptions.Singleline);
                if (m.Success && m.Groups.Count > 1) return Unescape(m.Groups[1].Value);
            }
            catch { }
            return "";
        }

        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\r\\n", " ").Replace("\\n", " ").Replace("\\\"", "\"").Replace("\\\\", "\\").Trim();
        }
    }
}
