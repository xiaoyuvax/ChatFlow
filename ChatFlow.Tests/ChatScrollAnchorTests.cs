using System.Collections;
using System.Reflection;
using ChatFlow;
using Xunit;
using WF = System.Windows.Forms;

namespace ChatFlow.Tests
{
    /// <summary>
    /// 用户往上翻历史时，后台刷新（流式增量、宿主整表刷新）不得把视口弹走：
    /// 之前流式更新发现「末尾行不在视口」就整表重建，清空高度缓存后又按估算重排，
    /// 每个 250ms 把用户眼前的正文弹到别处，选区也跟着丢。
    /// </summary>
    [Collection("ThemeState")]
    public class ChatScrollAnchorTests
    {
        private static string Body(int lines)
            => string.Concat(Enumerable.Repeat(
                "这是一段较长的正文，用来验证用户往上翻历史时后台刷新不会把视口弹走。\n", lines));

        /// <summary>长短悬殊的会话：估算与实测比例差得远，重建清缓存后位置会明显跑偏。</summary>
        private static readonly int[] Lengths = { 2, 60, 3, 25, 2, 90, 4 };

        private static ChatTurnBook Book(out ChatTurn streaming)
        {
            var book = new ChatTurnBook();
            for (int i = 0; i < Lengths.Length; i++)
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", "问题 " + i),
                    Assistant = new ChatMessage("assistant", Body(Lengths[i]))
                });
            streaming = new ChatTurn
            {
                User = new ChatMessage("user", "最后一个问题"),
                Assistant = new ChatMessage("assistant", Body(50)),
                Streaming = true
            };
            book.AddTurn(streaming);
            return book;
        }

        private static object Field(object target, string name)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

        private static int TopTurn(ChatFlowControl flow)
        {
            var rows = ((IDictionary)Field(flow, "realized")).Values.Cast<ChatMessageRow>()
                .Where(r => r.Visible).ToList();
            var row = rows.Where(r => r.Top <= 1 && r.Top + r.Height > 1)
                .OrderByDescending(r => r.Top).FirstOrDefault();
            return row?.Item == null ? -1 : flow.IndexOfTurn(row.Item.Turn);
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

        [StaFact]
        public void StreamingUpdates_WhileScrolledUp_KeepViewport()
        {
            using var flow = new ChatFlowControl
            {
                Size = new System.Drawing.Size(900, 600),
                ChatFont = new System.Drawing.Font("Microsoft YaHei UI", 9F)
            };
            using var host = new WF.Form { Size = new System.Drawing.Size(940, 660) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                flow.Bind(Book(out var streaming));
                Pump(500);
                var vbar = (WF.VScrollBar)Field(flow, "vbar");
                Assert.True(vbar.Visible);

                // 用户往上翻到中段
                vbar.Value = (vbar.Maximum - vbar.LargeChange + 1) / 2;
                Pump(300);
                int top = TopTurn(flow);
                Assert.True(top >= 0);

                for (int i = 0; i < 6; i++)
                {
                    streaming.AssistantText = Body(50) + Body((i + 1) * 3);
                    flow.UpdateStreamingMessage(streaming, streaming.AssistantText);
                    Pump(300);
                    Assert.Equal(top, TopTurn(flow));
                }
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void StreamingText_IsAppliedAndKeepsViewport()
        {
            using var flow = new ChatFlowControl
            {
                Size = new System.Drawing.Size(900, 600),
                ChatFont = new System.Drawing.Font("Microsoft YaHei UI", 9F)
            };
            using var host = new WF.Form { Size = new System.Drawing.Size(940, 660) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                // 宿主（Loop/旁路）只在收尾时写 turn.AssistantText：流式正文得由 UpdateStreamingMessage 落到数据上
                var book = Book(out var streaming);
                streaming.AssistantText = "";
                flow.Bind(book);
                Pump(500);
                var vbar = (WF.VScrollBar)Field(flow, "vbar");
                vbar.Value = (vbar.Maximum - vbar.LargeChange + 1) / 2;
                Pump(300);
                int top = TopTurn(flow);

                string text = "";
                for (int i = 0; i < 6; i++)
                {
                    text += Body(3);
                    flow.UpdateStreamingMessage(streaming, text);
                    Pump(300);
                    Assert.Equal(top, TopTurn(flow));
                }

                var items = (IEnumerable<ChatFlowItem>)Field(flow, "items");
                var item = items.LastOrDefault(i => i.Turn == streaming && i.Kind == ChatFlowItemKind.Assistant);
                Assert.NotNull(item);
                Assert.Equal(text, item.Text);
            }
            finally
            {
                host.Hide();
            }
        }

        /// <summary>
        /// 用户停在「正在写的那一轮」里阅读（没到底，显示「跳到末尾」）：这一轮正文继续变长时，
        /// 整表刷新不得按比例还原 —— 按比例会让视口随着这一轮变高一直往下滑（用户侧：文字一直在滚）。
        /// </summary>
        [StaFact]
        public void StreamingTurnGrowth_WhilePinned_DoesNotMoveText()
        {
            using var flow = new ChatFlowControl
            {
                Size = new System.Drawing.Size(900, 600),
                ChatFont = new System.Drawing.Font("Microsoft YaHei UI", 9F)
            };
            using var host = new WF.Form { Size = new System.Drawing.Size(940, 660) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = Book(out var streaming);
                flow.Bind(book);
                Pump(500);
                var vbar = (WF.VScrollBar)Field(flow, "vbar");
                Assert.True(vbar.Visible);

                // 停在最后这一轮靠下的位置（不是末尾）
                int max = Math.Max(0, vbar.Maximum - vbar.LargeChange + 1);
                vbar.Value = Math.Max(0, max - 300);
                Pump(300);
                Assert.False(flow.AtEnd, "测试前提：视口应停在末尾上方");
                Assert.True(((WF.Control)Field(flow, "jumpToEndButton")).Visible, "测试前提：应显示「跳到末尾」");

                int pinned = vbar.Value;
                for (int i = 0; i < 5; i++)
                {
                    streaming.AssistantText = Body(50) + Body((i + 1) * 10);
                    book.RaiseChanged();   // 宿主整表刷新（结构变化）
                    Pump(300);
                    Assert.True(Math.Abs(vbar.Value - pinned) <= 2,
                        $"第 {i + 1} 次刷新后视口漂移：{pinned} → {vbar.Value}");
                }
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void Streaming_WhileFollowing_KeepsJumpButtonHidden()
        {
            using var flow = new ChatFlowControl
            {
                Size = new System.Drawing.Size(900, 600),
                ChatFont = new System.Drawing.Font("Microsoft YaHei UI", 9F)
            };
            using var host = new WF.Form { Size = new System.Drawing.Size(940, 660) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = Book(out var streaming);
                flow.Bind(book);
                Pump(500);
                var button = (WF.Control)Field(flow, "jumpToEndButton");
                Assert.True(flow.AtEnd);
                Assert.False(button.Visible, "跟在末尾时不该显示「跳到末尾」");

                for (int i = 0; i < 5; i++)
                {
                    streaming.AssistantText = Body(50) + Body((i + 1) * 10);
                    book.RaiseChanged();
                    Pump(300);
                    Assert.True(flow.AtEnd, $"第 {i + 1} 次刷新后没跟到末尾");
                    Assert.False(button.Visible, $"第 {i + 1} 次刷新时「跳到末尾」闪了一下");
                }
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void FullRebuild_WhileScrolledUp_KeepsViewport()
        {
            using var flow = new ChatFlowControl
            {
                Size = new System.Drawing.Size(900, 600),
                ChatFont = new System.Drawing.Font("Microsoft YaHei UI", 9F)
            };
            using var host = new WF.Form { Size = new System.Drawing.Size(940, 660) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = Book(out _);
                flow.Bind(book);
                Pump(500);
                var vbar = (WF.VScrollBar)Field(flow, "vbar");
                vbar.Value = (vbar.Maximum - vbar.LargeChange + 1) / 2;
                Pump(300);
                int top = TopTurn(flow);
                Assert.True(top > 0);

                book.RaiseChanged();   // 宿主整表刷新（结构变化）
                Pump(300);

                Assert.Equal(top, TopTurn(flow));
            }
            finally
            {
                host.Hide();
            }
        }
        [StaFact]
        public void FullRebuild_InsideSplitLongBody_DoesNotJumpToBodyStart()
        {
            using var flow = new ChatFlowControl
            {
                Size = new System.Drawing.Size(900, 600),
                ChatFont = new System.Drawing.Font("Microsoft YaHei UI", 9F)
            };
            using var host = new WF.Form { Size = new System.Drawing.Size(940, 660) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                // 超过 MaxItemChars（1 万字）的正文会被切成多个分片；片段之间共用同一个 Turn，
                // 只按 (Turn, IsUser) 还原就会落到第一片上，视口被拽回正文开头。
                var book = new ChatTurnBook();
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", "长回答"),
                    Assistant = new ChatMessage("assistant", LongBody())
                });
                flow.Bind(book);
                Pump(600);

                var vbar = (WF.VScrollBar)Field(flow, "vbar");
                Assert.True(vbar.Visible);
                vbar.Value = (vbar.Maximum - vbar.LargeChange + 1) / 2;
                Pump(400);

                var before = TopRow(flow);
                Assert.NotNull(before);
                Assert.True(before.Item.IsContinuation, "测试前提：视口应停在长正文的后续分片上");

                book.RaiseChanged();   // 宿主整表刷新（结构变化）
                Pump(400);

                var after = TopRow(flow);
                Assert.NotNull(after);
                Assert.True(after.Item.IsContinuation, "整表刷新后视口被弹回正文开头");
            }
            finally
            {
                host.Hide();
            }
        }

        /// <summary>
        /// 只是拖动滚动条（没有任何刷新）：行高从估算变实测、markdown 降级/回升都会改前缀和，
        /// 视口必须跟着走，不能每到某一行就弹回前面。
        /// </summary>
        [StaFact]
        public void DraggingScrollbar_LongBody_DoesNotJumpBack()
        {
            using var flow = new ChatFlowControl
            {
                Size = new System.Drawing.Size(900, 600),
                ChatFont = new System.Drawing.Font("Microsoft YaHei UI", 9F)
            };
            using var host = new WF.Form { Size = new System.Drawing.Size(940, 660) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                for (int i = 0; i < 4; i++)
                    book.AddTurn(new ChatTurn
                    {
                        User = new ChatMessage("user", "问题 " + i),
                        Assistant = new ChatMessage("assistant", Body(6))
                    });
                book.AddTurn(new ChatTurn
                {
                    User = new ChatMessage("user", "长回答"),
                    Assistant = new ChatMessage("assistant", LongBody())
                });
                flow.Bind(book);
                Pump(600);

                var vbar = (WF.VScrollBar)Field(flow, "vbar");
                Assert.True(vbar.Visible);

                int floor = 0;
                for (int i = 0; i <= 24; i++)
                {
                    // 真实拖动时滚动条会被系统钳到当前范围：范围随高度变化，这里每步重取
                    int max = Math.Max(0, vbar.Maximum - vbar.LargeChange + 1);
                    vbar.Value = Math.Max(0, Math.Min(max, max * i / 24));
                    Pump(80);   // 比 markdown 回升还短：模拟连续拖动
                    int top = TopItemIndex(flow);
                    if (top < 0) continue;
                    // 允许 ±1（相邻行高度变化会让边界条目换一个），再大就是「弹回」
                    Assert.True(top >= floor - 1,
                        $"拖到 {i}/24 时视口弹回：top={top}，之前最高到 {floor}");
                    floor = Math.Max(floor, top);
                }
                Assert.True(floor > 6, $"没能滚进长正文里就没测到：floor={floor}");
            }
            finally
            {
                host.Hide();
            }
        }

        /// <summary>
        /// 长正文正常收尾：视口回到本轮正文开头（而不是停在末尾），用户能从头读；
        /// 正文不足一屏时没有可读的开头可言，按上限钳制后仍停在末尾。
        /// </summary>
        [StaFact]
        public void AnswerStart_LongTurn_ShowsBodyHeadAndStopsFollowing()
        {
            using var flow = new ChatFlowControl
            {
                Size = new System.Drawing.Size(900, 600),
                ChatFont = new System.Drawing.Font("Microsoft YaHei UI", 9F)
            };
            using var host = new WF.Form { Size = new System.Drawing.Size(940, 660) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                for (int i = 0; i < 3; i++)
                    book.AddTurn(new ChatTurn
                    {
                        User = new ChatMessage("user", "问题 " + i),
                        Assistant = new ChatMessage("assistant", Body(6))
                    });
                var longTurn = new ChatTurn
                {
                    User = new ChatMessage("user", "长回答"),
                    Assistant = new ChatMessage("assistant", LongBody())
                };
                book.AddTurn(longTurn);
                flow.Bind(book);
                Pump(600);
                Assert.True(flow.AtEnd, "测试前提：绑定后跟在末尾");

                flow.ScrollToAnswerStart(longTurn);
                Pump(400);

                var top = TopRow(flow);
                Assert.NotNull(top);
                Assert.Same(longTurn, top.Item.Turn);
                Assert.Equal(ChatFlowItemKind.Assistant, top.Item.Kind);
                Assert.False(flow.AtEnd, "正文开头已对齐视口顶部，不该还在末尾");
                Assert.True(((WF.Control)Field(flow, "jumpToEndButton")).Visible, "离开末尾后应显示「跳到末尾」");
            }
            finally
            {
                host.Hide();
            }
        }

        [StaFact]
        public void AnswerStart_ShortTurn_StaysAtEnd()
        {
            using var flow = new ChatFlowControl
            {
                Size = new System.Drawing.Size(900, 600),
                ChatFont = new System.Drawing.Font("Microsoft YaHei UI", 9F)
            };
            using var host = new WF.Form { Size = new System.Drawing.Size(940, 660) };
            host.Controls.Add(flow);
            host.Show();
            try
            {
                var book = new ChatTurnBook();
                for (int i = 0; i < 3; i++)
                    book.AddTurn(new ChatTurn
                    {
                        User = new ChatMessage("user", "问题 " + i),
                        Assistant = new ChatMessage("assistant", Body(6))
                    });
                var shortTurn = new ChatTurn
                {
                    User = new ChatMessage("user", "短回答"),
                    Assistant = new ChatMessage("assistant", Body(2))
                };
                book.AddTurn(shortTurn);
                flow.Bind(book);
                Pump(600);
                Assert.True(flow.AtEnd);

                flow.ScrollToAnswerStart(shortTurn);
                Pump(300);

                Assert.True(flow.AtEnd, "正文不足一屏时没有可读的开头，应留在末尾");
            }
            finally
            {
                host.Hide();
            }
        }

        private static int TopItemIndex(ChatFlowControl flow)
        {
            var items = (IList)Field(flow, "items");
            var row = TopRow(flow);
            return row?.Item == null ? -1 : items.IndexOf(row.Item);
        }

        private static string LongBody()
            => string.Concat(Enumerable.Range(0, 1200)
                .Select(i => $"这是第 {i:0000} 行正文，用来把长度顶过一万字。\n"));

        private static ChatMessageRow TopRow(ChatFlowControl flow)
            => ((IDictionary)Field(flow, "realized")).Values.Cast<ChatMessageRow>()
                .Where(r => r.Visible && r.Top <= 1 && r.Top + r.Height > 1)
                .OrderByDescending(r => r.Top)
                .FirstOrDefault();
    }
}
