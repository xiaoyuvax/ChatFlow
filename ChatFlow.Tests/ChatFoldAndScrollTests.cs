using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Xunit;

namespace ChatFlow.Tests
{
    /// <summary>右键「折叠所有思维链区域」与右上角「回到顶部」浮动按钮。</summary>
    [Collection("ThemeState")]
    public class ChatFoldAndScrollTests
    {
        private static object Field(object target, string name)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

        [StaFact]
        public void CollapseAllThinking_CollapsesPlainAndPhasedAreas()
        {
            using var host = new Form { Size = new Size(900, 600), StartPosition = FormStartPosition.Manual };
            var flow = new ChatFlowControl { Dock = DockStyle.Fill };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                // 有时间线的回话默认展开：这正是「折叠所有」要收掉的对象
                var plain = new ChatTurn
                {
                    User = new ChatMessage("user", "问题一"),
                    Assistant = new ChatMessage("assistant", "回答一"),
                    TimelineText = "→ read_file(a.txt) · 1s"
                };
                var phased = new ChatTurn
                {
                    User = new ChatMessage("user", "问题二"),
                    Assistant = new ChatMessage("assistant", "回答二"),
                    TimelineText = "→ read_file(b.txt) · 1s"
                };
                phased.Phases.Add(("先看目录", "第一段正文"));
                phased.Phases.Add(("再写文件", "第二段正文"));
                book.AddTurn(plain);
                book.AddTurn(phased);
                flow.Bind(book);
                Application.DoEvents();

                Assert.True(flow.HasExpandedThinking);
                flow.CollapseAllThinking();
                Application.DoEvents();

                Assert.False(plain.ThinkingExpanded ?? true);
                for (int p = 0; p < phased.Phases.Count; p++)
                    Assert.False(phased.PhaseExpanded[p]);
                Assert.False(flow.HasExpandedThinking);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void ScrollToTop_LeavesEndAndHidesTopButton()
        {
            using var flow = new ChatFlowControl
            {
                Size = new Size(900, 600),
                ChatFont = new Font("Microsoft YaHei UI", 9F)
            };
            using var host = new Form { Size = new Size(940, 660) };
            host.Controls.Add(flow);
            flow.Dock = DockStyle.Fill;
            host.Show();
            try
            {
                var body = string.Join("\n\n",
                    Enumerable.Range(0, 200).Select(i => $"第 {i} 段：用于撑高内容的占位文本，长度要足够换行。"));
                var book = new ChatTurnBook();
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", "写一篇长文"),
                    Assistant = new ChatMessage("assistant", body)
                });
                flow.Bind(book);
                Application.DoEvents();
                flow.ScrollToEnd();
                Application.DoEvents();
                Assert.True(flow.AtEnd, flow.GetLayoutDiagnostics());

                var topButton = (Control)Field(flow, "jumpToTopButton");
                Assert.True(topButton.Visible);
                // 右上角：水平在右半区、垂直在上半区
                Assert.True(topButton.Left > flow.ClientSize.Width / 2);
                Assert.True(topButton.Top < flow.ClientSize.Height / 2);

                flow.ScrollToTop();
                Application.DoEvents();
                Assert.False(flow.AtEnd, flow.GetLayoutDiagnostics());
                Assert.False(topButton.Visible);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }
    }
}
