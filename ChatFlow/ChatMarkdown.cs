using System.Text.RegularExpressions;

namespace ChatFlow
{
    internal static class ChatMarkdown
    {
        private static readonly Regex Heading = new(@"^\s{0,3}#{1,6}\s+\S", RegexOptions.Compiled);
        private static readonly Regex Fence = new(@"^\s*(```|~~~)", RegexOptions.Compiled);
        private static readonly Regex Quote = new(@"^\s{0,3}>\s?", RegexOptions.Compiled);
        private static readonly Regex TableSeparator = new(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$", RegexOptions.Compiled);
        private static readonly Regex ThematicBreak = new(@"^\s{0,3}((\*\s*){3,}|(-\s*){3,}|(_\s*){3,})$", RegexOptions.Compiled);
        private static readonly Regex ListItem = new(@"^\s{0,3}([-*+]|\d{1,9}[.)])\s+\S", RegexOptions.Compiled);
        private static readonly Regex Inline = new(@"(?:\*\*[^*\n]+\*\*|__[^_\n]+__|`[^`\n]+`|~~[^~\n]+~~|!?\[[^\]\n]*\]\([^)\n]+\))", RegexOptions.Compiled);

        public static bool IsMarkdown(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;

            int listLines = 0;
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (Fence.IsMatch(line) || Heading.IsMatch(line) || Quote.IsMatch(line)
                    || TableSeparator.IsMatch(line) || ThematicBreak.IsMatch(line))
                    return true;
                if (ListItem.IsMatch(line)) listLines++;
            }

            return listLines >= 2 || Inline.IsMatch(text);
        }

        private static readonly Regex FenceLine = new(@"^ {0,3}(?<fence>`{3,}|~{3,})[ \t]*(?<info>[^`]*)$", RegexOptions.Compiled);

        /// <summary>把 markdown 文本切成「散文段 / 围栏代码段」：散文交 MarkdownTextBox，代码段交 FCTB 按语言渲染。
        /// 围栏行本身不进入任何段（只作分隔）；未闭合围栏的剩余部分按代码处理（流式半截安全）。</summary>
        public static List<(bool IsCode, string Content, string Lang)> SplitSegments(string text)
        {
            var segments = new List<(bool IsCode, string Content, string Lang)>();
            var sb = new System.Text.StringBuilder();
            bool inCode = false;
            string lang = null;
            char fenceChar = '\0';
            int fenceLen = 0;

            void Flush()
            {
                if (sb.Length == 0) return;
                string content = sb.ToString().TrimEnd('\n');
                if (content.Length > 0) segments.Add((inCode, content, lang));
                sb.Clear();
            }

            foreach (string raw in text.Replace("\r\n", "\n").Split('\n'))
            {
                Match m = FenceLine.Match(raw);
                if (!inCode)
                {
                    if (m.Success)
                    {
                        Flush();
                        inCode = true;
                        fenceChar = m.Groups["fence"].Value[0];
                        fenceLen = m.Groups["fence"].Value.Length;
                        lang = m.Groups["info"].Value.Trim();
                        continue;
                    }
                }
                else if (m.Success && m.Groups["fence"].Value[0] == fenceChar
                         && m.Groups["fence"].Value.Length >= fenceLen
                         && m.Groups["info"].Value.Trim().Length == 0)
                {
                    Flush();
                    inCode = false;
                    lang = null;
                    continue;
                }
                sb.Append(raw).Append('\n');
            }
            Flush();
            return segments;
        }
    }
}
