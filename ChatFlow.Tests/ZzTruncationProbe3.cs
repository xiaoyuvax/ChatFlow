using System.Drawing;
using System.Reflection;
using System.Collections;
using System.Runtime.InteropServices;
using Xunit;
using Xunit.Abstractions;

namespace ChatFlow.Tests
{
    /// <summary>临时探针 3：在真实 240 DPI（2.5x）下复现正文末尾裁切。用完删除。</summary>
    [Collection("ThemeState")]
    public class ZzTruncationProbe3
    {
        private readonly ITestOutputHelper _out;
        public ZzTruncationProbe3(ITestOutputHelper output) => _out = output;

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr value);

        private static IEnumerable<ChatMessageRow> RealizedRows(ChatFlowControl flow)
        {
            var field = typeof(ChatFlowControl).GetField("realized", BindingFlags.NonPublic | BindingFlags.Instance)!;
            object dict = field.GetValue(flow)!;
            var values = (ICollection)dict.GetType().GetProperty("Values")!.GetValue(dict)!;
            foreach (ChatMessageRow row in values) yield return row;
        }

        [StaFact]
        public void ProbeHighDpi()
        {
            SetProcessDpiAwarenessContext(new IntPtr(-4));   // PER_MONITOR_AWARE_V2
            string dir = @"D:\PUB\OpenLoop\src\openloop\.tmp";
            foreach (int dipWidth in new[] { 1100, 900 })
            {
                using var flow = new ChatFlowControl();
                int scale = flow.DeviceDpi;
                int px = (int)Math.Round(dipWidth * scale / 96.0);
                using var host = new Form { Size = new Size(px, px * 7 / 10) };
                host.Controls.Add(flow);
                flow.Dock = DockStyle.Fill;
                host.Show();
                try
                {
                    _out.WriteLine($"dpi={scale} dipWidth={dipWidth} px={px}");
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
                        for (int i = 0; i < 6; i++) Application.DoEvents();

                        var row = RealizedRows(flow).FirstOrDefault(r => r.Item is { IsUser: false });
                        if (row == null) { _out.WriteLine($"  {Path.GetFileName(file)}: no row"); continue; }
                        var prose = row.MarkdownBox as MarkdownRowTextBox;
                        if (prose == null) { _out.WriteLine($"  {Path.GetFileName(file)}: plain"); continue; }
                        int lastY = prose.GetPositionFromCharIndex(Math.Max(0, prose.TextLength - 1)).Y;
                        int textBottom = lastY + prose.Font.Height;
                        _out.WriteLine($"  {Path.GetFileName(file)}: rowH={row.Height} proseTop={prose.Top} proseH={prose.Height} "
                            + $"pref={prose.GetPreferredContentHeight()} lastY={lastY} textBottom={textBottom} "
                            + $"clipped={textBottom > prose.Height} textLen={prose.TextLength}/{body.Length}");
                    }
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
