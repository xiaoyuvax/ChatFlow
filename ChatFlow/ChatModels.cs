using System.Text.Json.Serialization;

namespace ChatFlow
{
    /// <summary>一条聊天消息（本库的最小形态：角色 + 纯文本 + 附件）。</summary>
    public class ChatMessage
    {
        public ChatMessage()
        {
        }

        public ChatMessage(string role, string text)
        {
            Role = role;
            Text = text;
        }

        public string Role { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        /// <summary>本消息携带的附件（文件名→字节），历史查看用。</summary>
        public Dictionary<string, byte[]> Attachments { get; set; }
    }

    /// <summary>
    /// 一次问答回话（用户消息 + 助手回复），配对选中/删除等操作以此为原子单位。
    /// 助手正文之外的辅助文本（推理过程、工具调用轨迹）进入 Thinking 区：
    /// UI 默认折叠仅显示正文，用户可展开查看细节。
    /// </summary>
    public class ChatTurn
    {
        /// <summary>用户发出的消息（可能为 null：流式回复期间尚未创建下一条用户消息）。</summary>
        [JsonIgnore]
        public ChatMessage User { get; set; }

        /// <summary>助手回复（可能为 null：回复尚未生成完成时）。</summary>
        [JsonIgnore]
        public ChatMessage Assistant { get; set; }

        /// <summary>思考区：推理文本。</summary>
        [JsonIgnore]
        public string ReasoningText { get; set; } = string.Empty;

        /// <summary>思考区：工具调用轨迹（每行一条）。</summary>
        [JsonIgnore]
        public string ToolTraceText { get; set; } = string.Empty;

        /// <summary>聊天区可见的工具调用行（每次调用一行，长参折叠）。</summary>
        [JsonIgnore]
        public string ToolCallLines { get; set; } = string.Empty;

        /// <summary>
        /// 思维片段与工具调用按发生顺序穿插后的文本（工具块形如 tool(args) · 1s）。
        /// 非空时聊天正文按这个顺序展示，思考区不再重复；旧数据为空则退回两段式。
        /// </summary>
        [JsonIgnore]
        public string TimelineText { get; set; } = string.Empty;

        /// <summary>
        /// 分段展示：正文出现前的思维/工具为一段，正文之后又出现思维就另起一段。
        /// 每段 =（本段思维与工具过程，本段正文）。空列表 = 旧行为（整轮一个折叠区）。
        /// </summary>
        [JsonIgnore]
        public List<(string Thinking, string Answer)> Phases { get; } = [];

        /// <summary>宿主写入的 timeline.Version 快照：分段数据与 timeline 同步时跳过重建。</summary>
        [JsonIgnore]
        public int PhasesDirty { get; set; }

        /// <summary>
        /// 每段折叠区各自的展开状态（键 = Phases 下标）。整轮的 ThinkingExpanded 只服务不分段的旧数据，
        /// 分段后不能再共用一个值：用户展开第一段，不该把后面几段一起拉开。
        /// </summary>
        [JsonIgnore]
        public Dictionary<int, bool> PhaseExpanded { get; } = new();


        /// <summary>思考区汇总：已执行的工具调用步数。</summary>
        /// <summary>协作提示（agent 自动插入的仿用户消息）：界面折叠成一行标题、淡黄底区分。</summary>
        [JsonIgnore]
        public bool Notice { get; set; }

        /// <summary>自动提示的来源（collab / mention / puppet / other）：折叠头据此标来源，空 = 未分类。</summary>
        [JsonIgnore]
        public string NoticeKind { get; set; } = string.Empty;

        /// <summary>被用户的「立即」消息顶替（不是停止）：收尾时不补「（已停止）」之类占位文案。</summary>
        [JsonIgnore]
        public bool Superseded { get; set; }

        [JsonIgnore]
        public int ToolStepCount { get; set; }

        /// <summary>思考区汇总：工具调用总耗时（毫秒）。</summary>
        [JsonIgnore]
        public long ToolTotalMs { get; set; }

        /// <summary>思考区折叠状态（数据侧持有；null=未交互，按有内容即折叠）。</summary>
        [JsonIgnore]
        public bool? ThinkingExpanded { get; set; }

        /// <summary>流式进行中（true：正文为空时 UI 显示「思考中……」占位）。</summary>
        [JsonIgnore]
        public bool Streaming { get; set; }

        /// <summary>流式期间「正在调用某工具」的实时状态文本（多行 = 并行批次同时执行）。
        /// 由 <see cref="ToolStarted"/>/<see cref="ToolsFinished"/> 维护。</summary>
        [JsonIgnore]
        public string ToolStatusText { get; set; }

        /// <summary>工具闪现区内容（思维链尾部半行）：没有正文、也没有工具行时，
        /// 由 ChatFlowControl 按节拍算好写进来，正文 FCTB 直接显示它。
        /// 宿主不写这个字段；只在流式期间有意义，落盘时丢弃。</summary>
        [JsonIgnore]
        public string FlashText { get; set; }

        /// <summary>记录一个开始执行的工具行：并行批次同时显示，同一行不重复叠加。</summary>
        public void ToolStarted(string line)
        {
            if (string.IsNullOrEmpty(line)) return;
            ToolStatusText = string.IsNullOrEmpty(ToolStatusText) ? line
                : ToolStatusText.Contains(line) ? ToolStatusText
                : ToolStatusText + "\n" + line;
        }

        /// <summary>一批工具收尾：正文区的执行中提示整批清除，残留的思维链闪现一并让位给工具行。</summary>
        public void ToolsFinished()
        {
            ToolStatusText = null;
            FlashText = null;
        }

        /// <summary>最近一条已完成的工具行（整轮累计里最后一行非空文本）；没有返回 null。
        /// 正文区在没有正文时的兜底显示：思维链尾部闪现还没算出来（或工具行刚变化）时显示它。</summary>
        [JsonIgnore]
        public string LastToolLine
        {
            get
            {
                string lines = ToolCallLines;
                if (string.IsNullOrEmpty(lines)) return null;
                int end = lines.Length;
                while (end > 0)
                {
                    int start = lines.LastIndexOf('\n', end - 1) + 1;
                    string line = lines[start..end].Trim();
                    if (line.Length > 0) return line;
                    end = start - 1;
                }
                return null;
            }
        }

        /// <summary>是否有思考区内容（决定折叠头是否显示）。</summary>
        [JsonIgnore]
        public bool HasThinking => !string.IsNullOrEmpty(ReasoningText) || !string.IsNullOrEmpty(ToolTraceText);

        /// <summary>本回话用户消息携带的附件（文件名→字节）。</summary>
        [JsonIgnore]
        public Dictionary<string, byte[]> Attachments { get; set; }

        /// <summary>UI 勾选状态；勾选任一条会置位整个回话。</summary>
        [JsonIgnore]
        public bool Selected
        {
            get => _selected;
            set
            {
                if (_selected == value) return;
                _selected = value;
            }
        }
        private bool _selected;

        [JsonIgnore]
        public string UserText
        {
            get => User?.Text ?? string.Empty;
            set { if (User != null) User.Text = value; }
        }

        /// <summary>宿主拼在用户消息前面的说明块（技能展开、TCF 启用提示）：界面按宿主消息样式单独显示；
        /// 文本本身仍整条作为这一轮的用户消息，只是正文区只画用户自己写的部分。</summary>
        [JsonIgnore]
        public string HostPrefix { get; set; } = string.Empty;

        /// <summary>用户自己写的那段（去掉宿主说明块）；正文与「最宽行」都按它算。</summary>
        [JsonIgnore]
        public string UserBody
            => HostPrefix.Length > 0 && UserText.StartsWith(HostPrefix, StringComparison.Ordinal)
                ? UserText[HostPrefix.Length..].TrimStart('\r', '\n')
                : UserText;

        /// <summary>宿主说明块单独显示时用的那一轮（缓存：重建条目后展开/收起状态还在）。</summary>
        [JsonIgnore]
        public ChatTurn HostView { get; set; }

        [JsonIgnore]
        public string AssistantText
        {
            get => Assistant?.Text ?? string.Empty;
            set { if (Assistant != null) Assistant.Text = value; }
        }
    }

    /// <summary>
    /// 聊天数据集（含 system 消息与回话序列）。是 ChatFlowControl 的直接数据源。
    /// </summary>
    public class ChatTurnBook
    {
        private readonly List<ChatTurn> turns = [];

        /// <summary>回话增删/文本变化时触发（控件全量刷新自身）。</summary>
        public event EventHandler TurnChanged;

        public SystemMessageWrapper SystemMessage { get; set; }

        public IReadOnlyList<ChatTurn> Turns => turns;

        public void RaiseChanged() => TurnChanged?.Invoke(this, EventArgs.Empty);

        public void RaiseTextChanged() => TurnChanged?.Invoke(this, EventArgs.Empty);

        public void Clear()
        {
            turns.Clear();
            SystemMessage = null;
            RaiseChanged();
        }

        public void AddTurn(ChatTurn turn)
        {
            turns.Add(turn);
            RaiseChanged();
        }

        public void RemoveTurns(HashSet<ChatTurn> set)
        {
            turns.RemoveAll(t => set.Contains(t));
            RaiseChanged();
        }

        public void ReplaceAll(IEnumerable<ChatTurn> newTurns, ChatMessage systemMessage)
        {
            turns.Clear();
            turns.AddRange(newTurns);
            SystemMessage = systemMessage != null ? new SystemMessageWrapper(systemMessage) : null;
            RaiseChanged();
        }
    }

    /// <summary>System 消息包装（不属于任何回话）。</summary>
    public class SystemMessageWrapper
    {
        public SystemMessageWrapper(ChatMessage message) => Message = message;

        public ChatMessage Message { get; set; }

        public string Text => Message?.Text ?? string.Empty;
    }
}
