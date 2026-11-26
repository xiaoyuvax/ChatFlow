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

                // 折叠头可见（有轨迹即显示），位于正文（y=MessageTopPad+2 起步）下方之上
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

        [StaFact]
        public void Phases_AfterFirstPiece_NoMessageTopPad()
        {
            // 回归：「正文之后的思维段」是同一轮的第二条，若也吃整块消息顶部留白，
            // 两段正文之间就夹出一条空行（用户侧：思维区展开控件上方多一空行）。
            var turn = new ChatTurn { User = new ChatMessage("user", "测试") };
            turn.Phases.Add(("第一段思维", "第一段正文"));
            turn.Phases.Add(("第二段思维", "第二段正文"));

            using var ownerFlow = new ChatFlowControl();
            using var first = new ChatMessageRow(ownerFlow);
            using var second = new ChatMessageRow(ownerFlow);
            var font = new Font("Consolas", 9.75F);
            var host = new Form();
            host.Controls.Add(first);
            host.Controls.Add(second);
            host.Show();
            try
            {
                var a = new ChatFlowItem(turn, ChatFlowItemKind.Assistant) { Phase = 0 };
                var b = new ChatFlowItem(turn, ChatFlowItemKind.Assistant) { Phase = 1 };
                int ha = first.Bind(a, font, 600);
                int hb = second.Bind(b, font, 600);
                var fa = GetField<WF.Label>(first, "lblThinking");
                var fb = GetField<WF.Label>(second, "lblThinking");

                Assert.True(fa.Visible && fb.Visible, "两段都有思维，折叠头都应可见");
                Assert.True(fb.Top < fa.Top, "后续段折叠头紧接上一条，不再留消息顶部留白");
                Assert.True(hb < ha, $"后续段比首段矮一块顶部留白（首段 {ha} / 后续 {hb}）");
            }
            finally
            {
                font.Dispose();
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void Notice_NoMessageTopPad()
        {
            // 回归：协作提示整行铺淡黄底，若也吃消息顶部留白，色块会凭空高出一截（用户侧：大面积黄色）。
            var plain = new ChatTurn { User = new ChatMessage("user", "提示") };
            var notice = new ChatTurn { User = new ChatMessage("user", "提示"), Notice = true };

            using var ownerFlow = new ChatFlowControl();
            using var normal = new ChatMessageRow(ownerFlow);
            using var marked = new ChatMessageRow(ownerFlow);
            var font = new Font("Consolas", 9.75F);
            var host = new Form();
            host.Controls.Add(normal);
            host.Controls.Add(marked);
            host.Show();
            try
            {
                int hn = normal.Bind(new ChatFlowItem(plain, ChatFlowItemKind.User), font, 600);
                int hm = marked.Bind(new ChatFlowItem(notice, ChatFlowItemKind.User), font, 600);
                var tn = GetField<WF.Control>(normal, "txt");
                var tm = GetField<WF.Control>(marked, "txt");

                Assert.True(tm.Top < tn.Top, "协作提示不再留消息顶部留白");
                Assert.True(hm < hn, $"协作提示比普通消息矮一块顶部留白（普通 {hn} / 提示 {hm}）");
            }
            finally
            {
                font.Dispose();
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void NoticeReply_FillsThinkingOnly_AndHasNoTopPad()
        {
            // 协作提示的答复轮：思考区铺淡黄底（与提示同一格式，用户可整块跳过），正文保持默认底色。
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "【协作提示】私信"),
                Assistant = new ChatMessage("assistant", ""),
                Notice = true,
                ThinkingExpanded = true,
                ReasoningText = "想一下"
            };
            var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
            Assert.False(ChatMessageRow.HasTopPad(item), "协作提示答复轮不留消息顶部空行");

            using var ownerFlow = new ChatFlowControl();
            using var row = new ChatMessageRow(ownerFlow);
            var font = new Font("Consolas", 9.75F);
            var host = new Form();
            host.Controls.Add(row);
            host.Show();
            try
            {
                row.Bind(item, font, 600);
                var lbl = GetField<WF.Label>(row, "lblThinking");
                var txt = GetField<WF.Control>(row, "txt");
                var region = (System.Drawing.Rectangle)typeof(ChatMessageRow)
                    .GetField("_noticeRegion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .GetValue(row);
                System.Drawing.Color fill = ChatTheme.Current != null && ChatTheme.Current.NoticeBack != System.Drawing.Color.Empty
                    ? ChatTheme.Current.NoticeBack
                    : System.Drawing.Color.FromArgb(0xFF, 0xFF, 0xE0);

                Assert.Equal(fill, lbl.BackColor);       // 思考区（折叠头）铺淡黄底
                Assert.NotEqual(fill, txt.BackColor);    // 正文保持默认底色
                Assert.False(region.IsEmpty, "答复轮应有思考区色块范围");
                Assert.True(region.Bottom <= row.Height, "色块不能超出本行");
            }
            finally
            {
                font.Dispose();
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void FeedbackNoticeReply_KeepsThinkingUnfilled()
        {
            // 用户反馈（/complain）是宿主消息：答复属于会话正题，思考区不沿用提示行的淡黄底
            var turn = new ChatTurn
            {
                User = new ChatMessage("user", "[用户反馈] 对 Loop 表现不满"),
                Assistant = new ChatMessage("assistant", ""),
                Notice = true,
                NoticeKind = "feedback",
                ThinkingExpanded = true,
                ReasoningText = "想一下"
            };
            var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);

            using var ownerFlow = new ChatFlowControl();
            using var row = new ChatMessageRow(ownerFlow);
            var font = new Font("Consolas", 9.75F);
            var host = new Form();
            host.Controls.Add(row);
            host.Show();
            try
            {
                row.Bind(item, font, 600);
                var lbl = GetField<WF.Label>(row, "lblThinking");
                var thinking = GetField<WF.Control>(row, "txtThinking");
                var region = (System.Drawing.Rectangle)typeof(ChatMessageRow)
                    .GetField("_noticeRegion", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
                    .GetValue(row);
                System.Drawing.Color fill = ChatTheme.Current != null && ChatTheme.Current.NoticeBack != System.Drawing.Color.Empty
                    ? ChatTheme.Current.NoticeBack
                    : System.Drawing.Color.FromArgb(0xFF, 0xFF, 0xE0);

                Assert.NotEqual(fill, lbl.BackColor);
                Assert.NotEqual(fill, thinking.BackColor);
                Assert.True(region.IsEmpty, "反馈轮答复不铺淡黄底");
            }
            finally
            {
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
