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

        /// <summary>思考区汇总：已执行的工具调用步数。</summary>
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

        /// <summary>流式期间「正在调用某工具」的实时状态文本。</summary>
        [JsonIgnore]
        public string ToolStatusText { get; set; }

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
