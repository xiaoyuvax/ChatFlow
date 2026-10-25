using System.Collections;
using System.Reflection;
using Xunit;
using ChatFlow;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    /// <summary>
    /// 末尾「思考中…」指示行：执行中必须常驻在最后一条消息之后，执行结束立即移除。
    /// </summary>
    [Collection("ThemeState")]
    public class ChatThinkingIndicatorTests
    {
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
