using System.Collections;
using System.Reflection;
using Xunit;
using ChatFlow;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    /// <summary>
    /// 末尾「工作中…」指示行：正文还没出字时占位（不额外显示空白正文框），一开始流式正文就撤掉，执行结束也移除。
    /// </summary>
    [Collection("ThemeState")]
    public class ChatThinkingIndicatorTests
    {
        [StaFact]
        public void StreamingTurn_NoBodyText_HasNoBlankMessageRow()
        {
            var book = new ChatTurnBook();
            book.AddTurn(NewStreamingTurn());
            using var flow = new ChatFlowControl();

            flow.Bind(book);

            var items = Items(flow);
            Assert.Equal(2, items.Count);   // 只有用户消息 + 指示行，没有空白正文行
            Assert.Equal(ChatFlowItemKind.Thinking, items[^1].Kind);
            Assert.DoesNotContain(items, item => item.Kind == ChatFlowItemKind.Assistant);
        }

        [StaFact]
        public void StreamingTurn_BodyTextArrived_RemovesThinkingIndicator()
        {
            var book = new ChatTurnBook();
            var turn = NewStreamingTurn();
            book.AddTurn(turn);
            using var flow = new ChatFlowControl();
            flow.Bind(book);

            turn.AssistantText = "先说结论：";
            book.RaiseChanged();

            var items = Items(flow);
            Assert.DoesNotContain(items, item => item.Kind == ChatFlowItemKind.Thinking);
            Assert.Equal(ChatFlowItemKind.Assistant, items[^1].Kind);
        }

        [StaFact]
        public void EmptyBody_WithThinking_LeavesNoBlankBodyBox()
        {
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "测试"),
                Assistant = new ChatMessage("assistant", ""),
                Streaming = true,
                ReasoningText = "先看仓库结构。",
            };
            using var owner = new ChatFlowControl();
            using var row = new ChatMessageRow(owner);
            using var host = new WF.Form();
            host.Controls.Add(row);
            host.Show();
            try
            {
                row.Bind(new ChatFlowItem(turn, ChatFlowItemKind.Assistant), new Font("Consolas", 9.75F), 600);
                Assert.Equal(0, row.BodyBox.Height);
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void StreamingTurn_AppendsThinkingIndicatorAtEnd()
        {
            var book = new ChatTurnBook();
            book.AddTurn(NewStreamingTurn());
            using var flow = new ChatFlowControl();

            flow.Bind(book);

            var items = Items(flow);
            Assert.NotEmpty(items);
            Assert.Equal(ChatFlowItemKind.Thinking, items[^1].Kind);
            Assert.Equal(string.Empty, items[^1].Text);
        }

        [StaFact]
        public void StreamingEnded_RemovesThinkingIndicator()
        {
            var book = new ChatTurnBook();
            var turn = NewStreamingTurn();
            book.AddTurn(turn);
            using var flow = new ChatFlowControl();
            flow.Bind(book);

            turn.Streaming = false;
            book.RaiseChanged();

            Assert.DoesNotContain(Items(flow), item => item.Kind == ChatFlowItemKind.Thinking);
        }

        [StaFact]
        public void VisibleFlow_CreatesThinkingRow()
        {
            using var flow = new ChatFlowControl { Size = new Size(600, 400) };
            using var host = new WF.Form();
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                book.AddTurn(NewStreamingTurn());

                flow.Bind(book);
                Application.DoEvents();

                Assert.Equal(1, ThinkingRowCount(flow));
                var row = ThinkingRow(flow);
                Assert.NotNull(row);
                Assert.True(row.Width > 0 && row.Height > 0, "指示行应已布局");

                // 绘制冒烟：指示条应真的画出胶囊/弧线/文案（不能是纯底色）
                using var bmp = new Bitmap(row.Width, row.Height);
                row.DrawToBitmap(bmp, new Rectangle(0, 0, row.Width, row.Height));
                Color back = bmp.GetPixel(0, 0);
                int painted = 0;
                for (int y = 0; y < bmp.Height; y += 2)
                    for (int x = 0; x < bmp.Width; x += 2)
                        if (bmp.GetPixel(x, y) != back) painted++;
                Assert.True(painted > 20, $"指示行未绘制，差异像素 {painted}");
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void StreamingTurn_BodyArrivesViaStreamingUpdate_RemovesThinkingIndicator()
        {
            // 现场：先有思考区（助手行已实例化），正文随后流式到达；此时走就地重绑，指示行也得撤掉
            using var flow = new ChatFlowControl { Size = new Size(600, 400) };
            using var host = new WF.Form();
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                var turn = NewStreamingTurn();
                turn.ReasoningText = "先看仓库结构。";
                book.AddTurn(turn);
                flow.Bind(book);
                Application.DoEvents();
                Assert.Equal(1, ThinkingRowCount(flow));

                turn.AssistantText = "先说结论：";
                flow.UpdateStreamingMessage(turn, "先说结论：");

                Assert.DoesNotContain(Items(flow), item => item.Kind == ChatFlowItemKind.Thinking);
                Application.DoEvents();
                Assert.Equal(0, ThinkingRowCount(flow));
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void StreamingEnded_WithoutBody_RemovesThinkingIndicatorViaStreamingUpdate()
        {
            using var flow = new ChatFlowControl { Size = new Size(600, 400) };
            using var host = new WF.Form();
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                var turn = NewStreamingTurn();
                turn.ReasoningText = "先看仓库结构。";
                book.AddTurn(turn);
                flow.Bind(book);
                Application.DoEvents();
                Assert.Equal(1, ThinkingRowCount(flow));

                turn.Streaming = false;
                flow.UpdateStreamingMessage(turn, "");

                Assert.DoesNotContain(Items(flow), item => item.Kind == ChatFlowItemKind.Thinking);
                Application.DoEvents();
                Assert.Equal(0, ThinkingRowCount(flow));
            }
            finally
            {
                host.Hide();
            }
        }

        private static ChatTurn NewStreamingTurn() => new()
        {
            User = new ChatMessage("user", "帮我整理一下这次改动"),
            Assistant = new ChatMessage("assistant", ""),
            Streaming = true,
        };

        private static List<ChatFlowItem> Items(ChatFlowControl flow)
            => (List<ChatFlowItem>)Field(flow, "items");

        private static int ThinkingRowCount(ChatFlowControl flow)
            => ((IDictionary)Field(flow, "thinkingRows")).Count;

        private static Control ThinkingRow(ChatFlowControl flow)
            => ((IDictionary)Field(flow, "thinkingRows")).Values.Cast<Control>().FirstOrDefault();

        private static object Field(ChatFlowControl flow, string name)
            => typeof(ChatFlowControl)
                .GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?
                .GetValue(flow)
                ?? throw new InvalidOperationException($"字段 {name} 不存在");
    }
}
