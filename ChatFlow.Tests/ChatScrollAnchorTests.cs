using System.Collections;
using System.Reflection;
using ChatFlow;
using Xunit;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    /// <summary>
    /// 用户往上翻历史时，后台刷新（流式增量、宿主整表刷新）不得把视口弹走：
    /// 之前流式更新发现「末尾行不在视口」就整表重建，清空高度缓存后又按估算重排，
    /// 每个 250ms 把用户眼前的正文弹到别处，选区也跟着丢。
    /// </summary>
    [Collection("ThemeState")]
    public class ChatScrollAnchorTests
    {
        private static string Body(int lines)
            => string.Concat(Enumerable.Repeat(
                "这是一段较长的正文，用来验证用户往上翻历史时后台刷新不会把视口弹走。\n", lines));

        /// <summary>长短悬殊的会话：估算与实测比例差得远，重建清缓存后位置会明显跑偏。</summary>
        private static readonly int[] Lengths = { 2, 60, 3, 25, 2, 90, 4 };

        private static ChatTurnBook Book(out ChatTurn streaming)
        {
            var book = new ChatTurnBook();
            for (int i = 0; i < Lengths.Length; i++)
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", "问题 " + i),
                    Assistant = new ChatMessage("assistant", Body(Lengths[i]))
                });
            streaming = new ChatTurn
            {
                User = new ChatMessage("user", "最后一个问题"),
                Assistant = new ChatMessage("assistant", Body(50)),
                Streaming = true
            };
            book.AddTurn(streaming);
            return book;
        }

        private static object Field(object target, string name)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

        private static int TopTurn(ChatFlowControl flow)
        {
            var rows = ((IDictionary)Field(flow, "realized")).Values.Cast<ChatMessageRow>()
                .Where(r => r.Visible).ToList();
            var row = rows.Where(r => r.Top <= 1 && r.Top + r.Height > 1)
                .OrderByDescending(r => r.Top).FirstOrDefault();
            return row?.Item == null ? -1 : flow.IndexOfTurn(row.Item.Turn);
        }

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
        public void StreamingUpdates_WhileScrolledUp_KeepViewport()
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
                flow.Bind(Book(out var streaming));
                Pump(500);
                var vbar = (WF.VScrollBar)Field(flow, "vbar");
                Assert.True(vbar.Visible);

                // 用户往上翻到中段
                vbar.Value = (vbar.Maximum - vbar.LargeChange + 1) / 2;
                Pump(300);
                int top = TopTurn(flow);
                Assert.True(top >= 0);

                for (int i = 0; i < 6; i++)
                {
                    streaming.AssistantText = Body(50) + Body((i + 1) * 3);
                    flow.UpdateStreamingMessage(streaming, streaming.AssistantText);
                    Pump(300);
                    Assert.Equal(top, TopTurn(flow));
                }
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void StreamingText_IsAppliedAndKeepsViewport()
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
                // 宿主（Loop/旁路）只在收尾时写 turn.AssistantText：流式正文得由 UpdateStreamingMessage 落到数据上
                var book = Book(out var streaming);
                streaming.AssistantText = "";
                flow.Bind(book);
                Pump(500);
                var vbar = (WF.VScrollBar)Field(flow, "vbar");
                vbar.Value = (vbar.Maximum - vbar.LargeChange + 1) / 2;
                Pump(300);
                int top = TopTurn(flow);

                string text = "";
                for (int i = 0; i < 6; i++)
                {
                    text += Body(3);
                    flow.UpdateStreamingMessage(streaming, text);
                    Pump(300);
                    Assert.Equal(top, TopTurn(flow));
                }

                var items = (IEnumerable<ChatFlowItem>)Field(flow, "items");
                var item = items.LastOrDefault(i => i.Turn == streaming && i.Kind == ChatFlowItemKind.Assistant);
                Assert.NotNull(item);
                Assert.Equal(text, item.Text);
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void FullRebuild_WhileScrolledUp_KeepsViewport()
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
                var book = Book(out _);
                flow.Bind(book);
                Pump(500);
                var vbar = (WF.VScrollBar)Field(flow, "vbar");
                vbar.Value = (vbar.Maximum - vbar.LargeChange + 1) / 2;
                Pump(300);
                int top = TopTurn(flow);
                Assert.True(top > 0);

                book.RaiseChanged();   // 宿主整表刷新（结构变化）
                Pump(300);

                Assert.Equal(top, TopTurn(flow));
            }
            finally
            {
                host.Hide();
            }
        }
    }
}
