using System.Collections;
using System.Reflection;
using Xunit;
using ChatFlow;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    /// <summary>后台标签页里收尾的长正文：隐藏状态下 RichEdit 不排版，散文块量到的只有几十像素，
    /// 行高按这个偏小值落盘，切回来正文只剩开头一段、滚到底也看不到末尾。重新可见时必须重测。</summary>
    [Collection("ThemeState")]
    public class HiddenRebindTests
    {
        private static readonly string Long = string.Join("\n\n", new[]
        {
            "## 1. 先说结论",
            "这一段足够长，用来保证隐藏状态下量出来的高度一定小于真实排版高度：" + new string('字', 180),
            "| 列一 | 列二 |\n|---|---|\n| 甲 | 乙 |\n| 丙 | 丁 |",
            "## 2. 再展开",
            new string('文', 240),
            "最后一段是用户最容易看不到的尾巴。" + new string('尾', 60),
        });

        [StaFact]
        public void FinalText_WhileHidden_IsRemeasuredWhenShown()
        {
            using var form = new WF.Form { Size = new System.Drawing.Size(900, 700) };
            using var flow = new ChatFlowControl { Dock = WF.DockStyle.Fill };
            form.Controls.Add(flow);
            _ = flow.Handle;

            var book = new ChatTurnBook();
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "问"),
                Assistant = new ChatMessage("assistant", "半截"),
                Streaming = true,
            };
            book.AddTurn(turn);
            flow.Bind(book);
            WF.Application.DoEvents();
            flow.UpdateStreamingMessage(turn, "半截", force: true);
            WF.Application.DoEvents();

            // 收尾：正文换成全文，此时标签页还没切过来
            turn.Streaming = false;
            turn.Assistant.Text = Long;
            book.RaiseTextChanged();
            WF.Application.DoEvents();

            form.Show();
            WF.Application.DoEvents();
            WF.Application.DoEvents();

            var prose = Prose(flow);
            Assert.NotNull(prose);
            // 可见后 RichEdit 的排版是异步的（ContentsResized → 延迟重测）：等它稳定再断言，
            // 否则量到的是排版前那一帧的偏小值（96 vs 464），断言的是时序而不是行为。
            for (int i = 0; i < 50 && prose.Height < prose.GetPreferredContentHeight(); i++)
            {
                System.Threading.Thread.Sleep(20);
                WF.Application.DoEvents();
            }
            Assert.True(prose.Height >= prose.GetPreferredContentHeight(),
                $"散文块被裁：{prose.Height} < {prose.GetPreferredContentHeight()}");
        }

        private static CDS.Markdown.MarkdownTextBox Prose(ChatFlowControl flow)
        {
            var realized = typeof(ChatFlowControl)
                .GetField("realized", BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(flow) as IDictionary;
            if (realized == null) return null;
            foreach (DictionaryEntry entry in realized)
                if (entry.Value is ChatMessageRow row && row.MarkdownBox is CDS.Markdown.MarkdownTextBox prose)
                    return prose;
            return null;
        }
    }
}
