using System.Collections;
using System.Drawing;
using System.Reflection;
using Xunit;

namespace ChatFlow.Tests
{
    /// <summary>
    /// 分段 markdown 渲染只在段数少时启用：段太多（Agent 长轨迹，工具结果与代码块交替几十次）时，
    /// 「先全部散文、再全部代码」的堆叠会把最后一段正文顶到远处，滚到底也看不到。改走单文本框按原序显示。
    /// </summary>
    [Collection("ThemeState")]
    public class ChatMarkdownSegmentTests
    {
        private static IEnumerable<ChatMessageRow> RealizedRows(ChatFlowControl flow)
        {
            var field = typeof(ChatFlowControl).GetField("realized", BindingFlags.NonPublic | BindingFlags.Instance)!;
            object dict = field.GetValue(flow)!;
            var values = (ICollection)dict.GetType().GetProperty("Values")!.GetValue(dict)!;
            foreach (ChatMessageRow row in values) yield return row;
        }

        private static ChatMessageRow AssistantRow(ChatFlowControl flow)
            => RealizedRows(flow).FirstOrDefault(r => r.Item is { IsUser: false });

        private static ChatMessageRow BindSingle(ChatFlowControl flow, string body)
        {
            var book = new ChatTurnBook();
            book.AddTurn(new ChatTurn
            {
                User = new ChatMessage("user", "问题"),
                Assistant = new ChatMessage("assistant", body)
            });
            flow.Bind(book);
            return AssistantRow(flow);
        }

        private static string BodyWithFences(int fences)
            => string.Join("\n\n", Enumerable.Range(0, fences)
                .SelectMany(i => new[] { $"第 {i} 段正文。", "```\ncode " + i + "\n```" }));

        [StaFact]
        public void FewSegments_StillUseMarkdownRendering()
        {
            using var flow = new ChatFlowControl();
            using var host = new Form { Size = new Size(900, 500) };
            host.Controls.Add(flow);
            flow.Dock = DockStyle.Fill;
            host.Show();
            try
            {
                var row = BindSingle(flow, BodyWithFences(2));
                Assert.NotNull(row);
                Assert.True(row.IsMarkdownRendered);
                Assert.NotEmpty(row.CodeBoxes);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void ManySegments_FallBackToPlainSingleBox_SoOrderIsPreserved()
        {
            using var flow = new ChatFlowControl();
            using var host = new Form { Size = new Size(900, 500) };
            host.Controls.Add(flow);
            flow.Dock = DockStyle.Fill;
            host.Show();
            try
            {
                var row = BindSingle(flow, BodyWithFences(20));
                Assert.NotNull(row);
                Assert.False(row.IsMarkdownRendered);
                Assert.Empty(row.CodeBoxes);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        /// <summary>思维链走 FCTB（时间线默认展开、可折叠），只有答复正文用 MarkdownTextBox。</summary>
        [StaFact]
        public void TimelineThinking_IsCollapsedFctb_AndOnlyAnswerIsMarkdown()
        {
            using var flow = new ChatFlowControl();
            using var host = new Form { Size = new Size(900, 500) };
            host.Controls.Add(flow);
            flow.Dock = DockStyle.Fill;
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", "## 答\n\n正文"),
                    TimelineText = "**思维**\n\n```\ncode\n```",
                    ThinkingExpanded = false
                });
                flow.Bind(book);
                var row = AssistantRow(flow);
                Assert.NotNull(row);
                Assert.True(row.IsMarkdownRendered);      // 答复走 MarkdownTextBox
                Assert.False(row.ThinkingBox.Visible);    // 手动折叠后不展开
                Assert.DoesNotContain("**思维**", row.BodyBox.Text);

                book.Turns[0].ThinkingExpanded = true;
                flow.Bind(book);
                row = AssistantRow(flow);
                Assert.True(row.ThinkingBox.Visible);
                Assert.Contains("**思维**", row.ThinkingBox.Text);   // FCTB 原样显示，不渲染 markdown
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        /// <summary>展开的思考区按内容撑开：普通长度不应出现内部滚动条（高度不能按视口比例硬封顶）。</summary>
        [StaFact]
        public void ExpandedThinking_FitsContent_NoScrollbar_WhenShort()
        {
            using var flow = new ChatFlowControl();
            using var host = new Form { Size = new Size(900, 500) };
            host.Controls.Add(flow);
            flow.Dock = DockStyle.Fill;
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", "答"),
                    ReasoningText = string.Join("\n", Enumerable.Range(0, 6).Select(i => $"第 {i} 行思考")),
                    ThinkingExpanded = true
                });
                flow.Bind(book);
                var row = AssistantRow(flow);
                Assert.NotNull(row);
                Assert.True(row.ThinkingBox.Visible);
                Assert.False(row.ThinkingScrolls);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        /// <summary>展开的超长思维链拆成多行：每行都不超过窗口坐标上限，因此不需要内部滚动条。</summary>
        [StaFact]
        public void ExpandedLongThinking_IsSplitIntoRows_NoInnerScrollbar()
        {
            using var flow = new ChatFlowControl();
            using var host = new Form { Size = new Size(900, 500) };
            host.Controls.Add(flow);
            flow.Dock = DockStyle.Fill;
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", "## 答\n\n正文"),
                    ReasoningText = string.Join("\n", Enumerable.Range(0, 4000).Select(i => $"第 {i} 行思考，长度够撑高内容。")),
                    ThinkingExpanded = true
                });
                flow.Bind(book);
                flow.ScrollToEnd();

                string diag = flow.GetLayoutDiagnostics();
                int items = int.Parse(diag["items=".Length..diag.IndexOf(' ')]);
                Assert.True(items > 2, diag);
                foreach (var row in RealizedRows(flow))
                {
                    Assert.False(row.ThinkingScrolls, diag);
                    Assert.True(row.Height < 32_000, $"单行高度撞窗口坐标上限：{row.Height}");
                    Assert.True(row.Top > -32000, $"行位置被钳制：{row.Top}");
                }
                Assert.True(flow.AtEnd, diag);

                // 展开后正文必须在本行范围内（曾经因为思考区高度按旧宽度算，正文被挤到行外看不到）
                var lastAssistant = RealizedRows(flow)
                    .Where(r => r.Item is { IsUser: false })
                    .OrderByDescending(r => r.Top)
                    .FirstOrDefault();
                Assert.NotNull(lastAssistant);
                Assert.True(lastAssistant.MarkdownBox.Visible, diag);
                Assert.True(
                    lastAssistant.MarkdownBox.Top + lastAssistant.MarkdownBox.Height <= lastAssistant.Height + 2,
                    $"正文被挤出本行：boxTop={lastAssistant.MarkdownBox.Top} boxH={lastAssistant.MarkdownBox.Height} rowH={lastAssistant.Height}");
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        /// <summary>单条几十万像素高的行会撞 Win32 的 ±32767 像素定位上限（Y 被钳到 -32768），末尾永远滚不到。
        /// 超长正文必须拆片，任何已实例化行的 Top 都不能落在钳制值上。</summary>
        [StaFact]
        public void HugeMessage_IsSplitIntoRowsUnderTheWindowCoordinateLimit()
        {
            using var flow = new ChatFlowControl();
            using var host = new Form { Size = new Size(900, 500) };
            host.Controls.Add(flow);
            flow.Dock = DockStyle.Fill;
            host.Show();
            try
            {
                var body = string.Join("\n", Enumerable.Range(0, 3000)
                    .Select(i => $"第 {i} 行：一段足够长的占位文本，用来把这一条消息撑到窗口坐标上限以上。"));
                var book = new ChatTurnBook();
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", "长文"),
                    Assistant = new ChatMessage("assistant", body)
                });
                flow.Bind(book);
                flow.ScrollToEnd();

                string diag = flow.GetLayoutDiagnostics();
                int items = int.Parse(diag["items=".Length..diag.IndexOf(' ')]);
                Assert.True(items > 2, diag);
                foreach (var row in RealizedRows(flow))
                    Assert.True(row.Top > -32000, $"row top clamped: {row.Top}; {diag}");
                Assert.True(flow.AtEnd, diag);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        /// <summary>多块 markdown（散文/代码交替）的行高必须覆盖最后一块：测量与布局曾各算一遍块高，
        /// 布局按「块高 + 块间 4」堆叠、测量只加一次 4，块越多行高越短（每块差 4），末尾正文被裁。</summary>
        [StaFact]
        public void ManyBlocks_RowHeightCoversLastBlock()
        {
            using var flow = new ChatFlowControl();
            using var host = new Form { Size = new Size(900, 500) };
            host.Controls.Add(flow);
            flow.Dock = DockStyle.Fill;
            host.Show();
            try
            {
                string body = string.Join("\n\n", Enumerable.Range(0, 3).SelectMany(i => new[]
                {
                    $"第 {i} 段正文：" + new string('文', 60),
                    "```\ncode " + i + "\n```"
                })) + "\n\n最后一段正文，末尾不能被裁。";
                var row = BindSingle(flow, body);
                Assert.NotNull(row);
                Assert.True(row.IsMarkdownRendered);

                var field = typeof(ChatMessageRow).GetField("_blocks", BindingFlags.NonPublic | BindingFlags.Instance)!;
                foreach (var block in (IEnumerable)field.GetValue(row)!)
                {
                    var box = (Control)block.GetType().GetProperty("Box")!.GetValue(block)!;
                    Assert.True(box.Top + box.Height <= row.Height,
                        $"markdown 块超出本行：top={box.Top} h={box.Height} rowH={row.Height}");
                }
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        /// <summary>流式期间是纯文本，收尾才转 markdown：渲染后行高会变高，滚动范围必须跟着更新，
        /// 否则拖到底仍有末尾正文被裁（用户实测的底部截断）。</summary>
        [StaFact]
        public void StreamingThenMarkdown_KeepsBottomReachable()
        {
            using var flow = new ChatFlowControl();
            using var host = new Form { Size = new Size(900, 600) };
            host.Controls.Add(flow);
            flow.Dock = DockStyle.Fill;
            host.Show();
            try
            {
                string body = string.Join("\n\n", Enumerable.Range(0, 14)
                    .Select(i => $"## 第 {i} 段\n- 列表项 " + new string('长', 20)));

                var book = new ChatTurnBook();
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", ""),
                    Streaming = true
                };
                book.AddTurn(turn);
                flow.Bind(book);
                Application.DoEvents();

                // 收尾：正文转 markdown，整表重建后按新渲染量高度
                turn.AssistantText = body;
                turn.Streaming = false;
                book.RaiseChanged();
                Application.DoEvents();

                // 滚动/改宽会把行先按纯文本出帧、180ms 后才补 markdown（性能取舍）。
                // 断言前等这次升级完成，否则量到的是中间帧，与产品行为无关。
                for (int i = 0; i < 40; i++)
                {
                    var row = RealizedRows(flow).FirstOrDefault(r => r.Item is { IsUser: false });
                    if (row is { IsMarkdownRendered: true }) break;
                    System.Threading.Thread.Sleep(20);
                    Application.DoEvents();
                }

                string diag = flow.GetLayoutDiagnostics();
                Assert.True(flow.AtEnd, diag);
                var last = RealizedRows(flow).Where(r => r.Item is { IsUser: false })
                    .OrderByDescending(r => r.Top).FirstOrDefault();
                Assert.NotNull(last);
                Assert.True(last.IsMarkdownRendered, diag);
                Assert.True(last.MarkdownBox.Top + last.MarkdownBox.Height <= last.Height + 2,
                    $"正文被裁：top={last.MarkdownBox.Top} h={last.MarkdownBox.Height} rowH={last.Height}; {diag}");
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        /// <summary>行宽变窄后必须按新宽度重排散文块再量高度：RichEdit 要等下一次重绘才换行，
        /// 直接量到的是旧宽度的行数（偏少），盒高跟着变矮，正文被裁成最后一行/局部
        /// （折叠再展开思考区触发整表重绑才恢复）。</summary>
        [StaFact]
        public void ResizeRow_WhenProseGetsNarrower_KeepsFullHeight()
        {
            using var flow = new ChatFlowControl();
            using var host = new Form { Size = new Size(900, 700) };
            using var row = new ChatMessageRow(flow);
            host.Controls.Add(row);
            host.Show();
            try
            {
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "question"),
                    Assistant = new ChatMessage("assistant", string.Join("\n\n", Enumerable.Range(0, 10)
                        .Select(i => $"**Paragraph {i}**: this line is long enough to fill a wide row, "
                            + "and the measured height stays well below the plain-text estimate.")))
                };
                var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
                row.Height = row.Bind(item, flow.ChatFont, host.ClientSize.Width);
                Application.DoEvents();
                Assert.True(row.IsMarkdownRendered);
                var prose = (MarkdownRowTextBox)row.MarkdownBox;

                row.Width = 500;            // 只改宽度（等价于滚动条冒出来把行挤窄）
                Application.DoEvents();

                int need = prose.GetPreferredContentHeight();
                Assert.True(prose.Height >= need, $"散文块高度没跟上宽度变化：{prose.Height} < {need}");
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }
    }
}
