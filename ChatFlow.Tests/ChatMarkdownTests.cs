using Xunit;
using ChatFlow;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    [Collection("ThemeState")]
    public class ChatMarkdownTests
    {
        [Theory]
        [InlineData("# 标题")]
        [InlineData("**粗体**")]
        [InlineData("```\ncode\n```")]
        [InlineData("- 第一项\n- 第二项")]
        [InlineData("> 引用")]
        [InlineData("| a | b |\n|---|---|\n| 1 | 2 |")]
        [InlineData("见 [文档](https://example.com)")]
        public void IsMarkdown_DetectsCommonSyntax(string text)
            => Assert.True(ChatMarkdown.IsMarkdown(text));

        [Theory]
        [InlineData("你好，世界。")]
        [InlineData("价格是 3 * 4 = 12")]
        [InlineData("C:\\temp\\a.txt")]
        [InlineData("1. 先做这个")]
        public void IsMarkdown_LeavesPlainTextAlone(string text)
            => Assert.False(ChatMarkdown.IsMarkdown(text));

        [StaFact]
        public void Bind_SwapsFctbForMarkdownBox_OnlyWhenCompletedMarkdown()
        {
            using var flow = new ChatFlowControl();
            using var row = new ChatMessageRow(flow);
            var font = new System.Drawing.Font("Consolas", 9.75F);
            using var host = new WF.Form();
            host.Controls.Add(row);
            host.Show();
            try
            {
                var markdownTurn = new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", "## 标题\n\n- 第一项\n- 第二项"),
                };
                int measured = row.Bind(new ChatFlowItem(markdownTurn, ChatFlowItemKind.Assistant), font, 600);

                Assert.True(row.IsMarkdownRendered);
                Assert.False(row.BodyBox.Visible);
                Assert.True(row.MarkdownBox.Visible);
                Assert.True(measured > 0);

                var plainTurn = new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", "普通纯文本回复"),
                };
                row.Bind(new ChatFlowItem(plainTurn, ChatFlowItemKind.Assistant), font, 600);

                Assert.False(row.IsMarkdownRendered);
                Assert.True(row.BodyBox.Visible);
                Assert.False(row.MarkdownBox.Visible);
            }
            finally
            {
                font.Dispose();
                host.Hide();
            }
        }

        [StaFact]
        public void Bind_MarkdownRow_IndentsBodyAndUsesBorderWrap()
        {
            using var flow = new ChatFlowControl();
            using var row = new ChatMessageRow(flow);
            var font = new System.Drawing.Font("Consolas", 9.75F);
            using var host = new WF.Form();
            host.Controls.Add(row);
            host.Show();
            try
            {
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问"),
                    Assistant = new ChatMessage("assistant", "## 标题\n\n- 一\n- 二"),
                };
                row.Bind(new ChatFlowItem(turn, ChatFlowItemKind.Assistant), font, 600);

                Assert.True(row.IsMarkdownRendered);
                int expectedLeft = (int)Math.Round(12 * row.DeviceDpi / 96f);
                int expectedTop = (int)Math.Round(26 * row.DeviceDpi / 96f);
                Assert.Equal(expectedLeft, row.MarkdownBox.Left);
                Assert.Equal(expectedTop, row.MarkdownBox.Top);
                Assert.True(row.MarkdownBox.Width > 0);
                Assert.True(row.MarkdownBox.Left + row.MarkdownBox.Width <= row.Width);

                Assert.NotNull(row.MarkdownWrap);
                Assert.True(row.MarkdownWrap.Visible);
                Assert.Equal(expectedLeft, row.MarkdownWrap.Width);
                Assert.Equal(flow.BackColor, row.MarkdownWrap.BackColor);
            }
            finally
            {
                font.Dispose();
                host.Hide();
            }
        }

        [StaFact]
        public void Bind_KeepsFctbWhileStreaming()
        {
            using var flow = new ChatFlowControl();
            using var row = new ChatMessageRow(flow);
            var font = new System.Drawing.Font("Consolas", 9.75F);
            using var host = new WF.Form();
            host.Controls.Add(row);
            host.Show();
            try
            {
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", "**流式中的粗体**"),
                    Streaming = true,
                };
                row.Bind(new ChatFlowItem(turn, ChatFlowItemKind.Assistant), font, 600);

                Assert.False(row.IsMarkdownRendered);
                Assert.True(row.BodyBox.Visible);
            }
            finally
            {
                font.Dispose();
                host.Hide();
            }
        }
    }
}
