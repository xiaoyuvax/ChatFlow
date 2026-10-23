using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using Xunit;

namespace ChatFlow.Tests
{
    /// <summary>缩放字体后行高必须按新字体重新测量；否则 FCTB 会裁切放大后的中文。</summary>
    [Collection("ThemeState")]
    public class ChatFontZoomTests
    {
        [StaFact]
        public void ChatFont_Zoom_RemeasuresRows()
        {
            using var host = new Form { Size = new Size(900, 600), StartPosition = FormStartPosition.Manual };
            var flow = new ChatFlowControl { Dock = DockStyle.Fill, ChatFont = new Font("Arial", 9f) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", "缩放测试"),
                    Assistant = new ChatMessage("assistant",
                        string.Join("\n", Enumerable.Repeat("字体放大后中文行高必须跟着增大，不能裁切。", 10)))
                });
                flow.Bind(book);
                Application.DoEvents();

                var row = flow.Controls.OfType<ChatMessageRow>().First(r => r.Item != null && !r.Item.IsUser);
                int before = row.Height;

                flow.ChatFont = new Font("Arial", 14f);
                Application.DoEvents();
                row = flow.Controls.OfType<ChatMessageRow>().First(r => r.Item != null && !r.Item.IsUser);
                Assert.True(row.Height > before, $"zoom in did not grow the row ({before} -> {row.Height})");
                Assert.Equal(0, row.GetClipping());

                flow.ChatFont = new Font("Arial", 9f);
                Application.DoEvents();
                row = flow.Controls.OfType<ChatMessageRow>().First(r => r.Item != null && !r.Item.IsUser);
                Assert.InRange(row.Height, before - 2, before + 2);
                Assert.Equal(0, row.GetClipping());
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }
    }
}
