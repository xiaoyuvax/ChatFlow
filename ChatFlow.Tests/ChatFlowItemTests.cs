using Xunit;

namespace ChatFlow.Tests
{
    /// <summary>ChatFlowItem 的展示文本/思考区折叠头行为（从 VeniceMan 迁入的回归测试）。</summary>
    public class ChatFlowItemTests
    {
        [Fact]
        public void ThinkingHeader_ShowsStepsAndTotalDuration()
        {
            var turn = new ChatTurn
            {
                Assistant = new ChatMessage("assistant", "答"),
                ToolTraceText = "» #1 app_status() → 成功 · 12ms",
                ToolStepCount = 7,
                ToolTotalMs = 12300
            };
            var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
            Assert.Equal("思考与工具过程 · 7 步 · 12.3s", item.ThinkingHeaderText);
        }

        [Fact]
        public void ThinkingHeader_StepsWithoutDuration()
        {
            var turn = new ChatTurn
            {
                Assistant = new ChatMessage("assistant", "答"),
                ToolStepCount = 3,
                ToolTotalMs = 0
            };
            var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
            Assert.Equal("思考与工具过程 · 3 步", item.ThinkingHeaderText);
        }

        [Fact]
        public void ThinkingHeader_NoTools_FallsBackToReasoningPreview()
        {
            var turn = new ChatTurn
            {
                Assistant = new ChatMessage("assistant", "答"),
                ReasoningText = "用户想要赛博朋克城市图，先确认模型与比例。\n下一行不进摘要"
            };
            var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
            Assert.Equal("思考与工具过程 · 用户想要赛博朋克城市图，先确认模型与比例。", item.ThinkingHeaderText);
        }

        [Fact]
        public void Text_PrependsToolCallLines()
        {
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "u"),
                Assistant = new ChatMessage("assistant", "最终回答"),
                ToolCallLines = "» image_edit(path=J:/x.png)"
            };
            var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
            Assert.Contains("» image_edit", item.Text);
            Assert.Contains("最终回答", item.Text);
            Assert.StartsWith("»", item.Text);
        }

        [Fact]
        public void Text_StreamingHidesToolCallLines()
        {
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "u"),
                Assistant = new ChatMessage("assistant", ""),
                ToolCallLines = "» app_status()\n» puppet_get(name=MainForm)",
                Streaming = true
            };
            var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
            Assert.True(string.IsNullOrEmpty(item.Text), $"流式期间正文不应含工具行，实为：{item.Text}");

            turn.Streaming = false;
            Assert.StartsWith("» app_status()", item.Text);
            Assert.Contains("» puppet_get", item.Text);
        }

        [Fact]
        public void Text_StreamingKeepsAssistantText()
        {
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "u"),
                Assistant = new ChatMessage("assistant", "部分回答"),
                Streaming = true
            };
            var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
            Assert.Equal("部分回答", item.Text);
        }

        [Fact]
        public void Text_StreamingShowsToolStatus()
        {
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "u"),
                Assistant = new ChatMessage("assistant", ""),
                ToolCallLines = "» #1 image_generate(prompt=猫) · 8.0s",
                ToolStatusText = "» 正在调用 生图接口（image_generate）…",
                Streaming = true
            };
            var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
            Assert.Equal("» 正在调用 生图接口（image_generate）…", item.Text);
            Assert.DoesNotContain("image_generate(", item.Text);
        }
    }
}
