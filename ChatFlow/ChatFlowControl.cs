using FastColoredTextBoxNS;

namespace ChatFlow
{
    /// <summary>
    /// 聊天对话虚拟化流式视图。
    /// 内部按“条”（一条用户消息或一条助手消息）维护高度缓存与位置前缀和，
    /// 仅实例化可见区域附近一屏缓冲内的行控件，滚动时把离屏行控件移到滚动方向复用。
    /// 数据源：ChatTurnBook（每项 ChatTurn 含 User/Assistant 两条消息与 Selected 状态）。
    /// </summary>
    public class ChatFlowControl : Control
    {
        /// <summary>行高缓存（含上下间距），按 ChatFlowItem 顺序排列。</summary>
        private readonly List<ChatFlowItem> items = [];
        private readonly Dictionary<ChatFlowItem, ChatMessageRow> realized = [];
        private readonly Dictionary<ChatFlowItem, ChatCardRow> cardRows = [];
        private readonly List<ChatSurvey> surveys = [];
        private readonly List<ChatLink> links = [];
        private readonly Dictionary<ChatFlowItem, ChatLinkRow> linkRows = [];
        private readonly Dictionary<ChatFlowItem, ChatThinkingRow> thinkingRows = [];
        private string expandedSurveyId;
        private readonly Dictionary<ChatFlowItem, int> heightCache = [];
        private readonly List<ChatMessageRow> rowPool = [];
        private readonly VScrollBar vbar = new();
        /// <summary>底部居中的「跳到末尾」浮动按钮（不在末尾时显示）。</summary>
        private readonly JumpToEndButton jumpToEndButton;
        /// <summary>顶部浮动的「思考与工具过程」折叠头替身：原折叠头被滚出可见区时顶替它，点一下即折叠。</summary>
        private readonly StickyThinkingBar stickyThinking = new();
        /// <summary>当前顶替的是哪一块（点击折叠用）。</summary>
        private ChatFlowItem stickyTarget;
        /// <summary>内部改滚动值中：此时的 ValueChanged 不算「用户拖动」，不改变「跟随末尾」状态。</summary>
        private bool settingScrollInternally;

        private Font chatFont = new("Microsoft YaHei UI", 9F);
        private bool autoScrollToEnd = true;
        private int totalHeight;
        private int[] prefixSum = [];
        private readonly Dictionary<ChatFlowItem, int> itemIndex = [];

        /// <summary>选中回话数量变化时触发（用于外部刷新命令可用状态等）。</summary>
        public event EventHandler SelectionChanged;

        /// <summary>用户手动拖动滚动条或滚轮滚动时触发。</summary>
        public event EventHandler UserScrolled;

        /// <summary>点击正文/思考区里的可点击链接（URL、图像文件路径）后触发；宿主决定打开方式。</summary>
        public event EventHandler<ChatLinkClickedEventArgs> LinkClicked;

        /// <summary>请求撤回一个问答对（宿主负责删除回话并撤销文件修改）。</summary>
        public event EventHandler<ChatFlowActionEventArgs> UndoRequested;

        /// <summary>请求复制一个问答对（宿主负责写入剪贴板）。</summary>
        public event EventHandler<ChatFlowActionEventArgs> CopyRequested;

        internal bool CanUndo => UndoRequested != null;
        internal bool CanCopy => CopyRequested != null;

        /// <summary>右键「操作」子菜单的候选动作（作用于已勾选的对话）；为空则不显示该子菜单。</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public IReadOnlyList<ChatSelectionAction> SelectionActions { get; set; } = [];

        /// <summary>右键菜单切换「选择」模式后触发（宿主据此同步工具栏开关）。</summary>
        public event EventHandler SelectionModeChanged;

        /// <summary>右键「操作」里点了某一项后触发（按 Key 派发，语义由宿主决定）。</summary>
        public event EventHandler<ChatSelectionActionEventArgs> SelectionActionRequested;

        internal bool HasCheckedTurns => DataSource?.Turns.Any(t => t.Selected) == true;

        internal ChatMessageRow RowOf(ChatFlowItem item)
            => item != null && realized.TryGetValue(item, out var row) ? row : null;

        internal void ToggleSelectionMode()
        {
            ShowSelection = !ShowSelection;
            if (!ShowSelection) SelectTurns(_ => false);
            SelectionModeChanged?.Invoke(this, EventArgs.Empty);
        }

        internal void RaiseSelectionAction(string key)
        {
            if (!string.IsNullOrEmpty(key)) SelectionActionRequested?.Invoke(this, new ChatSelectionActionEventArgs(key));
        }

        internal void RequestUndo(ChatFlowItem item)
        {
            if (item != null) UndoRequested?.Invoke(this, new ChatFlowActionEventArgs(item.Turn));
        }

        internal void RequestCopy(ChatFlowItem item)
        {
            if (item != null) CopyRequested?.Invoke(this, new ChatFlowActionEventArgs(item.Turn));
        }

        internal void ShowChatMenu(ChatFlowItem item, Point screen) => ChatFlowMenu.Show(this, item, screen);

        /// <summary>调查卡提交（answers 为 key→value）。宿主负责持久化并回写 State/AnswerSummary。</summary>
        public event EventHandler<ChatSurveyEventArgs> SurveySubmitted;

        /// <summary>调查卡被跳过：收起，保留待答。</summary>
        public event EventHandler<ChatSurveyEventArgs> SurveySkipped;

        /// <summary>调查卡展开/收起（只有最近一条会保持展开）。</summary>
        public event EventHandler<ChatSurveyToggledEventArgs> SurveyToggled;

        /// <summary>设置嵌入对话流的调查卡；AnchorTurnIndex 决定插在哪一轮之后（-1 = 末尾）。</summary>
        public void SetSurveys(IEnumerable<ChatSurvey> value)
        {
            surveys.Clear();
            if (value != null) surveys.AddRange(value);
            if (heightCache.Count > 0) heightCache.Clear();
            RebuildItems();
        }

        public IReadOnlyList<ChatSurvey> Surveys => surveys;

        /// <summary>设置可点击导航条目（图标 + 标题）。</summary>
        public void SetLinks(IEnumerable<ChatLink> value)
        {
            links.Clear();
            if (value != null) links.AddRange(value);
            RebuildItems();
        }

        public event EventHandler<ChatLinkEventArgs> LinkActivated;

        private void OnCardSubmitted(ChatSurvey survey, Dictionary<string, string> answers)
        {
            survey.State = "answered";
            survey.AnswerSummary = answers.Count == 0
                ? "已提交"
                : string.Join(" · ", answers.Select(kv => kv.Value)).Trim();
            expandedSurveyId = null;
            SurveySubmitted?.Invoke(this, new ChatSurveyEventArgs(survey, answers));
            RebuildItems();
        }

        private void OnCardSkipped(ChatSurvey survey)
        {
            if (expandedSurveyId == survey.Id) expandedSurveyId = null;
            SurveySkipped?.Invoke(this, new ChatSurveyEventArgs(survey, null));
            RebuildItems();
        }

        private void OnCardToggled(ChatSurvey survey, bool expanded)
        {
            expandedSurveyId = expanded ? survey.Id : null;
            if (cardRows.Count > 0) { heightCache.Clear(); RequestRelayoutOfCards(); }
            SurveyToggled?.Invoke(this, new ChatSurveyToggledEventArgs(survey, expanded));
        }

        private void RequestRelayoutOfCards()
        {
            foreach (var item in items.Where(i => i.Kind == ChatFlowItemKind.Card).ToList())
                heightCache.Remove(item);
            RebuildSums();
            UpdateScrollbar();
            Relayout();
        }

        /// <summary>对话数据集；设置后控件全权负责展现与勾选操作。</summary>
        public ChatTurnBook DataSource { get; private set; }

        private bool showSelection;

        /// <summary>是否显示消息左侧勾选框；默认关闭，选择性保存时再开。</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool ShowSelection
        {
            get => showSelection;
            set
            {
                if (showSelection == value) return;
                showSelection = value;
                foreach (var row in realized.Values) row.ShowSelection = value;
            }
        }

        /// <summary>聊天正文字体；缩放时统一修改此属性。</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public Font ChatFont
        {
            get => chatFont;
            set
            {
                if (value == null) return;
                if (chatFont != null && !ReferenceEquals(chatFont, value))
                    chatFont.Dispose();
                chatFont = (Font)value.Clone();
                heightCache.Clear();
                foreach (var row in realized.Values.ToArray())
                    ReleaseRow(row);
                UpdateScrollAndLayout();
            }
        }

        /// <summary>是否自动跟随滚动到最新内容（用户向上翻阅时暂停，滚回底部后恢复）。</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        /// <summary>空列表时的占位提示。</summary>
        public string PlaceholderText { get; set; } = "暂无对话";

        public bool AutoScrollToEnd
        {
            get => autoScrollToEnd;
            set
            {
                autoScrollToEnd = value;
                if (value) ScrollToEnd();
            }
        }

        public ChatFlowControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint
                | ControlStyles.ResizeRedraw, true);
            BackColor = ChatTheme.Current?.Back ?? Color.WhiteSmoke;
            vbar.Dock = DockStyle.Right;
            vbar.ValueChanged += vbar_ValueChanged;
            Controls.Add(vbar);

            // 「跳到末尾」浮动按钮：用户手动滚离末尾时出现（底部居中），点了回末尾并恢复自动跟随
            jumpToEndButton = new JumpToEndButton { Visible = false };
            jumpToEndButton.Click += (_, _) => JumpToEnd();
            Controls.Add(jumpToEndButton);
            jumpTip.SetToolTip(jumpToEndButton, "跳到末尾（回到最新内容并恢复自动跟随）");

            // 顶部浮动的折叠头替身：长过程滚到看不见原折叠头时，用它就地折叠
            stickyThinking.Click += (_, _) => CollapseStickyThinking();
            Controls.Add(stickyThinking);

            ChatTheme.Changed += OnAppThemeChanged;
        }

        private readonly ToolTip jumpTip = new();

        /// <summary>是否处于内容末尾（滚动条不可见=内容不足一屏，也算末尾）。</summary>
        private bool IsAtEnd => !vbar.Visible || vbar.Value >= MaxScrollValue;

        /// <summary>当前是否已在内容末尾（滚动跟随/「跳到末尾」按钮的判定条件）。</summary>
        public bool AtEnd => IsAtEnd;

        /// <summary>程序内部改滚动值：不算「用户拖动」，不改变自动跟随状态。
        /// 必须按 MaxValue 上限钳制：控件尚未布局时（高度≈0）LargeChange 只有下限 10，
        /// 直接写 totalHeight-viewH 会越过 ScrollBar 允许的最大值并抛 ArgumentOutOfRangeException。</summary>
        private void SetScrollValueInternal(int value)
        {
            settingScrollInternally = true;
            try { vbar.Value = Math.Clamp(value, vbar.Minimum, Math.Max(vbar.Minimum, vbar.Maximum - vbar.LargeChange + 1)); }
            finally { settingScrollInternally = false; }
        }

        /// <summary>滚动值变化：非程序设置（用户拖滚动条/滚轮）时按是否在末尾决定自动跟随；并刷新浮动按钮。</summary>
        private void vbar_ValueChanged(object sender, EventArgs e)
        {
            if (updatingBar) return;   // UpdateScrollbar 内部改范围触发的回调：不重排、不改跟随状态
            if (!settingScrollInternally) autoScrollToEnd = IsAtEnd;
            Relayout();
            UpdateJumpToEndButton();
        }

        /// <summary>「跳到末尾」：恢复自动跟随、滚到底，按钮随之隐藏。</summary>
        private void JumpToEnd()
        {
            autoScrollToEnd = true;
            ScrollToEnd();
            UpdateJumpToEndButton();
        }

        /// <summary>按当前滚动位置显隐并定位浮动按钮（不在末尾才显示）。</summary>
        private void UpdateJumpToEndButton()
        {
            if (jumpToEndButton == null || IsDisposed) return;
            bool show = vbar.Visible && !IsAtEnd;
            if (jumpToEndButton.Visible != show) jumpToEndButton.Visible = show;
            if (show) LayoutJumpToEndButton();
        }

        /// <summary>浮动按钮定位：内容区水平居中、距底 10px；置顶避免被虚拟化行盖住。</summary>
        private void LayoutJumpToEndButton()
        {
            if (jumpToEndButton == null || !jumpToEndButton.Visible) return;
            int contentW = Math.Max(1, ClientSize.Width - (vbar.Visible ? vbar.Width : 0));
            int x = Math.Max(0, (contentW - jumpToEndButton.Width) / 2);
            int y = Math.Max(0, ClientSize.Height - jumpToEndButton.Height - 10);
            jumpToEndButton.Location = new Point(x, y);
            jumpToEndButton.BringToFront();
        }

        private int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96.0);

        /// <summary>顶部折叠头替身：找出「折叠头已滚出视口、但这一块还盖着视口顶部」的最后一个展开块。
        /// 多个块叠着滚过去时取最靠下的那个 —— 用户正看的就是它。</summary>
        private ChatFlowItem FindStickyTarget(int viewTop)
        {
            ChatFlowItem target = null;
            if (prefixSum.Length < items.Count + 1) return null;
            for (int i = 0; i < items.Count; i++)
            {
                var item = items[i];
                if (item.Kind != ChatFlowItemKind.Assistant) continue;
                if (item.ThinkingText == null || item.IsThinkingContinuation || !item.ThinkingExpanded) continue;
                int headerY = prefixSum[i] + Dip((item.IsContinuation ? 0 : ChatMessageRow.RoleIndent) + 2);
                // 长过程会被拆成多片（只有第一片有折叠头）：块的底部要算到同一轮的最后一片
                int last = i;
                while (last + 1 < items.Count && items[last + 1].Turn == item.Turn) last++;
                if (headerY >= viewTop || prefixSum[last + 1] <= viewTop) continue;
                target = item;
            }
            return target;
        }

        /// <summary>按当前滚动位置显隐并定位顶部替身条：文案、字体、水平位置、行高都与原折叠头一致。</summary>
        private void UpdateStickyThinking()
        {
            if (stickyThinking == null || IsDisposed) return;
            stickyTarget = FindStickyTarget(vbar.Visible ? vbar.Value : 0);
            if (stickyTarget == null)
            {
                if (stickyThinking.Visible) stickyThinking.Visible = false;
                return;
            }

            stickyThinking.Text = "▼ " + stickyTarget.ThinkingHeaderText + "（点击折叠）";
            stickyThinking.ForeColor = ChatTheme.Current?.ForeDim ?? Color.Gray;
            stickyThinking.BackColor = BackColor;   // 不透明：盖住下面的正文
            int contentW = Math.Max(1, ClientSize.Width - (vbar.Visible ? vbar.Width : 0));
            // 整行铺满（左侧不留缝），文字仍与行内折叠头的 x 对齐
            stickyThinking.TextIndent = Dip(ChatMessageRow.RoleIndent);
            stickyThinking.Bounds = new Rectangle(0, 0, contentW, Dip(ChatMessageRow.ThinkingHeaderH));
            if (!stickyThinking.Visible) stickyThinking.Visible = true;
            stickyThinking.Invalidate();
            stickyThinking.BringToFront();
        }

        /// <summary>点替身条：折叠该块，并把视口滚回原折叠头处（折叠头贴住顶部），替身随即隐藏。</summary>
        private void CollapseStickyThinking()
        {
            var turn = stickyTarget?.Turn;
            if (turn == null) return;
            turn.ThinkingExpanded = false;
            autoScrollToEnd = false;
            DataSource?.RaiseChanged();   // → RebuildItems：行高与条目全量重建
            var item = items.FirstOrDefault(i =>
                i.Turn == turn && i.ThinkingText != null && !i.IsThinkingContinuation);
            int idx = item != null ? items.IndexOf(item) : -1;
            if (idx >= 0 && vbar.Visible)
                SetScrollValueInternal(Math.Min(prefixSum[idx], MaxScrollValue));
            Relayout();
            UpdateJumpToEndButton();
            UpdateStickyThinking();
        }

        /// <summary>主题切换：底色换调色板、按新调色板重建聊天样式并重绑所有已实现行（行内配色/正文样式都在 Bind 里）。</summary>
        private void OnAppThemeChanged()
        {
            BackColor = ChatTheme.Current?.Back ?? Color.WhiteSmoke;
            ChatTextStyler.EnsureStyles();
            foreach (var row in realized.Values)
            {
                row.InvalidateBindCache();   // 颜色来自样式：文本没变也要重上一次
                row.Bind(row.Item, chatFont, RowTextWidth);
            }
            Relayout();
            jumpToEndButton?.Invalidate();
            stickyThinking?.Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) ChatTheme.Changed -= OnAppThemeChanged;
            base.Dispose(disposing);
        }

        /// <summary>WS_EX_COMPOSITED：把子行控件合成到离屏缓冲再整块贴出，
        /// 消除虚拟化行“释放/复用/移动”时子控件重绘造成的闪烁（含影响其它窗体的刷新风暴）。</summary>
        protected override CreateParams CreateParams
        {
            get
            {
                var cp = base.CreateParams;
                cp.ExStyle |= 0x02000000; // WS_EX_COMPOSITED
                return cp;
            }
        }

        #region 数据绑定

        /// <summary>绑定数据集并全量刷新（勾选状态在 ChatTurn 对象上，天然保留）。</summary>
        public void Bind(ChatTurnBook book)
        {
            if (DataSource != null)
                DataSource.TurnChanged -= OnTurnChanged;
            DataSource = book;
            if (DataSource != null)
                DataSource.TurnChanged += OnTurnChanged;
            RebuildItems();
        }

        private void OnTurnChanged(object sender, EventArgs e) => RebuildItems();

        /// <summary>单条消息渲染上限（字符）：超过即按行边界拆片。
        /// 依据：Win32 子窗口定位在约 ±32767 像素处被钳制，一条几十万像素高的行会永久挡住末尾内容。</summary>
        private const int MaxItemChars = 10_000;

        private static List<string> SplitLongText(string text)
        {
            var parts = new List<string>();
            int start = 0;
            while (start < text.Length)
            {
                int len = Math.Min(MaxItemChars, text.Length - start);
                if (start + len < text.Length)
                {
                    int nl = text.LastIndexOf('\n', start + len - 1, len);
                    if (nl > start + len / 2) len = nl + 1 - start;
                }
                parts.Add(text.Substring(start, len));
                start += len;
            }
            if (parts.Count == 0) parts.Add("");
            return parts;
        }

        private void RebuildItems()
        {
            items.Clear();
            heightCache.Clear();
            if (DataSource != null)
            {
                int index = 0;
                foreach (var turn in DataSource.Turns)
                {
                    ChatFlowItem lastMessage = null;
                    if (turn.User != null)
                    {
                        lastMessage = new ChatFlowItem(turn, ChatFlowItemKind.User);
                        items.Add(lastMessage);
                    }
                    // 流式回合：正文/思考一有内容才出助手条；两者都没有时不占位，把位置留给末尾指示行
                    if (turn.Assistant != null || turn.Streaming)
                    {
                        var message = new ChatFlowItem(turn, ChatFlowItemKind.Assistant);
                        string answer = message.Text;
                        string thinking = message.ThinkingText;
                        if (turn.Streaming && answer.Length == 0 && thinking == null)
                            lastMessage = null;   // 还没出字也没有思考区：这一行只会是「空白文本框」，位置留给末尾的指示行
                        else if (thinking != null && thinking.Length > MaxItemChars && message.ThinkingExpanded)
                        {
                            // 展开的长思维链按行拆片：单片仍远低于整行高度上限（Win32 把控件位置钳在 ±32767），
                            // 这样展开后不需要内部滚动条。后续分片不再显示折叠头，答复挂在最后一片。
                            // 流式期间同样要切：不切的话一条会涨成几万像素高，整行定位触发 ±32767 钳位，
                            // 重排校正再也收敛不了（界面表现为卡死）。
                            var parts = SplitLongText(thinking);
                            for (int i = 0; i < parts.Count; i++)
                            {
                                ChatFlowItem piece = i == 0
                                    ? message
                                    : new ChatFlowItem(turn, ChatFlowItemKind.Assistant)
                                    {
                                        IsContinuation = true,
                                        IsThinkingContinuation = true
                                    };
                                piece.ThinkingOverride = parts[i];
                                piece.TextOverride = i == parts.Count - 1 ? answer : "";
                                items.Add(piece);
                                lastMessage = piece;
                            }
                        }
                        else if (answer.Length > MaxItemChars)
                        {
                            // 单条太高会撞 Win32 的 32767 像素定位上限（控件 Y 被钳到 -32768），
                            // 表现为滚到底也看不到末尾内容；按行边界拆成多片，每片都远低于上限。
                            // 流式期间也要切，理由同上（一行涨太高会让重排校正循环收敛不了）。
                            var parts = SplitLongText(answer);
                            for (int p = 0; p < parts.Count; p++)
                            {
                                ChatFlowItem piece = p == 0
                                    ? message
                                    : new ChatFlowItem(turn, ChatFlowItemKind.Assistant) { IsContinuation = true };
                                piece.TextOverride = parts[p];
                                items.Add(piece);
                                lastMessage = piece;
                            }
                        }
                        else
                        {
                            items.Add(message);
                            lastMessage = message;
                        }
                    }
                    if (lastMessage != null) lastMessage.IsPairEnd = true;

                    foreach (var survey in surveys.Where(s => s.AnchorTurnIndex == index))
                        items.Add(new ChatFlowItem(turn, ChatFlowItemKind.Card) { Survey = survey });
                    foreach (var link in links.Where(l => l.AnchorTurnIndex == index))
                        items.Add(new ChatFlowItem(turn, ChatFlowItemKind.Link) { Link = link });
                    index++;
                }
                foreach (var survey in surveys.Where(s => s.AnchorTurnIndex < 0 || s.AnchorTurnIndex >= index))
                    items.Add(new ChatFlowItem(DataSource.Turns.LastOrDefault(), ChatFlowItemKind.Card) { Survey = survey });
                foreach (var link in links.Where(l => l.AnchorTurnIndex < 0 || l.AnchorTurnIndex >= index))
                    items.Add(new ChatFlowItem(DataSource.Turns.LastOrDefault(), ChatFlowItemKind.Link) { Link = link });

                // 执行进行中且正文还没出字：末尾显示「工作中…」指示行；正文一开始流式就撤掉（文字本身即进度提示）
                var streamingTurn = DataSource.Turns.LastOrDefault();
                if (streamingTurn?.Streaming == true
                    && new ChatFlowItem(streamingTurn, ChatFlowItemKind.Assistant).Text.Length == 0)
                    items.Add(new ChatFlowItem(streamingTurn, ChatFlowItemKind.Thinking));
            }

            UpdateScrollAndLayout();
            if (autoScrollToEnd) ScrollToEnd();
        }

        #endregion 数据绑定

        #region 布局 / 虚拟化

        private int RowTextWidth
            => Math.Max(40, ClientSize.Width - (vbar.Visible ? vbar.Width : 0) - ChatMessageRow.TextRightSpace);

        /// <summary>估算条目高度（未实例化时的预估，实例化后按真实渲染校正）。
        /// 估算结果也写入缓存：宽度/字体/结构变化时会清空，因此可安全记忆，
        /// 避免每次 RebuildSums 对所有长文本重复逐字符遍历（流式卡顿主因）。</summary>
        private bool IsLatestPending(ChatSurvey survey)
        {
            if (survey == null) return false;
            for (int i = surveys.Count - 1; i >= 0; i--)
            {
                if (surveys[i].State == "answered") continue;
                return surveys[i].Id == survey.Id;
            }
            return false;
        }

        private int EstimateItem(ChatFlowItem item)
        {
            if (item.Kind == ChatFlowItemKind.Thinking)
            {
                int thinkingH = (int)Math.Round(ChatThinkingRow.RowHeightDip * DeviceDpi / 96.0);
                heightCache[item] = thinkingH;
                return thinkingH;
            }
            if (item.Kind == ChatFlowItemKind.Link)
            {
                if (heightCache.TryGetValue(item, out int linkH)) return linkH;
                linkH = (int)Math.Round(30 * DeviceDpi / 96.0);
                heightCache[item] = linkH;
                return linkH;
            }
            if (item.Kind == ChatFlowItemKind.Card)
            {
                if (heightCache.TryGetValue(item, out int cardH)) return cardH;
                cardH = 64 + 26 * (item.Survey?.Fields.Count ?? 0);
                heightCache[item] = cardH;
                return cardH;
            }
            if (heightCache.TryGetValue(item, out int h)) return h;
            // 粗估：按字符宽度与文本长度推算行数（CJK 宽字符按 2 计），仅用于定位可见范围；
            // 真实高度在行实例化（Bind 返回测量值）后写入缓存。
            int usable = RowTextWidth - 8;
            int charW = Math.Max(6, (int)(chatFont.Size * 1.1));
            int perLine = Math.Max(10, usable / charW);
            int units = 0;
            // 折叠思考区只显示一行摘要；展开时计入思考区文本
            string text = item.Text ?? string.Empty;
            bool thinkingVisible = item.ThinkingText != null && item.ThinkingExpanded;
            if (item.ThinkingText != null && !item.ThinkingExpanded)
            {
                units += 4; // 折叠头一行
            }
            else if (thinkingVisible)
            {
                foreach (char c in item.ThinkingText) units += c > 127 ? 2 : 1;
            }
            foreach (char c in text)
                units += c > 127 ? 2 : 1;
            int lines = Math.Max(1, (units + perLine - 1) / perLine) + text.Count(ch => ch == '\n')
                + (thinkingVisible ? item.ThinkingText.Count(ch => ch == '\n') : 0);
            h = lines * (int)(chatFont.Size * 1.55f + 6) + 2 + 8 + ChatMessageRow.RoleIndent + 10;
            heightCache[item] = h;   // 记忆估算（宽度/字体/结构变化时缓存已清）
            return h;
        }

        private void UpdateScrollAndLayout()
        {
            int count = items.Count;
            prefixSum = new int[count + 1];
            itemIndex.Clear();
            totalHeight = 0;
            for (int i = 0; i < count; i++)
            {
                prefixSum[i] = totalHeight;
                itemIndex[items[i]] = i;
                totalHeight += EstimateItem(items[i]);
            }
            prefixSum[count] = totalHeight;

            UpdateScrollbar();
            Relayout();
        }

        private void UpdateScrollbar()
        {
            // 防重入：下面把 LargeChange 改小/改大时原生控件会钳制当前位置并触发 ValueChanged，
            // 那条链会同步跑一遍重排、把 totalHeight 改掉；若继续用进函数时算出的 maxScroll 设
            // Maximum，滚动上限就永远比真实末尾小一截（表现为"追不上流式输出、跳到末尾无效"）。
            if (updatingBar) return;
            updatingBar = true;
            try
            {
                UpdateScrollbarCore();
            }
            finally
            {
                updatingBar = false;
            }
        }

        private bool updatingBar;

        private void UpdateScrollbarCore()
        {
            int visibleH = Math.Max(1, ClientSize.Height);
            bool needBar = totalHeight > visibleH;
            if (vbar.Visible != needBar)
                vbar.Visible = needBar;
            if (!needBar) return;
            int maxScroll = Math.Max(0, totalHeight - visibleH);
            vbar.Minimum = 0;
            // WinForms 语义：可滚动上限 = Maximum - LargeChange + 1，故 Maximum 须按 LargeChange 抬高，
            // 使可滚动上限恰好 = maxScroll（内容末尾），否则末尾会多出可下拉的空白。
            vbar.LargeChange = Math.Max(10, visibleH / 2);
            vbar.Maximum = Math.Min(Math.Max(totalHeight - 1, 1), maxScroll + vbar.LargeChange - 1);
            vbar.SmallChange = 16;
            if (autoScrollToEnd)
                SetScrollValueInternal(maxScroll);
            else
                SetScrollValueInternal(Math.Min(vbar.Value, maxScroll));
        }

        /// <summary>把可见窗口 ± 一屏缓冲范围内的条目实例化，其余回收。
        /// 行高以真实渲染测量为准（Bind 返回测量值），与估算不符时校正缓存并重排直至收敛。</summary>
        private void Relayout()
        {
            // 重入保护：滚动条 Value 变更会同步触发本方法（UpdateScrollbar 设值期间），
            // 重入时直接返回——外层校正循环会按新滚动值重排
            if (inLayout) return;
            inLayout = true;
            try
            {
                RelayoutCore();
            }
            finally
            {
                inLayout = false;
            }
            UpdateJumpToEndButton();   // 内容/视口变化后同步浮动按钮显隐与位置
            UpdateStickyThinking();    // 折叠头被滚出可见区时，在顶部顶替它
        }

        /// <summary>行内折叠（代码块展开/收起）后：重测该行高度并重排，行高随折叠收缩/展开。</summary>
        internal void RequestRelayout(ChatMessageRow row)
        {
            if (row?.Item != null)
                heightCache[row.Item] = row.Remeasure();
            RebuildSums();
            UpdateScrollbar();
            Relayout();
        }

        private bool inLayout;

        private void RelayoutCore()
        {
            if (items.Count == 0)
            {
                foreach (var row in realized.Values.ToArray()) ReleaseRow(row);
                Invalidate();
                return;
            }

            bool corrected;
            int guard = 0;
            do
            {
                corrected = false;
                guard++;

                int viewTop = vbar.Visible ? vbar.Value : 0;
                int viewH = Math.Max(1, ClientSize.Height);
                int buffer = viewH;

                int first = Math.Max(0, FindFirstIndexAt(viewTop - buffer));
                int last = Math.Min(items.Count - 1, FindFirstIndexAt(viewTop + viewH + buffer));

                foreach (var (item, row) in realized.ToArray())
                {
                    if (!itemIndex.TryGetValue(item, out int idx)) { ReleaseRow(row); continue; }
                    if (idx < first || idx > last) ReleaseRow(row);
                }
                foreach (var (item, lrow) in linkRows.ToArray())
                {
                    if (!itemIndex.TryGetValue(item, out int lidx) || lidx < first || lidx > last)
                    {
                        linkRows.Remove(item);
                        lrow.Hide();
                    }
                }
                foreach (var (item, card) in cardRows.ToArray())
                {
                    if (!itemIndex.TryGetValue(item, out int idx) || idx < first || idx > last)
                    {
                        cardRows.Remove(item);
                        card.Hide();
                    }
                }
                foreach (var (item, thinking) in thinkingRows.ToArray())
                {
                    if (!itemIndex.TryGetValue(item, out int idx) || idx < first || idx > last)
                    {
                        thinkingRows.Remove(item);
                        thinking.Hide();
                    }
                }

                int y = prefixSum[first];
                int rowWidth = vbar.Visible ? Width - vbar.Width : Width;
                for (int i = first; i <= last; i++)
                {
                    var item = items[i];
                    int cached = prefixSum[i + 1] - prefixSum[i];
                    if (item.Kind == ChatFlowItemKind.Link)
                    {
                        if (!linkRows.TryGetValue(item, out var linkRow))
                        {
                            linkRow = new ChatLinkRow();
                            linkRow.Activated += l => LinkActivated?.Invoke(this, new ChatLinkEventArgs(l));
                            Controls.Add(linkRow);
                            linkRows[item] = linkRow;
                        }
                        int linkHeight = linkRow.Bind(item.Link, chatFont, rowWidth);
                        if (linkHeight != cached)
                        {
                            heightCache[item] = linkHeight;
                            corrected = true;
                        }
                        int linkInset = (int)Math.Round(12 * DeviceDpi / 96.0);
                        linkRow.SetBounds(linkInset, y - viewTop, Math.Max(40, rowWidth - linkInset * 2), prefixSum[i + 1] - prefixSum[i]);
                        linkRow.BringToFront();
                        y = prefixSum[i + 1];
                        continue;
                    }
                    if (item.Kind == ChatFlowItemKind.Card)
                    {
                        bool expandedCard = item.Survey != null
                            && item.Survey.State != "answered"
                            && (expandedSurveyId == item.Survey.Id
                                || expandedSurveyId == null && IsLatestPending(item.Survey));
                        if (!cardRows.TryGetValue(item, out var card))
                        {
                            card = new ChatCardRow { Visible = true };
                            card.Submitted += OnCardSubmitted;
                            card.Skipped += OnCardSkipped;
                            card.Toggled += OnCardToggled;
                            Controls.Add(card);
                            cardRows[item] = card;
                        }
                        int measuredCard = card.Bind(item.Survey, chatFont, rowWidth, expandedCard);
                        if (measuredCard != cached)
                        {
                            heightCache[item] = measuredCard;
                            corrected = true;
                        }
                        int inset = (int)Math.Round(12 * DeviceDpi / 96.0);
                        card.SetBounds(inset, y - viewTop, Math.Max(40, rowWidth - inset * 2), prefixSum[i + 1] - prefixSum[i]);
                        card.BringToFront();
                        y = prefixSum[i + 1];
                        continue;
                    }
                    if (item.Kind == ChatFlowItemKind.Thinking)
                    {
                        if (!thinkingRows.TryGetValue(item, out var thinking))
                        {
                            thinking = new ChatThinkingRow { Visible = true };
                            Controls.Add(thinking);
                            thinkingRows[item] = thinking;
                        }
                        int measuredThinking = thinking.Bind(rowWidth);
                        if (measuredThinking != cached)
                        {
                            heightCache[item] = measuredThinking;
                            corrected = true;
                        }
                        int thinkingInset = (int)Math.Round(12 * DeviceDpi / 96.0);
                        thinking.SetBounds(thinkingInset, y - viewTop,
                            Math.Max(40, rowWidth - thinkingInset * 2), prefixSum[i + 1] - prefixSum[i]);
                        thinking.BringToFront();
                        y = prefixSum[i + 1];
                        continue;
                    }
                    if (!realized.TryGetValue(item, out var row))
                    {
                        row = AcquireRow();
                        int measured = row.Bind(item, chatFont, rowWidth);
                        realized[item] = row;
                        row.SelectionToggled += Row_SelectionToggled;
                        row.ThinkingToggled += Row_ThinkingToggled;
                        row.LinkClicked += Row_LinkClicked;
                        row.ShowSelection = showSelection;
                        if (measured != cached)
                        {
                            heightCache[item] = measured;
                            corrected = true;
                        }
                    }
                    else if (guard > 1)
                    {
                        // 校正轮：已实例化行重测（Bind 时句柄未就绪的行此时已就绪）
                        int measured = row.Remeasure();
                        if (measured != cached)
                        {
                            heightCache[item] = measured;
                            corrected = true;
                        }
                    }
                    row.SetBounds(0, y - viewTop, rowWidth, prefixSum[i + 1] - prefixSum[i]);
                    y = prefixSum[i + 1];
                }

                if (corrected && guard < 6)
                {
                    // 高度校正后前缀和/滚动范围失效，重建后进入下一轮定位
                    RebuildSums();
                    UpdateScrollbar();
                }
            } while (corrected && guard < 6);
        }

        /// <summary>按 heightCache（已测量项）+ 估算（未测量项）重建前缀和。</summary>
        private void RebuildSums()
        {
            int count = items.Count;
            prefixSum = new int[count + 1];
            itemIndex.Clear();
            totalHeight = 0;
            for (int i = 0; i < count; i++)
            {
                prefixSum[i] = totalHeight;
                itemIndex[items[i]] = i;
                totalHeight += EstimateItem(items[i]);
            }
            prefixSum[count] = totalHeight;
        }

        /// <summary>二分查找：返回 y 坐标所在条目的索引。</summary>
        private int FindFirstIndexAt(int y)
        {
            if (y <= 0) return 0;
            if (y >= totalHeight) return items.Count;
            int lo = 0, hi = items.Count; // 找第一个 prefixSum[i+1] > y
            while (lo < hi)
            {
                int mid = (lo + hi) / 2;
                if (prefixSum[mid + 1] > y) hi = mid; else lo = mid + 1;
            }
            return lo;
        }

        private ChatMessageRow AcquireRow()
        {
            if (rowPool.Count > 0)
            {
                var row = rowPool[^1];
                rowPool.RemoveAt(rowPool.Count - 1);
                row.Visible = true;
                return row;
            }
            var newRow = new ChatMessageRow(this);
            Controls.Add(newRow);
            return newRow;
        }

        private void ReleaseRow(ChatMessageRow row)
        {
            if (realized.Remove(row.Item))
                row.SelectionToggled -= Row_SelectionToggled;
            row.ThinkingToggled -= Row_ThinkingToggled;
            row.LinkClicked -= Row_LinkClicked;
            row.Item = null;
            row.Visible = false;
            rowPool.Add(row);
        }

        private void Row_LinkClicked(ChatFlowItem item, ChatTextStyler.ChatLinkKind kind, string target)
            => LinkClicked?.Invoke(this, new ChatLinkClickedEventArgs(item, kind, target));

        private void Row_ThinkingToggled(ChatFlowItem item)
        {
            item.ToggleThinking();
            DataSource?.RaiseChanged(); // → TurnChanged → RebuildItems 全量重排
        }

        #endregion 布局 / 虚拟化

        #region 公共操作

        /// <summary>屏幕坐标命中测试：返回所在条目（用于上下文菜单定位）。</summary>
        public ChatFlowItem HitTest(Point screenPoint)
        {
            var p = PointToClient(screenPoint);
            int y = p.Y + (vbar.Visible ? vbar.Value : 0);
            int idx = FindFirstIndexAt(y);
            if (idx >= 0 && idx < items.Count && y < prefixSum[idx + 1])
                return items[idx];
            return null;
        }

        /// <summary>命中条目所在行 FCTB 的选中文本（行未实例化时返回空）。</summary>
        public string GetRowSelectedText(ChatFlowItem item)
            => realized.TryGetValue(item, out var row) ? row.GetSelectedText() : string.Empty;

        /// <summary>当前允许的最大滚动位置（内容底对齐视口底）。</summary>
        private int MaxScrollValue => Math.Max(0, totalHeight - Math.Max(1, ClientSize.Height));

        /// <summary>滚动到底部（跟随最新对话）。</summary>
        public void ScrollToEnd()
        {
            if (!vbar.Visible)
            {
                Relayout();
                return;
            }
            SetScrollValueInternal(Math.Min(MaxScrollValue, vbar.Value));
            int target = MaxScrollValue;
            if (vbar.Value < target)
                SetScrollValueInternal(target);
            else
                Relayout();
            UpdateJumpToEndButton();
        }

        /// <summary>滚动到指定回话（顶部对齐可见）。</summary>
        /// <summary>滚动到指定调查卡。</summary>
        public void ScrollToSurvey(string surveyId)
        {
            if (string.IsNullOrEmpty(surveyId)) return;
            var item = items.FirstOrDefault(i => i.Survey?.Id == surveyId);
            int idx = item != null ? items.IndexOf(item) : -1;
            if (idx < 0) return;
            if (!vbar.Visible) { Relayout(); return; }
            SetScrollValueInternal(Math.Min(prefixSum[idx], MaxScrollValue));
            Relayout();
            UpdateJumpToEndButton();
        }
        public void ScrollToTurn(int turnIndex)
        {
            if (DataSource == null || turnIndex < 0) return;
            var turn = DataSource.Turns[turnIndex];
            var item = items.FirstOrDefault(i => i.Turn == turn);
            int idx = item != null ? items.IndexOf(item) : -1;
            if (idx < 0) return;
            if (!vbar.Visible)
            {
                Relayout();
                return;
            }
            SetScrollValueInternal(Math.Min(prefixSum[idx], MaxScrollValue));
            Relayout();
            UpdateJumpToEndButton();
        }

        /// <summary>流式更新节流：高频调用合并为最多每 250ms 一次全量重建（RebuildItems 路径
        /// 经 STA 测试验证布局正确；逐 token 局部 patch 在行池复用场景有陈旧状态问题）。</summary>
        private DateTime lastStreamingRebuild = DateTime.MinValue;

        /// <summary>流式输出时更新最后一条助手消息（正文 + 思考区一起刷新）。
        /// 高频调用按 250ms 节流为全量重建；force=true 时跳过节流（阶段切换：占位→内容、完成）。</summary>
        public void UpdateStreamingMessage(ChatTurn turn, string fullText, bool force = false)
        {
            var item = items.FirstOrDefault(i => i.Turn == turn && i.Kind == ChatFlowItemKind.Assistant);
            if (item == null)
            {
                RebuildItems();
                item = items.FirstOrDefault(i => i.Turn == turn && i.Kind == ChatFlowItemKind.Assistant);
                if (item == null) return;
            }

            if (!force && (DateTime.UtcNow - lastStreamingRebuild).TotalMilliseconds < 250)
                return; // 节流窗口内跳过：数据侧已是最新，下次触发会带上全部增量
            lastStreamingRebuild = DateTime.UtcNow;

            // 已实例化该行 → 就地重绑（更新文本+重测该行高度），避免整表重建导致所有行对象反复释放/创建（闪烁）
            if (realized.TryGetValue(item, out var row))
            {
                int rowWidth = vbar.Visible ? Width - vbar.Width : Width;
                int measured = row.Bind(item, chatFont, rowWidth);
                if (measured > 0) heightCache[item] = measured;
                RebuildSums();
                UpdateScrollbar();
                Relayout();
            }
            else
            {
                // 行尚未实例化（如刚进入流式，末尾行不在视口）：走全量重建保证结构正确
                RebuildItems();
            }

            if (autoScrollToEnd)
            {
                ScrollToEnd();
                var last = items.LastOrDefault(i => !i.IsUser);
                if (last != null && realized.TryGetValue(last, out var r2))
                    r2.PlaceCaretEnd();
            }
            else Relayout();
        }

        /// <summary>
        /// 滚动到最后一条会话：末尾条目完整可见，光标位于最后字符。
        /// <b>默认尊重用户的滚动位置</b>：用户手动拖过滚动条/滚轮离开末尾（autoScrollToEnd=false）时直接返回，
        /// 不再把视图拽回底部（否则流式期间的每次更新都会「抢回」滚动条）。force=true 时才无条件滚到底。
        /// </summary>
        public void EnsureLastTurnVisible(bool force = false)
        {
            if (items.Count == 0) return;
            if (!force && !autoScrollToEnd) return;
            // 先按当前缓存滚动到底（近似定位，行可能因校正变高）
            ScrollToEnd();
            // 实例化末尾条目并校正高度后可能超出视口，再滚一次并置光标
            var lastItem = items[^1];
            Relayout();
            if (realized.TryGetValue(lastItem, out var row))
            {
                row.PlaceCaretEnd();
                ScrollToEnd();
            }
        }

        /// <summary>选中满足条件的回话。</summary>
        public void SelectTurns(Func<ChatTurn, bool> predicate)
        {
            if (DataSource == null) return;
            foreach (var turn in DataSource.Turns)
                turn.Selected = predicate(turn);
            SyncRowCheckStates();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>同步所有已实例化行的复选框显示（数据侧 Selected 变化后调用）。</summary>
        public void SyncRowCheckStates()
        {
            foreach (var row in realized.Values)
                row.SyncCheckState();
        }

        /// <summary>取得回话在数据集中的索引。</summary>
        public int IndexOfTurn(ChatTurn turn) => DataSource?.Turns.ToList().IndexOf(turn) ?? -1;

        /// <summary>诊断信息：每行 FCTB 需求高度 vs 实际分配高度（差值&gt;2 表示文本被裁剪）。</summary>
        public string GetLayoutDiagnostics()
        {
            var sb = new System.Text.StringBuilder();
            sb.Append($"items={items.Count} realized={realized.Count} pool={rowPool.Count} totalH={totalHeight} view={ClientSize.Height} vbar={(vbar.Visible ? vbar.Value.ToString() : "-")}");
            foreach (var (item, row) in realized)
            {
                if (!row.Visible) continue;
                itemIndex.TryGetValue(item, out int idx);
                int clipped = row.GetClipping();
                sb.Append($" | [{idx}]{(item.IsUser ? "U" : "A")} y={row.Top} h={row.Height} clip={(clipped == 0 ? "OK" : clipped.ToString())} md={(row.IsMarkdownRendered ? "1" : "0")} think={(row.ThinkingScrolls ? "scroll" : "ok")}");
            }
            return sb.ToString();
        }

        #endregion 公共操作

        #region 事件处理

        private void Row_SelectionToggled(ChatFlowItem item, bool checkedState)
        {
            // 勾选任一条 → 整个回话（问+答）联动选中；不完整回话只选已有条目
            item.Turn.Selected = checkedState;
            SyncRowCheckStates();
            SelectionChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>子行 FCTB 转发来的滚轮事件统一处理（避免行内滚动/缩放）。</summary>
        internal void ForwardWheel(int delta, bool ctrl)
        {
            if (ctrl)
            {
                float next = delta > 0 ? chatFont.Size + 1 : Math.Max(6, chatFont.Size - 1);
                if (Math.Abs(next - chatFont.Size) > 0.01f)
                    ChatFont = new Font(chatFont.FontFamily, next, chatFont.Style);
            }
            else if (vbar.Visible)
            {
                int lines = delta > 0 ? -vbar.SmallChange * 3 : vbar.SmallChange * 3;
                SetScrollValueInternal(Math.Max(vbar.Minimum, Math.Min(MaxScrollValue, vbar.Value + lines)));
                // 滚到底部恢复自动跟随；**向上滚（或任何离开末尾的动作）立即暂停自动跟随**
                autoScrollToEnd = vbar.Value >= MaxScrollValue;
                Relayout();
                UpdateJumpToEndButton();
                UserScrolled?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ForwardWheel(e.Delta, ModifierKeys == Keys.Control);
            if (e is HandledMouseEventArgs hme) hme.Handled = true;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (inLayout) return;
            // 宽度变化会改变换行 → 旧高度全部失效。释放已实例化行强制按新宽度重新 Bind 测量，
            // 否则残留旧高度/仅用估算，宽高不匹配会在底部留下大片空白（要向上滚才看得到文字）。
            heightCache.Clear();
            foreach (var row in realized.Values.ToArray())
                ReleaseRow(row);
            UpdateScrollAndLayout();
            LayoutJumpToEndButton();
        }

        /// <summary>
        /// 顶部的「思考与工具过程」折叠头替身：原折叠头被滚出可见区后在这里顶替它，
        /// 文案/字体/横向位置/行高都与行内折叠头一致，点一下即折叠那一块。
        /// </summary>
        private sealed class StickyThinkingBar : Control
        {
            private readonly Font headerFont = new("Microsoft YaHei UI", 7.5F);

            /// <summary>文字左侧内缩（像素）：与行内折叠头的 x 对齐，底色仍铺满整行。</summary>
            public int TextIndent { get; set; }

            public StickyThinkingBar()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint, true);
                Font = headerFont;   // 与行内折叠头同字体
                Cursor = Cursors.Hand;
                TabStop = false;
                Visible = false;
            }

            /// <summary>滚轮转发给宿主（替身条悬在正文之上，滚轮仍应滚动聊天区）。</summary>
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                (Parent as ChatFlowControl)?.ForwardWheel(e.Delta, ModifierKeys == Keys.Control);
                if (e is HandledMouseEventArgs hme) hme.Handled = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                using (var back = new SolidBrush(BackColor))
                    e.Graphics.FillRectangle(back, ClientRectangle);
                int indent = Math.Min(TextIndent, Math.Max(0, Width - 10));
                var rect = new Rectangle(indent, 0, Math.Max(10, Width - indent), Height);
                TextRenderer.DrawText(e.Graphics, Text, headerFont, rect, ForeColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) headerFont.Dispose();
                base.Dispose(disposing);
            }
        }

        /// <summary>
        /// 「跳到末尾」浮动按钮：圆形底 + 向下箭头（复用 arrow_up_color_16xLG 旋转 180°）。
        /// 用圆形 Region 裁掉方形边角 —— 深/浅底上都不会出现难看的透明方块边；配色取当前调色板。
        /// </summary>
        private sealed class JumpToEndButton : Control
        {
            private bool hover;

            public JumpToEndButton()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
                Size = new Size(36, 36);
                Cursor = Cursors.Hand;
                TabStop = false;
                BackColor = Color.Transparent;
                Region = new Region(CirclePath(new Rectangle(Point.Empty, Size)));
            }

            private int S(int value) => (int)Math.Round(value * DeviceDpi / 96f);

            protected override void OnHandleCreated(EventArgs e)
            {
                base.OnHandleCreated(e);
                Size = new Size(S(36), S(36));
                Region = new Region(CirclePath(new Rectangle(Point.Empty, Size)));
            }

            protected override void OnSizeChanged(EventArgs e)
            {
                base.OnSizeChanged(e);
                Region = new Region(CirclePath(new Rectangle(Point.Empty, Size)));
            }

            private static System.Drawing.Drawing2D.GraphicsPath CirclePath(Rectangle r)
            {
                var path = new System.Drawing.Drawing2D.GraphicsPath();
                path.AddEllipse(r);
                return path;
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

            /// <summary>滚轮转发给宿主（按钮悬浮在内容之上，滚轮仍应滚动聊天区）。</summary>
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                (Parent as ChatFlowControl)?.ForwardWheel(e.Delta, ModifierKeys == Keys.Control);
                if (e is HandledMouseEventArgs hme) hme.Handled = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var p = ChatTheme.Current;
                Color fill = p == null ? (hover ? Color.Gainsboro : Color.WhiteSmoke)
                                       : (hover ? p.MenuHover : p.Control);
                Color border = p?.Border ?? Color.Gray;
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                using (var br = new SolidBrush(fill)) g.FillEllipse(br, 0, 0, Width - 1, Height - 1);
                using (var pen = new Pen(border)) g.DrawEllipse(pen, 0, 0, Width - 1, Height - 1);

                                float cx = Width / 2f, cy = Height / 2f;
                using (var pen = new Pen(p?.Fore ?? Color.DimGray, Math.Max(1f, S(2) * 0.8f)))
                {
                    pen.StartCap = System.Drawing.Drawing2D.LineCap.Round;
                    pen.EndCap = System.Drawing.Drawing2D.LineCap.Round;
                    g.DrawLines(pen, new[]
                    {
                        new PointF(cx - S(4), cy - S(3)),
                        new PointF(cx, cy + S(2)),
                        new PointF(cx + S(4), cy - S(3))
                    });
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (items.Count == 0)
            {
                using var br = new SolidBrush(Color.DimGray);
                string tip = PlaceholderText;
                var sz = e.Graphics.MeasureString(tip, chatFont);
                e.Graphics.DrawString(tip, chatFont, br, (ClientSize.Width - sz.Width) / 2, 10);
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            heightCache.Clear();
            UpdateScrollAndLayout();
        }

        #endregion 事件处理
    }

    /// <summary>一条可显示的聊天消息（回话的一部分：用户/助手）。</summary>
    public enum ChatFlowItemKind
    {
        User,
        Assistant,
        Card,
        Link,
        /// <summary>末尾的「思考中…」动画行（仅执行进行中出现，不承载消息内容）。</summary>
        Thinking,
    }

    /// <summary>聊天区内可点击链接被点击的事件参数（条目 + 种类 + 目标文本）。</summary>
        public sealed class ChatSurveyEventArgs : EventArgs
    {
        public ChatSurveyEventArgs(ChatSurvey survey, Dictionary<string, string> answers)
        {
            Survey = survey;
            Answers = answers;
        }

        public ChatSurvey Survey { get; }
        public Dictionary<string, string> Answers { get; }
    }

    /// <summary>聊天区操作（撤回/复制）事件参数：目标问答对。</summary>
    public sealed class ChatFlowActionEventArgs : EventArgs
    {
        public ChatFlowActionEventArgs(ChatTurn turn) => Turn = turn;

        public ChatTurn Turn { get; }
    }

    /// <summary>右键「操作」子菜单的一项：Key 由宿主定义并派发，Text 直接显示。</summary>
    public sealed record ChatSelectionAction(string Key, string Text);

    public sealed class ChatSelectionActionEventArgs : EventArgs
    {
        public ChatSelectionActionEventArgs(string key) => Key = key;

        public string Key { get; }
    }

    public sealed class ChatSurveyToggledEventArgs : EventArgs
    {
        public ChatSurveyToggledEventArgs(ChatSurvey survey, bool expanded)
        {
            Survey = survey;
            Expanded = expanded;
        }

        public ChatSurvey Survey { get; }
        public bool Expanded { get; }
    }
public sealed class ChatLinkClickedEventArgs : EventArgs
    {
        public ChatLinkClickedEventArgs(ChatFlowItem item, ChatTextStyler.ChatLinkKind kind, string target)
        {
            Item = item;
            Kind = kind;
            Target = target;
        }

        /// <summary>被点击链接所在的条目（行已回收时为 null 的兜底不会发生：点击必然发生在已实例化行上）。</summary>
        public ChatFlowItem Item { get; }

        public ChatTextStyler.ChatLinkKind Kind { get; }

        /// <summary>URL 文本或本地文件路径（与聊天区内显示的文本一致）。</summary>
        public string Target { get; }
    }

    /// <summary>一条可显示的聊天消息（回话的一半：用户或助手）。</summary>
    public class ChatFlowItem    {
        public ChatFlowItem(ChatTurn turn, ChatFlowItemKind kind)
        {
            Turn = turn;
            Kind = kind;
        }

        public ChatTurn Turn { get; }
        public ChatFlowItemKind Kind { get; }
        internal bool IsPairEnd { get; set; }
        /// <summary>超长正文被拆片后的分片文本；null = 按 Turn 生成。</summary>
        internal string TextOverride { get; set; }
        /// <summary>超长思维链被拆片后的分片文本；null = 按 Turn 生成。</summary>
        internal string ThinkingOverride { get; set; }
        /// <summary>思维链的后续分片：不再重复折叠头，展开时直接铺开。</summary>
        internal bool IsThinkingContinuation { get; set; }
        /// <summary>超长正文的后续分片：不重复显示思考区折叠头。</summary>
        internal bool IsContinuation { get; set; }
        public ChatSurvey Survey { get; init; }
        public ChatLink Link { get; init; }
        public bool IsUser => Kind == ChatFlowItemKind.User;

        /// <summary>展示文本：助手条目在正文前附上可见的工具调用行（每次调用一行），示意 Agent 发起了工具/助理调用。
        /// 流式期间**不堆积已完成工具行**（历史 bug：正文底部堆到 11 行，与思考区轨迹重复）——但正在执行的
        /// 工具显示「正在调用 X…」实时状态（<see cref="ChatTurn.ToolStatusText"/>），避免长耗时工具（生图等）
        /// 期间界面只有「思考中……」而无任何说明；执行结束后一次性前置完整工具行。</summary>
        public string Text
        {
            get
            {
                if (Kind == ChatFlowItemKind.Thinking) return string.Empty;
                if (TextOverride != null) return TextOverride;
                if (IsUser) return Turn.UserText;
                if (Turn.Streaming)
                    return string.IsNullOrEmpty(Turn.AssistantText) ? Turn.ToolStatusText ?? string.Empty : Turn.AssistantText;
                // 正文只放答复；思维链与工具轨迹进思考区（FCTB，默认折叠）
                return Turn.AssistantText;
            }
        }

        /// <summary>思考区内容（仅助手条目有；用户条目返回 null）。有时间线时整段原样进 FCTB，不做 markdown 渲染。</summary>
        public string ThinkingText
        {
            get
            {
                if (IsUser || Kind == ChatFlowItemKind.Thinking) return null;
                if (ThinkingOverride != null) return ThinkingOverride;
                if (IsContinuation) return null;
                if (!string.IsNullOrEmpty(Turn.TimelineText)) return Turn.TimelineText;
                var parts = new List<string>();
                if (!string.IsNullOrEmpty(Turn.ToolTraceText)) parts.Add(Turn.ToolTraceText);
                else if (!string.IsNullOrEmpty(Turn.ToolCallLines)) parts.Add(Turn.ToolCallLines);
                if (!string.IsNullOrEmpty(Turn.ReasoningText)) parts.Add(Turn.ReasoningText);
                return parts.Count == 0 ? null : string.Join("\n", parts);
            }
        }

        /// <summary>思考区折叠头文本：固定标题 + 本次任务汇总（步数与总耗时）。
        /// 纯推理回话没有工具步骤 → 退回旧行为：附上思考首行摘要，展开前也能看到内容开头。</summary>
        public string ThinkingHeaderText
        {
            get
            {
                const string title = "思考与工具过程";
                if (IsUser) return title;
                string stats = Turn.ToolStepCount > 0 ? $" · {Turn.ToolStepCount} 步" : "";
                if (Turn.ToolTotalMs > 0) stats += $" · {FormatDuration(Turn.ToolTotalMs)}";
                if (stats.Length > 0) return title + stats;
                string preview = FirstLine(ThinkingText);
                return preview == null ? title : $"{title} · {preview}";
            }
        }

        /// <summary>首行摘要（≤60 字符）；无内容返回 null。</summary>
        private static string FirstLine(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return null;
            string first = text.Split('\n')[0].Trim();
            if (first.Length == 0) return null;
            return first.Length <= 60 ? first : first[..60] + "…";
        }

        /// <summary>耗时展示：毫秒 / 秒（1 位小数）。</summary>
        private static string FormatDuration(long ms)
            => ms < 1000 ? $"{ms}ms" : $"{ms / 1000.0:0.0}s";

        /// <summary>思考区是否展开（数据侧持有，跨重建保留；null=未交互按时间线有无决定：
        /// 有时间线（思维与工具交替）默认展开，否则看不到交错过程；纯推理仍折叠）。</summary>
        public bool ThinkingExpanded => Turn.ThinkingExpanded ?? !string.IsNullOrEmpty(Turn.TimelineText);

        /// <summary>切换思考区展开状态（重排由调用方触发）。</summary>
        public void ToggleThinking()
        {
            Turn.ThinkingExpanded = !ThinkingExpanded;
        }
    }
}
