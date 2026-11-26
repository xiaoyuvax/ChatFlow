using System.Drawing;
using Xunit;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    /// <summary>
    /// A table in a long reply takes the plain-text path (FCTB). The turn finishes with the same
    /// text as the last streaming chunk, so the row must still re-render then: the streaming pass
    /// cannot pad or hide the table (its source is still moving).
    /// </summary>
    public class ChatTableRenderTests
    {
        [StaFact]
        public void StreamingEnd_ReRendersLongText_SoTablePipesAreHidden()
        {
            using var flow = new ChatFlowControl();
            using var row = new ChatMessageRow(flow);
            using var font = new Font("Microsoft YaHei UI", 9.75F);
            using var host = new WF.Form();
            host.Controls.Add(row);
            host.Show();
            try
            {
                row.Width = 900;
                row.Height = 900;
                string text = new string('x', 3200) + "\n\n| a | b |\n| --- | --- |\n| ccc | d |";
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "hi"),
                    Assistant = new ChatMessage("assistant", text),
                    Streaming = true,
                };
                var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
                row.Bind(item, font, 600);

                turn.Streaming = false;
                row.Bind(item, font, 600);

                var box = row.BodyBox;
                for (int iLine = box.LinesCount - 3; iLine < box.LinesCount; iLine++)
                    for (int c = 0; c < box[iLine].Count; c++)
                        if (box[iLine][c].C == '|')
                            Assert.True(box.IsCharHidden(box[iLine], c),
                                $"pipe at line {iLine} col {c} is still visible after the turn finished");
            }
            finally
            {
                host.Hide();
            }
        }
    }
}
