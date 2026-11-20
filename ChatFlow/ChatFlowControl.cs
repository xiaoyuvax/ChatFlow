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

        /// <summary>已经用真实渲染量过高度的条目：校准时只重测估算出来的行。</summary>
        private readonly HashSet<ChatFlowItem> measuredItems = [];

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

        /// <summary>页面不在前台：只收数据，不做布局、行绑定与 markdown 重排。</summary>
        private bool suspendRendering;
        private bool renderDirty;

        /// <summary>本次浏览里用户点开/收起过的折叠区（轮 + 分段）；没点过的历史行向上回溯时默认折叠。</summary>
        private readonly HashSet<(ChatTurn Turn, int Phase)> thinkingTouched = [];

        private int totalHeight;
        private int[] prefixSum = [];
        private readonly Dictionary<ChatFlowItem, int> itemIndex = [];

        /// <summary>正文走 markdown 渲染的字符上限（默认 3000，超过改用带样式的纯文本）；宿主按设置覆盖，非正数回落默认。</summary>
        public static int MarkdownMaxChars
        {
            get => ChatMarkdown.MaxChars;
            set => ChatMarkdown.MaxChars = value > 0 ? value : 3000;
        }

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
            if (heightCache.Count > 0) ClearHeights();
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
            if (cardRows.Count > 0) { ClearHeights(); RequestRelayoutOfCards(); }
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
                ClearHeights();
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

        /// <summary>暂停渲染：会话页切走时只保留数据更新，不做布局、行绑定与 markdown 重排。
        /// 恢复时先回到底部，再按最新数据整表重建（流式输出随之继续）。</summary>
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool SuspendRendering
        {
            get => suspendRendering;
            set
            {
                if (suspendRendering == value) return;
                suspendRendering = value;
                if (value) return;
                autoScrollToEnd = true;
                if (renderDirty)
                {
                    renderDirty = false;
                    RebuildItems();
                }
                else ScrollToEnd();
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

            markdownDelay.Tick += (_, _) =>
            {
                markdownDelay.Stop();
                if (MarkdownReady || IsDisposed) return;
                MarkdownReady = true;
                UpgradeMarkdownRows();
            };

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
            if (!settingScrollInternally)
            {
                autoScrollToEnd = IsAtEnd;
                PauseMarkdown();   // 用户正在翻历史：先按纯文本排，停稳后再补 markdown
            }
            Relayout();
            UpdateJumpToEndButton();
        }

        /// <summary>「跳到末尾」：恢复自动跟随、按真实渲染重测一遍可见行高，再滚到底。</summary>
        private void JumpToEnd()
        {
            RemeasureLayout();   // 末尾正文常留着「量在渲染完成前」的偏小高度，滚到底前补一次
            ScrollToEnd();
            UpdateJumpToEndButton();
        }

        /// <summary>按当前滚动位置显隐并定位浮动按钮（用户滚离末尾、跟随已暂停时才显示）。
        /// 不能按「当前不在末尾」判：流式正文每拍都在长，跟随滚到底之后的几毫秒里也「不在末尾」，
        /// 按钮会一闪一闪地出现；而按钮一旦出现就意味着「用户正在读，别再滚」，所以只认跟随状态。</summary>
        private void UpdateJumpToEndButton()
        {
            if (jumpToEndButton == null || IsDisposed) return;
            bool show = vbar.Visible && !autoScrollToEnd;
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
            var item = stickyTarget;
            if (item?.Turn == null) return;
            // 只收起点中的这一段：分段之后整轮共用一个开关会连累别的折叠区
            thinkingTouched.Add((item.Turn, item.Phase));
            if (item.ThinkingExpanded) item.ToggleThinking();
            autoScrollToEnd = false;
            DataSource?.RaiseChanged();   // → RebuildItems：行高与条目全量重建
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
            if (disposing) markdownDelay.Dispose();
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
        internal const int MaxItemChars = 10_000;

        internal static List<string> SplitLongText(string text)
        {
            var parts = new List<string>();
            if (string.IsNullOrEmpty(text)) { parts.Add(""); return parts; }

            // 行首位置 + 该行是否在 ``` 围栏内。只在这些位置断开，分片里的围栏才能自己配平；
            // 否则下一片会从代码块中间开始，围栏错位后被当成新的代码块，正文被染色/多出折叠区。
            var lineStarts = new List<(int Pos, bool Inside)>();
            bool inFence = false;
            for (int i = 0; i < text.Length; i++)
            {
                if (i == 0 || text[i - 1] == '\n')
                {
                    lineStarts.Add((i, inFence));
                    if (i + 2 < text.Length && text[i] == '`' && text[i + 1] == '`' && text[i + 2] == '`')
                        inFence = !inFence;
                }
            }

            int start = 0;
            string carry = "";   // 上一片在围栏内硬断时补的闭合围栏；这一片开头要重新打开
            while (start < text.Length)
            {
                int budget = MaxItemChars - carry.Length;
                if (text.Length - start <= budget)
                {
                    parts.Add(carry + text[start..]);
                    break;
                }

                int limit = start + budget;
                int split = SafeSplitBefore(lineStarts, start, limit);
                if (split > start + budget / 2)
                {
                    parts.Add(carry + text[start..split]);
                    carry = "";
                    start = split;
                    continue;
                }

                // 整窗都在同一个围栏里（超长代码块）：就地断，末尾补闭合、下一片开头补开局，
                // 两片各自是合法代码块；否则后半段的围栏配对会整体错位。
                int nl = text.LastIndexOf('\n', limit - 1, budget);
                int cut = nl > start + budget / 2 ? nl + 1 : limit;
                bool inside = InsideFence(lineStarts, cut);
                parts.Add(carry + text[start..cut] + (inside ? (cut > 0 && text[cut - 1] == '\n' ? "```\n" : "\n```\n") : ""));
                carry = inside ? "```\n" : "";
                start = cut;
            }
            return parts;
        }

        /// <summary>窗口内最后一个「围栏外」的行首位置；没有返回 -1。</summary>
        private static int SafeSplitBefore(List<(int Pos, bool Inside)> lineStarts, int start, int limit)
        {
            int lo = 0, hi = lineStarts.Count - 1, best = -1;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (lineStarts[mid].Pos <= limit) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            for (int i = best; i >= 0; i--)
            {
                if (lineStarts[i].Pos <= start) break;
                if (!lineStarts[i].Inside) return lineStarts[i].Pos;
            }
            return -1;
        }

        private static bool InsideFence(List<(int Pos, bool Inside)> lineStarts, int offset)
        {
            int lo = 0, hi = lineStarts.Count - 1, best = 0;
            while (lo <= hi)
            {
                int mid = (lo + hi) / 2;
                if (lineStarts[mid].Pos <= offset) { best = mid; lo = mid + 1; }
                else hi = mid - 1;
            }
            return lineStarts.Count > 0 && lineStarts[best].Inside;
        }

        /// <summary>加一条助手条（phase=-1 表示不分段，按整轮显示）。返回实际加进去的最后一片；
        /// 流式空占位返回 null（不加条目，位置留给末尾指示行）。</summary>
        private ChatFlowItem AddAssistantPiece(ChatTurn turn, int phase)
        {
            var message = new ChatFlowItem(turn, ChatFlowItemKind.Assistant) { Phase = phase };
            string answer = message.Text;
            string thinking = message.ThinkingText;
            if (turn.Streaming && answer.Length == 0 && thinking == null)
                return null;   // 还没出字也没有思考区：这一行只会是「空白文本框」

            if (thinking != null && thinking.Length > MaxItemChars && message.ThinkingExpanded)
            {
                // 展开的长思维链按行拆片：单片仍远低于整行高度上限（Win32 把控件位置钳在 ±32767），
                // 这样展开后不需要内部滚动条。后续分片不再显示折叠头，答复挂在最后一片。
                // 流式期间同样要切：不切的话一条会涨成几万像素高，整行定位触发 ±32767 钳位，
                // 重排校正再也收敛不了（界面表现为卡死）。
                var parts = SplitLongText(thinking);
                ChatFlowItem last = null;
                for (int i = 0; i < parts.Count; i++)
                {
                    ChatFlowItem piece = i == 0
                        ? message
                        : new ChatFlowItem(turn, ChatFlowItemKind.Assistant)
                        {
                            IsContinuation = true,
                            IsThinkingContinuation = true,
                            Phase = phase
                        };
                    piece.ThinkingOverride = parts[i];
                    piece.TextOverride = i == parts.Count - 1 ? answer : "";
                    items.Add(piece);
                    last = piece;
                }
                return last;
            }

            if (answer.Length > MaxItemChars)
            {
                // 单条太高会撞 Win32 的 32767 像素定位上限（控件 Y 被钳到 -32768），
                // 表现为滚到底也看不到末尾内容；按行边界拆成多片，每片都远低于上限。
                // 流式期间也要切，理由同上（一行涨太高会让重排校正循环收敛不了）。
                var parts = SplitLongText(answer);
                ChatFlowItem last = null;
                for (int p = 0; p < parts.Count; p++)
                {
                    ChatFlowItem piece = p == 0
                        ? message
                        : new ChatFlowItem(turn, ChatFlowItemKind.Assistant) { IsContinuation = true, Phase = phase };
                    piece.TextOverride = parts[p];
                    items.Add(piece);
                    last = piece;
                }
                return last;
            }

            items.Add(message);
            return message;
        }

        private void RebuildItems()
        {
            if (suspendRendering) { renderDirty = true; return; }
            // 重建会换掉全部条目对象并清空高度缓存：先记住视口顶部是哪一轮，重建后钉回去。
            // 否则用户往上翻历史时，任何一次刷新（流式增量、宿主整表刷新）都会按估算重排，
            // 把眼前的正文弹到别处（用户侧表现：一拖滚动条就弹回某一行、开头文字选不住）。
            var anchor = !autoScrollToEnd ? CaptureTurnAnchor() : null;
            items.Clear();
            ClearHeights();
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
                        // 有分段就一段一条：正文之后的新思维因此落到新的折叠区、排在正文下面；旧数据退回整轮一条
                        if (turn.Phases.Count > 0)
                        {
                            ChatFlowItem last = null;
                            for (int p = 0; p < turn.Phases.Count; p++)
                                last = AddAssistantPiece(turn, p) ?? last;
                            lastMessage = last;
                        }
                        else lastMessage = AddAssistantPiece(turn, -1);
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

                // 执行进行中：末尾常驻「工作中…」指示行（正文已出字也保留，后台等待时用户仍能看到状态）；执行结束移除
                var streamingTurn = DataSource.Turns.LastOrDefault();
                if (streamingTurn?.Streaming == true)
                    items.Add(new ChatFlowItem(streamingTurn, ChatFlowItemKind.Thinking));
            }

            UpdateScrollAndLayout();
            if (anchor is { } a) RestoreTurnAnchor(a);
            if (autoScrollToEnd) ScrollToEnd();
        }

        /// <summary>视口顶部所在的轮，以及轮顶到视口顶的像素偏移。
        /// 必须按整轮算：长回答会被切成多个分片，只按单片算、还原时又会落到第一片上，
        /// 视口就被拽回这一轮的开头。偏移必须是像素而不是比例：比例会随这一轮变高
        /// （流式正文一直在长）不断变大，用户停在末尾上方阅读时文字会被一路推着滚。</summary>
        private (ChatTurn Turn, int Offset)? CaptureTurnAnchor()
        {
            if (!vbar.Visible || items.Count == 0 || prefixSum.Length < items.Count + 1) return null;
            int idx = Math.Min(FindFirstIndexAt(vbar.Value), items.Count - 1);
            if (idx < 0) return null;
            var turn = items[idx].Turn;
            if (turn == null) return null;

            int start = TurnBlockStart(idx);
            return (turn, Math.Max(0, vbar.Value - prefixSum[start]));
        }

        private void RestoreTurnAnchor((ChatTurn Turn, int Offset) anchor)
        {
            int idx = items.FindIndex(i => ReferenceEquals(i.Turn, anchor.Turn));
            if (idx < 0)
            {
                Relayout();
                return;
            }
            int start = TurnBlockStart(idx);
            int end = TurnBlockEnd(start);
            // 偏移落在轮内：这一轮变矮（折叠/估算收紧）时不出轮，也不越过末尾
            int target = Math.Min(prefixSum[end + 1], prefixSum[start] + anchor.Offset);
            SetScrollValueInternal(Math.Min(MaxScrollValue, target));
            Relayout();
        }

        /// <summary>同一轮的条目是连续的（用户行 / 正文各分片 / 卡片 / 链接），取整段的范围。</summary>
        private int TurnBlockStart(int idx)
        {
            var turn = items[idx].Turn;
            while (idx > 0 && ReferenceEquals(items[idx - 1].Turn, turn)) idx--;
            return idx;
        }

        private int TurnBlockEnd(int idx)
        {
            var turn = items[idx].Turn;
            while (idx + 1 < items.Count && ReferenceEquals(items[idx + 1].Turn, turn)) idx++;
            return idx;
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
            // 估算偏太多会让「实测 ↔ 估算」切换时整屏跳位，所以字符宽/行高都要按 DPI 缩放，
            // 且换行符只切行、不再额外加一行（否则长段落按换行数翻倍，估算会比实测大一倍）。
            float scale = DeviceDpi / 96f;
            int usable = Math.Max(40, RowTextWidth - 8);
            int charW = Math.Max(6, (int)Math.Round(chatFont.Size * 0.62f * scale));
            int perLine = Math.Max(10, usable / charW);
            int lineH = Math.Max(12, (int)Math.Round((chatFont.Size * 1.55f + 6) * scale));
            int lines = CountLines(item.Text, perLine);
            // 折叠思考区只显示一行摘要；展开时计入思考区文本
            if (item.ThinkingText != null)
                lines += item.ThinkingExpanded ? CountLines(item.ThinkingText, perLine) : 1;
            h = lines * lineH + 2 + 8 + ChatMessageRow.RoleIndent + 10;
            heightCache[item] = h;   // 记忆估算（宽度/字体/结构变化时缓存已清）
            return h;
        }

        /// <summary>按每行可容纳的单位数（半角=1，CJK=2）数行数；换行符只切行、不额外加行。</summary>
        private static int CountLines(string text, int perLine)
        {
            if (string.IsNullOrEmpty(text)) return 1;
            int lines = 1, units = 0;
            foreach (char c in text)
            {
                if (c == '\n')
                {
                    lines++;
                    units = 0;
                    continue;
                }
                units += c > 127 ? 2 : 1;
                if (units <= perLine) continue;
                lines++;
                units -= perLine;
            }
            return lines;
        }

        /// <summary>高度缓存失效（宽度/字体/结构/主题变化）：估算值与实测量一起作废。</summary>
        private void ClearHeights()
        {
            heightCache.Clear();
            measuredItems.Clear();
        }

        // markdown 块渲染极慢（CDS.Markdown 实测约 0.3ms/字符，一段 400 字要 130ms），
        // 快速拖动时每进入视口一行就卡一次。所以滚动期间按纯文本渲染（FCTB 只要几毫秒），
        // 停稳 180ms 后再把可见行补成 markdown。
        private readonly System.Windows.Forms.Timer markdownDelay = new() { Interval = 180 };

        /// <summary>现在是否允许把行渲成 markdown（滚动停稳后才允许）。</summary>
        internal bool MarkdownReady { get; private set; } = true;

        private void PauseMarkdown()
        {
            MarkdownReady = false;
            markdownDelay.Stop();
            markdownDelay.Start();
        }

        private void UpgradeMarkdownRows()
        {
            if (IsDisposed || suspendRendering || items.Count == 0) return;
            // 只把已经实例化的行重升成 markdown，且高度按实测回填：整份高度缓存作废会让
            // 所有行在「估算 ↔ 实测」之间来回跳，滚动位置跟着被弹到别处。
            var anchor = CaptureAnchor();
            int width = vbar.Visible ? Width - vbar.Width : Width;
            Point cursor = PointToClient(Cursor.Position);
            bool changed = false;
            foreach (var (item, row) in realized.ToArray())
            {
                // 用户正在这段里选字（或鼠标停在上面）：重绑会清掉选区/光标，别动它
                if (row.IsMarkdownRendered || row.HasTextSelection || row.Bounds.Contains(cursor)) continue;
                if (!ChatMarkdown.UseMarkdown(item.Text, item.Turn.Streaming)) continue;
                int measured = row.Bind(item, chatFont, width);
                measuredItems.Add(item);
                if (measured <= 0) continue;
                if (heightCache.TryGetValue(item, out int cached) && measured == cached) continue;
                heightCache[item] = measured;
                changed = true;
            }
            if (!changed)
            {
                Relayout();
                return;
            }
            // 只有可见行的高度变了：把视口顶部的条目钉住，别让用户眼前的正文被顶走
            RebuildSums();
            UpdateScrollbar();
            RestoreAnchor(anchor);
            Relayout();
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
            // 顺序不能反：LargeChange 的读取值会被「当前」Maximum 钳住（getter = min(stored, Maximum-Minimum+1)），
            // 先设 LargeChange 再抬 Maximum 会拿到上一次的偏小值，滚动上限就短一截 —— 末尾正文永远滚不到。
            int lc = Math.Max(10, visibleH / 2);
            vbar.Maximum = Math.Min(Math.Max(totalHeight - 1, 1), maxScroll + lc - 1);
            vbar.LargeChange = lc;
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
            if (suspendRendering) { renderDirty = true; return; }
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

        /// <summary>行内 markdown 渲染完成（RichTextBox 内容尺寸变化）后补一次重测：
        /// Bind 当刻量到的高度可能还没算进渲染后的换行/图片，滚动范围会短一截，
        /// 表现为拖到底仍有正文被裁。合并成一次，且高度没变就不重排。</summary>
        internal void RequestRelayoutDeferred(ChatMessageRow row)
        {
            if (row?.Item == null || !IsHandleCreated || !relayoutPending.Add(row)) return;
            BeginInvoke(new Action(() =>
            {
                relayoutPending.Remove(row);
                var item = row.Item;
                if (IsDisposed || suspendRendering || item == null) return;
                int height = row.Remeasure(recalcCode: false);
                if (heightCache.TryGetValue(item, out int cached) && height == cached) return;
                RequestRelayout(row);
            }));
        }

        private readonly HashSet<ChatMessageRow> relayoutPending = [];

        /// <summary>行内折叠（代码块展开/收起）后：重测该行高度并重排，行高随折叠收缩/展开。</summary>
        internal void RequestRelayout(ChatMessageRow row)
        {
            if (suspendRendering) { renderDirty = true; return; }
            var anchor = CaptureAnchor();
            if (row?.Item != null)
            {
                heightCache[row.Item] = row.Remeasure();
                measuredItems.Add(row.Item);
            }
            RebuildSums();
            UpdateScrollbar();
            RestoreAnchor(anchor);
            Relayout();
        }

        private bool inLayout;

        /// <summary>视口顶部所在的条目与条目内偏移：高度变化后据此恢复位置，不把用户眼前的正文顶走。</summary>
        private (int Anchor, int Offset) CaptureAnchor()
        {
            if (!vbar.Visible || items.Count == 0) return (0, 0);
            int top = vbar.Value;
            int anchor = Math.Min(FindFirstIndexAt(top), items.Count - 1);
            return (anchor, top - prefixSum[anchor]);
        }

        private void RestoreAnchor((int Anchor, int Offset) anchor)
        {
            if (autoScrollToEnd || items.Count == 0) return;
            int index = Math.Min(anchor.Anchor, items.Count - 1);
            SetScrollValueInternal(Math.Min(MaxScrollValue, prefixSum[index] + anchor.Offset));
        }

        /// <summary>上一次排版的正文行宽：滚动条显隐会改它，变了就得重测全部行高。</summary>
        private int lastLayoutWidth = -1;

        private void RelayoutCore()
        {
            if (items.Count == 0)
            {
                foreach (var row in realized.Values.ToArray()) ReleaseRow(row);
                Invalidate();
                return;
            }

            // 视口顶部的条目先记下来：行高从估算变实测（或反过来）会让前缀和整体变化，
            // 不把它钉住的话，用户拖到某个位置会被弹到别处（正是「滚到正文开头就弹回」）。
            var anchor = CaptureAnchor();

            // 滚动条显隐会改变行宽（宽度 → 换行 → 高度）：旧高度全部作废。
            // 不作废就会按旧宽度少算末尾高度，滚动条到底了仍有一截正文看不到。
            int layoutWidth = vbar.Visible ? Width - vbar.Width : Width;
            if (layoutWidth != lastLayoutWidth)
            {
                lastLayoutWidth = layoutWidth;
                ClearHeights();
                RebuildSums();
                UpdateScrollbar();
            }

            bool corrected;
            bool anyCorrection = false;
            int guard = 0;
            do
            {
                corrected = false;
                guard++;

                int viewTop = vbar.Visible ? vbar.Value : 0;
                int viewH = Math.Max(1, ClientSize.Height);
                // 只留一小圈余量：markdown 行每条要几十毫秒渲染，多预算半屏等于每次滚动多渲两行（快速拖动卡顿主因）。
                // 但也不能贴边回收：行一被回收，里面的选区就没了（用户选好正文再轻滚一下就白选）。
                int buffer = Math.Max(Dip(40), viewH / 4);

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
                            measuredItems.Add(item);
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
                            measuredItems.Add(item);
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
                            measuredItems.Add(item);
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
                        // 向上回溯历史时新填出来的行默认收起思考区：展开的长过程整屏都要排版，
                        // 用户本次浏览里点过的折叠区保留自己的选择。
                        if (item.Kind == ChatFlowItemKind.Assistant && !autoScrollToEnd
                            && !thinkingTouched.Contains((item.Turn, item.Phase)))
                            item.ForceThinkingCollapsed = true;
                        row = AcquireRow();
                        int measured = row.Bind(item, chatFont, rowWidth);
                        measuredItems.Add(item);   // 真实渲染量过了，后续校正不必再重测
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
                    else if (!measuredItems.Contains(item))
                    {
                        // 高度被作废过（宽度/结构变化）就补测：只测还没用真实渲染量过的行。
                        // 放在第一轮而不是只在校正轮：否则这一屏的行永远停在粗估高度，底部正文被裁。
                        int measured = row.Remeasure();
                        measuredItems.Add(item);
                        if (measured != cached)
                        {
                            heightCache[item] = measured;
                            corrected = true;
                        }
                    }
                    row.SetBounds(0, y - viewTop, rowWidth, prefixSum[i + 1] - prefixSum[i]);
                    y = prefixSum[i + 1];
                }

                if (corrected)
                {
                    anyCorrection = true;
                    if (guard < 6)
                    {
                        // 高度校正后前缀和/滚动范围失效，重建后进入下一轮定位
                        RebuildSums();
                        UpdateScrollbar();
                    }
                }
            } while (corrected && guard < 6);

            if (anyCorrection)
            {
                // 先按实测后的高度把视口顶部钉回原处，再按最终前缀和摆行位置。
                // 否则行停在旧偏移上，滚动条已按新高度到底，正文却还差一截（末尾截断），
                // 或者整屏被顶到别处（用户拖动时看到的「弹回」）。
                RestoreAnchor(anchor);
                int viewTop = vbar.Visible ? vbar.Value : 0;
                int finalWidth = vbar.Visible ? Width - vbar.Width : Width;
                foreach (var (item, row) in realized.ToArray())
                {
                    if (!itemIndex.TryGetValue(item, out int idx)) continue;
                    row.SetBounds(0, prefixSum[idx] - viewTop, finalWidth, prefixSum[idx + 1] - prefixSum[idx]);
                }
            }
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
            newRow.ChildFocused += OnRowFocused;
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
            if (item.Turn != null) thinkingTouched.Add((item.Turn, item.Phase));
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

        /// <summary>滚动到底部（跟随最新对话）：到底即恢复自动跟随，「跳到末尾」按钮随之隐藏。</summary>
        public void ScrollToEnd()
        {
            autoScrollToEnd = true;
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

        /// <summary>按真实渲染重测一遍可见行高并重排（跟随末尾时重排后仍停在末尾）。
        /// 末尾正文的高度常在 markdown/隐藏态还没排完时就被量走，缓存留着偏小的值，
        /// 表现是滚到底最后几行仍被裁；会话停止后用户点进聊天区或点「跳到末尾」时补这一刀。</summary>
        public void RemeasureLayout()
        {
            if (suspendRendering) { renderDirty = true; return; }
            if (items.Count == 0 || inLayout) return;
            ClearHeights();
            UpdateScrollAndLayout();
        }

        /// <summary>行内控件取到焦点：停在末尾且没有流式输出，说明用户正在看最新正文，补一次重测。</summary>
        private void OnRowFocused(object sender, EventArgs e)
        {
            if (!autoScrollToEnd) return;
            if (DataSource?.Turns.LastOrDefault()?.Streaming == true) return;
            RemeasureLayout();
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
        /// fullText 非空时写回 turn.AssistantText（宿主只在收尾时自己写）；同一行已实例化就地重绑，未实例化则什么都不做，
        /// 避免每 250ms 整表重建把用户往上翻的位置弹走。force=true 跳过节流（阶段切换）。</summary>
        public void UpdateStreamingMessage(ChatTurn turn, string fullText, bool force = false)
        {
            // 宿主（Loop / 旁路）只在收尾时写 turn.AssistantText，流式正文只交给这里：
            // 不落到数据上的话助手条目一直不存在，下面 item == null 的分支会每 250ms 整表重建一次
            // （用户侧：正文不显示、一拖滚动条就弹回、选区被清掉）。
            if (fullText != null) turn.AssistantText = fullText;

            // 页面不在前台：数据照常更新（切回来要看到最新），渲染等恢复后再做
            if (suspendRendering) { renderDirty = true; return; }

            // 长正文会被拆成多个分片：只有最后一片在视口底部，取第一片时它早已滚出屏幕，等于每拍都整表重建
            var item = items.LastOrDefault(i => i.Turn == turn && i.Kind == ChatFlowItemKind.Assistant);
            // 分段变了（正文之后又出思维 → 多一段）：条目对不上就整表重建，让新折叠区挂到正文下面
            int wantedPhase = turn.Phases.Count - 1;   // 没有分段时是 -1
            if (item != null && item.Phase != wantedPhase)
            {
                RebuildItems();
                item = items.LastOrDefault(i => i.Turn == turn && i.Kind == ChatFlowItemKind.Assistant);
                if (item == null) return;
            }
            if (item == null)
            {
                RebuildItems();
                item = items.LastOrDefault(i => i.Turn == turn && i.Kind == ChatFlowItemKind.Assistant);
                if (item == null) return;
            }
            // 超长思维链/正文拆片后，末片存的是拆分那一刻的快照：就地重绑只会把旧文本再画一遍，
            // 界面停在拆片处、要折叠再展开才更新。重建一次让末片按新文本重新切分。
            if (item.IsContinuation)
            {
                RebuildItems();
                item = items.LastOrDefault(i => i.Turn == turn && i.Kind == ChatFlowItemKind.Assistant);
                if (item == null) return;
            }

            // 「工作中…」指示行与当前最后一轮的流式状态保持一致：流式期间常驻末尾，执行结束才整表重建把它删掉
            bool indicatorPresent = items.Any(i => i.Turn == turn && i.Kind == ChatFlowItemKind.Thinking);
            bool indicatorExpected = turn.Streaming && ReferenceEquals(turn, DataSource?.Turns.LastOrDefault());
            if (indicatorPresent != indicatorExpected)
            {
                RebuildItems();
                return;
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
                measuredItems.Add(item);
                RebuildSums();
                UpdateScrollbar();
                Relayout();
            }
            // 行未实例化 = 用户已经往上翻、流式行在视口外：数据侧是最新的，滚回来时 Bind 自然带上全文。
            // 这里绝不能整表重建——那会清空高度缓存、换掉全部条目，把用户眼前的正文每 250ms 弹走一次。

            if (autoScrollToEnd)
            {
                ScrollToEnd();
                // 末尾指示行不是正文行：取最后一条正文分片来落光标
                var last = items.LastOrDefault(i => i.Kind == ChatFlowItemKind.Assistant);
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
            // 末尾可能是「工作中…」指示行（非正文），光标要落在最后一条正文分片上
            var lastItem = items.LastOrDefault(i => i.Kind == ChatFlowItemKind.Assistant) ?? items[^1];
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
                int target = Math.Max(vbar.Minimum, Math.Min(MaxScrollValue, vbar.Value + lines));
                // 先定跟随状态再滚：重排（ValueChanged 里触发）会按它决定是否贴底
                // 滚到底部恢复自动跟随；**向上滚（或任何离开末尾的动作）立即暂停自动跟随**
                autoScrollToEnd = target >= MaxScrollValue;
                PauseMarkdown();   // 滚轮翻历史：同用户拖动，先按纯文本排
                SetScrollValueInternal(target);
                // 重排与浮动按钮由 vbar_ValueChanged 完成；这里再调一次等于每格滚轮重排两遍
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
            ClearHeights();
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
            ClearHeights();
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
        /// <summary>分段序号（-1 = 不分段，按整轮显示）。</summary>
        internal int Phase { get; init; } = -1;
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
        /// 流式期间**不堆积已完成工具行**（历史 bug：正文底部堆到 11 行，与思考区轨迹重复）——正文为空时
        /// 只显示正在执行的工具行（<see cref="ChatTurn.ToolStatusText"/>，并行批次多行齐显、整批清除），
        /// 避免长耗时工具（生图等）期间界面只有「思考中……」而无任何说明；执行结束后一次性前置完整工具行。</summary>
        public string Text
        {
            get
            {
                if (Kind == ChatFlowItemKind.Thinking) return string.Empty;
                if (TextOverride != null) return TextOverride;
                if (Phase >= 0)
                {
                    string phaseAnswer = Phase < Turn.Phases.Count ? Turn.Phases[Phase].Answer : string.Empty;
                    // 末段正文还没出字时，正文区照旧显示正在执行的工具行（与不分段时的流式分支一致）
                    if (phaseAnswer.Length == 0 && Turn.Streaming && Phase == Turn.Phases.Count - 1)
                        return Turn.ToolStatusText ?? string.Empty;
                    return phaseAnswer;
                }
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
                if (Phase >= 0)
                {
                    string thinking = Phase < Turn.Phases.Count ? Turn.Phases[Phase].Thinking : null;
                    return string.IsNullOrEmpty(thinking) ? null : thinking;
                }
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
                if (Phase > 0)
                {
                    // 后续分段不再沿用整轮步数统计：只标它自己的首行摘要
                    string phasePreview = FirstLine(ThinkingText);
                    return phasePreview == null ? title : $"{title} · {phasePreview}";
                }
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

        /// <summary>本次浏览里被强制折叠（向上回溯时新填出的历史行）：只影响显示，不改数据侧记录。</summary>
        internal bool ForceThinkingCollapsed { get; set; }

        /// <summary>思考区是否展开（数据侧持有，跨重建保留；null=未交互按时间线有无决定：
        /// 有时间线（思维与工具交替）默认展开，否则看不到交错过程；纯推理仍折叠）。</summary>
        public bool ThinkingExpanded
            => !ForceThinkingCollapsed && (Phase >= 0
                ? (Turn.PhaseExpanded.TryGetValue(Phase, out bool expanded) ? expanded : DefaultThinkingExpanded)
                : Turn.ThinkingExpanded ?? DefaultThinkingExpanded);

        /// <summary>没交互过的默认值：宿主的设置优先；没设时有时间线（思维与工具交替）就展开，纯推理仍折叠。</summary>
        private bool DefaultThinkingExpanded => Turn.ThinkingExpanded ?? !string.IsNullOrEmpty(Turn.TimelineText);

        /// <summary>切换思考区展开状态（重排由调用方触发）。</summary>
        public void ToggleThinking()
        {
            // 先按用户看到的当前状态取反，再撤掉浏览期的强制折叠
            bool expand = !ThinkingExpanded;
            ForceThinkingCollapsed = false;
            // 每个折叠区各记各的：分段共用一个开关会让后面几段跟着一起开合
            if (Phase >= 0) Turn.PhaseExpanded[Phase] = expand;
            else Turn.ThinkingExpanded = expand;
        }
    }
}
