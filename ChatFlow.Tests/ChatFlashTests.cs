using System.Collections;
using System.Reflection;
using Xunit;

namespace ChatFlow.Tests
{
    /// <summary>
    /// 工具闪现区：没有正文、也没有正在执行的工具行时，按节拍把思维链尾部半行刷进正文 FCTB，
    /// 让用户看到模型还在干活（避免干等）。节拍由宿主按账本注入（钳在 300–5000ms）。
    /// </summary>
    [Collection("ThemeState")]
    public class ChatFlashTests
    {
        private static ChatMessageRow AssistantRow(ChatFlowControl flow)
        {
            var field = typeof(ChatFlowControl).GetField("realized", BindingFlags.NonPublic | BindingFlags.Instance)!;
            object dict = field.GetValue(flow)!;
            var values = (ICollection)dict.GetType().GetProperty("Values")!.GetValue(dict)!;
            foreach (ChatMessageRow row in values)
                if (row.Item is { IsUser: false }) return row;
            return null;
        }

        private static ChatFlowControl Hosted(out Form host)
        {
            var flow = new ChatFlowControl();
            host = new Form { Size = new Size(900, 500) };
            host.Controls.Add(flow);
            flow.Dock = DockStyle.Fill;
            host.Show();
            return flow;
        }

        [Fact]
        public void TailHalfLine_KeepsShortTailWhole()
        {
            using var font = new Font("Microsoft YaHei UI", 9F);
            Assert.Equal("正在检查渲染路径", ChatFlowControl.TailHalfLine("先想一段\n正在检查渲染路径", font, 2000));
        }

        [Fact]
        public void TailHalfLine_TakesLastNonEmptyLine()
        {
            using var font = new Font("Microsoft YaHei UI", 9F);
            Assert.Equal("尾巴", ChatFlowControl.TailHalfLine("头\n\n尾巴\n   \n", font, 2000));
        }

        [Fact]
        public void TailHalfLine_TruncatesToHalfLineWidth()
        {
            using var font = new Font("Microsoft YaHei UI", 9F);
            const string long_ = "这一段思维链很长很长很长很长很长很长很长很长很长很长很长很长";
            string flash = ChatFlowControl.TailHalfLine(long_, font, 120);

            Assert.StartsWith("…", flash);
            Assert.True(flash.Length < long_.Length, "超宽时必须截短");
            // 截出来的这一段本身就是「半行」：宽度不超过给定上限，也不明显偏窄
            int width = TextRenderer.MeasureText(flash, font, new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.SingleLine).Width;
            Assert.InRange(width, 60, 120);
            Assert.EndsWith(long_[^1].ToString(), flash);
        }

        [Fact]
        public void TailHalfLine_NoTextMeansNoFlash()
        {
            using var font = new Font("Microsoft YaHei UI", 9F);
            Assert.Null(ChatFlowControl.TailHalfLine("", font, 300));
            Assert.Null(ChatFlowControl.TailHalfLine(null, font, 300));
            Assert.Null(ChatFlowControl.TailHalfLine("  \n\t\n", font, 300));
        }

        [StaFact]
        public void Pulse_ShowsReasoningTailInBody_WhileNothingElseRuns()
        {
            var flow = Hosted(out var host);
            try
            {
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", ""),
                    Streaming = true,
                    ReasoningText = "第一行思路\n现在在核对补丁的锚点是否唯一"
                };
                var book = new ChatTurnBook();
                book.AddTurn(turn);
                flow.Bind(book);
                Application.DoEvents();

                flow.PulseFlash();
                Application.DoEvents();

                Assert.Equal("现在在核对补丁的锚点是否唯一", turn.FlashText);
                var row = AssistantRow(flow);
                Assert.NotNull(row);
                Assert.Equal(turn.FlashText, row.BodyBox.Text);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void Pulse_YieldsToToolLineAndToAnswer()
        {
            var flow = Hosted(out var host);
            try
            {
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", ""),
                    Streaming = true,
                    ReasoningText = "正在想"
                };
                var book = new ChatTurnBook();
                book.AddTurn(turn);
                flow.Bind(book);
                Application.DoEvents();

                flow.PulseFlash();
                Assert.Equal("正在想", turn.FlashText);

                // 有工具行在跑：正文区归工具行，闪现要让位
                turn.ToolStarted(" >> Read[path=a.cs]");
                flow.PulseFlash();
                Application.DoEvents();
                Assert.Null(turn.FlashText);
                Assert.Equal(" >> Read[path=a.cs]", AssistantRow(flow).BodyBox.Text);

                // 正文出字：同样让位（宿主写正文后照例调 UpdateStreamingMessage）
                turn.ToolsFinished();
                turn.AssistantText = "结果如下";
                flow.UpdateStreamingMessage(turn, null, force: true);
                flow.PulseFlash();
                Application.DoEvents();
                Assert.Null(turn.FlashText);
                Assert.Equal("结果如下", AssistantRow(flow).BodyBox.Text);

                // 正文清掉（下一步开始）但思维链没变：闪现按原节拍回来
                turn.AssistantText = "";
                flow.UpdateStreamingMessage(turn, null, force: true);
                flow.PulseFlash();
                Application.DoEvents();
                Assert.Equal("正在想", turn.FlashText);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void Pulse_DoesNothingAfterTurnSettles()
        {
            var flow = Hosted(out var host);
            try
            {
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", ""),
                    Streaming = false,
                    ReasoningText = "结论"
                };
                var book = new ChatTurnBook();
                book.AddTurn(turn);
                flow.Bind(book);
                Application.DoEvents();

                flow.PulseFlash();
                Application.DoEvents();

                Assert.Null(turn.FlashText);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void Pulse_ClearsFlashAfterTurnSettles()
        {
            var flow = Hosted(out var host);
            try
            {
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", ""),
                    Streaming = true,
                    ReasoningText = "收尾前最后一句思路"
                };
                var book = new ChatTurnBook();
                book.AddTurn(turn);
                flow.Bind(book);
                Application.DoEvents();

                flow.PulseFlash();
                Assert.Equal("收尾前最后一句思路", turn.FlashText);

                // 收尾（可能没出正文就停了）：闪现内容不能再留在正文里
                turn.Streaming = false;
                flow.UpdateStreamingMessage(turn, null, force: true);
                flow.PulseFlash();
                Application.DoEvents();

                Assert.Null(turn.FlashText);
                Assert.NotEqual("收尾前最后一句思路", AssistantRow(flow)?.BodyBox.Text);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void SuspendRendering_StopsFlashAndResumesOnReturn()
        {
            var flow = Hosted(out var host);
            try
            {
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", ""),
                    Streaming = true,
                    ReasoningText = "正在核对暂停渲染"
                };
                var book = new ChatTurnBook();
                book.AddTurn(turn);
                flow.Bind(book);
                flow.UpdateStreamingMessage(turn, null, force: true);
                Application.DoEvents();

                var pulse = (System.Windows.Forms.Timer)typeof(ChatFlowControl)
                    .GetField("flashPulse", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(flow)!;
                Assert.True(pulse.Enabled, "流式回合应当起表");

                // 页面切走：节拍器停摆，别再在后台空转
                flow.SuspendRendering = true;
                Assert.False(pulse.Enabled);

                // 切回来：仍在流式就重新起表，闪现照旧出现
                flow.SuspendRendering = false;
                Assert.True(pulse.Enabled, "切回后流式回合要重新起表");
                flow.PulseFlash();
                Application.DoEvents();
                Assert.Equal("正在核对暂停渲染", turn.FlashText);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void FlashInterval_IsClampedToLedgerRange()
        {
            using var flow = new ChatFlowControl();
            Assert.Equal(ChatFlowControl.DefaultFlashInterval, flow.FlashInterval);

            flow.FlashInterval = 100;
            Assert.Equal(ChatFlowControl.FlashMinInterval, flow.FlashInterval);
            Assert.Equal(300, flow.FlashInterval);

            flow.FlashInterval = 60_000;
            Assert.Equal(5000, flow.FlashInterval);

            flow.FlashInterval = 1500;
            Assert.Equal(1500, flow.FlashInterval);
        }
    }
}
