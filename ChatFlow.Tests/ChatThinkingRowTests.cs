using Xunit;
using ChatFlow;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    /// <summary>
    /// 思考区折叠行（ChatMessageRow）STA 单测：验证折叠头可见性与布局关系。
    /// 回归背景：行池复用且尺寸未变时 OnSizeChanged 不触发，正文 FCTB（z 序最顶）
    /// 停留在旧位置盖住折叠头，表现为"思考区/轨迹完全不可见"。
    /// </summary>
    [Collection("ThemeState")]   // 主题是全局静态态：切主题的用例必须与其它主题用例串行（同 ComboOwnerDrawTests）
    public class ChatThinkingRowTests
    {
        [StaFact]
        public void ThinkingHeader_VisibleAndAboveBody_WhenTraceExists()
        {
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "测试问题"),
                Assistant = new ChatMessage("assistant", "最终回答正文"),
                ToolTraceText = "» #1 app_status() → 成功 · 12ms\n» #2 puppet_describe(name=MainForm) → 成功 · 340ms",
                ToolStepCount = 2,
                ToolTotalMs = 352,
            };

            using var ownerFlow = new ChatFlowControl();
            using var row = new ChatMessageRow(ownerFlow);
            int measured;
            var host = new Form();
            host.Controls.Add(row);
            host.Show();                       // STA 单测：需要句柄与布局
            try
            {
                var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
                measured = row.Bind(item, new Font("Consolas", 9.75F), 600);

                // 折叠头可见（有轨迹即显示），位于正文（y=RoleIndent+2 起步）下方之上
                var fld = GetField<WF.Label>(row, "lblThinking");
                var txt = GetField<FastColoredTextBoxNS.FastColoredTextBox>(row, "txt");
                Assert.True(fld.Visible, "有思考区内容时折叠头必须可见");
                Assert.True(txt.Top > fld.Top, "正文必须位于折叠头之下（修复点：正文曾停留在顶部盖住折叠头）");
                Assert.True(measured > 60, $"测量高度应包含折叠头（实测 {measured}）");

                // 折叠头汇总：步数与总耗时（本次任务汇总数据 ↗ 行内可见）
                Assert.Contains("2 步", fld.Text);
                Assert.Contains("352ms", fld.Text);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void ThinkingHeader_Hidden_WhenNoThinking()
        {
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "测试"),
                Assistant = new ChatMessage("assistant", "纯文本回复"),
            };

            using var ownerFlow = new ChatFlowControl();
            using var row = new ChatMessageRow(ownerFlow);
            var host = new Form();
            host.Controls.Add(row);
            host.Show();
            try
            {
                var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
                row.Bind(item, new Font("Consolas", 9.75F), 600);
                var fld = GetField<WF.Label>(row, "lblThinking");
                Assert.False(fld.Visible, "无思考区内容时折叠头必须隐藏");
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void ExpandedThinking_ShowsThinkingBox_BetweenHeaderAndBody()
        {
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "测试"),
                Assistant = new ChatMessage("assistant", "最终回答"),
                ToolTraceText = "步骤一\n步骤二",
                ThinkingExpanded = true,
            };

            using var ownerFlow = new ChatFlowControl();
            using var row = new ChatMessageRow(ownerFlow);
            var host = new Form();
            host.Controls.Add(row);
            host.Show();
            try
            {
                var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
                row.Bind(item, new Font("Consolas", 9.75F), 600);
                var fld = GetField<WF.Label>(row, "lblThinking");
                var txtT = GetField<FastColoredTextBoxNS.FastColoredTextBox>(row, "txtThinking");
                var txt = GetField<FastColoredTextBoxNS.FastColoredTextBox>(row, "txt");
                Assert.True(txtT.Visible, "展开后思考区文本必须可见");
                Assert.True(fld.Top < txtT.Top && txtT.Top < txt.Top, "布局顺序应为 折叠头 → 思考区 → 正文");
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void ThinkingBox_BackColor_FollowsThemeSwitch_DarkToLight()
        {
            // 回归：从深色切回浅色后思考区仍是深底——底色只在构造函数里按当时主题定死，
            // 而主题切换只重绑（ChatFlowControl.OnAppThemeChanged → Bind）不重建控件。
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "测试"),
                Assistant = new ChatMessage("assistant", "最终回答"),
                ToolTraceText = "» #1 image_describe(modelId=x) → 成功 · 62ms",
                ThinkingExpanded = true,
            };

            using var ownerFlow = new ChatFlowControl();
            using var row = new ChatMessageRow(ownerFlow);
            var font = new Font("Consolas", 9.75F);
            var host = new Form();
            host.Controls.Add(row);
            host.Show();
            try
            {
                var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
                var box = GetField<FastColoredTextBoxNS.FastColoredTextBox>(row, "txtThinking");
                Color light, dark, restored;

                ChatTheme.Set(null);
                row.Bind(item, font, 600);
                light = box.BackColor;

                ChatTheme.Set(TestPalette.Dark);
                row.Bind(item, font, 600);
                dark = box.BackColor;

                ChatTheme.Set(null);
                row.Bind(item, font, 600);
                restored = box.BackColor;

                Assert.NotEqual(light, dark);
                Assert.Equal(light, restored);
            }
            finally
            {
                ChatTheme.Set(null);
                font.Dispose();
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void ThinkingText_IsDimmedRelativeToBody_DarkTheme()
        {
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "测试"),
                Assistant = new ChatMessage("assistant", "最终回答"),
                ToolTraceText = "步骤一",
                ThinkingExpanded = true,
            };

            using var ownerFlow = new ChatFlowControl();
            using var row = new ChatMessageRow(ownerFlow);
            var font = new Font("Consolas", 9.75F);
            var host = new Form();
            host.Controls.Add(row);
            host.Show();
            try
            {
                var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
                ChatTheme.Set(TestPalette.Dark);
                row.Bind(item, font, 600);

                var body = GetField<FastColoredTextBoxNS.FastColoredTextBox>(row, "txt");
                var thinking = GetField<FastColoredTextBoxNS.FastColoredTextBox>(row, "txtThinking");
                var palette = TestPalette.Dark;

                Assert.Equal(palette.ChatFore, body.ForeColor);
                Assert.Equal(palette.ChatThinkingFore, thinking.ForeColor);
                Assert.NotEqual(body.ForeColor, thinking.ForeColor);
            }
            finally
            {
                ChatTheme.Set(null);
                font.Dispose();
                host.Hide();
                host.Dispose();
            }
        }

        private static T GetField<T>(object instance, string name) where T : class
            => instance.GetType().GetField(name, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?.GetValue(instance) as T
               ?? throw new InvalidOperationException($"字段 {name} 不存在或类型不符");
    }
}
