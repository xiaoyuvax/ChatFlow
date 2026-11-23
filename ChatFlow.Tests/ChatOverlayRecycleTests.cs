using System.Collections;
using System.Reflection;
using ChatFlow;
using Xunit;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    /// <summary>
    /// 浮层控件（「工作中…」指示行 / 链接行 / 调查卡）离屏回收。
    /// 回归背景：离屏只 Hide() 留在控件树里，而每次 RebuildItems 都换一批 ChatFlowItem 对象，
    /// 旧控件再也对不上号 → 流式回合每刷新一次就漏一个窗口句柄；跑十几小时后 USER 句柄耗尽，
    /// CreateHandle 全线失败（Win32 1158「创建窗口句柄时出错」），界面连锁崩塌、进程退出。
    /// </summary>
    [Collection("ThemeState")]
    public class ChatOverlayRecycleTests
    {
        private static void Pump(int ms)
        {
            long start = Environment.TickCount64;
            while (Environment.TickCount64 - start < ms)
            {
                WF.Application.DoEvents();
                Thread.Sleep(5);
            }
        }

        private static int Count<T>(ChatFlowControl flow) where T : Control
            => flow.Controls.OfType<T>().Count();

        private static WF.VScrollBar Scrollbar(ChatFlowControl flow)
            => (WF.VScrollBar)typeof(ChatFlowControl)
                .GetField("vbar", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(flow);

        private static int Realized(ChatFlowControl flow)
            => ((IDictionary)typeof(ChatFlowControl)
                .GetField("realized", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(flow)).Count;

        [StaFact]
        public void StreamingRebuilds_ReuseIndicatorRow()
        {
            using var host = new WF.Form { Size = new Size(900, 600) };
            using var flow = new ChatFlowControl { Dock = WF.DockStyle.Fill };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问"),
                    Assistant = new ChatMessage("assistant", ""),
                    Streaming = true,
                };
                book.AddTurn(turn);
                flow.Bind(book);
                Pump(80);
                Assert.Equal(1, Count<ChatThinkingRow>(flow));

                // 宿主每次刷新都整表重建：指示行必须复用同一个控件，而不是每轮再漏一个
                for (int i = 0; i < 15; i++)
                {
                    turn.Assistant.Text = "第 " + i + " 段正文";
                    book.RaiseTextChanged();
                    Pump(10);
                }
                Assert.Equal(1, Count<ChatThinkingRow>(flow));
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void StreamingRebuilds_DoNotGrowRowPool()
        {
            using var host = new WF.Form { Size = new Size(900, 600) };
            using var flow = new ChatFlowControl { Dock = WF.DockStyle.Fill };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问"),
                    Assistant = new ChatMessage("assistant", ""),
                    Streaming = true,
                };
                book.AddTurn(turn);
                flow.Bind(book);
                Pump(80);
                int baseline = Realized(flow);

                for (int i = 0; i < 15; i++)
                {
                    turn.Assistant.Text = "第 " + i + " 段正文，长度够触发换行测量。";
                    book.RaiseTextChanged();
                    Pump(10);
                }
                // 重建只该换条目对象，实例化行数应与一开始一致（虚拟化复用行池，不再越建越多）
                Assert.True(Realized(flow) <= baseline + 1,
                    $"实例化行数从 {baseline} 涨到 {Realized(flow)}，说明重建后旧行没有被复用");
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void Scrolling_ReusesLinkRows()
        {
            using var host = new WF.Form { Size = new Size(900, 600) };
            using var flow = new ChatFlowControl { Dock = WF.DockStyle.Fill };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                for (int i = 0; i < 40; i++)
                    book.AddTurn(new ChatTurn
                    {
                        User = new ChatMessage("user", "问题 " + i),
                        Assistant = new ChatMessage("assistant", "回答 " + i),
                    });
                flow.Bind(book);
                flow.SetLinks(Enumerable.Range(0, 40)
                    .Select(i => new ChatLink { Id = "l" + i, AnchorTurnIndex = i, Title = "链接 " + i })
                    .ToList());
                Pump(200);

                var vbar = Scrollbar(flow);
                Assert.True(vbar.Visible, "内容应够长到出现滚动条");
                int before = Count<ChatLinkRow>(flow);
                for (int i = 0; i < 4; i++)
                {
                    vbar.Value = Math.Max(vbar.Minimum, vbar.Maximum - vbar.LargeChange + 1);
                    Pump(50);
                    vbar.Value = vbar.Minimum;
                    Pump(50);
                }
                int after = Count<ChatLinkRow>(flow);
                Assert.True(after <= before + 2, $"链接行离屏后应被复用，不该越滚越多：{before} → {after}");
            }
            finally
            {
                host.Hide();
            }
        }
    }
}
