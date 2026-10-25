# ChatFlow

**WinForms 虚拟化聊天流式视图控件**：只实例化可见区域附近的行，滚动时复用离屏行控件，长对话与逐 token 流式输出都不卡。

> [!WARNING]
> 项目正在开发中，API 与行为可能随时调整，暂不保证兼容性。

- 源码：<https://github.com/xiaoyuvax/ChatFlow>
- 镜像：<https://gitee.com/xiaoyuvax/ChatFlow>

## 特性

- **虚拟化**：按条目维护高度缓存与位置前缀和，只为视口 ± 一屏缓冲实例化 `ChatMessageRow`，其余回收进池复用
- **流式输出**：`UpdateStreamingMessage` 高频调用按 250ms 节流；已实例化行就地重绑，避免整表重建闪烁
- **执行中指示**：最后一轮 `Streaming` 时，聊天末尾常驻「思考中…」动画行（旋转弧线 + 胶囊底），正文开始流式后仍保留，执行结束后自动移除
- **思考区**：推理文本与工具调用轨迹汇总到可折叠区，折叠头显示步数与总耗时；流式期间显示「正在调用 X…」实时状态
- **正文样式**：粗体、斜体、行内代码、标题、URL、本地图片路径着色；代码块按语言高亮并默认折叠（基于 FastColoredTextBox）
- **Markdown 渲染**：文本加载完成后，判定为 Markdown 的正文自动改用 `MarkdownTextBox`（CDS.Markdown.Lite）显示标题、列表、代码块、表格等基础排版；流式期间仍使用 FCTB
- **可点击链接**：URL 与图片文件路径命中测试，`LinkClicked` 事件交给宿主决定打开方式
- **回话级勾选**：勾选用户或助手任一条即选中整个回话；`SelectTurns` / `SelectionChanged` 支持批量选中与删除
- **滚动体验**：自动跟随末尾（用户上翻即暂停），离开末尾时底部居中显示「跳到末尾」浮动按钮，Ctrl+滚轮缩放字体
- **主题接缝**：`ChatTheme.Set(ChatPalette)` 一次性切换配色，控件不依赖任何宿主主题管理器
- **附件模型**：`ChatMessage.Attachments`（文件名 → 字节），供历史记录与图片预览使用

## 环境要求

- `net7.0-windows7.0` ~ `net10.0-windows7.0`，WinForms
- 依赖 [Vax-FCTB](https://www.nuget.org/packages/Vax-FCTB) `2.19.0.224`（FastColoredTextBox）
- 依赖 [CDS.Markdown.Lite](https://www.nuget.org/packages/CDS.Markdown.Lite) `1.5.7`（`net8.0`+；`net7.0` 目标不引用，正文保持 FCTB 渲染）

## 快速开始

```csharp
using ChatFlow;

var book = new ChatTurnBook();
book.AddTurn(new ChatTurn
{
    User = new ChatMessage("user", "你好，介绍一下 ChatFlow"),
    Assistant = new ChatMessage("assistant", "这是一个**虚拟化**的聊天视图。"),
});

var chat = new ChatFlowControl
{
    Dock = DockStyle.Fill,
    ChatFont = new Font("Consolas", 9.75F),
};
chat.Bind(book);
chat.LinkClicked += (_, e) => MessageBox.Show($"{e.Kind}: {e.Target}");
Controls.Add(chat);
```

流式追加：

```csharp
var turn = new ChatTurn
{
    User = new ChatMessage("user", "写一首诗"),
    Streaming = true,
    ReasoningText = "先确定韵脚……",
    ToolStatusText = "正在调用 search…",
};
book.AddTurn(turn);

// 每收到一段增量就更新一次（内部节流）
chat.UpdateStreamingMessage(turn, accumulatedText);

// 阶段切换或结束时强制刷新
chat.UpdateStreamingMessage(turn, accumulatedText, force: true);
```

需要自定义右键菜单时，可用 `HitTest` 定位条目、`GetRowSelectedText` 取选中文本。

## 构建与测试

```powershell
dotnet build ChatFlow.slnx -c Release
dotnet test ChatFlow.Tests/ChatFlow.Tests.csproj -c Release
```

测试为 xUnit + `Xunit.StaFact`（WinForms 需要 STA），覆盖虚拟化布局、思考区折叠、文本样式、链接命中与点击转发。

## 目录结构

```
ChatFlow/        控件库（ChatFlowControl、ChatMessageRow、ChatTurnBook、ChatTheme…）
ChatFlow.Tests/  STA 单元测试
ChatFlow.slnx    解决方案
```

## 许可证

MIT，见 [LICENSE](LICENSE)。
