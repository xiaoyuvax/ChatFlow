using System.Text.RegularExpressions;
using FastColoredTextBoxNS;
using FastColoredTextBoxNS.Text;
using FastColoredTextBoxNS.Types;

namespace ChatFlow
{
    /// <summary>
    /// 聊天文本美化器：regex 驱动的实时样式 + 结构化折叠（基于 FCTB 的 SetStyle / FoldingMarkers）。
    /// 正文样式：**粗体**、*斜体*、`行内代码`、```代码块```（底色+左边框，可折叠）、URL、本地文件路径、井号标题。
    /// 思考区样式：» 工具调用行（琥珀粗体）、→ 成功/失败（绿/红）、输出行（灰）、【附件】标题、🧠 标记。
    /// 折叠：``` 围栏成对折叠，长代码块默认收起（点击行左 ○ 标记展开）；【附件】/🧠/» 行到下一个空行折叠为块。
    /// 标记字符一律选用 Consolas 可渲染的字形（emoji 会显示成方框）。
    /// 只读渲染增强，不改变文本内容；每次赋文本后全量重算（流式重建安全）。
    /// </summary>
    public static class ChatTextStyler
    {
        // ============ 样式（按当前主题重建；FCTB 渲染期只读。Style 为抽象类，实际用 TextStyle） ============
        private static Style BoldStyle, ItalicStyle, InlineCodeStyle, CodeBlockStyle, HeadingStyle, LinkStyle,
            PathStyle, ToolCallStyle, ToolOkStyle, ToolFailStyle, ToolOutputStyle, AttachmentStyle, ReasoningMarkStyle,
            PromptBlockStyle, ResMarkStyle;

        private static int _paletteStamp = int.MinValue;

        /// <summary>按当前主题（浅色 = 原硬编码配色）重建样式；调色板未变则跳过。主题切换时由 ChatFlowControl 调用。</summary>
        internal static void EnsureStyles()
        {
            var p = ChatTheme.Current;
            int stamp = p?.GetHashCode() ?? 0;
            if (_paletteStamp == stamp && BoldStyle != null) return;
            _paletteStamp = stamp;
            if (p == null)
            {
                BoldStyle = new TextStyle(Brushes.Black, null, FontStyle.Bold);
                ItalicStyle = new TextStyle(Brushes.Gray, null, FontStyle.Italic);
                InlineCodeStyle = new TextStyle(Brushes.Black, Brushes.Gainsboro, FontStyle.Regular);
                CodeBlockStyle = new TextStyle(Brushes.Black, Brushes.WhiteSmoke, FontStyle.Regular);
                HeadingStyle = new TextStyle(Brushes.MidnightBlue, null, FontStyle.Bold);
                LinkStyle = new TextStyle(Brushes.RoyalBlue, null, FontStyle.Underline);
                PathStyle = new TextStyle(Brushes.Teal, null, FontStyle.Regular);
                ToolCallStyle = new TextStyle(Brushes.DarkGoldenrod, null, FontStyle.Bold);
                ToolOkStyle = new TextStyle(Brushes.SeaGreen, null, FontStyle.Bold);
                ToolFailStyle = new TextStyle(Brushes.Firebrick, null, FontStyle.Bold);
                ToolOutputStyle = new TextStyle(Brushes.Gray, null, FontStyle.Regular);
                AttachmentStyle = new TextStyle(Brushes.SteelBlue, null, FontStyle.Bold);
                ReasoningMarkStyle = new TextStyle(Brushes.Gray, null, FontStyle.Italic);
                // 提示词块：只换字色（不铺字符底色）——深青字在白底上对比充足（浅色主题）
                PromptBlockStyle = new TextStyle(new SolidBrush(Color.FromArgb(0x2F, 0x6F, 0x5F)), null, FontStyle.Regular);
                // 分辨率档位词（1K/2K/4K/8K…）：深青加粗字，提醒用户留意这类词（同样不加字符底色）
                ResMarkStyle = new TextStyle(new SolidBrush(Color.FromArgb(0x0E, 0x74, 0x90)), null, FontStyle.Bold);
            }
            else
            {
                Brush B(Color c) => new SolidBrush(c);
                BoldStyle = new TextStyle(B(p.Fore), null, FontStyle.Bold);
                ItalicStyle = new TextStyle(B(p.ForeDim), null, FontStyle.Italic);
                InlineCodeStyle = new TextStyle(B(p.InputFore), B(p.Control), FontStyle.Regular);
                CodeBlockStyle = new TextStyle(B(p.InputFore), B(p.InputBack), FontStyle.Regular);
                HeadingStyle = new TextStyle(B(p.Accent), null, FontStyle.Bold);
                LinkStyle = new TextStyle(B(Color.FromArgb(0x6F, 0xB3, 0xF0)), null, FontStyle.Underline);
                PathStyle = new TextStyle(B(Color.FromArgb(0x6F, 0xCF, 0xCF)), null, FontStyle.Regular);
                ToolCallStyle = new TextStyle(B(Color.FromArgb(0xE7, 0xC4, 0x6A)), null, FontStyle.Bold);
                ToolOkStyle = new TextStyle(B(Color.FromArgb(0x7F, 0xDB, 0x9A)), null, FontStyle.Bold);
                ToolFailStyle = new TextStyle(B(Color.FromArgb(0xF0, 0x8A, 0x7A)), null, FontStyle.Bold);
                ToolOutputStyle = new TextStyle(B(p.ForeDim), null, FontStyle.Regular);
                AttachmentStyle = new TextStyle(B(p.Accent), null, FontStyle.Bold);
                ReasoningMarkStyle = new TextStyle(B(p.ForeDim), null, FontStyle.Italic);
                // 提示词块：深色底上改用低饱和薄荷字（#3A3E44 上对比 ≈7:1），同样不铺字符底色
                PromptBlockStyle = new TextStyle(B(Color.FromArgb(0x9C, 0xD8, 0xC4)), null, FontStyle.Regular);
                // 分辨率档位词：亮青加粗字（深色底上清晰，且不刺眼）
                ResMarkStyle = new TextStyle(B(Color.FromArgb(0x7F, 0xE3, 0xE3)), null, FontStyle.Bold);
            }
        }

        private static Style[] AllStyles =>
        [
            BoldStyle, ItalicStyle, InlineCodeStyle, CodeBlockStyle, HeadingStyle, LinkStyle, PathStyle,
            ToolCallStyle, ToolOkStyle, ToolFailStyle, ToolOutputStyle, AttachmentStyle, ReasoningMarkStyle,
            PromptBlockStyle, ResMarkStyle
        ];

        // ============ 正则（^ 锚点配 Multiline：逐行生效） ============
        private static readonly Regex BoldRegex = new(@"\*\*(?<text>[^\*\n]+)\*\*", RegexOptions.Compiled);
        private static readonly Regex ItalicRegex = new(@"(?<!\*)\*(?<text>[^\*\n]+)\*(?!\*)", RegexOptions.Compiled);
        private static readonly Regex InlineCodeRegex = new(@"`(?<text>[^`\n]+)`", RegexOptions.Compiled);
        private static readonly Regex FenceRegex = new(@"^```.*$", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex UrlRegex = new(@"\bhttps?://[^\s）)】」]+", RegexOptions.Compiled);
        // 本地路径：盘符（J:/、J:\）或 UNC（\\server\share）；允许路径内空格（如 "My God"），
        // 但不吞尾部空白与 CJK/全角字符（中文紧随路径时在此断开）。
        // 前置 (?<![A-Za-z0-9]) 避免把 "https://" 里的 "s:/" 误判成盘符路径。
        private static readonly Regex PathRegex = new(
            @"(?<![A-Za-z0-9])(?:[A-Za-z]:[\\/]|\\\\)[^\s<>|:*?""`),;\r\n\u3000-\u303F\u3400-\u4DBF\u4E00-\u9FFF\uFF00-\uFFEF]+" +
            @"(?: [^\s<>|:*?""`),;\r\n\u3000-\u303F\u3400-\u4DBF\u4E00-\u9FFF\uFF00-\uFFEF]+)*",
            RegexOptions.Compiled);
        private static readonly Regex HeadingRegex = new(@"^(?<text>#{1,6}\s+.+)$", RegexOptions.Compiled | RegexOptions.Multiline);
        // 可点击的本地路径：与 <see cref="PathRegex"/> 同起点，但允许 CJK 文件名，且必须以「.扩展名 + 非字母数字边界」收尾，
        // 否则 "J:/x.png已保存" 会被整句吞掉而认不出图片；是否图片仍由 AttachmentHelper.IsImage 判定（扩展名列表单一来源）。
        private static readonly Regex LinkPathRegex = new(
            @"(?<![A-Za-z0-9])(?:[A-Za-z]:[\\/]|\\\\)[^\s<>|:*?""`),;\r\n]+(?: [^\s<>|:*?""`),;\r\n]+)*\.[A-Za-z0-9]{1,5}(?![A-Za-z0-9])",
            RegexOptions.Compiled);
        // 形如「» #3 tool(args) → 成功 · 420ms」：序号可选（新格式带步号，历史文本可能不带）
        private static readonly Regex ToolCallRegex = new(@"^»\s*(#\d+\s+)?\S+\s*\(.*$", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex ToolOkRegex = new(@"→\s*成功", RegexOptions.Compiled);
        private static readonly Regex ToolFailRegex = new(@"→\s*失败", RegexOptions.Compiled);
        private static readonly Regex ToolOutputRegex = new(@"^\s+(输出|data|error)\s*:.*$", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex AttachmentHeaderRegex = new(@"^【附件\s+[^】]*】\r?$", RegexOptions.Compiled | RegexOptions.Multiline);
        private static readonly Regex ReasoningMarkRegex = new(@"^🧠\s*(?<text>.*)$", RegexOptions.Compiled | RegexOptions.Multiline);
        /// <summary>思考区可折叠块的首行：附件标题 / 🧠 思考标记 / » 工具轨迹行。</summary>
        private static readonly Regex FoldableStartRegex = new(@"^(【附件\s+[^】]*】|🧠|»)", RegexOptions.Compiled | RegexOptions.Multiline);

        /// <summary>提示词块行首的 markdown 引用号（模型常用 &gt; 逐行标注提示词）。</summary>
        private static readonly Regex BlockquoteLineRegex = new(@"^>\s?", RegexOptions.Compiled);

        /// <summary>
        /// 分辨率档位词：1K / 2K / 4K / 8K 之类（前后不接字母数字，避免 4Kindle 之类误伤）。
        /// 这类词极易被当成「分辨率参数」，聊天区高亮出来提醒用户留意。
        /// </summary>
        private static readonly Regex ResolutionWordRegex = new(
            @"(?<![A-Za-z0-9])\d{1,2}\s?[Kk](?![A-Za-z0-9])", RegexOptions.Compiled);

        /// <summary>负面提示词段头（正文里的提示词一部分：与正向一起变色）。</summary>
        private static readonly Regex NegativePromptLineRegex = new(
            @"^\s*(【负面提示词】|【负面提示词[:：]|【负面提示】|负面提示词[:：]|负面[:：]|negative[ _]?prompt[:：]?)",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // ============ 可点击链接（URL / 图像文件路径） ============

        /// <summary>可点击链接种类：URL 交由宿主外部处理，图像文件路径交由宿主本地预览。</summary>
        public enum ChatLinkKind
        {
            Url,
            ImageFile,
        }

        /// <summary>文本中的一个可点击链接（绝对字符区间 + 目标文本）。</summary>
        internal readonly record struct ChatLink(int Start, int Length, ChatLinkKind Kind, string Target);

        /// <summary>收集文本中可点击的链接：URL + 图像文件路径（PathStyle 已高亮的那批里按扩展名过滤）。</summary>
        internal static List<ChatLink> CollectLinks(string text)
        {
            var links = new List<ChatLink>();
            if (string.IsNullOrEmpty(text)) return links;
            foreach (Match m in UrlRegex.Matches(text))
                links.Add(new ChatLink(m.Index, m.Length, ChatLinkKind.Url, m.Value));
            foreach (Match m in LinkPathRegex.Matches(text))
                if (ChatMedia.IsImage(m.Value))
                    links.Add(new ChatLink(m.Index, m.Length, ChatLinkKind.ImageFile, m.Value));
            links.Sort((a, b) => a.Start.CompareTo(b.Start));
            return links;
        }

        /// <summary>命中测试：返回覆盖给定绝对字符偏移的链接；无则 null（越界偏移安全）。</summary>
        internal static ChatLink? LinkAt(List<ChatLink> links, int index)
        {
            if (links == null || links.Count == 0 || index < 0) return null;
            foreach (var l in links)
                if (index >= l.Start && index < l.Start + l.Length) return l;
            return null;
        }

        /// <summary>单测开关：替身 FCTB 无真实渲染管线时跳过 ClearStyle（默认执行，保证流式重建不叠样式）。</summary>
        internal static bool ResetStyles = true;

        // ============ 入口 ============
        /// <summary>正文消息样式。text 为 null/空时仅清空旧样式。</summary>
        public static void ApplyBody(RowTextBox box, string text)
        {
            EnsureStyles();
            if (box == null || box.IsDisposed) return;
            try
            {
                box.ClearFoldingMarkers();
                var range = box.Range;
                if (ResetStyles) range.ClearStyle(AllStyles);
                box.LinkSpans = CollectLinks(text);
                if (string.IsNullOrEmpty(text)) return;

                // 提示词块底带先铺（作为底色层），其余样式再叠上去：块内路径/行内代码仍能看清
                ApplyPromptBlockStyle(box, text);
                // 分辨率档位词（8K/4K…）：先标出来提醒留意，路径/链接等更具体的样式仍可覆盖它
                range.SetStyle(ResMarkStyle, ResolutionWordRegex);
                // 路径先上，再上「行内代码/URL」——后应用的样式覆盖前者（代码内的路径不抢行内代码底色）
                range.SetStyle(PathStyle, PathRegex);
                range.SetStyle(InlineCodeStyle, InlineCodeRegex);
                range.SetStyle(CodeBlockStyle, FenceRegex);
                range.SetStyle(BoldStyle, BoldRegex);
                range.SetStyle(ItalicStyle, ItalicRegex);
                range.SetStyle(LinkStyle, UrlRegex);
                range.SetStyle(HeadingStyle, HeadingRegex);
                ApplyCodeBlockFolding(box, text);
                ApplyBraceBlockFolding(box, text); // 无围栏的多行 JSON（正文里也可能出现）
            }
            catch
            {
                // 样式失败不影响文本内容显示
            }
        }

        /// <summary>思考区样式（工具调用/结果/输出行、附件标题、🧠 标记 + 结构化折叠）。text 为 null/空时仅清空。</summary>
        public static void ApplyThinking(RowTextBox box, string text)
        {
            EnsureStyles();
            if (box == null || box.IsDisposed) return;
            try
            {
                box.ClearFoldingMarkers();
                var range = box.Range;
                if (ResetStyles) range.ClearStyle(AllStyles);
                box.LinkSpans = CollectLinks(text);
                if (string.IsNullOrEmpty(text)) return;

                range.SetStyle(ToolCallStyle, ToolCallRegex);
                range.SetStyle(ToolOkStyle, ToolOkRegex);
                range.SetStyle(ToolFailStyle, ToolFailRegex);
                range.SetStyle(ToolOutputStyle, ToolOutputRegex);
                range.SetStyle(AttachmentStyle, AttachmentHeaderRegex);
                range.SetStyle(ReasoningMarkStyle, ReasoningMarkRegex);
                // 最后上路径：让灰底输出行里的本地路径也醒目
                range.SetStyle(PathStyle, PathRegex);
                ApplyBlockFolding(box, text);
                ApplyBraceBlockFolding(box, text); // 工具输出里的裸 JSON 默认折叠（省空间）
            }
            catch
            {
                // 样式失败不影响文本内容显示
            }
        }

        /// <summary>对任意 RowTextBox 应用自定义规则集（外部扩展点；先清全部聊天样式）。</summary>
        public static void ApplyCustom(RowTextBox box, params (Regex pattern, Style style)[] rules)
        {
            EnsureStyles();
            if (box == null || box.IsDisposed || rules == null) return;
            try
            {
                var range = box.Range;
                if (ResetStyles) range.ClearStyle(AllStyles);
                foreach (var (pattern, style) in rules)
                    range.SetStyle(style, pattern);
            }
            catch { /* 忽略样式异常 */ }
        }

        // ============ 折叠 ============

        /// <summary>代码块默认折叠的最小内容行数：太短的块不折（折了只剩一行没意义）。</summary>
        internal const int MinCodeBlockContentLinesToCollapse = 3;

        /// <summary>代码块围栏成对折叠：``` 行交替配对（开始/结束）。
        /// 内容行数 ≥ 阈值的块默认收起（省空间），点击左侧 ○ 标记展开。
        /// <b>例外：含提示词语言括号（[zh:…:zh] / [en:…:en]）的块默认展开</b>——那是要一眼看全的提示词正文。</summary>
        private static void ApplyCodeBlockFolding(RowTextBox box, string text)
        {
            var matches = FenceRegex.Matches(text);
            for (int i = 0; i + 1 < matches.Count; i += 2)
            {
                int startLine = LineIndexOf(text, matches[i].Index);
                int endLine = LineIndexOf(text, matches[i + 1].Index);
                MarkFold(box, startLine, endLine);
                string span = text[matches[i].Index..(matches[i + 1].Index + matches[i + 1].Length)];
                if (HasLanguageBracket(span)) continue;   // 提示词括号段：留折叠标记但不自动收起
                if (endLine - startLine - 1 >= MinCodeBlockContentLinesToCollapse)
                    TryCollapse(box, startLine);
            }
        }

        /// <summary>文本是否含提示词语言括号（[zh: … :zh] / [en: … :en]）——这类块默认展开不折叠。</summary>
        internal static bool HasLanguageBracket(string text)
            => !string.IsNullOrEmpty(text)
               && (text.Contains("[zh:", StringComparison.OrdinalIgnoreCase)
                   || text.Contains(":zh]", StringComparison.OrdinalIgnoreCase)
                   || text.Contains("[en:", StringComparison.OrdinalIgnoreCase)
                   || text.Contains(":en]", StringComparison.OrdinalIgnoreCase));

        // ============ 提示词块：一眼看出「这段是提示词」，但不刺眼 ============

        /// <summary>
        /// 提示词块样式：语言括号块（[zh: … :zh] / [en: … :en]）整段铺柔和底带 + 不刺眼的同色系字；
        /// 没有括号对时退化为「连续行首 &gt; 引用行」块（模型常用 &gt; 逐行标注提示词），同样铺底带。
        /// 只用样式（不改文本），并留可手动折叠的标记（默认展开）。
        /// </summary>
        private static void ApplyPromptBlockStyle(RowTextBox box, string text)
        {
            foreach (var (start, end) in FindPromptBracketSpans(text))
            {
                StyleLines(box, start, end, PromptBlockStyle);
                if (start < box.LinesCount && string.IsNullOrEmpty(box[start].FoldingStartMarker))
                    MarkFold(box, start, end);   // 语言括号块：留可手动折叠的标记（默认展开）
            }
            // 无括号的单语言提示词块（连续 > 引用行）：只上色 + 缩进，不做折叠（没有括号对，折叠没有意义）
            foreach (var (start, end) in FindBlockquoteSpans(text))
                StyleLines(box, start, end, PromptBlockStyle);
            // 负面提示词段：属于提示词内容的一部分，与正向一起变色
            foreach (var (start, end) in FindNegativePromptSpans(text))
                StyleLines(box, start, end, PromptBlockStyle);
        }

        /// <summary>提示词块整体缩进的空格串（纯展示用，不改模型原文）。</summary>
        private const string PromptBlockIndent = "    ";

        /// <summary>
        /// 把提示词块（<c>[zh: … :zh]</c> / <c>[en: … :en]</c>，或连续 <c>&gt;</c> 引用行块）**整段缩进**后返回，
        /// 供聊天区展示——缩进是「这段是提示词」的醒目信号（无括号对时替代折叠）。
        /// 只影响显示文本；ChatTurn 里的模型原文不变，复制该行时会带上这段缩进。
        /// </summary>
        public static string IndentPromptBlocks(string text)
        {
            if (string.IsNullOrEmpty(text)) return text;
            var spans = FindPromptBracketSpans(text);
            spans.AddRange(FindBlockquoteSpans(text));
            spans.AddRange(FindNegativePromptSpans(text));
            if (spans.Count == 0) return text;

            var lines = text.Replace("\r\n", "\n").Split('\n');
            foreach (var (start, end) in spans)
                for (int i = Math.Max(0, start); i <= Math.Min(end, lines.Length - 1); i++)
                    if (lines[i].Length > 0 && !lines[i].StartsWith(PromptBlockIndent, StringComparison.Ordinal))
                        lines[i] = PromptBlockIndent + lines[i];
            return string.Join("\n", lines);
        }

        /// <summary>提示词语言括号块的行区间（含括号行；**必须成对闭合**，未闭合的不算——流式半截/工具参数里的片段不该整段染色）。</summary>
        private static List<(int start, int end)> FindPromptBracketSpans(string text)
        {
            var spans = new List<(int, int)>();
            if (string.IsNullOrEmpty(text)) return spans;
            string[] lines = text.Split('\n');
            int start = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                string t = lines[i];
                if (start < 0)
                {
                    // 工具轨迹/工具参数行（» #2 show_text(title=…, text=[zh: …）里的 [zh: 只是参数片段，
                    // 不是会话里的提示词正文，不得据此把后面整块染色
                    if (IsToolTraceOrArgsLine(t)) continue;
                    if (t.Contains("[zh:", StringComparison.OrdinalIgnoreCase)
                        || t.Contains("[en:", StringComparison.OrdinalIgnoreCase))
                        start = i;
                }
                else if (t.Contains(":zh]", StringComparison.OrdinalIgnoreCase)
                         || t.Contains(":en]", StringComparison.OrdinalIgnoreCase))
                {
                    spans.Add((start, i));
                    start = -1;
                }
            }
            // 未闭合（流式半截）不染色：等闭合后再说
            return spans;
        }

        /// <summary>是否工具轨迹行（» 开头）或工具调用/参数行（tool-call:、show_text(…、args=…）。</summary>
        private static bool IsToolTraceOrArgsLine(string line)
        {
            string t = (line ?? string.Empty).TrimStart();
            if (t.StartsWith('»')) return true;
            return t.StartsWith("tool-call:", StringComparison.OrdinalIgnoreCase)
                   || t.StartsWith("tool-result:", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("tool-call: {", StringComparison.OrdinalIgnoreCase)
                   || t.Contains("show_text(", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 从一段文本里提取「提示词正文」（成对的 [zh:…:zh] / [en:…:en] 块 + 负面提示词段），
        /// 供工具/宿主把会话回答里的提示词原样拿出来（模型只需写一次，不必再抄进参数）。
        /// 无提示词块返回空串。
        /// </summary>
        public static string ExtractPromptText(string text)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;
            var spans = FindPromptBracketSpans(text);
            spans.AddRange(FindNegativePromptSpans(text));
            if (spans.Count == 0) return string.Empty;

            string[] lines = text.Replace("\r\n", "\n").Split('\n');
            var picked = new SortedSet<int>();
            foreach (var (start, end) in spans)
                for (int i = Math.Max(0, start); i <= Math.Min(end, lines.Length - 1); i++)
                    picked.Add(i);
            return string.Join("\n", picked.Select(i => lines[i])).Trim();
        }

        /// <summary>连续「&gt; 引用行」块的行区间（≥3 行才视为提示词块，避免误伤普通引用）。</summary>
        private static List<(int start, int end)> FindBlockquoteSpans(string text)
        {
            var spans = new List<(int, int)>();
            if (string.IsNullOrEmpty(text)) return spans;
            string[] lines = text.Split('\n');
            int start = -1;
            for (int i = 0; i <= lines.Length; i++)
            {
                bool isQuote = i < lines.Length && BlockquoteLineRegex.IsMatch(lines[i].TrimEnd('\r'));
                if (isQuote)
                {
                    if (start < 0) start = i;
                    continue;
                }
                if (start >= 0)
                {
                    if (i - start >= 3) spans.Add((start, i - 1));
                    start = -1;
                }
            }
            return spans;
        }

        /// <summary>负面提示词段的行区间：段头行起、到空行或下一个标记/标题为止（与正向提示词一起变色/缩进）。</summary>
        private static List<(int start, int end)> FindNegativePromptSpans(string text)
        {
            var spans = new List<(int, int)>();
            if (string.IsNullOrEmpty(text)) return spans;
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                if (!NegativePromptLineRegex.IsMatch(lines[i].TrimEnd('\r'))) continue;
                int end = i;
                for (int j = i + 1; j < lines.Length; j++)
                {
                    string t = lines[j].TrimEnd('\r').Trim();
                    if (t.Length == 0) break;                 // 空行 = 段结束
                    if (t.StartsWith('【') || t.StartsWith('#')) break;
                    end = j;
                }
                spans.Add((i, end));
                i = end;
            }
            return spans;
        }

        /// <summary>整行范围 [startLine, endLine] 上样式（行号越界/不同步时静默跳过）。</summary>
        private static void StyleLines(RowTextBox box, int startLine, int endLine, Style style)
        {
            if (startLine < 0 || endLine < startLine || startLine >= box.LinesCount) return;
            endLine = Math.Min(endLine, box.LinesCount - 1);
            try
            {
                box.GetRange(new Place(0, startLine), new Place(box[endLine].Count, endLine)).SetStyle(style);
            }
            catch { /* 行号与文本不同步：忽略 */ }
        }

        /// <summary>fenced 代码块的成对行号区间（``` 交替配对；未闭合的尾围栏忽略）。</summary>
        private static List<(int start, int end)> FindFenceSpans(string text)
        {
            var spans = new List<(int, int)>();
            var matches = FenceRegex.Matches(text);
            for (int i = 0; i + 1 < matches.Count; i += 2)
                spans.Add((LineIndexOf(text, matches[i].Index), LineIndexOf(text, matches[i + 1].Index)));
            return spans;
        }

        /// <summary>默认收起指定起始行的折叠块（无标记/行号越界时静默跳过，不影响文本显示）。</summary>
        private static void TryCollapse(RowTextBox box, int startLine)
        {
            try
            {
                if (startLine >= 0 && startLine < box.LinesCount
                    && !string.IsNullOrEmpty(box[startLine].FoldingStartMarker))
                    box.CollapseFoldingBlock(startLine);
            }
            catch { /* 折叠失败不影响文本显示 */ }
        }

        /// <summary>无围栏多行 JSON 块默认折叠的最小跨度（行数）：太短的不折。</summary>
        internal const int MinBraceBlockSpanToCollapse = 3;

        /// <summary>
        /// 折叠无围栏的多行 JSON/花括号块（典型：思考区工具输出的裸 JSON，没有 ``` 围栏）。
        /// 用括号深度扫描定位跨行块（忽略字符串内的括号），跳过已被围栏折叠覆盖的起始行。
        /// </summary>
        private static void ApplyBraceBlockFolding(RowTextBox box, string text)
        {
            var fences = FindFenceSpans(text);
            foreach (var (start, end, blockStart, blockEnd) in FindMultilineBraceBlocks(text))
            {
                // 提示词语言括号 [zh: … :zh]：**保留可折叠标记（允许手动收起），但默认展开**
                bool isPromptBracket = HasLanguageBracket(text[blockStart..(blockEnd + 1)]);
                if (!isPromptBracket && end - start < MinBraceBlockSpanToCollapse) continue;
                // 围栏内的 JSON 交给围栏折叠（避免同一段两套标记互相改可见状态）
                if (fences.Any(f => start >= f.start && end <= f.end)) continue;
                if (start < box.LinesCount && !string.IsNullOrEmpty(box[start].FoldingStartMarker)) continue;
                MarkFold(box, start, end);
                if (isPromptBracket) continue;   // 默认展开
                TryCollapse(box, start);
            }
        }

        /// <summary>括号深度扫描：返回跨多行的 { } / [ ] 块（起始/结束行号 + 起止字符下标，字符串内的括号不计）。</summary>
        private static List<(int startLine, int endLine, int startIndex, int endIndex)> FindMultilineBraceBlocks(string text)
        {
            var blocks = new List<(int, int, int, int)>();
            int line = 0, depth = 0, startLine = -1, startIndex = -1;
            bool inStr = false, esc = false;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (c == '\n') { line++; continue; }
                if (inStr)
                {
                    if (esc) esc = false;
                    else if (c == '\\') esc = true;
                    else if (c == '"') inStr = false;
                    continue;
                }
                if (c == '"') { inStr = true; continue; }
                if (c == '{' || c == '[')
                {
                    if (depth == 0) { startLine = line; startIndex = i; }
                    depth++;
                }
                else if ((c == '}' || c == ']') && depth > 0)
                {
                    depth--;
                    if (depth == 0 && startLine >= 0)
                    {
                        if (line > startLine) blocks.Add((startLine, line, startIndex, i));
                        startLine = -1;
                        startIndex = -1;
                    }
                }
            }
            return blocks;
        }

        /// <summary>思考区块折叠：【附件】/🧠/» 行到下一个空行为一块（空行不折叠，保持分区间距）。</summary>
        private static void ApplyBlockFolding(RowTextBox box, string text)
        {
            string[] lines = text.Split('\n');
            for (int i = 0; i < lines.Length; i++)
            {
                string trimmed = lines[i].TrimEnd('\r');
                if (!FoldableStartRegex.IsMatch(trimmed)) continue;
                int end = i + 1;
                while (end < lines.Length && !string.IsNullOrWhiteSpace(lines[end].TrimEnd('\r'))) end++;
                MarkFold(box, i, end - 1);
                i = end - 1;
            }
        }

        /// <summary>标记折叠对：行号写入 FoldingStartMarker/EndMarker（非空即配对，FindEndOfFoldingBlock Strategy1 兼容）。</summary>
        private static void MarkFold(RowTextBox box, int startLine, int endLine)
        {
            if (endLine <= startLine) return;                       // 单行块不折叠
            if (startLine >= box.LinesCount || endLine >= box.LinesCount) return;
            box[startLine].FoldingStartMarker = "chat-fold";
            box[endLine].FoldingEndMarker = "chat-fold";
        }

        /// <summary>字符偏移 → 行号（纯文本计算，与 FCTB 内部状态解耦）。</summary>
        private static int LineIndexOf(string text, int charIndex)
        {
            if (charIndex <= 0) return 0;
            int line = 0;
            int max = Math.Min(charIndex, text.Length);
            for (int i = 0; i < max; i++)
                if (text[i] == '\n') line++;
            return line;
        }

        // ============ 测试辅助 ============

        /// <summary>测试访问：关键样式实例（随主题重建，始终指向当前实例）。</summary>
        internal static Style BoldStyleField => BoldStyle;
        internal static Style ItalicStyleField => ItalicStyle;
        internal static Style InlineCodeStyleField => InlineCodeStyle;
        internal static Style LinkStyleField => LinkStyle;
        internal static Style PathStyleField => PathStyle;
        internal static Style HeadingStyleField => HeadingStyle;
        internal static Style ToolCallStyleField => ToolCallStyle;
        internal static Style ToolOkStyleField => ToolOkStyle;
        internal static Style ToolFailStyleField => ToolFailStyle;
        internal static Style ToolOutputStyleField => ToolOutputStyle;
        internal static Style AttachmentStyleField => AttachmentStyle;
        internal static Style ReasoningMarkStyleField => ReasoningMarkStyle;

        internal static bool IsFoldStart(FastColoredTextBox box, int lineIndex)
            => lineIndex < box.LinesCount && !string.IsNullOrEmpty(box[lineIndex].FoldingStartMarker);

        internal static bool IsFoldEnd(FastColoredTextBox box, int lineIndex)
            => lineIndex < box.LinesCount && !string.IsNullOrEmpty(box[lineIndex].FoldingEndMarker);
    }
}
