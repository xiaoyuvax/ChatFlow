using System.Drawing;
using System.Reflection;
using System.Collections;
using Xunit;
using Xunit.Abstractions;

namespace ChatFlow.Tests
{
    /// <summary>临时探针：用真实会话正文验证 markdown 正文是否被行高裁掉。用完删除。</summary>
    [Collection("ThemeState")]
    public class ZzTruncationProbe
    {
        private readonly ITestOutputHelper _out;
        public ZzTruncationProbe(ITestOutputHelper output) => _out = output;

        private static IEnumerable<ChatMessageRow> RealizedRows(ChatFlowControl flow)
        {
            var field = typeof(ChatFlowControl).GetField("realized", BindingFlags.NonPublic | BindingFlags.Instance)!;
            object dict = field.GetValue(flow)!;
            var values = (ICollection)dict.GetType().GetProperty("Values")!.GetValue(dict)!;
            foreach (ChatMessageRow row in values) yield return row;
        }

        [StaFact]
        public void Probe()
        {
            string dir = @"D:\PUB\OpenLoop\src\openloop\.tmp";
            foreach (int width in new[] { 1000, 1150 })
            {
                using var flow = new ChatFlowControl();
                using var host = new Form { Size = new Size(width, 800) };
                host.Controls.Add(flow);
                flow.Dock = DockStyle.Fill;
                host.Show();
                try
                {
                    foreach (string file in Directory.GetFiles(dir, "trunc-*.md").OrderBy(f => f))
                    {
                        string body = File.ReadAllText(file);
                        var book = new ChatTurnBook();
                        book.AddTurn(new ChatTurn
                        {
                            User = new ChatMessage("user", "问题"),
                            Assistant = new ChatMessage("assistant", body)
                        });
                        flow.Bind(book);
                        for (int i = 0; i < 5; i++) Application.DoEvents();

                        var row = RealizedRows(flow).FirstOrDefault(r => r.Item is { IsUser: false });
                        if (row == null) { _out.WriteLine($"{Path.GetFileName(file)} @{width}: no row"); continue; }
                        var prose = row.MarkdownBox as MarkdownRowTextBox;
                        if (prose == null)
                        {
                            _out.WriteLine($"{Path.GetFileName(file)} @{width}: not markdown (len={body.Length})");
                            continue;
                        }
                        int pref = prose.GetPreferredContentHeight();
                        int lastY = prose.GetPositionFromCharIndex(Math.Max(0, prose.TextLength - 1)).Y;
                        int lastLineH = prose.Font.Height;
                        int textBottom = lastY + lastLineH;
                        _out.WriteLine($"{Path.GetFileName(file)} @{width}: chars={body.Length} rowH={row.Height} "
                            + $"proseH={prose.Height} pref={pref} lastY={lastY} textBottom={textBottom} "
                            + $"clipped={(textBottom > prose.Height)} md={row.IsMarkdownRendered} textLen={prose.TextLength}");
                    }
                    _out.WriteLine($"diag @{width}: {flow.GetLayoutDiagnostics()}");
                }
                finally
                {
                    host.Hide();
                    host.Dispose();
                }
            }
        }
    }
}
