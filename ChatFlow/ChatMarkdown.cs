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
    }
}
