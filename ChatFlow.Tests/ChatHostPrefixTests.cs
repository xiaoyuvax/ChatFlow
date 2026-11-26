using ChatFlow;
using Xunit;

namespace ChatFlow.Tests
{
    /// <summary>宿主说明块（技能展开 / TCF 启用提示）不再混在用户气泡里：正文只画用户自己写的那段。</summary>
    public class ChatHostPrefixTests
    {
        [Fact]
        public void UserItem_draws_only_user_body_when_host_prefix_set()
        {
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "[目标编辑面] 说明\n\n检查不必要的项目"),
                HostPrefix = "[目标编辑面] 说明"
            };
            var item = new ChatFlowItem(turn, ChatFlowItemKind.User);
            Assert.Equal("检查不必要的项目", item.Text);
            // 发给模型/落盘的仍是整条
            Assert.Equal("[目标编辑面] 说明\n\n检查不必要的项目", turn.UserText);
        }

        [Fact]
        public void UserItem_keeps_full_text_without_host_prefix()
        {
            var turn = new ChatTurn { User = new ChatMessage("user", "普通问题") };
            var item = new ChatFlowItem(turn, ChatFlowItemKind.User);
            Assert.Equal("普通问题", item.Text);
        }
    }
}
