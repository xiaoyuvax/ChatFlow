using System.Collections;
using System.Reflection;
using FastColoredTextBoxNS;
using FastColoredTextBoxNS.Feature;
using Xunit;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    /// <summary>
    /// markdown 模式下的围栏代码段是独立 FCTB：点折叠 gutter 后必须让行重测高度，
    /// 否则行高停在旧值（折叠后留白 / 展开时被矮行裁住，看起来「点不开」）。
    /// </summary>
    [Collection("ThemeState")]
    public class ChatCodeFoldTests
    {
        private const string Body =
            "说明\n\n```csharp\nvoid M()\n{\n    A();\n    B();\n    C();\n}\n```\n";

        [StaFact]
        public void CodeBox_FoldGutterClick_RemeasuresRow()
        {
            using var flow = new ChatFlowControl();
            using var host = new WF.Form { Size = new Size(900, 700) };
            host.Controls.Add(flow);
            flow.Dock = WF.DockStyle.Fill;
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", "看看代码"),
                    Assistant = new ChatMessage("assistant", Body),
                });
                flow.Bind(book);

                var row = RealizedRows(flow).First(r => r.Item is { IsUser: false });
                Assert.True(row.IsMarkdownRendered);
                var box = row.CodeBoxes[0];
                int expanded = row.Height;

                ClickFoldingMarker(box);
                Assert.True(row.Height < expanded, $"折叠后行高没变：{row.Height} == {expanded}");

                ClickFoldingMarker(box);
                Assert.True(row.Height >= expanded - 2, $"展开后行高没恢复：{row.Height} < {expanded}");
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        private static void ClickFoldingMarker(RowTextBox box)
        {
            box.Refresh();
            var markers = (List<VisualMarker>)typeof(FastColoredTextBox)
                .GetField("visibleMarkers", BindingFlags.NonPublic | BindingFlags.Instance)!
                .GetValue(box)!;
            var marker = markers.FirstOrDefault(m => m is CollapseFoldingMarker or ExpandFoldingMarker);
            Assert.NotNull(marker);
            var rect = marker.rectangle;
            var at = new Point(rect.Left + rect.Width / 2, rect.Top + rect.Height / 2);
            var onClick = typeof(RowTextBox).GetMethod("OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
            onClick.Invoke(box, [new WF.MouseEventArgs(WF.MouseButtons.Left, 1, at.X, at.Y, 0)]);
        }

        private static IEnumerable<ChatMessageRow> RealizedRows(ChatFlowControl flow)
        {
            var field = typeof(ChatFlowControl).GetField("realized", BindingFlags.NonPublic | BindingFlags.Instance)!;
            object dict = field.GetValue(flow)!;
            var values = (ICollection)dict.GetType().GetProperty("Values")!.GetValue(dict)!;
            foreach (ChatMessageRow row in values) yield return row;
        }
    }
}
