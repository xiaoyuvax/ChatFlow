using System.Collections;
using System.Reflection;
using Xunit;
using ChatFlow;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    /// <summary>超长文本的行高与重复绑定：流式期间也要切片，内容没变不要重设文本。</summary>
    [Collection("ThemeState")]
    public class ChatLongTextSplitTests
    {
        [StaFact]
        public void StreamingExpandedLongThinking_IsSplitIntoPieces()
        {
            var book = new ChatTurnBook();
            book.AddTurn(new ChatTurn
            {
                User = new ChatMessage("user", "看看这次改动"),
                Assistant = new ChatMessage("assistant", "结论在这。"),
                Streaming = true,
                ReasoningText = new string('思', 25_000),
                ThinkingExpanded = true,
            });
            using var flow = new ChatFlowControl();

            flow.Bind(book);

            var pieces = Items(flow).Where(i => i.Kind == ChatFlowItemKind.Assistant).ToList();
            Assert.Equal(3, pieces.Count);   // 25k / MaxItemChars(10k) = 3 片
            Assert.All(pieces, p => Assert.True((p.ThinkingText ?? string.Empty).Length <= 10_000));
        }

        [StaFact]
        public void StreamingLongAnswer_IsSplitIntoPieces()
        {
            var book = new ChatTurnBook();
            book.AddTurn(new ChatTurn
            {
                User = new ChatMessage("user", "写长一点"),
                Assistant = new ChatMessage("assistant", new string('答', 12_000)),
                Streaming = true,
            });
            using var flow = new ChatFlowControl();

            flow.Bind(book);

            var pieces = Items(flow).Where(i => i.Kind == ChatFlowItemKind.Assistant).ToList();
            Assert.Equal(2, pieces.Count);
            Assert.All(pieces, p => Assert.True(p.Text.Length <= 10_000));
        }

        [StaFact]
        public void Rebinding_identical_content_does_not_reset_the_text()
        {
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "问"),
                Assistant = new ChatMessage("assistant", "第一段普通正文。"),
            };
            using var owner = new ChatFlowControl();
            using var row = new ChatMessageRow(owner);
            using var host = new WF.Form();
            host.Controls.Add(row);
            host.Show();
            try
            {
                var font = new Font("Consolas", 9.75F);
                row.Bind(new ChatFlowItem(turn, ChatFlowItemKind.Assistant), font, 600);

                row.BodyBox.SelectionStart = 0;
                row.BodyBox.SelectionLength = 0;
                row.Bind(new ChatFlowItem(turn, ChatFlowItemKind.Assistant), font, 600);
                Assert.Equal(0, row.BodyBox.SelectionStart);   // 同样的内容：跳过重设，光标没被拽到末尾

                turn.AssistantText = "改过的正文。";
                row.Bind(new ChatFlowItem(turn, ChatFlowItemKind.Assistant), font, 600);
                Assert.Equal(row.BodyBox.TextLength, row.BodyBox.SelectionStart);   // 内容变了：重设，光标到末尾
                Assert.Equal("改过的正文。", row.BodyBox.Text);
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void ContinuationPiece_HidesRoleLabel_AndJoinsText()
        {
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "问"),
                Assistant = new ChatMessage("assistant", new string('答', 150)),
            };
            using var owner = new ChatFlowControl();
            using var first = new ChatMessageRow(owner);
            using var next = new ChatMessageRow(owner);
            using var host = new WF.Form();
            host.Controls.Add(first);
            host.Controls.Add(next);
            host.Show();
            try
            {
                var font = new Font("Consolas", 9.75F);
                first.ShowSelection = true;
                next.ShowSelection = true;
                int firstH = first.Bind(new ChatFlowItem(turn, ChatFlowItemKind.Assistant), font, 600);
                int nextH = next.Bind(
                    new ChatFlowItem(turn, ChatFlowItemKind.Assistant) { IsContinuation = true }, font, 600);

                Assert.True(CheckBoxOf(first).Visible, "整轮勾选框挂第一片");
                Assert.False(CheckBoxOf(next).Visible, "续片不单独出勾选框，避免只取消其中一段");

                Assert.False(RoleLabel(next).Visible, "续片不该显示「AI（续）」之类的角色标签");
                var role = RoleLabel(first);
                Assert.True(role.AutoSize, "角色标签必须自适应宽度，否则更长的文案会被裁掉");
                Assert.True(role.Width >= role.GetPreferredSize(Size.Empty).Width, "标签宽度容得下完整文案");
                Assert.True(next.BodyBox.Top < first.BodyBox.Top, "续片正文应上移到角色标签行原来的位置");
                Assert.True(nextH < firstH, "续片不再为角色标签行留高度");
            }
            finally
            {
                host.Hide();
            }
        }

        private static WF.Label RoleLabel(object row)
            => (WF.Label)row.GetType()
                .GetField("lblRole", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(row)!;

        private static WF.CheckBox CheckBoxOf(object row)
            => (WF.CheckBox)row.GetType()
                .GetField("chk", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(row)!;

        private static List<ChatFlowItem> Items(ChatFlowControl flow)
            => (List<ChatFlowItem>)typeof(ChatFlowControl)
                .GetField("items", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(flow)!;
    }
}
