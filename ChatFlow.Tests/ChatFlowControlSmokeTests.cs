using System.Drawing;
using Xunit;

namespace ChatFlow.Tests
{
    /// <summary>冒烟：五条回话（含 markdown、工具行、思考区）能绑定并完成布局，不抛异常。</summary>
    [Collection("ThemeState")]
    public class ChatFlowControlSmokeTests
    {
        [StaFact]
        public void BindFiveTurns_RendersWithoutError()
        {
            using var flow = new ChatFlowControl();
            var book = new ChatTurnBook();
            for (int i = 0; i < 5; i++)
            {
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", $"问题 {i}"),
                    Assistant = new ChatMessage("assistant",
                        "# 标题\n\n**粗体** 与 `code` 与 *斜体*\n\n```\nblock\n```\n\n- 列表项")
                };
                if (i == 0)
                {
                    turn.ToolCallLines = "→ read_file(path=a.txt)";
                    turn.ToolStepCount = 1;
                    turn.ToolTotalMs = 420;
                    turn.ReasoningText = "先看目录";
                }
                book.AddTurn(turn);
            }

            var host = new Form { Size = new Size(900, 500) };
            host.Controls.Add(flow);
            flow.Dock = DockStyle.Fill;
            host.Show();
            try
            {
                flow.Bind(book);
                Assert.Equal(5, book.Turns.Count);
                Assert.True(flow.Height > 0);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }
    }
}
