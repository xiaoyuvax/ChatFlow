using System.Collections;
using System.Drawing;
using System.Reflection;
using Xunit;

namespace ChatFlow.Tests
{
    [Collection("ThemeState")]
    public class MarkdownTailProbe
    {
        private const string Answer = """
对，但要精确两点。

**1. 不是「整条输出是一个 JSON」，是「整条消息是结构化对象」**

`content`（正文文本）和 `tool_calls`（数组）是同一条 assistant 消息里的两个字段，可以同时存在。Claude Code 经常一条消息里先说「我先看一下」再挂一个 `tool_use`，正文和调用是并列的，不是二选一。所以「按 JSON 解析整条输出」也不准确——客户端拿到的是已经解析好的对象，连 JSON.parse 都不用做（OpenAI 的 `arguments` 是例外，见下）。

**2. OpenAI 的 `arguments` 仍然是 JSON 字符串**

```json
"function": {"name": "read_file", "arguments": "{\"path\":\"a.txt\"}"}
```
这一步还是要 parse。所以差别不是「有没有 JSON 解析」，而是三件事：

| | 我们 | 结构化通道 |
|---|---|---|
| **边界谁切** | 我们从自由文本里找 `tool-call:` / `<invoke>` 的起点终点，靠括号配对猜 | API 给定字段，天然有边界 |
| **谁保证语法合法** | 模型自由发挥，可能多一个 `}`、漏一个标签 | 服务端按 schema 约束生成，写不坏 |
| **调用↔结果怎么配对** | 靠 `tool-result:` 塞回 user 消息，只能靠内容猜 | `call_id` / `tool_use_id` 精确对应 |

**3. 所以「文本协议容易出错」要改一个字：不是容易错，是错了没法区分**

结构化通道里出现坏 JSON，几乎只可能是 `finish_reason=length` 被截断——一种原因，一种处理（续写/加预算）。文本协议里坏 JSON 有五种可能：模型写坏、被截断、模板 token 泄漏、网关吞了开头、模型在讲解协议而不是调用。五种要靠启发式分辨，这才是我们那 700 行解析器的由来。

这次的现场正是第一种+第三种叠加：模型吐的是它训练时的原生模板（`<｜｜DSML｜｜> invoke`），网关没按该模型模板解析成结构，于是当成正文漏出来；而它写 args 时又多了一个 `}`。**我们是在客户端逆向还原一个本来不该由我们解析的东西**——能修，但每换一个模型就得追一次装饰前缀。
""";

        private static IEnumerable<(Control Box, string Source)> Blocks(ChatMessageRow row)
        {
            var field = typeof(ChatMessageRow).GetField("_blocks", BindingFlags.NonPublic | BindingFlags.Instance)!;
            foreach (var block in (IEnumerable)field.GetValue(row)!)
            {
                var type = block.GetType();
                var prose = type.GetField("Prose")!.GetValue(block) as Control;
                var code = type.GetField("Code")!.GetValue(block) as Control;
                var source = type.GetField("ProseSource")!.GetValue(block) as string;
                if (prose != null) yield return (prose, source);
                if (code != null) yield return (code, null);
            }
        }

        [Theory]
        [InlineData(1200)]
        [InlineData(700)]
        [InlineData(420)]
        public void TailIsNotClipped(int width)
        {
            using var flow = new ChatFlowControl();
            using var host = new Form { Size = new Size(1400, 900) };
            using var row = new ChatMessageRow(flow);
            host.Controls.Add(row);
            host.Show();
            try
            {
                var turn = new ChatTurn
                {
                    User = new ChatMessage("user", "问题"),
                    Assistant = new ChatMessage("assistant", Answer),
                };
                var item = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
                row.Height = row.Bind(item, flow.ChatFont, width);
                row.Width = width;
                Application.DoEvents();
                Application.DoEvents();

                Assert.True(row.IsMarkdownRendered);
                var report = new List<string>();
                var blocks = Blocks(row).ToList();
                int bottom = 0;
                for (int i = 0; i < blocks.Count; i++)
                {
                    var (box, source) = blocks[i];
                    if (box is CDS.Markdown.MarkdownTextBox prose)
                    {
                        int need = prose.GetPreferredContentHeight();
                        report.Add($"prose need={need} got={box.Height} top={box.Top} chars={prose.TextLength} tail={prose.Text[^Math.Min(12, prose.TextLength)..]}");
                        if (i == blocks.Count - 1)
                            Assert.True(prose.Text.TrimEnd().EndsWith("每换一个模型就得追一次装饰前缀。"), "文本被丢掉：" + prose.Text[^40..]);
                        Assert.True(box.Height >= need, $"w={width} 散文被裁：{string.Join(" | ", report)}");
                    }
                    else
                    {
                        report.Add($"code need? got={box.Height} top={box.Top}");
                    }
                    bottom = Math.Max(bottom, box.Top + box.Height);
                }
                Assert.True(bottom <= row.Height + 2, $"w={width} 行高不足：bottom={bottom} rowH={row.Height} :: {string.Join(" | ", report)}");
            }
            finally
            {
                host.Hide();
                host.Dispose();
            }
        }
    }
}
