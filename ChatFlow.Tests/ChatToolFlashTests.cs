using System.Collections;
using System.Reflection;
using FastColoredTextBoxNS.Text;
using Xunit;

namespace ChatFlow.Tests
{
    /// <summary>执行中的工具行（ChatTurn.ToolStatusText，形如 " >> Read[path=a.cs]"）在正文区闪现：
    /// 必须走 FCTB 并像思考区一样给工具名/方括号染色，不能落进 MarkdownTextBox
    /// （" >> " 会被 markdown 当块引用识别，于是既不染色也排成引用块）。</summary>
    [Collection("ThemeState")]
    public class ChatToolFlashTests
    {
        private static ChatMessageRow AssistantRow(ChatFlowControl flow)
        {
            var field = typeof(ChatFlowControl).GetField("realized", BindingFlags.NonPublic | BindingFlags.Instance)!;
            object dict = field.GetValue(flow)!;
            var values = (ICollection)dict.GetType().GetProperty("Values")!.GetValue(dict)!;
            foreach (ChatMessageRow row in values)
                if (row.Item is { IsUser: false }) return row;
            return null;
        }

        private static bool HasToolStyle(ChatMessageRow row, int line, int charIndex)
        {
            var chars = row.BodyBox[line];
            return chars[charIndex].Styles?.Contains(ChatTextStyler.ToolCallStyleField) == true;
        }

        [StaFact]
        public void StreamingToolFlash_StaysInFctb_AndToolNameIsStyled()
        {
            using var flow = new ChatFlowControl();
            using var host = new Form { Size = new Size(900, 500) };
            host.Controls.Add(flow);
            flow.Dock = DockStyle.Fill;
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", ""),
                    Streaming = true
                };
                book.AddTurn(turn);
                flow.Bind(book);
                Application.DoEvents();

                // 宿主 onToolStart 的同一路径：清正文、挂执行中工具行
                turn.ToolStarted(" >> Read[path=a.cs]");
                flow.UpdateStreamingMessage(turn, "", force: true);
                Application.DoEvents();

                var row = AssistantRow(flow);
                Assert.NotNull(row);
                Assert.False(row.IsMarkdownRendered, "执行中的工具行不该走 markdown 渲染");
                Assert.True(HasToolStyle(row, 0, " >> Read".Length - 1), "工具名未染色");
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        /// <summary>宿主没来得及清 ToolStatusText（停止/失败轮）、或正文里就只剩工具行时，
        /// 正文已不是流式：仍必须按 FCTB 工具行渲染，而不是被 markdown 吞成引用块。</summary>
        [StaFact]
        public void SettledToolFlash_StaysInFctb_AndToolNameIsStyled()
        {
            using var flow = new ChatFlowControl();
            using var host = new Form { Size = new Size(900, 500) };
            host.Controls.Add(flow);
            flow.Dock = DockStyle.Fill;
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", " >> Grep[pattern=ChatFlow]")
                };
                book.AddTurn(turn);
                flow.Bind(book);
                Application.DoEvents();

                var row = AssistantRow(flow);
                Assert.NotNull(row);
                Assert.False(row.IsMarkdownRendered, "只有工具行的正文不该走 markdown 渲染");
                Assert.True(HasToolStyle(row, 0, " >> Grep".Length - 1), "工具名未染色");
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        /// <summary>工具收尾后正文还没出字：正文 FCTB 不隐藏、保持原位（行高与闪现时一致），
        /// 下一批工具行直接复用同一块，末尾内容不会随闪现反复上下跳。</summary>
        [StaFact]
        public void FlashSlot_StaysInPlace_AfterToolFinishes()
        {
            using var flow = new ChatFlowControl();
            using var host = new Form { Size = new Size(900, 500) };
            host.Controls.Add(flow);
            flow.Dock = DockStyle.Fill;
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", ""),
                    Streaming = true
                };
                book.AddTurn(turn);
                flow.Bind(book);
                Application.DoEvents();

                turn.ToolStarted(" >> Read[path=a.cs]");
                flow.UpdateStreamingMessage(turn, "", force: true);
                Application.DoEvents();

                var row = AssistantRow(flow);
                Assert.NotNull(row);
                int flashHeight = row.Height;
                Assert.True(row.BodyBox.Height > 0, "闪现时正文 FCTB 应占位");

                // 工具收尾、正文还没出字：位置保留，不回落成 0
                turn.ToolsFinished();
                turn.ToolStepCount = 1;
                flow.UpdateStreamingMessage(turn, "", force: true);
                Application.DoEvents();
                Assert.True(row.BodyBox.Height > 0, "闪现结束后正文 FCTB 不该隐藏");
                Assert.Equal("", row.BodyBox.Text);
                Assert.Equal(flashHeight, row.Height);

                // 下一批工具行复用同一块：仍在原位
                turn.ToolStarted(" >> Grep[pattern=x]");
                flow.UpdateStreamingMessage(turn, "", force: true);
                Application.DoEvents();
                Assert.Equal(flashHeight, row.Height);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [Fact]
        public void UseMarkdown_RejectsToolFlashOnlyText()
        {
            Assert.False(ChatMarkdown.UseMarkdown(" >> Read[path=a.cs]", false));
            Assert.False(ChatMarkdown.UseMarkdown(" >> Read[path=a.cs]\n >> Grep[pattern=x]", false));
            Assert.False(ChatMarkdown.UseMarkdown("» #1 app_status() → 成功 · 12ms", false));
            // 真正的正文仍按 markdown 判定
            Assert.True(ChatMarkdown.UseMarkdown("# 标题\n\n正文", false));
            Assert.False(ChatMarkdown.UseMarkdown("# 标题\n\n正文", true));
        }
    }
}
