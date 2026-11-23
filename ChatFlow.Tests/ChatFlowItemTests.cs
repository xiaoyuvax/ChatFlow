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
        public void Text_IsOnlyAnswer_ToolLinesGoToThinking()
        {
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "u"),
                Assistant = new ChatMessage("assistant", "最终回答"),
                ToolCallLines = "» image_edit(path=J:/x.png)"
            };
            var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
            Assert.Equal("最终回答", item.Text);
            Assert.Contains("» image_edit", item.ThinkingText);
        }

        [Fact]
        public void Text_StreamingKeepsToolCallLinesOutOfBody()
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
            Assert.True(string.IsNullOrEmpty(item.Text));
            Assert.StartsWith("» app_status()", item.ThinkingText);
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

        [Fact]
        public void Text_TimelineGoesToThinking_BodyIsAnswer()
        {
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "u"),
                Assistant = new ChatMessage("assistant", "最终回答"),
                TimelineText = "先看仓库。\n\nGrep(a) · 1s",
                ReasoningText = "先看仓库。",
                ToolCallLines = "Grep(a) · 1s"
            };
            var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);

            Assert.Equal("最终回答", item.Text);
            Assert.Equal("先看仓库。\n\nGrep(a) · 1s", item.ThinkingText);
            // 有时间线就默认展开，否则看不到思维与工具的交错过程
            Assert.True(item.ThinkingExpanded);
        }

        [Fact]
        public void ThinkingExpanded_DefaultsCollapsedWithoutTimeline()
        {
            var turn = new ChatTurn
            {
                Assistant = new ChatMessage("assistant", ""),
                ReasoningText = "先看仓库。"
            };
            var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
            Assert.False(item.ThinkingExpanded);

            turn.ThinkingExpanded = false;   // 用户手动折叠过：时间线也不再自动展开
            turn.TimelineText = "先看仓库。\n\nGrep(a) · 1s";
            Assert.False(item.ThinkingExpanded);
        }

        [Fact]
        public void Text_TimelineStreamingShowsStatus_ThinkingKeepsTimeline()
        {
            var turn = new ChatTurn
            {
                Assistant = new ChatMessage("assistant", ""),
                TimelineText = "先看仓库。",
                ToolStatusText = "正在调用 Grep…",
                Streaming = true
            };
            var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);

            Assert.Equal("正在调用 Grep…", item.Text);
            Assert.Equal("先看仓库。", item.ThinkingText);
        }

        [Fact]
        public void ToolStarted_ShowsParallelLinesInBody_UntilToolsFinish()
        {
            var turn = new ChatTurn
            {
                Assistant = new ChatMessage("assistant", ""),
                Streaming = true
            };
            turn.ToolStarted(" >> Read[path=a.cs]");
            turn.ToolStarted(" >> Grep[pattern=b]");
            var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
            Assert.Equal(" >> Read[path=a.cs]\n >> Grep[pattern=b]", item.Text);

            turn.ToolsFinished();
            Assert.Equal(string.Empty, item.Text);
        }

        [Fact]
        public void ToolStarted_DoesNotStackSameLine()
        {
            var turn = new ChatTurn
            {
                Assistant = new ChatMessage("assistant", ""),
                Streaming = true
            };
            turn.ToolStarted(" >> Read[path=a.cs]");
            turn.ToolStarted(" >> Read[path=a.cs]");
            Assert.Equal(" >> Read[path=a.cs]", turn.ToolStatusText);
        }

        [Fact]
        public void PhaseThinkingExpandsIndependently()
        {
            var turn = new ChatTurn
            {
                Assistant = new ChatMessage("assistant", ""),
                TimelineText = ""
            };
            turn.Phases.Add(("想一", ""));
            turn.Phases.Add(("想二", ""));

            var first = new ChatFlowItem(turn, ChatFlowItemKind.Assistant) { Phase = 0 };
            var second = new ChatFlowItem(turn, ChatFlowItemKind.Assistant) { Phase = 1 };
            Assert.False(first.ThinkingExpanded);
            Assert.False(second.ThinkingExpanded);

            first.ToggleThinking();
            Assert.True(first.ThinkingExpanded);
            Assert.False(second.ThinkingExpanded);

            second.ToggleThinking();
            first.ToggleThinking();
            Assert.False(first.ThinkingExpanded);
            Assert.True(second.ThinkingExpanded);
        }

        [Fact]
        public void PhaseItem_ShowsOwnThinkingAndAnswer()
        {
            var turn = new ChatTurn
            {
                Assistant = new ChatMessage("assistant", "正文一正文二"),
                TimelineText = "想一\n\n想二",
                ToolStepCount = 7
            };
            turn.Phases.Add(("想一", "正文一"));
            turn.Phases.Add(("想二", "正文二"));

            var first = new ChatFlowItem(turn, ChatFlowItemKind.Assistant) { Phase = 0 };
            Assert.Equal("正文一", first.Text);
            Assert.Equal("想一", first.ThinkingText);
            Assert.Equal("思考与工具过程 · 7 步", first.ThinkingHeaderText);   // 首段沿用整轮统计

            var second = new ChatFlowItem(turn, ChatFlowItemKind.Assistant) { Phase = 1 };
            Assert.Equal("正文二", second.Text);
            Assert.Equal("想二", second.ThinkingText);
            Assert.Equal("思考与工具过程 · 想二", second.ThinkingHeaderText);   // 后续分段只标自己的摘要
        }

        [Fact]
        public void NoticeTurn_AssistantPhasesFoldIndependently()
        {
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "【协作提示】同工作区新会话已启动"),
                Assistant = new ChatMessage("assistant", ""),
                Notice = true,
                NoticeKind = "collab",
                ThinkingExpanded = false
            };
            turn.Phases.Add(("想一", ""));
            turn.Phases.Add(("想二", ""));

            var notice = new ChatFlowItem(turn, ChatFlowItemKind.User);
            var first = new ChatFlowItem(turn, ChatFlowItemKind.Assistant) { Phase = 0 };
            var second = new ChatFlowItem(turn, ChatFlowItemKind.Assistant) { Phase = 1 };

            notice.ToggleThinking();                       // 点开协作提示正文
            Assert.True(notice.ThinkingExpanded);
            Assert.False(first.ThinkingExpanded);          // 提示展开不连带这一轮回答的思考区
            Assert.False(second.ThinkingExpanded);

            first.ToggleThinking();                        // 回答的思考区各自开合
            Assert.True(first.ThinkingExpanded);
            Assert.False(second.ThinkingExpanded);
            first.ToggleThinking();
            Assert.False(first.ThinkingExpanded);
        }

        [Fact]
        public void Notice_FeedbackShowsFirstLineOnly()
        {
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "[用户反馈] 对 Loop 表现不满：第一条消息要折叠\r\n第二行不进标题"),
                Assistant = new ChatMessage("assistant", ""),
                Notice = true,
                NoticeKind = "feedback"
            };
            var item = new ChatFlowItem(turn, ChatFlowItemKind.User);
            Assert.Equal("[用户反馈] 对 Loop 表现不满：第一条消息要折叠", item.ThinkingHeaderText);
            Assert.Equal(string.Empty, item.Text);   // 全文只在折叠区里
            Assert.False(item.ThinkingExpanded);     // 默认收起
        }

        [Fact]
        public void Notice_CollabKeepsLabelAndTruncatesFirstLine()
        {
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", new string('长', 80)),
                Notice = true,
                NoticeKind = "collab"
            };
            var item = new ChatFlowItem(turn, ChatFlowItemKind.User);
            Assert.Equal("协作提示 · " + new string('长', 60) + "…", item.ThinkingHeaderText);
        }
    }
}
