using System.Text.RegularExpressions;
using Xunit;

namespace ChatFlow.Tests
{
    /// <summary>长消息按行拆片：分片必须自己配平 ``` 围栏，否则下一片从代码块中间开始，
    /// 围栏被当成新的代码块起点，正文被染色并多出折叠区（用户侧：思维链渲染错乱）。</summary>
    public class SplitLongTextTests
    {
        private static int Fences(string s) => Regex.Matches(s, "(?m)^```").Count;

        private static string WithoutFenceLines(string s) => Regex.Replace(s, "(?m)^```.*\n?", "");

        [Fact]
        public void Split_WhenFenceEndsBeforeLimit_SplitsOutsideAndKeepsText()
        {
            string code = string.Join("\n", Enumerable.Repeat("var x = 1;", 200));
            string text = new string('a', 2000) + "\n```csharp\n" + code + "\n```\n" + new string('b', 6000);

            var parts = ChatFlowControl.SplitLongText(text);

            Assert.True(parts.Count > 1);
            Assert.Equal(text, string.Concat(parts));          // 围栏外切开：不插标记，原文不变
            Assert.All(parts, p => Assert.Equal(0, Fences(p) % 2));
        }

        [Fact]
        public void Split_HugeFence_ClosesAndReopensEachPart()
        {
            string code = string.Join("\n", Enumerable.Repeat("var y = 2;", 3000));
            string text = "```csharp\n" + code + "\n```\n";

            var parts = ChatFlowControl.SplitLongText(text);

            Assert.True(parts.Count > 1);
            Assert.All(parts, p => Assert.Equal(0, Fences(p) % 2));   // 每片都能自己配平
            Assert.All(parts, p => Assert.True(p.Length <= ChatFlowControl.MaxItemChars + 8));
            Assert.Equal(WithoutFenceLines(text), WithoutFenceLines(string.Concat(parts)));
        }

        [Fact]
        public void Split_PlainTextWithoutNewlines_AddsNoFenceMarkers()
        {
            string text = new string('x', ChatFlowControl.MaxItemChars * 2 + 100);
            var parts = ChatFlowControl.SplitLongText(text);
            Assert.Equal(text, string.Concat(parts));
            Assert.All(parts, p => Assert.DoesNotContain("```", p));
        }
    }
}
