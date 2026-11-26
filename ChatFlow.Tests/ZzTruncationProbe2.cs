using System.Drawing;
using System.Reflection;
using System.Collections;
using Xunit;
using Xunit.Abstractions;

namespace ChatFlow.Tests
{
    /// <summary>临时探针 2：复现「流式收尾 → markdown」与「宽度变化」后的正文末尾裁切。用完删除。</summary>
    [Collection("ThemeState")]
    public class ZzTruncationProbe2
    {
        private readonly ITestOutputHelper _out;
        public ZzTruncationProbe2(ITestOutputHelper output) => _out = output;

        private static IEnumerable<ChatMessageRow> RealizedRows(ChatFlowControl flow)
        {
            var field = typeof(ChatFlowControl).GetField("realized", BindingFlags.NonPublic | BindingFlags.Instance)!;
            object dict = field.GetValue(flow)!;
            var values = (ICollection)dict.GetType().GetProperty("Values")!.GetValue(dict)!;
            foreach (ChatMessageRow row in values) yield return row;
        }

        private void Report(string tag, ChatFlowControl flow, string body)
        {
            var row = RealizedRows(flow).Where(r => r.Item is { IsUser: false })
                .OrderByDescending(r => r.Top).FirstOrDefault();
            if (row == null) { _out.WriteLine($"{tag}: no row"); return; }
            var prose = row.MarkdownBox as MarkdownRowTextBox;
            string clipped;
            if (prose == null) clipped = "n/a(plain)";
            else
            {
                int lastY = prose.GetPositionFromCharIndex(Math.Max(0, prose.TextLength - 1)).Y;
                int textBottom = lastY + prose.Font.Height;
                clipped = $"proseH={prose.Height} pref={prose.GetPreferredContentHeight()} lastY={lastY} "
                    + $"bottom={textBottom} clipped={textBottom > prose.Height}";
            }
            _out.WriteLine($"{tag}: chars={body.Length} md={row.IsMarkdownRendered} rowH={row.Height} "
                + $"rowTop={row.Top} atEnd={flow.AtEnd} | {clipped} | {flow.GetLayoutDiagnostics()}");
        }

        [StaFact]
        public void StreamingThenFinish()
        {
            string dir = @"D:\PUB\OpenLoop\src\openloop\.tmp";
            foreach (string file in Directory.GetFiles(dir, "trunc-*.md").OrderBy(f => f))
            {
                string body = File.ReadAllText(file);
                using var flow = new ChatFlowControl();
                using var host = new Form { Size = new Size(1100, 800) };
                host.Controls.Add(flow);
                flow.Dock = DockStyle.Fill;
                host.Show();
                try
                {
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

                    // 逐段流式（模拟真实增量）
                    for (int i = 400; i < body.Length; i += 400)
                    {
                        turn.AssistantText = body[..i];
                        flow.UpdateStreamingMessage(turn, null);
                        Application.DoEvents();
                    }
                    turn.AssistantText = body;
                    flow.UpdateStreamingMessage(turn, null);
                    Application.DoEvents();

                    // 收尾
                    turn.Streaming = false;
                    book.RaiseChanged();
                    Application.DoEvents();
                    Application.DoEvents();
                    Report($"{Path.GetFileName(file)} afterFinish", flow, body);

                    // 再滚一次（用户拖动）
                    flow.ScrollToEnd();
                    Application.DoEvents();
                    Report($"{Path.GetFileName(file)} afterScroll", flow, body);
                }
                finally
                {
                    host.Hide();
                    host.Dispose();
                }
            }
        }

        [StaFact]
        public void NarrowThenWide()
        {
            string dir = @"D:\PUB\OpenLoop\src\openloop\.tmp";
            foreach (string file in Directory.GetFiles(dir, "trunc-*.md").OrderBy(f => f))
            {
                string body = File.ReadAllText(file);
                using var flow = new ChatFlowControl();
                using var host = new Form { Size = new Size(1100, 800) };
                host.Controls.Add(flow);
                flow.Dock = DockStyle.Fill;
                host.Show();
                try
                {
                    var book = new ChatTurnBook();
                    book.AddTurn(new ChatTurn
                    {
                        User = new ChatMessage("user", "问题"),
                        Assistant = new ChatMessage("assistant", body)
                    });
                    flow.Bind(book);
                    Application.DoEvents();
                    Report($"{Path.GetFileName(file)} wide", flow, body);

                    host.Size = new Size(760, 800);   // 侧栏打开：变窄
                    Application.DoEvents();
                    Application.DoEvents();
                    Report($"{Path.GetFileName(file)} narrow", flow, body);
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
