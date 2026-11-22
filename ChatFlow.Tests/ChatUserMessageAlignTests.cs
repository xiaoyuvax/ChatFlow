using System.Reflection;
using Xunit;
using ChatFlow;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    /// <summary>
    /// 用户消息靠右、宽度封顶行宽一半（文字在盒内左对齐），助手消息贴左占满整行；
    /// 行内不再有「用户 / AI」角色标注，角色靠左右位置区分。
    /// </summary>
    [Collection("ThemeState")]
    public class ChatUserMessageAlignTests
    {
        private const int RowWidth = 800;

        [StaFact]
        public void UserMessage_IsRightAligned_AndCappedAtHalfRowWidth()
        {
            using var owner = new ChatFlowControl();
            using var row = new ChatMessageRow(owner);
            using var host = new WF.Form();
            host.Controls.Add(row);
            host.Show();
            try
            {
                var font = new Font("Consolas", 9.75F);
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", new string('长', 400)),   // 足够长：顶到半宽上限
                    Assistant = new ChatMessage("assistant", "答"),
                };
                row.Width = RowWidth;
                row.Height = 300;
                row.Bind(new ChatFlowItem(turn, ChatFlowItemKind.User), font, RowWidth);

                var body = row.BodyBox;
                int dip8 = Dip(row, ChatMessageRow.TextRightSpace);
                Assert.True(body.Left > 0, $"用户消息的正文盒应靠右（Left={body.Left}）");
                Assert.True(body.Width <= RowWidth / 2 + 1, $"用户消息宽度不得超过行宽一半（Width={body.Width}）");
                Assert.True(body.Right <= RowWidth, $"正文盒不得超出行宽（Right={body.Right}）");
                Assert.True(body.Right >= RowWidth - dip8 - 1, $"正文盒右边界应贴齐行尾留白（Right={body.Right}）");
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void AssistantMessage_StaysLeftAligned_FullWidth()
        {
            using var owner = new ChatFlowControl();
            using var row = new ChatMessageRow(owner);
            using var host = new WF.Form();
            host.Controls.Add(row);
            host.Show();
            try
            {
                var font = new Font("Consolas", 9.75F);
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问"),
                    Assistant = new ChatMessage("assistant", "答"),
                };
                row.Width = RowWidth;
                row.Height = 300;
                row.Bind(new ChatFlowItem(turn, ChatFlowItemKind.Assistant), font, RowWidth);

                var body = row.BodyBox;
                Assert.Equal(0, body.Left);
                Assert.Equal(RowWidth - Dip(row, ChatMessageRow.TextRightSpace), body.Width);
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void Rows_HaveNoRoleLabel()
        {
            using var owner = new ChatFlowControl();
            using var row = new ChatMessageRow(owner);
            using var host = new WF.Form();
            host.Controls.Add(row);
            host.Show();
            try
            {
                var font = new Font("Consolas", 9.75F);
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问"),
                    Assistant = new ChatMessage("assistant", "答"),
                };
                row.Width = RowWidth;
                row.Height = 300;
                row.Bind(new ChatFlowItem(turn, ChatFlowItemKind.User), font, RowWidth);

                Assert.Null(typeof(ChatMessageRow).GetField("lblRole",
                    BindingFlags.NonPublic | BindingFlags.Instance));
                foreach (WF.Control child in row.Controls)
                    if (child is WF.Label label)
                        Assert.DoesNotContain(label.Text, new[] { "用户", "AI" });
            }
            finally
            {
                host.Hide();
            }
        }

        private static int Dip(WF.Control control, int value)
            => (int)Math.Round(value * control.DeviceDpi / 96f);
    }
}
