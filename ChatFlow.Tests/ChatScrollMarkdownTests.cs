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

                Assert.All(Rows(flow), r => Assert.False(r.IsMarkdownRendered));

                Pump(500);
                Assert.Contains(Rows(flow), r => r.IsMarkdownRendered);
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
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant",
                        string.Concat(Enumerable.Range(0, 3).Select(Markdown)))
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
    }
}
