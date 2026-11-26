using System.Collections;
using System.Reflection;
using ChatFlow;
using Xunit;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    /// <summary>
    /// 会话页性能：不在前台时暂停渲染；向上回溯历史时新填出的行默认收起思考区。
    /// </summary>
    [Collection("ThemeState")]
    public class ChatRenderPerformanceTests
    {
        private static string Body(int lines)
            => string.Concat(Enumerable.Repeat("这是一段用来撑高内容、让历史需要滚动的正文。\n", lines));

        private static ChatTurnBook Book(int turns)
        {
            var book = new ChatTurnBook();
            for (int i = 0; i < turns; i++)
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", "问题 " + i),
                    Assistant = new ChatMessage("assistant", Body(10)),
                    ReasoningText = string.Join("\n", Enumerable.Range(0, 30).Select(n => $"推理第 {n} 行，内容略长一些。")),
                    ToolCallLines = "→ read_file(path=a.txt)",
                    ThinkingExpanded = true
                });
            return book;
        }

        private static object Field(object target, string name)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

        private static List<ChatFlowItem> Items(ChatFlowControl flow)
            => ((IList)Field(flow, "items")).Cast<ChatFlowItem>().ToList();

        private static List<ChatMessageRow> VisibleRows(ChatFlowControl flow)
            => ((IDictionary)Field(flow, "realized")).Values.Cast<ChatMessageRow>()
                .Where(r => r.Visible && r.Bottom > 0 && r.Top < flow.ClientSize.Height)
                .ToList();

        private static void Pump(int ms)
        {
            long start = Environment.TickCount64;
            while (Environment.TickCount64 - start < ms)
            {
                WF.Application.DoEvents();
                System.Threading.Thread.Sleep(10);
            }
        }

        [StaFact]
        public void SuspendRendering_SkipsLayoutUntilResumed()
        {
            using var flow = new ChatFlowControl();
            using var host = new WF.Form { Size = new System.Drawing.Size(900, 500) };
            host.Controls.Add(flow);
            flow.Dock = WF.DockStyle.Fill;
            host.Show();
            try
            {
                var book = Book(12);
                flow.SuspendRendering = true;
                flow.Bind(book);
                Assert.Empty(Items(flow));
                Assert.Empty(((IDictionary)Field(flow, "realized")).Keys.Cast<object>());

                // 暂停期间的流式更新只落数据，不触发重建
                var streaming = book.Turns[^1];
                flow.UpdateStreamingMessage(streaming, Body(20));
                Assert.Empty(Items(flow));

                flow.SuspendRendering = false;
                Assert.NotEmpty(Items(flow));
                Assert.NotEmpty(((IDictionary)Field(flow, "realized")).Keys.Cast<object>());
                Assert.True(flow.AtEnd, flow.GetLayoutDiagnostics());
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void ScrollingUp_RealizesHistoryRowsCollapsed()
        {
            using var flow = new ChatFlowControl
            {
                Size = new System.Drawing.Size(900, 600),
                ChatFont = new System.Drawing.Font("Microsoft YaHei UI", 9F)
            };
            using var host = new WF.Form { Size = new System.Drawing.Size(940, 660) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = Book(20);
                flow.Bind(book);
                Pump(500);
                var vbar = (WF.VScrollBar)Field(flow, "vbar");
                Assert.True(vbar.Visible);

                // 用户往上翻历史：新填出来的行默认折叠，不管数据侧记录的是展开
                vbar.Value = (vbar.Maximum - vbar.LargeChange + 1) / 2;
                Pump(400);

                var history = VisibleRows(flow)
                    .Where(r => r.Item?.Kind == ChatFlowItemKind.Assistant && r.Item.ThinkingText != null)
                    .ToList();
                Assert.NotEmpty(history);
                Assert.All(history, r => Assert.False(r.Item.ThinkingExpanded));
                Assert.All(history, r => Assert.True(r.Item.Turn.ThinkingExpanded));

                // 用户点开的那一块保持展开（浏览期强制折叠不能覆盖用户的选择）
                var item = history[0].Item;
                var toggle = typeof(ChatFlowControl).GetMethod("Row_ThinkingToggled",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.NotNull(toggle);
                toggle.Invoke(flow, [item]);
                Pump(300);

                var again = Items(flow).First(i => ReferenceEquals(i.Turn, item.Turn) && i.Kind == ChatFlowItemKind.Assistant);
                Assert.True(again.ThinkingExpanded);
            }
            finally
            {
                host.Hide();
            }
        }
    }
}
