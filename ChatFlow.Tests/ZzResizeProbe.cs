using System.Collections;
using System.Diagnostics;
using System.Reflection;
using ChatFlow;
using Xunit;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    [Collection("ThemeState")]
    public class ZzResizeProbe
    {
        private static readonly string Log = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "chatflow-resize-probe.txt");

        private static string Body(int lines)
            => string.Concat(Enumerable.Repeat("这是一段用来撑高内容、让历史需要滚动的正文，含 **粗体** 与 `代码` 片段。\n", lines));

        private static ChatTurnBook Book(int turns)
        {
            var book = new ChatTurnBook();
            for (int i = 0; i < turns; i++)
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", "问题 " + i),
                    Assistant = new ChatMessage("assistant", Body(8)),
                    ReasoningText = string.Join("\n", Enumerable.Range(0, 40).Select(n => $"推理第 {n} 行，内容略长一些，用来模拟真实思维链的长度。")),
                    ToolCallLines = "→ read_file(path=a.txt)",
                    ThinkingExpanded = true
                });
            return book;
        }

        private static object Field(object target, string name)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

        private static MethodInfo RM(string name)
            => typeof(ChatMessageRow).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);

        private static void Pump(int ms)
        {
            long start = Environment.TickCount64;
            while (Environment.TickCount64 - start < ms)
            {
                WF.Application.DoEvents();
                System.Threading.Thread.Sleep(5);
            }
        }

        private static long Time(Action a)
        {
            var sw = Stopwatch.StartNew();
            a();
            return sw.ElapsedMilliseconds;
        }

        [StaFact]
        public void Bind_Cost()
        {
            System.IO.File.WriteAllText(Log, "");
            using var flow = new ChatFlowControl
            {
                Size = new System.Drawing.Size(2750, 1290),
                ChatFont = new System.Drawing.Font("Microsoft YaHei UI", 9F)
            };
            using var host = new WF.Form { Size = new System.Drawing.Size(2800, 1400) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                flow.Bind(Book(40));
                Pump(800);
                flow.ScrollToEnd();
                Pump(800);

                var realized = (IDictionary)Field(flow, "realized");
                var rows = realized.Values.Cast<ChatMessageRow>().Where(r => r.Visible).ToList();
                foreach (var row in rows)
                {
                    var item = row.Item;
                    string tag = $"{item.Kind}/md={row.IsMarkdownRendered}/h={row.Height}";
                    long bind = Time(() => row.Bind(item, flow.ChatFont, flow.Width));
                    long geom = Time(() => RM("UpdateBodyGeometry").Invoke(row, [item, flow.ChatFont, flow.Width]));
                    long layout = Time(() => RM("LayoutContent").Invoke(row, null));
                    long measure = Time(() => RM("MeasureRow").Invoke(row, null));
                    long bodyH = Time(() => RM("BodyContentHeight").Invoke(row, null));
                    long thinkH = Time(() => RM("ThinkingBoxHeight").Invoke(row, null));
                    long indent = Time(() => ChatTextStyler.IndentPromptBlocks(item.Text));
                    System.IO.File.AppendAllText(Log,
                        $"{tag}: bind={bind} geom={geom} layout={layout} measure={measure} bodyH={bodyH} thinkH={thinkH} indent={indent}\n");
                }
            }
            finally
            {
                host.Hide();
            }
        }
    }
}
