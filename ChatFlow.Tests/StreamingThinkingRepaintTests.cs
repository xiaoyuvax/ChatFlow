using System.Collections;
using System.Reflection;
using Xunit;
using ChatFlow;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    /// <summary>
    /// 现场：免费模型长时间只吐思维链，正文一直是空的；界面必须跟着思维链增量重绘，
    /// 而不是等用户折叠再展开才看到中间内容。
    /// </summary>
    [Collection("ThemeState")]
    public class StreamingThinkingRepaintTests
    {
        [StaFact]
        public void ThinkingGrowsWhileBodyUnchanged_BoxTextFollows()
        {
            using var flow = new ChatFlowControl { Size = new Size(600, 400) };
            using var host = new WF.Form();
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "整理一下这次改动"),
                    Assistant = new ChatMessage("assistant", ""),
                    Streaming = true,
                    ThinkingExpanded = true,
                    ReasoningText = "第一步：看目录。",
                };
                book.AddTurn(turn);
                flow.Bind(book);
                Application.DoEvents();

                turn.ReasoningText += "\n第二步：读文件。";
                flow.UpdateStreamingMessage(turn, "", force: true);
                Application.DoEvents();

                var row = RealizedRows(flow).Values.First(r => r.Item?.Kind == ChatFlowItemKind.Assistant);
                Assert.Contains("第二步", row.ThinkingBox.Text);
            }
            finally
            {
                host.Hide();
            }
        }

        /// <summary>超过拆片阈值（MaxItemChars）的思维链：末片持有拆分当时的文本快照，
        /// 流式增量必须继续进到界面，不能停在拆片那一刻（现场：必须折叠再展开才更新）。</summary>
        [StaFact]
        public void LongThinkingSplit_TailFollowsStreaming()
        {
            using var flow = new ChatFlowControl { Size = new Size(600, 400) };
            using var host = new WF.Form();
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "整理一下这次改动"),
                    Assistant = new ChatMessage("assistant", ""),
                    Streaming = true,
                    ThinkingExpanded = true
                };
                turn.Phases.Add((new string('思', ChatFlowControl.MaxItemChars + 2_000), ""));
                book.AddTurn(turn);
                flow.Bind(book);
                Application.DoEvents();
                flow.UpdateStreamingMessage(turn, "", force: true);
                Application.DoEvents();

                turn.Phases[0] = (turn.Phases[0].Thinking + "【尾部新增】", "");
                flow.UpdateStreamingMessage(turn, "", force: true);
                Application.DoEvents();

                var rows = RealizedRows(flow).Values.Where(r => r.Item?.Kind == ChatFlowItemKind.Assistant).ToList();
                Assert.Contains(rows, r => r.ThinkingBox.Text.Contains("【尾部新增】"));
            }
            finally
            {
                host.Hide();
            }
        }

        private static Dictionary<ChatFlowItem, ChatMessageRow> RealizedRows(ChatFlowControl flow)
            => (Dictionary<ChatFlowItem, ChatMessageRow>)typeof(ChatFlowControl)
                .GetField("realized", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(flow)!;
    }
}
