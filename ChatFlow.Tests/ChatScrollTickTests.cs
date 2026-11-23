using System.Drawing;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using Xunit;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    /// <summary>
    /// 拖动滚动条时的轮次刻度与预览浮窗：每轮一条刻线、浮窗显示那一轮的用户输入（截断）、靠近刻线磁吸。
    /// 注意数据绑定后默认跟随末尾（视口停在最后一轮），要按滚动位置判断当前轮。
    /// </summary>
    [Collection("ThemeState")]
    public class ChatScrollTickTests
    {
        private static object Field(object target, string name)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

        private static ChatTurnBook Book(int turns, int lines = 12)
        {
            var body = string.Concat(Enumerable.Repeat("这是一段用来撑高的正文，验证拖动滚动条时的轮次刻度。\n", lines));
            var book = new ChatTurnBook();
            for (int i = 0; i < turns; i++)
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", $"第 {i + 1} 个问题：这一轮的用户输入要能显示在浮窗里"),
                    Assistant = new ChatMessage("assistant", body)
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

        private static WF.VScrollBar Bar(ChatFlowControl flow) => (WF.VScrollBar)Field(flow, "vbar");

        [StaFact]
        public void Ticks_OneperTurnWithUserInput_AscendingByOffset()
        {
            using var flow = new ChatFlowControl
            {
                Size = new Size(900, 500),
                ChatFont = new Font("Microsoft YaHei UI", 9F)
            };
            using var host = new Form { Size = new Size(940, 560) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                flow.Bind(Book(12));
                Pump(400);

                var offsets = flow.ScrollTickOffsets;
                Assert.Equal(12, offsets.Count);
                for (int i = 1; i < offsets.Count; i++)
                    Assert.True(offsets[i] > offsets[i - 1], $"刻度偏移不是严格递增：{offsets[i - 1]} → {offsets[i]}");
                Assert.Equal(0, offsets[0]);   // 第一条刻线就是内容顶部
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void Drag_BeginsTicksAndPreview_AndEndsOnEndScroll()
        {
            using var flow = new ChatFlowControl
            {
                Size = new Size(900, 500),
                ChatFont = new Font("Microsoft YaHei UI", 9F)
            };
            using var host = new Form { Size = new Size(940, 560) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                flow.Bind(Book(12));
                Pump(400);
                Assert.False(flow.ScrollTicksVisible);
                Assert.True(string.IsNullOrEmpty(flow.ScrollPreviewText));

                flow.BeginScrollDrag();
                Pump(150);
                Assert.True(flow.ScrollDragActive);
                Assert.True(flow.ScrollTicksVisible);
                // 绑定后停在末尾：浮窗显示最后一轮
                Assert.Equal("12/12", flow.ScrollPreviewCaption);
                Assert.Contains("第 12 个问题", flow.ScrollPreviewText);

                flow.EndScrollDrag();
                Pump(150);
                Assert.False(flow.ScrollDragActive);
                Assert.False(flow.ScrollTicksVisible);
                Assert.True(string.IsNullOrEmpty(flow.ScrollPreviewCaption));
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void Preview_FollowsScrollPosition_AndTruncatesLongInput()
        {
            using var flow = new ChatFlowControl
            {
                Size = new Size(900, 500),
                ChatFont = new Font("Microsoft YaHei UI", 9F)
            };
            using var host = new Form { Size = new Size(940, 560) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                string longInput = string.Join("", Enumerable.Repeat("很长的用户输入", 40));   // 280 字
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", longInput),
                    Assistant = new ChatMessage("assistant", string.Concat(Enumerable.Repeat("撑高正文。\n", 12)))
                });
                for (int i = 0; i < 10; i++)
                    book.AddTurn(new ChatTurn
                    {
                        User = new ChatMessage("user", $"第 {i + 2} 个问题"),
                        Assistant = new ChatMessage("assistant", string.Concat(Enumerable.Repeat("撑高正文。\n", 12)))
                    });
                flow.Bind(book);
                Pump(400);

                var bar = Bar(flow);
                flow.BeginScrollDrag();
                Pump(120);

                // 回到顶部：浮窗换成第一轮，且长输入被截断
                bar.Value = 0;
                Pump(200);
                Assert.Equal("1/11", flow.ScrollPreviewCaption);
                string first = flow.ScrollPreviewText;
                Assert.True(first.Length <= 61, $"浮窗文本没截断：{first.Length} 字");
                Assert.EndsWith("…", first);

                // 拖到末尾：浮窗对准离当前位置最近的那一轮（末轮比视口高时可能不是最后一轮，按同一判据核对）
                int maxScroll = Math.Max(0, bar.Maximum - bar.LargeChange + 1);
                bar.Value = maxScroll;
                Pump(200);
                var offsets = flow.ScrollTickOffsets;
                int expected = 0;
                int bestDelta = int.MaxValue;
                for (int i = 0; i < offsets.Count; i++)
                {
                    int delta = Math.Abs(bar.Value - Math.Min(offsets[i], maxScroll));
                    if (delta >= bestDelta) continue;   // 相等时取靠前那一轮
                    bestDelta = delta;
                    expected = i;
                }
                Assert.Equal($"{expected + 1}/{offsets.Count}", flow.ScrollPreviewCaption);
                Assert.Contains($"第 {expected + 1} 个问题", flow.ScrollPreviewText);
                flow.EndScrollDrag();
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void Drag_NearTick_SnapsToThatTurnHead()
        {
            using var flow = new ChatFlowControl
            {
                Size = new Size(900, 500),
                ChatFont = new Font("Microsoft YaHei UI", 9F)
            };
            using var host = new Form { Size = new Size(940, 560) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                flow.Bind(Book(16));
                Pump(500);

                var bar = Bar(flow);
                Assert.True(bar.Visible);
                int max = Math.Max(0, bar.Maximum - bar.LargeChange + 1);
                var offsets = flow.ScrollTickOffsets.Where(o => o > 0 && o < max - 1).ToList();
                Assert.True(offsets.Count > 2);

                // 没在拖动：程序设值不受磁吸影响
                bar.Value = offsets[1] + 3;
                Pump(200);
                int plain = bar.Value;

                // 拖动中靠近某条刻线（3 像素内）：贴到那一轮头部
                flow.BeginScrollDrag();
                int tick = flow.ScrollTickOffsets.Where(o => o > 0 && o < max - 1).ElementAt(2);
                bar.Value = Math.Min(max, tick + 3);
                Pump(300);
                Assert.Contains(bar.Value, flow.ScrollTickOffsets);
                Assert.EndsWith("/16", flow.ScrollPreviewCaption);
                flow.EndScrollDrag();
                Pump(120);
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void Overlays_SitLeftOfTheScrollBar_AndSurviveDarkTheme()
        {
            using var flow = new ChatFlowControl
            {
                Size = new Size(900, 500),
                ChatFont = new Font("Microsoft YaHei UI", 9F)
            };
            using var host = new Form { Size = new Size(940, 560) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                flow.Bind(Book(12));
                Pump(400);
                var bar = Bar(flow);
                flow.BeginScrollDrag();
                Pump(100);

                var ticks = (Control)Field(flow, "scrollTicks");
                var preview = (Control)Field(flow, "scrollPreview");
                Assert.True(ticks.Visible && preview.Visible);
                // 刻度槽紧贴滚动条左侧（不盖住原生滚动条：箭头/滑块照常可用）
                Assert.Equal(bar.Left - ticks.Width, ticks.Left);
                Assert.Equal(bar.Bounds.Height, ticks.Height);
                Assert.True(ticks.Width > 0 && ticks.Width <= 16, $"刻度槽宽度异常：{ticks.Width}");
                // 浮窗在刻度槽左侧，且不越出控件
                Assert.True(preview.Right <= ticks.Left, $"浮窗压住刻度槽：preview.Right={preview.Right} ticks.Left={ticks.Left}");
                Assert.True(preview.Left >= 0 && preview.Bottom <= flow.ClientSize.Height);

                // 深色主题下重绘不炸、几何不变
                ChatTheme.Set(TestPalette.Dark);
                try
                {
                    bar.Value = 0;
                    Pump(150);
                    Assert.True(ticks.Visible && preview.Visible);
                }
                finally
                {
                    ChatTheme.Set(null);
                }
                flow.EndScrollDrag();
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void CompactPreview_CollapsesWhitespaceAndHandlesEmpty()
        {
            Assert.Equal("a b c", ChatFlowControl.CompactPreview("a\n\n  b\tc\n", 40));
            Assert.Equal("abcdef…", ChatFlowControl.CompactPreview("abcdefghij", 6));
            Assert.Equal("（无用户输入）", ChatFlowControl.CompactPreview("   ", 40));
            Assert.Equal("（无用户输入）", ChatFlowControl.CompactPreview(null, 40));
        }
    }
}
