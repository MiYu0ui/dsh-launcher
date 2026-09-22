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
        /// <summary>
        /// 一次 release 的解析结果。字段大多是"有就填、没有就空"，缺哪个都不影响流程：
        /// exe 资产地址（AssetUrl）才是硬条件，校验值可以来自 .sha256 资产或 API 的 digest。
        /// </summary>
        internal class Release
        {
            public string Tag = "";            // 原始 tag，形如 v1.7.2（发布工作流按这个格式打 tag）
            public string Version = "";        // tag 去掉前导的 v/V —— 对内一律用这个当版本号
            public string PublishedAt = "";    // 发布时间（原始字符串，由 UpdateCheck 解析成 DateTime）
            public string AssetName = "";      // 命中的资产名（GitHub 会把名字里的空格换成点，别拿它当文件名用）
            public string AssetUrl = "";       // exe 资产的直接下载地址
            public string ShaUrl = "";         // 可选的 .sha256 资产（约定是 exe 地址后面加 .sha256）
            public string Digest = "";         // API 的 digest 字段（sha256:...），.sha256 资产缺失时的兜底校验值
            public string Notes = "";          // release 正文前 400 字；目前只解析、不使用，留给诊断和以后做更新说明
        }

        private const string ApiBase = "https://api.github.com/repos/";   // 后面拼 slug + /releases/latest
        private const string ExeAsset = "DSH Launcher.exe";              // 要找的资产名；比对时见 SameAsset
        private const string ShaAsset = "DSH Launcher.exe.sha256";      // 发布工作流按这个固定名字上传校验值

        /// <summary>是否配了仓库坐标。没配就只能走"本地构建产物"那条更新路。</summary>
        public static bool Enabled { get { return ResolveSlug().Length > 0; } }

        // ResolveSlug 的缓存：null = 还没解析过，"" = 解析过但没有。否定结果也要缓存，
        // 否则每次更新检查都会去读一遍盘（并重复走一遍文件不存在的分支）。
        private static string _cachedSlug;

        /// <summary>
        /// 仓库坐标：优先取构建时注入的（CI 里由 github.repository 自动写好）；
        /// 为空时读 exe 同目录的 repo.txt —— 这样手动下载的 Release 包也能指向仓库，
        /// 不用为了改个坐标重新编译。
        /// </summary>
        /// <remarks>
        /// 结果在进程内缓存（见 _cachedSlug），所以 repo.txt 只在首次解析时读一次：
        /// 运行期往 exe 旁边放 repo.txt 不会生效，要重启启动器。
        /// 这里只做"看起来像 owner/repo"的粗校验（有斜杠且不在开头），仓库是否真的存在交给 404 去判。
        /// </remarks>
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

        /// <summary>查询最新 release（GET /releases/latest）；失败返回 null 并通过 error 说明原因。</summary>
        /// <remarks>
        /// 注意返回**非 null 也可能 error 非空**：找到了 release 但它没有 exe 资产时就是这样，
        /// 所以调用方除了判空，还要自己看 AssetUrl 有没有值（UpdateCheck.Check 就是这么做的）。
        /// 404 被单独识别出来，因为"仓库是 Private"在本项目里是已知且预期的情况。
        /// </remarks>
        /// <param name="error">失败或部分失败的原因，可直接展示给用户。</param>
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

        /// <summary>
        /// 在 releases JSON 里找出指定名字的资产并填进 rel（找不到就什么都不改）。
        /// 做法：先定位每个 browser_download_url，再回溯到**该资产对象的起点**
        /// （资产对象以它自己的 url 字段打头，形如 .../releases/assets/），
        /// 然后把 name 与 digest 的查找严格限制在这一段内 —— 不依赖任何固定字符窗口。
        /// </summary>
        /// <remarks>
        /// 这里原先用固定窗口："从 url 往回最多 700 字符找 name、再往后 900 字符找 digest"。
        /// GitHub 给资产对象加上 uploader 子对象（20 多个 URL 字段）后，
        /// name 到 browser_download_url 的距离涨到约 1420 字符，exe 资产于是永远配不上，
        /// 「检查更新」会误报"该 Release 里没有 DSH Launcher.exe 资产"；
        /// 而 digest 本来就在 url 之前、却按"向后找"处理，同样永远取不到值。
        /// </remarks>
        private static void FindAsset(string json, string wantName, Release rel, bool isSha)
        {
            const string AssetStart = "\"url\":\"https://api.github.com/repos/";
            int at = 0;
            while (true)
            {
                int u = json.IndexOf("\"browser_download_url\"", at, StringComparison.Ordinal);
                if (u < 0) return;

                int left = json.LastIndexOf(AssetStart, u, StringComparison.Ordinal);
                if (left < 0 || left >= u) { at = u + 24; continue; }
                string body = json.Substring(left, u - left);   // 当前资产对象：起点 → url 之前

                string name = Pick(body, "\"name\"\\s*:\\s*\"([^\"]+)\"");
                // GitHub 上传资产时会把文件名里的空格换成点（"DSH Launcher.exe" → "DSH.Launcher.exe"），
                // 所以比较时把 空格/点/下划线 全部抹掉再比，避免因改名匹配不上。
                if (SameAsset(name, wantName))
                {
                    string url = Pick(json.Substring(u), "\"browser_download_url\"\\s*:\\s*\"([^\"]+)\"");
                    string digest = Pick(body, "\"digest\"\\s*:\\s*\"([^\"]+)\"");
                    if (isSha) { rel.ShaUrl = url; }
                    else { rel.AssetUrl = url; rel.AssetName = name; rel.Digest = digest; }
                    return;
                }
                at = u + 24;
            }
        }

        /// <summary>
        /// 抹掉空格、点、下划线、连字符并转小写，用于资产名比对 ——
        /// 上传时 GitHub 会改写文件名，只有去掉这些分隔符再比才认得出是同一个资产。
        /// </summary>
        private static string NormalizeName(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            StringBuilder sb = new StringBuilder(s.Length);
            foreach (char c in s)
            {
                if (c == ' ' || c == '.' || c == '_' || c == '-') continue;
                sb.Append(char.ToLowerInvariant(c));
            }
            return sb.ToString();
        }

        /// <summary>两个资产名在"忽略分隔符与大小写"的意义下是否算同一个。</summary>
        private static bool SameAsset(string a, string b)
        {
            return NormalizeName(a) == NormalizeName(b);
        }

        /// <summary>下载并校验 exe，返回落地文件路径；任何一步失败都返回 "" 并说明原因。</summary>
        /// <remarks>
        /// 校验值优先取 .sha256 资产（工作流写的是一行 "hash  文件名"，这里只取第一个 token），
        /// 拿不到再退回 API 的 digest 字段；**两者都没有就拒装** —— 宁可装不上，也不装一个无法验证的东西。
        /// 下载内容会整个读进内存（exe 只有几 MB），校验通过后才落盘，不会用半截文件覆盖掉能用的旧版本。
        /// </remarks>
        /// <param name="rel">FetchLatest 的结果，AssetUrl 必须非空。</param>
        /// <param name="targetPath">落地路径；所在目录不存在会自动创建。</param>
        /// <param name="error">失败原因，可直接展示给用户。</param>
        /// <returns>落地文件的路径；失败返回 ""。</returns>
        public static string Download(Release rel, string targetPath, out string error)
        {
            error = "";
            try
            {
                byte[] data = HttpGetBytes(rel.AssetUrl, 120000);
                // 过小 = 明显不是 exe（多半是错误页或代理返回的 HTML），直接判失败，别拿去覆盖能用的旧版本
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
        /// <summary>GET 并当 UTF-8 文本返回；失败向上抛，由调用方归因。</summary>
        private static string HttpGet(string url, int timeoutMs)
        {
            byte[] b = HttpGetBytes(url, timeoutMs);
            return b == null ? null : Encoding.UTF8.GetString(b);
        }

        /// <summary>
        /// 同步 GET，返回原始字节。超时同时作用于连接与读写，读到的内容整体放进内存。
        /// 走 .NET 的 HTTP 栈（Windows 上即 Schannel），且未显式设置代理 ——
        /// 所以本机的 TLS 策略与代理环境会直接影响成败，这也正是"检查更新失败"的常见原因。
        /// </summary>
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

        /// <summary>
        /// 用正则从 JSON 文本里取第一个捕获组并做反转义；匹配不上或正则出错都返回 ""（不抛异常）。
        /// 用的是 static Regex.Match，复用 .NET 内部的正则缓存，不需要自己缓存 pattern。
        /// </summary>
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

        /// <summary>
        /// 只还原最常见的几种 JSON 转义：\r\n 与 \n（都折成空格）、\" 与 \\ ，不做 \uXXXX 解码 ——
        /// 若 API 把中文转义成 \u 形式，这里会原样留下，属于已知的能力边界。
        /// </summary>
        private static string Unescape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\r\\n", " ").Replace("\\n", " ").Replace("\\\"", "\"").Replace("\\\\", "\\").Trim();
        }
    }
}
