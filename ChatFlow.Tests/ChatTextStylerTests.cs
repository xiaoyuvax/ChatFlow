using FastColoredTextBoxNS.Types;
using Xunit;
using ChatFlow;

namespace ChatFlow.Tests
{
    /// <summary>
    /// ChatTextStyler 的 STA UI 测试：真实 RowTextBox（FCTB）上验证 regex 样式与折叠标记。
    /// 锁定行为：markdown 强调/行内代码/URL/本地路径/标题着色、代码块围栏成对折叠（长块默认收起）、
    /// 思考区工具行/结果/输出/附件/🧠 样式、思考区块折叠到空行。
    /// 工具行现为「» #N tool(args) → 成功 · 420ms」（步号 + 耗时）。
    /// </summary>
    [Collection("ThemeState")]
    public class ChatTextStylerTests
    {
        private static RowTextBox NewBox()
            => new()
            {
                ReadOnly = true,
                ShowScrollBars = false,
                WordWrap = true,
                ShowLineNumbers = false,
            };

        [StaFact]
        public void Body_AppliesBoldItalicCodeUrlHeadingStyles()
        {
            using var box = NewBox();
            string text = "标题行如下：\n**重要加粗** 与 *轻斜体* 混排\n代码 `var x = 1;` 与链接 https://venice.ai/api\n# 一级标题";
            box.Text = text;
            ChatTextStyler.ApplyBody(box, text);

            var chars = box[1];
            string line2 = box.GetLineText(1);
            Assert.Contains("**", line2);
            Assert.True(HasStyle(chars, line2.IndexOf("**", StringComparison.Ordinal), ChatTextStyler.BoldStyleField), "粗体样式未生效");
            // 行内代码
            var line = box[2];
            string line3 = box.GetLineText(2);
            Assert.True(HasStyle(line, line3.IndexOf("`var", StringComparison.Ordinal), ChatTextStyler.InlineCodeStyleField), "行内代码样式未生效");
            // URL
            Assert.True(HasStyle(line, line3.IndexOf("https://", StringComparison.Ordinal), ChatTextStyler.LinkStyleField), "链接样式未生效");
            // 标题
            Assert.True(HasStyle(box[3], 0, ChatTextStyler.HeadingStyleField), "标题样式未生效");
        }

        [StaFact]
        public void Body_LocalPaths_GetPathStyle_UrlNotMistaken()
        {
            using var box = NewBox();
            string text = "结果文件：J:/My God/[NEW]/x.png 已保存\n另见 https://venice.ai/x";
            box.Text = text;
            ChatTextStyler.ApplyBody(box, text);

            string line0 = box.GetLineText(0);
            Assert.True(HasStyle(box[0], line0.IndexOf("J:/", StringComparison.Ordinal), ChatTextStyler.PathStyleField), "盘符路径未着色");
            // 路径内的空格不应截断着色（"My God"）
            Assert.True(HasStyle(box[0], line0.IndexOf("[NEW]", StringComparison.Ordinal), ChatTextStyler.PathStyleField), "路径中段未着色");
            // URL 里的 "s:/" 不能被误判成盘符路径
            string line1 = box.GetLineText(1);
            int urlAt = line1.IndexOf("https://", StringComparison.Ordinal);
            Assert.False(HasStyle(box[1], urlAt + 4, ChatTextStyler.PathStyleField), "URL 被误判为路径");
            Assert.True(HasStyle(box[1], urlAt, ChatTextStyler.LinkStyleField), "URL 链接样式未生效");
        }

        [StaFact]
        public void Body_CodeBlockFences_FormFoldingPairs()
        {
            using var box = NewBox();
            string text = "前文\n```python\nprint(1)\nprint(2)\n```\n后文";
            box.Text = text;
            ChatTextStyler.ApplyBody(box, text);

            Assert.True(ChatTextStyler.IsFoldStart(box, 1), "第 1 个 ``` 应为折叠开始");
            Assert.True(ChatTextStyler.IsFoldEnd(box, 4), "第 2 个 ``` 应为折叠结束");
            Assert.False(ChatTextStyler.IsFoldStart(box, 0));
            Assert.False(ChatTextStyler.IsFoldEnd(box, 5));
        }

        [StaFact]
        public void Body_LanguageBracketPrompt_NotCollapsedByDefault()
        {
            // 提示词语言括号段（[zh: … :zh]）即使被 ``` 围栏包住也默认展开：用户要一眼看全提示词
            using var box = NewBox();
            string text = "```[zh:\n人像摄影，主体@图1\n背面全身可见\n极简背景\n:zh]\n```";
            box.Text = text;
            ChatTextStyler.ApplyBody(box, text);

            Assert.True(ChatTextStyler.IsFoldStart(box, 0), "围栏仍留折叠标记（可手动收起）");
            Assert.Equal(VisibleState.Visible, box.GetVisibleState(0));
            Assert.Equal(VisibleState.Visible, box.GetVisibleState(2));
        }

        [StaFact]
        public void Body_BareLanguageBracket_FoldableButExpanded()
        {
            // 无围栏的 [zh: … :zh] 是方括号块：**允许手动折叠（有标记），但默认展开**
            using var box = NewBox();
            string text = "[zh:\n人像摄影\n全身可见\n:zh]";
            box.Text = text;
            ChatTextStyler.ApplyBody(box, text);

            Assert.True(ChatTextStyler.IsFoldStart(box, 0), "语言括号块应保留折叠标记（可手动收起）");
            Assert.Equal(VisibleState.Visible, box.GetVisibleState(0));
            Assert.Equal(VisibleState.Visible, box.GetVisibleState(1));
        }

        [StaFact]
        public void Body_LongCodeBlock_CollapsedByDefault()
        {
            // 够长的代码块默认收起（省空间）：起始 ``` 行变为隐藏块起点，内容行 Hidden
            using var box = NewBox();
            string text = "前文\n```json\n{\n  \"a\": 1,\n  \"b\": 2\n}\n```\n后文";
            box.Text = text;
            ChatTextStyler.ApplyBody(box, text);

            Assert.True(ChatTextStyler.IsFoldStart(box, 1));
            Assert.Equal(VisibleState.StartOfHiddenBlock, box.GetVisibleState(1));
            Assert.Equal(VisibleState.Hidden, box.GetVisibleState(2));
        }

        [StaFact]
        public void Body_ShortCodeBlock_NotCollapsed()
        {
            // 太短的块不默认折叠，但仍是可手动折叠的围栏对
            using var box = NewBox();
            string text = "前文\n```json\n{\"a\":1}\n```\n后文";
            box.Text = text;
            ChatTextStyler.ApplyBody(box, text);

            Assert.True(ChatTextStyler.IsFoldStart(box, 1));
            Assert.Equal(VisibleState.Visible, box.GetVisibleState(1));
            Assert.Equal(VisibleState.Visible, box.GetVisibleState(2));
        }

        [StaFact]
        public void Thinking_RawMultilineJsonOutput_CollapsedByDefault()
        {
            // 工具输出里的裸 JSON（无 ``` 围栏）也应默认折叠（此前只有围栏块会折）
            using var box = NewBox();
            string text = "» #2 puppet_registry() → 成功 · 14ms\n    输出: {\n      \"Total\": 3,\n      \"Instances\": [\n        {\"a\":1},\n        {\"b\":2}\n      ]\n    }";
            box.Text = text;
            ChatTextStyler.ApplyThinking(box, text);

            Assert.Equal(VisibleState.StartOfHiddenBlock, box.GetVisibleState(1));
            Assert.Equal(VisibleState.Hidden, box.GetVisibleState(2));
        }

        [StaFact]
        public void Thinking_ShortSingleLineJson_NotCollapsed()
        {
            using var box = NewBox();
            string text = "» #1 app_status() → 成功 · 2ms\n    输出: {\"Total\":3}";
            box.Text = text;
            ChatTextStyler.ApplyThinking(box, text);

            Assert.Equal(VisibleState.Visible, box.GetVisibleState(1));
        }

        [StaFact]
        public void Thinking_ToolLinesAndOutputAndAttachmentGetStyles()
        {
            using var box = NewBox();
            string text = "» image_edit(path=J:/x.png) → 失败\n    输出: 模型未返回\n【附件 ref.png】\n🧠 用户意图分析";
            box.Text = text;
            ChatTextStyler.ApplyThinking(box, text);

            Assert.True(HasStyle(box[0], 0, ChatTextStyler.ToolCallStyleField), "工具调用行样式未生效");
            string line0 = box.GetLineText(0);
            Assert.True(HasStyle(box[0], line0.IndexOf("失败", StringComparison.Ordinal), ChatTextStyler.ToolFailStyleField), "失败标记样式未生效");
            string line1 = box.GetLineText(1);
            Assert.True(HasStyle(box[1], line1.IndexOf("输出", StringComparison.Ordinal), ChatTextStyler.ToolOutputStyleField), "输出行样式未生效");
            Assert.True(HasStyle(box[2], 0, ChatTextStyler.AttachmentStyleField), "附件标题样式未生效");
            Assert.True(HasStyle(box[3], 0, ChatTextStyler.ReasoningMarkStyleField), "🧠 标记样式未生效");
        }

        [StaFact]
        public void Thinking_BlocksFoldUntilBlankLine()
        {
            using var box = NewBox();
            string text = "» tool_a(x=1) → 成功\n    输出: 数据行一\n    输出: 数据行二\n\n» tool_b(y=2) → 成功\n    输出: 独立块";
            box.Text = text;
            ChatTextStyler.ApplyThinking(box, text);

            Assert.True(ChatTextStyler.IsFoldStart(box, 0), "第一个工具块应折叠");
            Assert.True(ChatTextStyler.IsFoldEnd(box, 2), "块应折叠到空行前");
            Assert.False(ChatTextStyler.IsFoldStart(box, 3), "空行不是折叠开始");
            Assert.True(ChatTextStyler.IsFoldStart(box, 4), "空行后的第二个工具块独立折叠");
        }

        [StaFact]
        public void Restyle_ClearsOldStyles_NoDoubleApplication()
        {
            using var box = NewBox();
            string text = "纯文本一行";
            box.Text = text;
            ChatTextStyler.ApplyBody(box, text);
            ChatTextStyler.ApplyBody(box, text); // 二次应用：先清后加，不叠样式

            var chars = box[0];
            for (int i = 0; i < chars.Count; i++)
            {
                var c = chars[i];
                int styleCount = c.Styles?.Count(s => s != null) ?? 0;
                Assert.True(styleCount <= 1, $"字符 {i} 样式叠加（{styleCount} 个）");
            }
        }

        [StaFact]
        public void Thinking_NumberedToolLine_WithDuration_StillStyled()
        {
            // 新格式：» #3 tool(args) → 成功 · 420ms（步号 + 耗时不能破坏工具行样式与折叠锚点）
            using var box = NewBox();
            string text = "» #3 image_edit(path=J:/x.png) → 成功 · 420ms\n    输出: 已写入记录";
            box.Text = text;
            ChatTextStyler.ApplyThinking(box, text);

            Assert.True(HasStyle(box[0], 0, ChatTextStyler.ToolCallStyleField), "带步号的工具行样式未生效");
            string line0 = box.GetLineText(0);
            Assert.True(HasStyle(box[0], line0.IndexOf("成功", StringComparison.Ordinal), ChatTextStyler.ToolOkStyleField), "成功标记样式未生效");
            Assert.True(ChatTextStyler.IsFoldStart(box, 0), "带步号的工具块应可作为折叠首行");
        }

        private static bool HasStyle(Line line, int charIndex, Style style)
        {
            if (charIndex < 0 || line == null || charIndex >= line.Count) return false;
            var styles = line[charIndex].Styles;
            return styles != null && styles.Contains(style);
        }

        // ============ 可点击链接（URL / 图像文件路径） ============

        [Fact]
        public void CollectLinks_ImagePath_IsImageFile_WithFullTarget()
        {
            var links = ChatTextStyler.CollectLinks("结果文件：J:/My God/[NEW]/x.png 已保存");
            var link = Assert.Single(links);
            Assert.Equal(ChatTextStyler.ChatLinkKind.ImageFile, link.Kind);
            Assert.Equal("J:/My God/[NEW]/x.png", link.Target);
        }

        [Fact]
        public void CollectLinks_BackslashPath_WithSpace_KeptWhole()
        {
            var links = ChatTextStyler.CollectLinks(@"原型在 C:\My God\铁蛋.png 里");
            var link = Assert.Single(links);
            Assert.Equal(ChatTextStyler.ChatLinkKind.ImageFile, link.Kind);
            Assert.Equal(@"C:\My God\铁蛋.png", link.Target);
        }

        [Fact]
        public void CollectLinks_NonImagePath_IsNotClickable()
        {
            Assert.Empty(ChatTextStyler.CollectLinks("配置文件 J:/story/x.json 已更新"));
        }

        [Fact]
        public void CollectLinks_CjkFileName_And_CjkImmediatelyAfterPath_StillDetected()
        {
            // 中文紧跟路径时不能整句吞掉（这是 CJK 路径可点击的关键回归）
            var links = ChatTextStyler.CollectLinks("原型 J:/MY GOD/铁蛋.png已保存，另见 J:/人物包/铁蛋_1.1.png 原型图");
            Assert.Equal(2, links.Count);
            Assert.Equal("J:/MY GOD/铁蛋.png", links[0].Target);
            Assert.Equal("J:/人物包/铁蛋_1.1.png", links[1].Target);
        }

        [Fact]
        public void CollectLinks_Url_WinsOverImageExtension()
        {
            // URL 一律按 Url 处理（含图片 URL），不得被路径规则切碎
            var links = ChatTextStyler.CollectLinks("封面 https://venice.ai/x/cover.png 可看");
            var link = Assert.Single(links);
            Assert.Equal(ChatTextStyler.ChatLinkKind.Url, link.Kind);
            Assert.Equal("https://venice.ai/x/cover.png", link.Target);
        }

        [Fact]
        public void CollectLinks_Mixed_SortedByStartOffset()
        {
            string text = "图 J:/a.png 与页 https://venice.ai/doc";
            var links = ChatTextStyler.CollectLinks(text);

            Assert.Equal(2, links.Count);
            Assert.Equal(ChatTextStyler.ChatLinkKind.ImageFile, links[0].Kind);
            Assert.Equal(ChatTextStyler.ChatLinkKind.Url, links[1].Kind);
            Assert.True(links[0].Start < links[1].Start);
            Assert.Equal("J:/a.png", text.Substring(links[0].Start, links[0].Length));
            Assert.Equal("https://venice.ai/doc", text.Substring(links[1].Start, links[1].Length));
        }

        [Fact]
        public void LinkAt_CoversInterior_ButNotEdges()
        {
            string text = "看图 J:/a.png 结束";
            var links = ChatTextStyler.CollectLinks(text);
            int at = text.IndexOf("J:/", StringComparison.Ordinal);

            Assert.Equal("J:/a.png", ChatTextStyler.LinkAt(links, at)?.Target);
            Assert.Equal("J:/a.png", ChatTextStyler.LinkAt(links, at + 4)?.Target);
            Assert.Null(ChatTextStyler.LinkAt(links, at - 1));
            Assert.Null(ChatTextStyler.LinkAt(links, at + "J:/a.png".Length));
            Assert.Null(ChatTextStyler.LinkAt(links, -1));   // FCTB 越界点（PointToPosition 返回 -1）
            Assert.Null(ChatTextStyler.LinkAt(null, 3));
        }

        [StaFact]
        public void Body_PopulatesLinkSpans_OnRealBox()
        {
            using var box = NewBox();
            string text = "结果：J:/My God/x.png 已保存；见 https://venice.ai/doc";
            box.Text = text;
            ChatTextStyler.ApplyBody(box, text);

            Assert.Equal(2, box.LinkSpans.Count);
            var (img, url) = (box.LinkSpans[0], box.LinkSpans[1]);
            Assert.Equal(ChatTextStyler.ChatLinkKind.ImageFile, img.Kind);
            Assert.Equal("J:/My God/x.png", text.Substring(img.Start, img.Length));
            Assert.Equal(ChatTextStyler.ChatLinkKind.Url, url.Kind);
            Assert.Equal("https://venice.ai/doc", text.Substring(url.Start, url.Length));
        }

        [Fact]
        public void ExtractPromptText_NegativeInsideBracketBlock_AppearsOnce()
        {
            string text = "[zh: 1girl, solo\n负面提示词：lowres, bad hands\n:zh]";

            Assert.Equal(text, ChatTextStyler.ExtractPromptText(text));
        }

        [Fact]
        public void ExtractPromptText_NegativeAfterBracketBlock_IsIncludedOnce()
        {
            string text = "[zh: 1girl, solo\n:zh]\n负面提示词：lowres, bad hands";

            Assert.Equal(text, ChatTextStyler.ExtractPromptText(text));
        }
    }
}
