using System.Collections;
using System.Diagnostics;
using System.Reflection;
using ChatFlow;
using Xunit;
using Xunit.Abstractions;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    /// <summary>
    /// 拖动历史时的渲染策略与末尾高度：
    /// markdown 块排版很慢（每块几十毫秒），滚动期间按纯文本渲染，停稳后再补；
    /// 补完高度变了，滚动范围必须跟着走，否则拖到底仍有正文被裁。
    /// 样本必须是真实回复的形态（markdown + 正则关注点混排），否则量不出真实瓶颈。
    /// </summary>
    [Collection("ThemeState")]
    public class ChatScrollMarkdownTests
    {
        private readonly ITestOutputHelper _out;

        public ChatScrollMarkdownTests(ITestOutputHelper output) => _out = output;

        /// <summary>真实 agent 回复：标题/列表/表格/围栏代码 + 粗体/行内代码/URL/本地路径/分辨率词。</summary>
        private static string Markdown(int i)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine($"## 第 {i} 段结论");
            sb.AppendLine();
            sb.AppendLine("先看 `src/OpenLoop.App/SessionView.cs:1261` 的轮询，把 250ms 的快照合并一次；"
                + "**注意** 不要碰 D:\\PUB\\OpenLoop\\src\\OpenLoop.Service\\Worker\\WorkerSessionHost.cs"
                + " 里的 Revision 语义。");
            sb.AppendLine();
            sb.AppendLine("相关文档：https://learn.microsoft.com/dotnet/desktop/winforms/ 与 "
                + "https://venice.ai/api/v1/models 。");
            sb.AppendLine();
            sb.AppendLine("- `ChatFlowControl.UpdateStreamingMessage` 取最后一个分片");
            sb.AppendLine("- 输出 4K 采样时先降 2K，见图 `assets/out_4k.png`");
            sb.AppendLine();
            sb.AppendLine("| 阶段 | 耗时 |");
            sb.AppendLine("|---|---|");
            sb.AppendLine("| markdown | 130ms |");
            sb.AppendLine();
            sb.AppendLine("```csharp");
            sb.AppendLine("var snapshot = await client.GetAsync(id, ct);");
            sb.AppendLine("if (snapshot.Revision == last) return;");
            sb.AppendLine("```");
            return sb.ToString();
        }

        private static ChatTurnBook LongBook(int turns)
        {
            var book = new ChatTurnBook();
            for (int i = 0; i < turns; i++)
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", "问题 " + i),
                    Assistant = new ChatMessage("assistant", Markdown(i))
                });
            return book;
        }

        private static void Pump(int ms)
        {
            long start = Environment.TickCount64;
            while (Environment.TickCount64 - start < ms)
            {
                WF.Application.DoEvents();
                System.Threading.Thread.Sleep(10);
            }
        }

        private static object Field(object target, string name)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

        private static List<ChatMessageRow> Rows(ChatFlowControl flow)
            => ((IDictionary)Field(flow, "realized")).Values.Cast<ChatMessageRow>().ToList();

        private static long Min(Action action, int runs = 3)
        {
            long best = long.MaxValue;
            for (int i = 0; i < runs; i++)
            {
                var sw = Stopwatch.StartNew();
                action();
                best = Math.Min(best, sw.ElapsedMilliseconds);
            }
            return best;
        }

        [StaFact]
        public void ScrollingFallsBackToPlainText_AndRestoresMarkdownWhenIdle()
        {
            using var flow = new ChatFlowControl { Size = new System.Drawing.Size(800, 500) };
            using var host = new WF.Form { Size = new System.Drawing.Size(840, 560) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                flow.Bind(LongBook(12));
                Pump(400);
                Assert.Contains(Rows(flow), r => r.IsMarkdownRendered);

                var vbar = (WF.VScrollBar)Field(flow, "vbar");
                Assert.True(vbar.Visible);
                vbar.Value = Math.Min(vbar.Maximum - vbar.LargeChange + 1, vbar.LargeChange);

                Assert.False(flow.MarkdownReady);

                Pump(500);
                Assert.Contains(Rows(flow), r => r.IsMarkdownRendered);
            }
            finally
            {
                host.Hide();
            }
        }

        private static int TopIndex(ChatFlowControl flow)
        {
            var vbar = (WF.VScrollBar)Field(flow, "vbar");
            var items = (IList)Field(flow, "items");
            int[] sums = (int[])Field(flow, "prefixSum");
            int y = vbar.Visible ? vbar.Value : 0;
            int lo = 0, hi = items.Count;
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (sums[mid + 1] > y) hi = mid; else lo = mid + 1;
            }
            return Math.Min(lo, Math.Max(0, items.Count - 1));
        }

        /// <summary>视口顶部落在哪个条目、以及在该条目内部偏了多少像素。</summary>
        private static (int Index, int Offset) Anchor(ChatFlowControl flow)
        {
            var vbar = (WF.VScrollBar)Field(flow, "vbar");
            int[] sums = (int[])Field(flow, "prefixSum");
            int y = vbar.Visible ? vbar.Value : 0;
            int index = TopIndex(flow);
            return (index, y - sums[Math.Min(index, sums.Length - 1)]);
        }

        /// <summary>
        /// 滚动停稳后补 markdown 时不能把滚动位置弹走：高度只能在实测值上更新，
        /// 不能整份作废回到估算值（那会让视口顶部的条目整体错位，用户眼前的正文被顶走）。
        /// 样本里放一条超长纯文本回复：它的估算高度和实测差得最多，正好能暴露这种回退。
        /// </summary>
        [StaFact]
        public void MarkdownUpgrade_KeepsViewportAnchor()
        {
            using var flow = new ChatFlowControl { Size = new System.Drawing.Size(800, 500) };
            using var host = new WF.Form { Size = new System.Drawing.Size(840, 560) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = LongBook(3);
                for (int i = 0; i < 8; i++)
                    book.AddTurn(new ChatTurn
                    {
                        User = new ChatMessage("user", "长问 " + i),
                        Assistant = new ChatMessage("assistant", string.Concat(Enumerable.Repeat(
                            "这是一段很长的正文，用来把这一行撑得很高；估算高度和真实渲染差得最多。\n", 60)))
                    });
                foreach (var turn in LongBook(4).Turns) book.AddTurn(turn);
                flow.Bind(book);
                Pump(500);

                var vbar = (WF.VScrollBar)Field(flow, "vbar");
                Assert.True(vbar.Visible);
                // 一路滚到底：途中每一条长回复都会被真实渲染并记下实测高度
                for (int i = 0; i < 200 && vbar.Value < vbar.Maximum - vbar.LargeChange; i++)
                {
                    vbar.Value = Math.Min(vbar.Maximum - vbar.LargeChange + 1, vbar.Value + vbar.LargeChange);
                    WF.Application.DoEvents();
                    Pump(20);
                }
                Pump(400);
                // 再往回滚两屏（用户回去挑正文），停下后标记会补回来
                vbar.Value = Math.Max(0, vbar.Value - vbar.LargeChange * 2);
                Pump(50);
                var before = Anchor(flow);

                Pump(600);                              // 停稳：markdown 补回来
                Assert.True(flow.MarkdownReady);
                var after = Anchor(flow);
                Assert.Equal(before.Index, after.Index);
                // 视口顶部的像素位置必须留在同一条目里的同一处（允许少许变化：顶部那一行自己会变高）
                Assert.InRange(after.Offset, Math.Max(0, before.Offset - 60), before.Offset + 60);
            }
            finally
            {
                host.Hide();
            }
        }

        /// <summary>拖动/滚轮滚动时位置只能单调变化：行高从估算变实测不能让视口往回弹。</summary>
        [StaFact]
        public void Scrolling_DoesNotBounceBack()
        {
            using var flow = new ChatFlowControl { Size = new System.Drawing.Size(800, 500) };
            using var host = new WF.Form { Size = new System.Drawing.Size(840, 560) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                for (int i = 0; i < 6; i++)
                {
                    book.AddTurn(new ChatTurn
                    {
                        User = new ChatMessage("user", "问 " + i),
                        Assistant = new ChatMessage("assistant", $"## 答 {i}\n\n- 一\n- 二")
                    });
                    book.AddTurn(new ChatTurn
                    {
                        User = new ChatMessage("user", "长问 " + i),
                        Assistant = new ChatMessage("assistant", string.Concat(Enumerable.Repeat(
                            "很长的正文段落，用来把这一行撑高；估算和实测差得最多的地方就在这里。\n", 40)))
                    });
                }
                flow.Bind(book);
                Pump(500);

                var vbar = (WF.VScrollBar)Field(flow, "vbar");
                Assert.True(vbar.Visible);
                int step = Math.Max(20, vbar.LargeChange / 2);
                int prev = vbar.Value;
                for (int i = 0; i < 120; i++)
                {
                    int before = vbar.Value;
                    int target = Math.Min(vbar.Maximum - vbar.LargeChange + 1, before + step);
                    if (target <= before) break;
                    vbar.Value = target;
                    WF.Application.DoEvents();
                    Pump(10);
                    Assert.True(vbar.Value >= before, $"向下滚动回弹：{before} → {vbar.Value}（第 {i} 步）");
                    prev = vbar.Value;
                }

                for (int i = 0; i < 120; i++)
                {
                    int before = vbar.Value;
                    int target = Math.Max(0, before - step);
                    if (target >= before) break;
                    vbar.Value = target;
                    WF.Application.DoEvents();
                    Pump(10);
                    Assert.True(vbar.Value <= before, $"向上滚动回弹：{before} → {vbar.Value}（第 {i} 步）");
                }
            }
            finally
            {
                host.Hide();
            }
        }

        /// <summary>用户选好正文再轻滚一下，选区不能被行回收/重绑清掉（否则复制不了）。</summary>
        [StaFact]
        public void SelectingText_SurvivesSmallScroll()
        {
            using var flow = new ChatFlowControl { Size = new System.Drawing.Size(800, 500) };
            using var host = new WF.Form { Size = new System.Drawing.Size(840, 560) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                flow.Bind(LongBook(12));
                Pump(400);

                var row = Rows(flow).First(r => !r.IsMarkdownRendered && r.Item?.IsUser == true);
                row.BodyBox.SelectionStart = 0;
                row.BodyBox.SelectionLength = 4;
                Assert.True(row.HasTextSelection);

                var vbar = (WF.VScrollBar)Field(flow, "vbar");
                vbar.Value = Math.Max(0, vbar.Value - 20);
                Pump(500);

                Assert.True(row.HasTextSelection, "轻滚后选区被清掉了");
                Assert.Equal(4, row.GetSelectedText().Length);
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void AfterMarkdownUpgrade_BottomRowIsFlushWithViewport()
        {
            using var flow = new ChatFlowControl { Size = new System.Drawing.Size(800, 500) };
            using var host = new WF.Form { Size = new System.Drawing.Size(840, 560) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                flow.Bind(LongBook(12));
                Pump(400);
                flow.ScrollToEnd();
                Pump(400);
                flow.ScrollToEnd();
                Pump(200);

                Assert.True(flow.AtEnd);
                var rows = Rows(flow);
                int bottom = rows.Max(r => r.Bottom);
                Assert.InRange(bottom, flow.ClientSize.Height - 2, flow.ClientSize.Height + 2);
                Assert.DoesNotContain("clip=1", flow.GetLayoutDiagnostics());
            }
            finally
            {
                host.Hide();
            }
        }

        /// <summary>长回复不走 markdown：SetMarkdown 随长度超线性变慢，长文按带样式的纯文本渲染。</summary>
        [StaFact]
        public void LongAnswer_FallsBackToStyledPlainText()
        {
            using var flow = new ChatFlowControl();
            using var row = new ChatMessageRow(flow);
            var font = new System.Drawing.Font("Consolas", 9.75F);
            using var host = new WF.Form();
            host.Controls.Add(row);
            host.Show();
            try
            {
                var longText = string.Concat(Enumerable.Range(0, 3).Select(Markdown));
                longText += new string('文', Math.Max(1, ChatMarkdown.MaxChars + 1 - longText.Length));
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", longText)
                };
                row.Bind(new ChatFlowItem(turn, ChatFlowItemKind.Assistant), font, 600);
                Assert.False(row.IsMarkdownRendered);
                Assert.True(row.BodyBox.Visible);

                var shortTurn = new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", Markdown(1))
                };
                row.Bind(new ChatFlowItem(shortTurn, ChatFlowItemKind.Assistant), font, 600);
                Assert.True(row.IsMarkdownRendered);
            }
            finally
            {
                font.Dispose();
                host.Hide();
            }
        }

        /// <summary>
        /// 量两条渲染路径的真实开销：纯文本（FCTB + 正则样式）与 markdown（分段 + SetMarkdown）。
        /// 数字打到测试输出，用来判断瓶颈在哪一侧，而不是靠猜。
        /// </summary>        [StaFact]
        public void RenderCost_RegexStylingVsMarkdownRendering()
        {
            using var host = new WF.Form { Size = new System.Drawing.Size(900, 760) };
            using var plain = new RowTextBox
            {
                ReadOnly = true,
                ShowScrollBars = false,
                WordWrap = true,
                ShowLineNumbers = false,
                Width = 780,
                Height = 700
            };
            using var md = new MarkdownRowTextBox { Width = 780, Height = 700, TabStop = false };
            host.Controls.Add(plain);
            host.Controls.Add(md);
            host.Show();
            try
            {
                foreach (int copies in new[] { 1, 3 })
                {
                    string text = string.Concat(Enumerable.Range(0, copies * 4).Select(Markdown));
                    long route = Min(() =>
                    {
                        _ = ChatMarkdown.IsMarkdown(text);
                        _ = ChatMarkdown.SplitSegments(text);
                    });
                    long regex = Min(() =>
                    {
                        plain.Text = text;
                        ChatTextStyler.ApplyBody(plain, text);
                    });
                    long markdown = Min(() => md.SetMarkdown(text), runs: copies == 1 ? 3 : 1);
                    double perChar = markdown / (double)Math.Max(1, text.Length);

                    _out.WriteLine($"{text.Length,6} 字: route={route}ms regexApply={regex}ms "
                        + $"markdown={markdown}ms ({perChar:0.000}ms/字)");

                    // 只兜底病态回退（正则灾难性回溯 / 排版平方级膨胀），不做精确基准
                    Assert.True(regex < 3000, $"正则样式耗时异常：{regex}ms / {text.Length} 字");
                    Assert.True(markdown < 8000, $"markdown 渲染耗时异常：{markdown}ms / {text.Length} 字");
                }
            }
            finally
            {
                host.Hide();
            }
        }

        /// <summary>滚动上限必须等于「内容高−视口高」：WinForms 的 LargeChange 读取值被当前 Maximum 钳住
        /// （min(stored, Maximum-Minimum+1)），一次更新里既长内容又抬 Maximum 时若用 getter 凑 Maximum，
        /// 会按旧的偏小值算（实测 139 vs 需要 172），末尾正文永远滚不到。</summary>
        [StaFact]
        public void UpdateScrollbar_RangeFollowsTotalHeight_WhenGrowingInOneStep()
        {
            using var flow = new ChatFlowControl { Size = new System.Drawing.Size(800, 500) };
            using var host = new WF.Form { Size = new System.Drawing.Size(840, 560) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var totalField = typeof(ChatFlowControl).GetField("totalHeight",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var update = typeof(ChatFlowControl).GetMethod("UpdateScrollbar",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                var vbar = (WF.VScrollBar)Field(flow, "vbar");

                // 旧视口留下的低位范围：stored LargeChange=250 但 Maximum 只有 216（getter 读到 217）
                vbar.Visible = true;
                vbar.LargeChange = 250;
                vbar.Maximum = 216;

                totalField.SetValue(flow, flow.ClientSize.Height + 172);
                update.Invoke(flow, null);

                Assert.Equal(172, vbar.Maximum - vbar.LargeChange + 1);
            }
            finally
            {
                host.Hide();
            }
        }

        /// <summary>会话停止后补一次重测：末尾行的高度被砍小（等价于「量在渲染完成前」留下的偏小值），
        /// RemeasureLayout 必须按真实渲染长回来，视图仍贴底。</summary>
        [StaFact]
        public void RemeasureLayout_RestoresTrailingBodyHeight()
        {
            using var flow = new ChatFlowControl { Size = new System.Drawing.Size(800, 500) };
            using var host = new WF.Form { Size = new System.Drawing.Size(840, 560) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                flow.Bind(LongBook(6));
                Pump(400);
                flow.ScrollToEnd();
                Pump(300);

                var last = Rows(flow).OrderBy(r => r.Top).Last();
                int full = last.Height;
                last.Height = full - 40;
                Assert.Equal(full - 40, last.Height);

                flow.RemeasureLayout();
                Pump(300);

                Assert.True(flow.AtEnd);
                var again = Rows(flow).OrderBy(r => r.Top).Last();
                Assert.InRange(again.Height, full - 2, full + 2);
                Assert.InRange(again.Bottom, flow.ClientSize.Height - 2, flow.ClientSize.Height + 2);
            }
            finally
            {
                host.Hide();
            }
        }
    }
}
