using System.Reflection;
using FastColoredTextBoxNS;
using Xunit;
using ChatFlow;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    /// <summary>
    /// 聊天区可点击链接（URL / 图像文件路径）STA 单测：
    /// 命中测试映射（PointToPosition → LinkSpans）与行→容器事件转发。
    /// 合成点击：把链接字符偏移经 PositionToPoint 转成坐标，反射调用 RowTextBox.OnMouseDown（protected）。
    /// </summary>
    [Collection("ThemeState")]
    public class ChatLinkClickTests
    {
        private const string BodyText = "看图 J:/MY GOD/铁蛋.png 与页面 https://example.com/doc 即可";

        [StaFact]
        public void Row_LeftClickOnImagePath_RaisesLinkClicked()
        {
            using var ownerFlow = new ChatFlowControl();
            using var row = new ChatMessageRow(ownerFlow);
            var host = new Form { Size = new Size(900, 400) };
            host.Controls.Add(row);
            host.Show();
            try
            {
                row.Bind(new ChatFlowItem(MakeTurn(), ChatFlowItemKind.Assistant), new Font("Consolas", 9.75F), 800);
                var box = GetField<RowTextBox>(row, "txt");

                (string kind, string target) fired = default;
                row.LinkClicked += (_, kind, target) => fired = (kind.ToString(), target);

                int at = BodyText.IndexOf("J:/MY GOD/铁蛋.png", StringComparison.Ordinal) + 3;
                ClickAt(box, box.PositionToPoint(at));

                Assert.Equal("ImageFile", fired.kind);
                Assert.Equal("J:/MY GOD/铁蛋.png", fired.target);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void Row_LeftClickOnUrl_RaisesLinkClicked()
        {
            using var ownerFlow = new ChatFlowControl();
            using var row = new ChatMessageRow(ownerFlow);
            var host = new Form { Size = new Size(900, 400) };
            host.Controls.Add(row);
            host.Show();
            try
            {
                row.Bind(new ChatFlowItem(MakeTurn(), ChatFlowItemKind.Assistant), new Font("Consolas", 9.75F), 800);
                var box = GetField<RowTextBox>(row, "txt");

                (string kind, string target) fired = default;
                row.LinkClicked += (_, kind, target) => fired = (kind.ToString(), target);

                int at = BodyText.IndexOf("https://example.com/doc", StringComparison.Ordinal) + 5;
                ClickAt(box, box.PositionToPoint(at));

                Assert.Equal("Url", fired.kind);
                Assert.Equal("https://example.com/doc", fired.target);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void Row_LeftClickOnPlainText_DoesNotRaise()
        {
            using var ownerFlow = new ChatFlowControl();
            using var row = new ChatMessageRow(ownerFlow);
            var host = new Form { Size = new Size(900, 400) };
            host.Controls.Add(row);
            host.Show();
            try
            {
                row.Bind(new ChatFlowItem(MakeTurn(), ChatFlowItemKind.Assistant), new Font("Consolas", 9.75F), 800);
                var box = GetField<RowTextBox>(row, "txt");

                int fired = 0;
                row.LinkClicked += (_, _, _) => fired++;

                ClickAt(box, box.PositionToPoint(0)); // "看图" 位于普通文本
                Assert.Equal(0, fired);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        [StaFact]
        public void Flow_RealizedRowClick_ForwardsToControl()
        {
            // 容器级：行池实例化 → 事件接线 → LinkClicked 出控件
            using var flow = new ChatFlowControl();
            var book = new ChatTurnBook();
            book.AddTurn(MakeTurn());
            flow.Bind(book);
            var host = new Form { Size = new Size(900, 400) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                flow.CreateControl();      // 建句柄
                flow.ClientSize = new Size(880, 380);
                flow.ScrollToEnd();

                string target = null;
                flow.LinkClicked += (_, e) => target = e.Target;

                var realized = flow.GetType()
                    .GetField("realized", BindingFlags.NonPublic | BindingFlags.Instance)!
                    .GetValue(flow) as System.Collections.IDictionary;
                Assert.NotEmpty(realized.Keys);
                // 取真正含链接的那一行（用户行无链接，点它不触发）
                var row = realized.Values.Cast<ChatMessageRow>()
                    .FirstOrDefault(r => GetField<RowTextBox>(r, "txt").LinkSpans.Count > 0);
                Assert.NotNull(row);
                var box = GetField<RowTextBox>(row, "txt");

                int at = BodyText.IndexOf("J:/MY GOD/铁蛋.png", StringComparison.Ordinal) + 3;
                ClickAt(box, box.PositionToPoint(at));

                Assert.Equal("J:/MY GOD/铁蛋.png", target);
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }

        private static ChatTurn MakeTurn() => new()
        {
            User = new ChatMessage("user", "图在哪"),
            Assistant = new ChatMessage("assistant", BodyText),
        };

        private static void ClickAt(RowTextBox box, Point at)
        {
            var onClick = typeof(RowTextBox).GetMethod(
                "OnMouseDown", BindingFlags.NonPublic | BindingFlags.Instance)!;
            var args = new WF.MouseEventArgs(WF.MouseButtons.Left, 1, at.X, at.Y, 0);
            onClick.Invoke(box, [args]);
        }

        private static T GetField<T>(object instance, string name) where T : class
            => instance.GetType().GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(instance) as T
               ?? throw new InvalidOperationException($"字段 {name} 不存在或类型不符");
    }
}
