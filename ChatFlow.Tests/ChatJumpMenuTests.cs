using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Xunit;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    /// <summary>
    /// 「回到顶部 / 跳到末尾」浮动按钮的悬停菜单：列出该方向上的每一轮，点一行跳到那一轮开头。
    /// </summary>
    [Collection("ThemeState")]
    public class ChatJumpMenuTests
    {
        private static object Field(object target, string name)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

        private static ChatTurnBook Book(int turns, int lines = 12)
        {
            var body = string.Concat(Enumerable.Repeat("这是一段用来撑高的正文，验证浮层跳转到某一轮。\n", lines));
            var book = new ChatTurnBook();
            for (int i = 0; i < turns; i++)
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", $"第 {i + 1} 个问题：这一轮的用户输入要能显示在菜单里"),
                    Assistant = new ChatMessage("assistant", body)
                });
            return book;
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

        private static WF.VScrollBar Bar(ChatFlowControl flow) => (WF.VScrollBar)Field(flow, "vbar");

        private static ChatFlowControl Host(out Form host)
        {
            var flow = new ChatFlowControl
            {
                Size = new Size(900, 500),
                ChatFont = new Font("Microsoft YaHei UI", 9F)
            };
            host = new Form { Size = new Size(940, 560) };
            host.Controls.Add(flow);
            host.Show();
            flow.Dock = DockStyle.Fill;
            return flow;
        }

        [StaFact]
        public void HoverTopButton_ListsPreviousTurnsNearestFirst()
        {
            var flow = Host(out var host);
            try
            {
                flow.Bind(Book(12));
                Pump(400);
                Assert.False(flow.JumpMenuVisible);

                flow.BeginJumpMenuHover(fromTop: true);
                Pump(120);
                Assert.True(flow.JumpMenuVisible);
                var texts = flow.JumpMenuTexts;
                Assert.True(texts.Count >= 10, $"菜单项太少：{texts.Count}");
                // 由近及远：第一项是视口上方最近的一轮，最后一项是第一条
                Assert.Contains("第 11 个问题", texts[0]);
                Assert.Contains("第 1 个问题", texts[texts.Count - 1]);
                // 视口当前所在的这一轮不在「之前」里
                Assert.DoesNotContain(texts, t => t.Contains("第 12 个问题"));
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void HoverTopButton_MenuSitsUnderButtonInsideClient()
        {
            var flow = Host(out var host);
            try
            {
                flow.Bind(Book(12));
                Pump(400);
                flow.BeginJumpMenuHover(fromTop: true);
                Pump(120);
                Assert.True(flow.JumpMenuVisible);

                var menu = (Control)Field(flow, "jumpMenu");
                var anchor = (Control)Field(flow, "jumpToTopButton");
                // 顶部按钮的菜单在其下方，且完整落在控件可见区内（不会压到按钮本身）
                Assert.True(menu.Top >= anchor.Bottom);
                Assert.True(menu.Right <= flow.ClientSize.Width);
                Assert.True(menu.Bottom <= flow.ClientSize.Height);
                Assert.True(menu.Width > 40 && menu.Height > 20);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void HoverEndButton_ListsFollowingTurnsNearestFirst_AndClickJumpsToTurn()
        {
            var flow = Host(out var host);
            try
            {
                flow.Bind(Book(12));
                Pump(400);
                flow.ScrollToTop();
                Pump(150);

                flow.BeginJumpMenuHover(fromTop: false);
                Pump(120);
                Assert.True(flow.JumpMenuVisible);
                var texts = flow.JumpMenuTexts;
                Assert.Equal(11, texts.Count);   // 顶部之后还剩 11 轮
                Assert.Contains("第 2 个问题", texts[0]);
                Assert.Contains("第 12 个问题", texts[texts.Count - 1]);

                // 点第一项：第 2 轮贴到视口顶部，自动跟随关闭、末尾按钮出现
                var offsets = flow.ScrollTickOffsets;
                flow.ClickJumpMenuItem(0);
                Pump(250);
                Assert.False(flow.JumpMenuVisible);
                Assert.False(flow.AtEnd);
                Assert.Equal(offsets[1], Bar(flow).Value);
                var endButton = (Control)Field(flow, "jumpToEndButton");
                Assert.True(endButton.Visible);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void HoverAtTop_ShowsNoMenu()
        {
            var flow = Host(out var host);
            try
            {
                flow.Bind(Book(6));
                Pump(400);
                flow.ScrollToTop();
                Pump(150);
                flow.BeginJumpMenuHover(fromTop: true);
                Pump(120);
                Assert.False(flow.JumpMenuVisible);   // 上面没有别的轮次：不弹空菜单
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void MenuLabel_TruncatesLongInputWithEllipsis()
        {
            var flow = Host(out var host);
            try
            {
                var book = new ChatTurnBook();
                string longInput = string.Join("", Enumerable.Repeat("很长的用户输入", 60));   // 420 字
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", longInput),
                    Assistant = new ChatMessage("assistant", string.Concat(Enumerable.Repeat("撑高正文。\n", 12)))
                });
                for (int i = 0; i < 10; i++)
                    book.AddTurn(new ChatTurn
                    {
                        User = new ChatMessage("user", $"第 {i + 2} 个问题"),
                        Assistant = new ChatMessage("assistant", string.Concat(Enumerable.Repeat("撑高正文。\n", 12)))
                    });
                flow.Bind(book);
                Pump(400);

                flow.BeginJumpMenuHover(fromTop: true);
                Pump(120);
                Assert.True(flow.JumpMenuVisible);
                string last = flow.JumpMenuTexts[^1];   // 由近及远：最后一项就是第一条长输入
                Assert.EndsWith("…", last);
                Assert.True(last.Length <= ChatFlowControl.JumpMenuChars + 1, $"截断长度不对：{last.Length}");
                Assert.StartsWith("很长的用户输入", last);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void ShowTurnPicker_ListsAllUserTurnsNewestFirst_AndPicksOne()
        {
            var flow = Host(out var host);
            try
            {
                flow.Bind(Book(5));
                Pump(400);
                ChatTurn picked = null;
                flow.ShowTurnPicker(turn => picked = turn);
                Pump(120);
                Assert.True(flow.JumpMenuVisible);

                var menu = (Control)Field(flow, "jumpMenu");
                // 上弹：完整落在控件底部一侧
                Assert.True(menu.Bottom <= flow.ClientSize.Height);
                Assert.True(menu.Top > flow.ClientSize.Height / 2);
                Assert.True(menu.Right <= flow.ClientSize.Width);

                var texts = flow.JumpMenuTexts;
                Assert.Equal(5, texts.Count);
                Assert.StartsWith("5/5 · ", texts[0]);            // 最近的一轮在最上面 = 全部上下文
                Assert.Contains("第 5 个问题", texts[0]);
                Assert.StartsWith("1/5 · ", texts[4]);

                flow.ClickJumpMenuItem(2);                        // 从上往下第 3 行 = 第 3 轮
                Pump(120);
                Assert.False(flow.JumpMenuVisible);
                Assert.NotNull(picked);
                Assert.StartsWith("第 3 个问题", picked.UserText);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }
    }
}
