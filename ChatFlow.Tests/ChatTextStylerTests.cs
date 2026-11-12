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
        public void Thinking_MarkdownBlocksAreFoldableButExpanded()
        {
            // 思维链里的 markdown 块（围栏代码、引用块）要能被折叠，但默认展开
            using var box = NewBox();
            string text = "先看仓库。\n\n```cs\nvar a = 1;\nvar b = 2;\n```\n\n> 引用一\n> 引用二\n> 引用三";
            box.Text = text;
            ChatTextStyler.ApplyThinking(box, text);

            Assert.True(ChatTextStyler.IsFoldStart(box, 2), "围栏代码块应可折叠");
            Assert.True(ChatTextStyler.IsFoldEnd(box, 5), "围栏代码块应有折叠终点");
            Assert.True(ChatTextStyler.IsFoldStart(box, 7), "引用块应可折叠");
            Assert.True(ChatTextStyler.IsFoldEnd(box, 9), "引用块应有折叠终点");
            Assert.Equal(VisibleState.Visible, box.GetVisibleState(3));
            Assert.Equal(VisibleState.Visible, box.GetVisibleState(8));
        }

        [StaFact]
        public void Thinking_FencedCode_GetsLanguageSyntax_AndStaysExpanded()
        {
            // 思维链围栏内的代码按 info 语言着色（FCTB 内置高亮），并且默认展开、可手动折叠
            using var box = NewBox();
            string text = "先想一下。\n```cs\nvar a = 1;\n```";
            box.Text = text;
            ChatTextStyler.ApplyThinking(box, text);

            var keyword = box.SyntaxHighlighter.KeywordStyle;
            Assert.NotNull(keyword);
            Assert.True(HasStyle(box[2], 0, keyword), "围栏内的 C# 关键字未按语言着色");
            Assert.Equal(VisibleState.Visible, box.GetVisibleState(2));
            Assert.True(ChatTextStyler.IsFoldStart(box, 1), "围栏仍应可手动折叠");
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
        public void Thinking_NativeToolMarker_KeepsNameStyled()
        {
            // 原生调用带 > 标记：标记本身不染色，工具名和方括号照旧
            using var box = NewBox();
            string text = " >> > Shell[command=dir] · 1.2s";
            box.Text = text;
            ChatTextStyler.ApplyThinking(box, text);

            Assert.True(HasStyle(box[0], text.IndexOf("Shell", StringComparison.Ordinal), ChatTextStyler.ToolCallStyleField),
                "> 前缀下的工具名未着色");
            Assert.True(HasStyle(box[0], text.IndexOf('[', StringComparison.Ordinal), ChatTextStyler.ToolCallStyleField));
            Assert.True(HasStyle(box[0], text.LastIndexOf(']'), ChatTextStyler.ToolCallStyleField));
            Assert.False(HasStyle(box[0], text.IndexOf("> Shell", StringComparison.Ordinal), ChatTextStyler.ToolCallStyleField),
                "> 标记不该染色");
        }

        [StaFact]
        public void Thinking_NewToolLine_DoesNotStartFoldBlock()
        {
            // 工具行后面紧跟正文时，不能被当成折叠区起点（会把后续正文吞进折叠块）
            using var box = NewBox();
            string text = " >> Shell[command=git push] · 5.2s\nPush succeeded. Now tag and release.\nWrite notes file.";
            box.Text = text;
            ChatTextStyler.ApplyThinking(box, text);

            Assert.False(ChatTextStyler.IsFoldStart(box, 0), "新格式工具行不该形成折叠区");
            Assert.False(ChatTextStyler.IsFoldStart(box, 1), "正文不该被拖进折叠区");
        }

        [StaFact]
        public void Thinking_ToolLineWithTruncatedQuotedArgs_DoesNotFoldFollowingProse()
        {
            // 工具行参数压平成一行时会被截断（…），常留半边引号；方括号扫描不能顺着它把后面的正文吞成折叠块
            using var box = NewBox();
            string text =
                " >> > Shell[command=powershell -NoProfile -Command \"Get-ChildItem -Path . -Filter appsettings*.json -Recurse -Depth 2 | Select-Objec…] · 1.1s\n" +
                "\n" +
                "The tool-call line apparently didn't get executed (no tool-result came back; instead my shell call ran).\n" +
                "So Puppet must be reached via HTTP from the shell: zcurl. Let's find the key name in appsettings.json.\n" +
                "\n" +
                " >> > Shell[command=powershell -NoProfile -Command \"$j=[IO.File]::ReadAllText('appsettings.json',[Text.Encoding]::UTF8) | ConvertFro…] · 808ms\n" +
                "继续正文";
            box.Text = text;
            ChatTextStyler.ApplyThinking(box, text);

            for (int i = 0; i < box.LinesCount; i++)
                Assert.False(ChatTextStyler.IsFoldStart(box, i), $"第 {i} 行不该成为折叠区起点");
            Assert.Equal(VisibleState.Visible, box.GetVisibleState(2));
        }

        [StaFact]
        public void Thinking_NewToolLine_StylesOnlyNameAndBrackets()
        {
            using var box = NewBox();
            string text = " >> Shell[command=git status --short] · 1.2s";
            box.Text = text;
            ChatTextStyler.ApplyThinking(box, text);

            var chars = box[0];
            int name = text.IndexOf("Shell", StringComparison.Ordinal);
            int open = text.IndexOf('[', StringComparison.Ordinal);
            int close = text.LastIndexOf(']');
            for (int i = name; i <= open; i++)
                Assert.True(HasStyle(chars, i, ChatTextStyler.ToolCallStyleField), $"第 {i} 个字符没染色：{text[i]}");
            Assert.True(HasStyle(chars, close, ChatTextStyler.ToolCallStyleField), "右方括号没染色");
            // 前缀、参数、耗时保持正文色
            Assert.False(HasStyle(chars, 1, ChatTextStyler.ToolCallStyleField), ">> 前缀不该染色");
            Assert.False(HasStyle(chars, open + 1, ChatTextStyler.ToolCallStyleField), "参数不该染色");
            Assert.False(HasStyle(chars, close + 1, ChatTextStyler.ToolCallStyleField), "耗时不染色");
        }

        [StaFact]
        public void Thinking_PlainFunctionCall_IsNotStyledAsTool()
        {
            // 正文里的普通函数调用不能当工具行：此前整行被当成工具调用染色
            using var box = NewBox();
            string text = "Application.SetHighDpiMode(HighDpiMode.SystemAware);";
            box.Text = text;
            ChatTextStyler.ApplyThinking(box, text);

            Assert.False(HasStyle(box[0], 0, ChatTextStyler.ToolCallStyleField), "普通函数调用被误判成工具行");
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

        [StaFact]
        public void Thinking_TimelineToolLine_GetsToolStyle()
        {
            // 时间线工具行（RunTimeline.Render）：形如 ` >> Tool[args] · 1ms`
            using var box = NewBox();
            string text = "先看仓库。\n\n >> ReadFile[offset=1, path=src/A.cs] · 1ms";
            box.Text = text;
            ChatTextStyler.ApplyThinking(box, text);

            string line = " >> ReadFile[offset=1, path=src/A.cs] · 1ms";
            Assert.True(HasStyle(box[2], line.IndexOf("ReadFile", StringComparison.Ordinal), ChatTextStyler.ToolCallStyleField),
                "时间线工具行的工具名未着色");
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
        public void CollectLinks_NonImagePath_IsFileLink()
        {
            var link = Assert.Single(ChatTextStyler.CollectLinks("配置文件 J:/story/x.json 已更新"));
            Assert.Equal(ChatTextStyler.ChatLinkKind.File, link.Kind);
            Assert.Equal("J:/story/x.json", link.Target);
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
        public void CollectLinks_BuildError_ParenNotPartOfPath()
        {
            // 截图里的形态：路径后紧跟 (行,列): 报错位置——左括号不能算进路径（')' 本就已终止）
            var link = Assert.Single(ChatTextStyler.CollectLinks(
                @"FAIL build C:\Users\me\repos\OpenLoop\src\OpenLoop.A\OpenLoop.A.csproj(11,14): error MSB4025"));
            Assert.Equal(ChatTextStyler.ChatLinkKind.File, link.Kind);
            Assert.Equal(@"C:\Users\me\repos\OpenLoop\src\OpenLoop.A\OpenLoop.A.csproj", link.Target);
        }

        [Fact]
        public void CollectLinks_ParenFolderFollowedBySeparator_KeptWhole()
        {
            // ")" 后紧跟分隔符 = 括号是真实目录的一部分（Program Files (x86)\…），不得在 "(" 处断开
            var link = Assert.Single(ChatTextStyler.CollectLinks(@"已安装到 C:\Program Files (x86)\app\x.exe 里"));
            Assert.Equal(ChatTextStyler.ChatLinkKind.File, link.Kind);
            Assert.Equal(@"C:\Program Files (x86)\app\x.exe", link.Target);
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
        public void CollectLinks_ExtensionlessToolPath_DoesNotBacktrackExponentially()
        {
            // 回归：ListDir(path=C:/Users/.../OpenLoop) 这类无扩展名长路径曾让 CollectLinks 回溯爆炸（UI 卡死数分钟）
            var sw = System.Diagnostics.Stopwatch.StartNew();
            var links = ChatTextStyler.CollectLinks("[工具] ListDir(path=C:/Users/Xiaoyu/source/repos/OpenLoop) · 5ms");
            sw.Stop();

            Assert.Empty(links);
            Assert.True(sw.ElapsedMilliseconds < 5000, $"CollectLinks 耗时 {sw.ElapsedMilliseconds}ms");
        }

        [Fact]
        public void CollectLinks_PathFollowedByCommand_StopsAtTheSpace()
        {
            // 末尾空格不属于路径：尾巴是命令行而不是路径
            var link = Assert.Single(ChatTextStyler.CollectLinks(
                @"运行 C:\Users\Xiaoyu\source\repos\ChatFlow\app.json status --short 结束"));
            Assert.Equal(@"C:\Users\Xiaoyu\source\repos\ChatFlow\app.json", link.Target);
        }

        [Fact]
        public void CollectLinks_QuotedPath_WithSpace_KeptWhole()
        {
            var link = Assert.Single(ChatTextStyler.CollectLinks("\"C:\\Users\\me\\My Docs\\x.txt\" 已生成"));
            Assert.Equal(@"C:\Users\me\My Docs\x.txt", link.Target);
        }

        [Fact]
        public void CollectLinks_ParenthesizedPath_WithSpace_KeptWhole()
        {
            var link = Assert.Single(ChatTextStyler.CollectLinks("见 (C:\\Users\\me\\My Docs\\x.txt) 说明"));
            Assert.Equal(@"C:\Users\me\My Docs\x.txt", link.Target);
        }

        [Fact]
        public void CollectLinks_Url_StopsAtIllegalChars_AndDropsTrailingPunctuation()
        {
            var links = ChatTextStyler.CollectLinks("见 https://venice.ai/x，另见 https://venice.ai/y.");
            Assert.Equal(2, links.Count);
            Assert.Equal("https://venice.ai/x", links[0].Target);
            Assert.Equal("https://venice.ai/y", links[1].Target);
        }

        [StaFact]
        public void Body_PathFollowedByCommand_DoesNotStyleTheCommand()
        {
            using var box = NewBox();
            string text = @"运行 C:\Users\Xiaoyu\source\repos\ChatFlow status --short 结束";
            box.Text = text;
            ChatTextStyler.ApplyBody(box, text);

            var line = box[0];
            string lineText = box.GetLineText(0);
            Assert.True(HasStyle(line, lineText.IndexOf("ChatFlow", StringComparison.Ordinal), ChatTextStyler.PathStyleField),
                "路径本体未着色");
            Assert.False(HasStyle(line, lineText.IndexOf("status", StringComparison.Ordinal), ChatTextStyler.PathStyleField),
                "尾部命令被误判为路径");
            Assert.False(HasStyle(line, lineText.IndexOf("--short", StringComparison.Ordinal), ChatTextStyler.PathStyleField),
                "尾部参数被误判为路径");
        }

        [StaFact]
        public void Body_FencedPathWithSpace_IsStyledWithoutTheFence()
        {
            using var box = NewBox();
            string text = "见 \"C:\\Users\\me\\My Docs\" 里";
            box.Text = text;
            ChatTextStyler.ApplyBody(box, text);

            var line = box[0];
            string lineText = box.GetLineText(0);
            Assert.True(HasStyle(line, lineText.IndexOf("My Docs", StringComparison.Ordinal), ChatTextStyler.PathStyleField),
                "围栏内带空格的路径未着色");
            Assert.False(HasStyle(line, lineText.IndexOf('"'), ChatTextStyler.PathStyleField), "引号不应算作路径");
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
