using System.Text.RegularExpressions;

namespace ChatFlow
{
    public static class ChatMarkdown
    {
        private static readonly Regex Heading = new(@"^\s{0,3}#{1,6}\s+\S", RegexOptions.Compiled);
        private static readonly Regex Fence = new(@"^\s*(```|~~~)", RegexOptions.Compiled);
        private static readonly Regex Quote = new(@"^\s{0,3}>\s?", RegexOptions.Compiled);
        private static readonly Regex TableSeparator = new(@"^\s*\|?\s*:?-{3,}:?\s*(\|\s*:?-{3,}:?\s*)+\|?\s*$", RegexOptions.Compiled);
        private static readonly Regex ThematicBreak = new(@"^\s{0,3}((\*\s*){3,}|(-\s*){3,}|(_\s*){3,})$", RegexOptions.Compiled);
        private static readonly Regex ListItem = new(@"^\s{0,3}([-*+]|\d{1,9}[.)])\s+\S", RegexOptions.Compiled);
        private static readonly Regex Inline = new(@"(?:\*\*[^*\n]+\*\*|__[^_\n]+__|`[^`\n]+`|~~[^~\n]+~~|!?\[[^\]\n]*\]\([^)\n]+\))", RegexOptions.Compiled);

        /// <summary>返回正文里第一个 markdown 表格首行（表头）的行号（0 起）；没有表格返回 -1。
        /// 判据与 FCTB 的 AlignMarkdownTables 一致：本行含 |，下一行是分隔线行。跳过围栏内的代码。</summary>
        public static int FirstTableLine(string text)
        {
            if (string.IsNullOrEmpty(text)) return -1;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            bool inFence = false;
            for (int i = 0; i < lines.Length; i++)
            {
                if (Fence.IsMatch(lines[i])) { inFence = !inFence; continue; }
                if (inFence) continue;
                if (i + 1 >= lines.Length) break;
                if (string.IsNullOrWhiteSpace(lines[i]) || !lines[i].Contains('|')) continue;
                if (!TableSeparator.IsMatch(lines[i + 1])) continue;
                return i;
            }
            return -1;
        }

        /// <summary>提取正文里第一个 markdown 表格的原文（表头 + 分隔线 + 数据行，保留原样）；没有表格返回 null。</summary>
        public static string ExtractFirstTable(string text)
        {
            int start = FirstTableLine(text);
            if (start < 0) return null;
            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            int end = start + 1;
            while (end < lines.Length && lines[end].Trim().Length > 0 && lines[end].Contains('|'))
                end++;
            return string.Join("\n", lines[start..end]);
        }

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

        /// <summary>
        /// 分段渲染（散文段 + 代码段各自成块）只在段数少时启用：块按「先全部散文、再全部代码」堆叠，
        /// 段一多（Agent 长轨迹：几十个工具结果交替着代码块）阅读顺序就乱了，且控件的测量/布局开销大。
        /// 超过这个段数改走单文本框，保证顺序与滚动正确。要达到「任意段数都按原序穿插」，需改成每段一个散文块。
        /// </summary>
        public const int MaxSegments = 8;

        /// <summary>
        /// 超过这个长度的正文不再走 markdown：SetMarkdown 实测 0.38ms/字且随长度超线性
        /// （2 千字 0.8 秒、6 千字 6.5 秒、1.2 万字 21 秒），纯文本 FCTB + 正则样式 1.2 万字只要 8ms。
        /// 长回复按带样式的纯文本渲染（代码块仍会着色/可折叠），短回复才用 markdown。
        /// 阈值定在 3000：1500 会让大多数完整回复都退回 FCTB（markdown 视觉丢失），再往上则渲染耗时开始明显拖慢首绘。
        /// 宿主可经 <see cref="ChatFlowControl.MarkdownMaxChars"/> 覆盖。
        /// </summary>
        internal static int MaxChars { get; set; } = 3000;

        /// <summary>工具行（执行中提示 " >> Tool[args]" / 旧格式 "» tool(args)"）。</summary>
        private static readonly Regex ToolFlashLine = new(@"^[ \t]*(?:»|>>[ \t])", RegexOptions.Compiled);

        /// <summary>整段正文只有工具行：这是「正在调用什么」的提示，不是正文。
        /// 必须留在 FCTB 里按工具行染色——" >> " 会被 markdown 当块引用吞掉，排成灰底引用块且工具名不着色。</summary>
        public static bool IsToolFlash(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return false;
            bool any = false;
            foreach (string line in text.Replace("\r\n", "\n").Split('\n'))
            {
                if (string.IsNullOrWhiteSpace(line)) continue;
                if (!ToolFlashLine.IsMatch(line)) return false;
                any = true;
            }
            return any;
        }

        /// <summary>这一段正文该走 markdown 渲染还是带样式的纯文本（流式中/工具行提示/过长/段太多都退回纯文本）。</summary>
        public static bool UseMarkdown(string text, bool streaming)
        {
            if (streaming || string.IsNullOrEmpty(text) || text.Length > MaxChars) return false;
            if (IsToolFlash(text)) return false;
            return IsMarkdown(text) && SplitSegments(text).Count <= MaxSegments;
        }

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
