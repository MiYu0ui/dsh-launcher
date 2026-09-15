using System;
using System.Text.RegularExpressions;

namespace DshLauncher
{
    /// <summary>
    /// 落盘前的敏感信息脱敏。
    /// DSH 服务地址形如 http://127.0.0.1:3080/?token=xxxx，这个 token 是访问本地 Web UI 的凭据，
    /// 而日志/诊断包是排查问题时最常被分享出去的东西 —— 所以统一在"写出"这一层抹掉。
    /// 只脱敏 token 的值，其余原样保留，方便对照。
    /// </summary>
    internal static class SecretMask
    {
        private static readonly Regex TokenPattern = new Regex(
            @"([?&]token=)[^\s&""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        public static string Apply(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.IndexOf("token=", StringComparison.OrdinalIgnoreCase) < 0) return text;
            try { return TokenPattern.Replace(text, "$1***"); }
            catch { return text; }
        }

        /// <summary>给用户看的地址：把 token 参数整个去掉（比打码更干净）。</summary>
        public static string StripToken(string url)
        {
            if (string.IsNullOrEmpty(url)) return url;
            int q = url.IndexOf('?');
            if (q < 0) return url;
            string head = url.Substring(0, q);
            string tail = url.Substring(q);
            string cleaned = TokenPattern.Replace(tail, "");
            cleaned = cleaned.Replace("?&", "?").Replace("&&", "&").TrimEnd('?', '&');
            return cleaned.Length == 0 ? head : head + cleaned;
        }
    }
}
