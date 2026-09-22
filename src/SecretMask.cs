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
        /// <summary>
        /// 匹配 <c>?token=</c> / <c>&amp;token=</c> 后面的凭据值（保留参数名，只换掉值）。
        /// </summary>
        /// <remarks>
        /// 值一直吃到空白、<c>&amp;</c>、引号或尖括号为止（不会跨过这些字符继续吃）：
        /// 所以 URL 后面的其它参数、日志里的引号包裹都不会被连带吃掉；
        /// 不区分大小写（<c>Token=</c> 也认），并预编译 —— 它在日志写入路径上被频繁调用。
        /// </remarks>
        private static readonly Regex TokenPattern = new Regex(
            @"([?&]token=)[^\s&""'<>]+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>把文本里所有 token 的值替换成 <c>***</c>，参数名与其余内容原样保留。</summary>
        /// <param name="text">要落盘的文本；null / 空串原样返回。</param>
        /// <returns>脱敏后的文本；没有出现 <c>token=</c> 时直接返回原对象（不做任何改写）。</returns>
        /// <remarks>
        /// 先做一次大小写不敏感的快速判断再动正则，绝大多数日志行都不含 token，能省下一次全文匹配。
        /// 替换本身也包在 try 里：脱敏失败时宁可原样返回 —— 这是"尽力而为"的兜底，
        /// 不是最后一道防线（真正敏感的地址应当在写日志之前就已经用 <see cref="StripToken"/> 处理过）。
        /// </remarks>
        public static string Apply(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            if (text.IndexOf("token=", StringComparison.OrdinalIgnoreCase) < 0) return text;
            try { return TokenPattern.Replace(text, "$1***"); }
            catch { return text; }
        }

        /// <summary>给用户看的地址：把 token 参数整个去掉（比打码更干净）。</summary>
        /// <param name="url">完整地址；null / 空串 / 不含 <c>?</c> 时原样返回。</param>
        /// <returns>去掉 token 参数后的地址；只剩主机部分时连 <c>?</c> 一起清掉。</returns>
        /// <remarks>
        /// 比 <see cref="Apply"/> 更彻底：连参数名都不留（显示给用户看时，"token=***" 也是噪声）。
        /// 只处理 <c>?</c> 之后的部分，路径里出现 <c>token=</c> 字样不会被误删；
        /// 剥掉后留下的空 <c>?</c> / 连续 <c>&amp;</c> 会一并清理掉。
        /// </remarks>
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
