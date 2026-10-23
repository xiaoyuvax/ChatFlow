using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Xunit;

namespace ChatFlow.Tests
{
    [Collection("ThemeState")]
    public class ChatActionsTests
    {
        [StaFact]
        public void PairEnd_RaisesUndoAndCopyWithTurn()
        {
            using var host = new Form { Size = new Size(900, 600), StartPosition = FormStartPosition.Manual };
            var flow = new ChatFlowControl { Dock = DockStyle.Fill };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", "回答")
                };
                var book = new ChatTurnBook();
                book.AddTurn(turn);
                flow.Bind(book);
                Application.DoEvents();

                var row = flow.Controls.OfType<ChatMessageRow>()
                    .First(r => r.Item != null && !r.Item.IsUser);
                Assert.True(row.Item.IsPairEnd);

                ChatTurn undone = null;
                ChatTurn copied = null;
                flow.UndoRequested += (_, e) => undone = e.Turn;
                flow.CopyRequested += (_, e) => copied = e.Turn;
                flow.RequestUndo(row.Item);
                flow.RequestCopy(row.Item);

                Assert.Same(turn, undone);
                Assert.Same(turn, copied);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }
    }
}
