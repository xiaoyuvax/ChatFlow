using System.Drawing.Drawing2D;
using FastColoredTextBoxNS;
#if NET8_0_OR_GREATER
using CDS.Markdown;
#endif

namespace ChatFlow
{
    /// <summary>
    /// 单条聊天消息行：复选框 + 角色标签 + 只读 FCTB（高度=内容高度，无滚动条，滚轮转发给父 ChatFlowControl）。
    /// </summary>
    public class ChatMessageRow : Panel
    {
        public const int RoleIndent = 24;      // 角色标签行高度
        public const int TextRightSpace = 8;   // 文本右侧留白
        private const int RowVPad = 8;         // 行上下留白
        private const int ThinkingHeaderH = 18; // 思考区折叠头高度
        private const int FoldGutterWidth = 12; // FCTB 左侧折叠 gutter（○ 标记可见所需宽度）
        private const int ActionSize = 24;      // 右下角操作按钮边长
        private const int ActionGap = 4;        // 两个按钮间距
        private const int ActionZoneW = 132;    // 右下角悬停感应区宽
        private const int ActionZoneH = 40;     // 右下角悬停感应区高
        /// <summary>markdown 模式下代码段的等宽字体（正文字体同字号）。</summary>
        internal static readonly string CodeFontFamily = "Consolas";
        private readonly CheckBox chk;
        private readonly Label lblRole;
        private readonly Label lblThinking;     // 思考区折叠/展开头（无思考区时隐藏）
        private readonly RowTextBox txt;         // 正文
        private readonly RowTextBox txtThinking; // 思考区（可折叠，展开时显示在正文上方）
#if NET8_0_OR_GREATER
        private readonly MarkdownRowTextBox md;  // 正文 markdown 渲染（判定为 markdown 时替代 txt）
        private readonly Panel _mdWrap;          // md 的外壳：左侧露出与 FCTB 边框色一致的边距
        private readonly List<RowTextBox> _codeBoxes = new(); // markdown 模式下的围栏代码段（FCTB 按语言渲染）
#endif
        private bool markdownMode;
        private readonly ChatActionButton btnUndo;
        private readonly ChatActionButton btnCopy;
        private readonly System.Windows.Forms.Timer actionHide = new() { Interval = 150 };
        private readonly ChatFlowControl owner;

        private bool _binding;
        private int _contentWidth;               // 正文可用宽度（Bind 传入的宽度，避免复用行时用到旧 Width）
        private bool thinkingVisible;            // 当前条目是否有思考区
        private bool thinkingHeaderShown;        // 是否显示折叠头（后续分片不显示）
        private bool thinkingBoxShown;           // 是否显示思考区文本（不用 Control.Visible：行在池里时父级不可见会误判）
        private bool canUndo;
        private bool canCopy;
        private bool actionsShown;

        /// <summary>思考区展开状态切换（由折叠头点击触发）。</summary>
        public event Action<ChatFlowItem> ThinkingToggled;

        /// <summary>勾选状态变化（参数为勾选后的状态），由容器联动整回话。</summary>
        public event Action<ChatFlowItem, bool> SelectionToggled;

        /// <summary>点击可点击链接（URL / 图像文件路径）后触发；由容器转交宿主。</summary>
        public event Action<ChatFlowItem, ChatTextStyler.ChatLinkKind, string> LinkClicked;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public ChatFlowItem Item { get; internal set; }

        private int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96f);

        /// <summary>正文可用宽度（已扣右侧留白）。Bind 时用传入宽度，之后跟随行宽。</summary>
        private int ContentWidth => _contentWidth > 0
            ? _contentWidth
            : Math.Max(Dip(20), Width - Dip(TextRightSpace));

        private static ChatPalette P => ChatTheme.Current;   // null = 浅色（沿用原配色）

        public ChatMessageRow(ChatFlowControl owner)
        {
            this.owner = owner;
            BackColor = Color.Transparent;

            chk = new CheckBox
            {
                AutoSize = false,
                Size = new Size(Dip(20), Dip(20)),
                Location = new Point(Dip(2), Dip(2)),
                TabStop = false,
            };
            chk.CheckedChanged += (s, e) =>
            {
                if (!_binding)
                    SelectionToggled?.Invoke(Item, chk.Checked);
            };

            lblRole = new Label
            {
                AutoSize = false,
                Size = new Size(Dip(RoleIndent + 20), Dip(20)),
                Location = new Point(Dip(24), Dip(2)),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.DimGray,
                Font = new Font("Microsoft YaHei UI", 7.5F),
                Text = "?",
            };

            txt = new RowTextBox
            {
                ReadOnly = true,
                ShowScrollBars = false,
                WordWrap = true,
                ShowLineNumbers = false,
                TextAreaBorder = TextAreaBorderType.None,
                Paddings = new Padding(Dip(2)),
                LeftPadding = Dip(FoldGutterWidth), // 左侧 gutter：代码块折叠 ○ 标记需此宽度才可见可点
                UseCJK = CJKMode.Hanzi,
                TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit, // 消锯齿（双缓冲面下 ClearType 不可用）
                AllowProportionalFont = true, // 聊天区用比例字体（只读；Tab/列选择不可用）
            };
            txt.MouseWheelForwarded += (delta, ctrl) => owner.ForwardWheel(delta, ctrl);
            txt.FoldingToggled += () => owner.RequestRelayout(this);
            txt.LinkClicked += (kind, target) => LinkClicked?.Invoke(Item, kind, target);

            lblThinking = new Label
            {
                AutoSize = false,
                Size = new Size(Dip(200), Dip(18)),
                Location = new Point(Dip(RoleIndent), Dip(RoleIndent + 2)),
                TextAlign = ContentAlignment.MiddleLeft,
                ForeColor = Color.Gray,
                Font = new Font("Microsoft YaHei UI", 7.5F),
                Cursor = Cursors.Hand,
                Visible = false,
                Text = "▶ 思考与工具过程",
            };
            lblThinking.Click += (s, e) => ThinkingToggled?.Invoke(Item);

            txtThinking = new RowTextBox
            {
                ReadOnly = true,
                ShowScrollBars = false,
                WordWrap = true,
                ShowLineNumbers = false,
                TextAreaBorder = TextAreaBorderType.None,
                Paddings = new Padding(Dip(2)),
                LeftPadding = Dip(FoldGutterWidth),
                UseCJK = CJKMode.Hanzi,
                BackColor = owner.BackColor,
                Visible = false,
                TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit, // 消锯齿（双缓冲面下 ClearType 不可用）
                AllowProportionalFont = true, // 思考区同正文：比例字体
            };
            txtThinking.MouseWheelForwarded += (delta, ctrl) => owner.ForwardWheel(delta, ctrl);
            txtThinking.FoldingToggled += () => owner.RequestRelayout(this);
            txtThinking.LinkClicked += (kind, target) => LinkClicked?.Invoke(Item, kind, target);

            txt.ContextMenuRequested += p => owner.ShowChatMenu(Item, p);
            txtThinking.ContextMenuRequested += p => owner.ShowChatMenu(Item, p);

#if NET8_0_OR_GREATER
            md = new MarkdownRowTextBox
            {
                Visible = false,
                TabStop = false,
                BackColor = owner.BackColor,
            };
            md.MouseWheelForwarded += (delta, ctrl) => owner.ForwardWheel(delta, ctrl);
            md.LinkClicked += (kind, target) => LinkClicked?.Invoke(Item, kind, target);
            md.MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtons.Right)
                    owner.ShowChatMenu(Item, md.PointToScreen(e.Location));
            };
            HookActions(md);

            _mdWrap = new Panel
            {
                Visible = false,
                TabStop = false,
                BackColor = owner.BackColor,
            };
            //竖线：与 FCTB 的 LeftIndentLine 同位（LeftIndent-7，96DPI 像素）、同色（Silver）
            _mdWrap.Paint += (_, e) =>
            {
                using var pen = new Pen(Color.Silver);
                int lx = _mdWrap.Width - 7;
                e.Graphics.DrawLine(pen, lx, 0, lx, _mdWrap.Height - 1);
            };
            _mdWrap.MouseUp += (_, e) =>
            {
                if (e.Button == MouseButtons.Right)
                    owner.ShowChatMenu(Item, _mdWrap.PointToScreen(e.Location));
            };
#endif

            btnUndo = new ChatActionButton(ChatActionKind.Undo) { Visible = false, TabStop = false, Cursor = Cursors.Hand };
            btnCopy = new ChatActionButton(ChatActionKind.Copy) { Visible = false, TabStop = false, Cursor = Cursors.Hand };
            btnUndo.Click += (_, _) => { HideActions(); owner.RequestUndo(Item); };
            btnCopy.Click += (_, _) => { HideActions(); owner.RequestCopy(Item); };
            actionHide.Tick += (_, _) =>
            {
                actionHide.Stop();
                if (!PointerOnActions()) HideActions();
            };
            HookActions(txt);
            HookActions(txtThinking);
            foreach (var button in new[] { btnUndo, btnCopy })
            {
                button.MouseEnter += (_, _) => { actionHide.Stop(); ShowActions(); };
                button.MouseLeave += (_, _) => ScheduleHideActions();
            }

            Controls.Add(chk);
            Controls.Add(lblRole);
            Controls.Add(lblThinking);
            Controls.Add(txtThinking);
            Controls.Add(txt);
#if NET8_0_OR_GREATER
            Controls.Add(_mdWrap);
            Controls.Add(md);
#endif
            Controls.Add(btnUndo);
            Controls.Add(btnCopy);
        }

        /// <summary>绑定数据（池化复用时调用）。返回该行实际需要的高度（含角色栏与留白）。
        /// 测量基于本行 FCTB 真实渲染（句柄已创建，WordWrap 生效）。</summary>
        public int Bind(ChatFlowItem item, Font font, int reservedWidth)
        {
            _binding = true;
            _contentWidth = Math.Max(Dip(20), reservedWidth - Dip(TextRightSpace));
            // 行表面与勾选框底色：主题切换只重绑（OnAppThemeChanged → Bind），不会重建控件，
            // 池化行上一次主题残留的底色（ChatTheme 对快照后新建的行无从还原）必须在这里重贴
            BackColor = Color.Transparent;
            chk.BackColor = owner.BackColor;
            txt.BackColor = owner.BackColor;
            txtThinking.BackColor = owner.BackColor;
            canUndo = owner.CanUndo && item.IsPairEnd;
            canCopy = owner.CanCopy && item.IsPairEnd;
            HideActions();
            btnUndo.BackColor = owner.BackColor;
            btnCopy.BackColor = owner.BackColor;
            Item = item;
            ContextMenuStrip = owner.ContextMenuStrip;
            chk.ContextMenuStrip = owner.ContextMenuStrip;
            lblRole.ContextMenuStrip = owner.ContextMenuStrip;
            txt.ContextMenuStrip = owner.ContextMenuStrip;
            lblRole.Text = item.IsUser ? "用户" : (item.IsContinuation ? "AI（续）" : "AI");
            lblRole.ForeColor = P != null ? P.Accent : (item.IsUser ? Color.SteelBlue : Color.SeaGreen);
            bool useMarkdown =
#if NET8_0_OR_GREATER
                !item.Turn.Streaming && ChatMarkdown.IsMarkdown(item.Text)
                && ChatMarkdown.SplitSegments(item.Text).Count <= ChatMarkdown.MaxSegments;
#else
                false;
#endif
            // 提示词块整段缩进后再上样式（缩进只作用于显示文本，模型原文不动）
            string body = ChatTextStyler.IndentPromptBlocks(item.Text);
            if (useMarkdown)
            {
#if NET8_0_OR_GREATER
                md.BackColor = owner.BackColor;
                md.ForeColor = P != null ? P.ChatFore : Color.FromArgb(0x1B, 0x26, 0x33);
                md.Font = new Font(font, font.Style);
                md.Width = Math.Max(Dip(20), _contentWidth - Dip(FoldGutterWidth));
                _ = md.Handle;
                BindMarkdownSegments(item.Text, font, reservedWidth);
#endif
            }
            else
            {
                txt.SetFontPreserveZoom(font);
                txt.SetTextCaretEnd(body);
                ChatTextStyler.ApplyBody(txt, body);
            }
            ShowMarkdown(useMarkdown);
            chk.Checked = item.Turn.Selected;
            chk.Visible = _showSelection;
            txt.BackColor = owner.BackColor;
            txt.ForeColor = P != null ? P.ChatFore : Color.FromArgb(0x1B, 0x26, 0x33);
            txtThinking.ForeColor = P != null ? P.ChatThinkingFore : Color.FromArgb(0x5F, 0x6E, 0x7E);
            // 思考区底色与聊天背景一致，避免 FCTB 出现色块。
            txtThinking.BackColor = owner.BackColor;

            // 思考区：有内容才显示折叠头；执行中的「思考中…」由末尾的动画行统一提示。
            // 长思维链拆片后的后续分片没有折叠头，展开态下直接铺开。
            thinkingVisible = item.ThinkingText != null;
            thinkingHeaderShown = thinkingVisible && !item.IsThinkingContinuation;
            thinkingBoxShown = thinkingVisible && (item.ThinkingExpanded || item.IsThinkingContinuation);
            lblThinking.Visible = thinkingHeaderShown;
            lblThinking.Text = item.ThinkingExpanded
                ? "▼ " + item.ThinkingHeaderText + "（点击折叠）"
                : "▶ " + item.ThinkingHeaderText + "（点击展开）";
            lblThinking.ForeColor = P != null ? P.ForeDim : Color.Gray;
            if (thinkingBoxShown && item.ThinkingText != null)
            {
                txtThinking.SetFontPreserveZoom(GetThinkingFont(font));
                txtThinking.SetTextCaretEnd(item.ThinkingText);
                ChatTextStyler.ApplyThinking(txtThinking, item.ThinkingText);
                txtThinking.ContextMenuStrip = owner.ContextMenuStrip;
            }
            txtThinking.Visible = thinkingBoxShown;
            _binding = false;

            // 真实测量：本行在最终宽度下换行后的内容高度
            if (!markdownMode)
            {
                txt.Width = _contentWidth;
                _ = txt.Handle; // 强制创建句柄：FCTB 的 RecalcWordWrap 仅在 Created 时执行，否则换行被推迟
                txt.NeedRecalc(true, true);
            }
            if (thinkingBoxShown)
            {
                txtThinking.Width = _contentWidth;
                _ = txtThinking.Handle;
                txtThinking.NeedRecalc(true, true);
            }
            LayoutContent(); // 主动布局：复用行尺寸未变时 OnSizeChanged 不触发，靠这里刷新子控件位置
            return MeasureRow();
        }

        /// <summary>本行所需高度（含角色栏与留白）；布局测量唯一出口。</summary>
        private int MeasureRow()
        {
            int contentH = markdownMode
                ? MarkdownContentHeight()
                : txt.TextHeight + txt.Paddings.Vertical + Dip(4);
            if (thinkingHeaderShown)
            {
                contentH += Dip(ThinkingHeaderH); // 折叠头始终占高（折叠时也显示）
                if (thinkingBoxShown)
                    contentH += ThinkingBoxHeight();
            }
            else if (thinkingBoxShown)
            {
                // 后续分片没有折叠头，只有思考区
                contentH += ThinkingBoxHeight();
            }
            return contentH + Dip(RoleIndent) + Dip(2) + Dip(RowVPad);
        }

        /// <summary>整行高度的安全上限（设备像素）：Win32 把子窗口位置钳在 ±32767，行再高就摆不到位、底部内容永远滚不到。</summary>
        private const int MaxRowHeightPx = 30_000;

        private int ThinkingNeedHeight()
            => txtThinking.TextHeight + txtThinking.Paddings.Vertical + Dip(4);

        /// <summary>展开后的思考区高度：先按内容原样撑开，只有「思考区 + 正文」会超过整行安全上限时才封顶并在盒内滚动。
        /// 不能按视口高度封顶——那样普通长度的思维链也会白白出现内部滚动条。</summary>
        private int ThinkingBoxHeight()
        {
            int body = markdownMode
                ? MarkdownContentHeight()
                : txt.TextHeight + txt.Paddings.Vertical + Dip(4);
            int room = Math.Max(Dip(120), MaxRowHeightPx - body - Dip(ThinkingHeaderH) - Dip(RowVPad) - Dip(RoleIndent));
            return Math.Min(ThinkingNeedHeight(), room);
        }

#if NET8_0_OR_GREATER
        /// <summary>markdown 模式绑定：围栏代码段用等宽字体 FCTB 按语言渲染，其余散文交给 MarkdownTextBox。</summary>
        private void BindMarkdownSegments(string text, Font font, int reservedWidth)
        {
            var segments = ChatMarkdown.SplitSegments(text);
            var prose = segments.Where(s => !s.IsCode).Select(s => s.Content).ToList();
            md.SetMarkdown(string.Join("\n\n", prose));

            // 复用已有的代码段 FCTB（池化按索引对应，多余的移除，不足的新建）
            int codeCount = segments.Count(s => s.IsCode);
            while (_codeBoxes.Count > codeCount)
            {
                var last = _codeBoxes[^1];
                _codeBoxes.RemoveAt(_codeBoxes.Count - 1);
                Controls.Remove(last);
                last.Dispose();
            }
            foreach (var box in _codeBoxes) box.Visible = true;

            int idx = 0;
            foreach (var (isCode, content, lang) in segments)
            {
                if (!isCode) continue;
                var box = idx < _codeBoxes.Count ? _codeBoxes[idx] : null;
                if (box == null)
                {
                    box = CreateCodeBox();
                    _codeBoxes.Add(box);
                    Controls.Add(box);
                }
                idx++;
                box.BackColor = owner.BackColor;
                box.Font = new Font(CodeFontFamily, font.Size, font.Style);
                box.Width = _contentWidth;
                _ = box.Handle; // 强制创建句柄：RecalcWordWrap 仅在 Created 时执行
                var language = FastColoredTextBoxNS.Text.LanguageDetector.StringToLanguage(lang ?? "");
                if (box.Language != language)
                {
                    box.Language = language; // setter 内部重建样式并整段重算
                    box.Text = content;
                }
                else
                {
                    box.Text = content;
                }
                box.NeedRecalc(true, true);
            }
        }

        private RowTextBox CreateCodeBox()
        {
            var box = new RowTextBox
            {
                ReadOnly = true,
                ShowScrollBars = false,
                WordWrap = true,
                ShowLineNumbers = false,
                TextAreaBorder = TextAreaBorderType.None,
                Paddings = new Padding(Dip(2)),
                LeftPadding = Dip(FoldGutterWidth),
                UseCJK = CJKMode.Hanzi,
                TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit,
                Visible = false,
                TabStop = false,
            };
            // 折叠/展开后必须重测本行高度：否则行高停在旧值，展开的内容被矮行裁住（看起来点不开）
            box.FoldingToggled += () => owner.RequestRelayout(this);
            return box;
        }
#endif

        private int MarkdownContentHeight()
        {
#if NET8_0_OR_GREATER
            int start = md.SelectionStart;
            int length = md.SelectionLength;
            int height = md.GetPreferredContentHeight() + Dip(4);
            md.Select(start, length);
            // 与 LayoutContent 的纵向堆叠保持一致：每个代码段后面还有 Dip(4) 的间隔，漏算会把末尾裁掉。
            foreach (var box in _codeBoxes)
                if (box.Visible)
                    height += Math.Max(Dip(10), box.TextHeight + box.Paddings.Vertical + Dip(4)) + Dip(4);
            return height;
#else
            return 0;
#endif
        }

        private void ShowMarkdown(bool show)
        {
            markdownMode = show;
            txt.Visible = !show;
#if NET8_0_OR_GREATER
            md.Visible = show;
            _mdWrap.Visible = show;
            foreach (var box in _codeBoxes) box.Visible = show;
#endif
        }

        public void ApplyFont(Font font)
        {
            txt.SetFontPreserveZoom(font);
            if (txtThinking.Visible) txtThinking.SetFontPreserveZoom(GetThinkingFont(font));
#if NET8_0_OR_GREATER
            foreach (var box in _codeBoxes)
                box.SetFontPreserveZoom(new Font(CodeFontFamily, font.Size, font.Style));
#endif
        }

        /// <summary>思考区专用字体：比正文小一号、斜体，与正式答复明确区分。</summary>
        private Font GetThinkingFont(Font body)
            => new(body.FontFamily, Math.Max(6f, body.Size - 1f), FontStyle.Italic);

        /// <summary>流式更新文本（不重置复选框/角色）。光标置末尾，无选区。</summary>
        public void UpdateText(string fullText)
        {
            _binding = true;
            ShowMarkdown(false);
            string body = ChatTextStyler.IndentPromptBlocks(fullText);
            txt.SetTextCaretEnd(body);
            ChatTextStyler.ApplyBody(txt, body);
            _binding = false;
        }

        /// <summary>内部测试访问：正文/思考区 FCTB（供 STA 单测验证样式与折叠标记）。</summary>
        internal RowTextBox BodyBox => txt;
        internal RowTextBox ThinkingBox => txtThinking;
        internal bool IsMarkdownRendered => markdownMode;
#if NET8_0_OR_GREATER
        internal Control MarkdownBox => md;
        internal Control MarkdownWrap => _mdWrap;
        internal IReadOnlyList<RowTextBox> CodeBoxes => _codeBoxes;
#endif

        /// <summary>流式更新思考区文本（可见性/摘要/展开内容变化后主动重布局）。</summary>
        public void UpdateThinkingText(string thinkingText, bool expanded, bool streaming = false)
        {
            if (Item == null) return;
            bool visible = !string.IsNullOrEmpty(thinkingText);
            thinkingVisible = visible;
            thinkingHeaderShown = visible && !Item.IsThinkingContinuation;
            lblThinking.Visible = thinkingHeaderShown;
            if (!visible) { thinkingBoxShown = false; txtThinking.Visible = false; LayoutContent(); return; }
            lblThinking.Text = expanded
                ? "▼ " + Item.ThinkingHeaderText + "（点击折叠）"
                : "▶ " + Item.ThinkingHeaderText + "（点击展开）";
            lblThinking.ForeColor = P != null ? P.ForeDim : Color.Gray;
            thinkingBoxShown = visible && (expanded || Item.IsThinkingContinuation);
            txtThinking.Visible = thinkingBoxShown;
            if (thinkingBoxShown)
            {
                _binding = true;
                txtThinking.SetTextCaretEnd(thinkingText);
                ChatTextStyler.ApplyThinking(txtThinking, thinkingText);
                _binding = false;
            }
            LayoutContent();
        }

        /// <summary>光标置于最后字符，清除选区（不改变文本）。</summary>
        public void PlaceCaretEnd()
        {
            if (!markdownMode) txt.PlaceCaretEnd();
        }

        /// <summary>在当前宽度/字体下重测本行高度（含角色栏与留白）。</summary>
        public int Remeasure()
        {
            if (!markdownMode)
            {
                _ = txt.Handle;
                txt.NeedRecalc(true, true);
            }
#if NET8_0_OR_GREATER
            else
            {
                // 折叠/展开只改了行的可见状态，TextHeight 要等 FCTB 重算；不重算会量到旧高度
                foreach (var box in _codeBoxes)
                {
                    if (!box.Visible) continue;
                    _ = box.Handle;
                    box.NeedRecalc(true, true);
                }
            }
#endif
            if (txtThinking.Visible)
            {
                _ = txtThinking.Handle;
                txtThinking.NeedRecalc(true, true);
            }
            return MeasureRow();
        }

        /// <summary>展开的思考区当前是否带内部滚动条（诊断：内容超过整行安全上限时才会出现）。</summary>
        internal bool ThinkingScrolls => thinkingBoxShown && txtThinking.ShowScrollBars;

        /// <summary>诊断：正文文本框的分配高度与需求高度差值（正数=裁剪像素数，0=正常）。
        /// markdown 模式与「只有思考区」的行没有这个比较对象，直接算 0。</summary>
        public int GetClipping()
        {
            if (markdownMode) return 0;
            if (string.IsNullOrEmpty(txt.Text)) return 0;
            _ = txt.Handle;
            txt.NeedRecalc(true, true);
            int need = txt.TextHeight + txt.Paddings.Vertical + Dip(4);
            return Math.Max(0, need - txt.Height);
        }

        /// <summary>数据侧 Selected 变化后同步复选框。</summary>
        public void SyncCheckState()
        {
            if (Item == null) return;
            _binding = true;
            chk.Checked = Item.Turn.Selected;
            _binding = false;
        }

        private bool _showSelection;

        /// <summary>是否显示左侧勾选框（选择性保存等场景才开）。</summary>
        public bool ShowSelection
        {
            get => _showSelection;
            set
            {
                if (_showSelection == value) return;
                _showSelection = value;
                chk.Visible = value;
            }
        }

        public string GetSelectedText()
        {
#if NET8_0_OR_GREATER
            if (markdownMode) return md.SelectedText;
#endif
            return txt.SelectedText;
        }

        private void HookActions(Control control)
        {
            control.MouseEnter += (_, _) => UpdateActionZone(control, control.PointToClient(Cursor.Position));
            control.MouseMove += (_, e) => UpdateActionZone(control, e.Location);
            control.MouseLeave += (_, _) => ScheduleHideActions();
        }

        private void UpdateActionZone(Control control, Point point)
        {
            if (!canUndo && !canCopy) return;
            var rowPoint = new Point(control.Left + point.X, control.Top + point.Y);
            if (rowPoint.X >= Width - Dip(ActionZoneW) && rowPoint.Y >= Height - Dip(ActionZoneH))
                ShowActions();
            else
                ScheduleHideActions();
        }

        private void ShowActions()
        {
            if (actionsShown || (!canUndo && !canCopy)) return;
            actionHide.Stop();
            actionsShown = true;
            btnUndo.Visible = canUndo;
            btnCopy.Visible = canCopy;
            btnUndo.BringToFront();
            btnCopy.BringToFront();
        }

        private void HideActions()
        {
            actionHide.Stop();
            actionsShown = false;
            btnUndo.Visible = false;
            btnCopy.Visible = false;
        }

        private void ScheduleHideActions()
        {
            if (!actionsShown) return;
            actionHide.Stop();
            actionHide.Start();
        }

        private bool PointerOnActions()
        {
            Point point = PointToClient(Cursor.Position);
            return (btnUndo.Visible && btnUndo.Bounds.Contains(point))
                || (btnCopy.Visible && btnCopy.Bounds.Contains(point));
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right) owner.ShowChatMenu(Item, PointToScreen(e.Location));
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                actionHide.Stop();
                actionHide.Dispose();
#if NET8_0_OR_GREATER
                foreach (var box in _codeBoxes) box.Dispose();
                _codeBoxes.Clear();
#endif
            }
            base.Dispose(disposing);
        }

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            // 行宽变了：正文可用宽度随之更新，子控件与高度测量必须用同一个宽度
            if (Width > 0) _contentWidth = Math.Max(Dip(20), Width - Dip(TextRightSpace));
            LayoutContent();
        }

        /// <summary>子控件布局：折叠头 →（展开时）思考区 → 正文，正文永远在最下且 z 序最顶。
        /// 必须显式调用（Bind / 尺寸变化 / 思考区可见性切换），不能依赖 OnSizeChanged。</summary>
        private void LayoutContent()
        {
            chk.SetBounds(Dip(2), Dip(2), Dip(20), Dip(20));
            lblRole.SetBounds(Dip(24), Dip(2), Dip(RoleIndent + 20), Dip(20));

            int y = Dip(RoleIndent) + Dip(2);
            int contentW = ContentWidth;
            if (thinkingHeaderShown)
            {
                lblThinking.SetBounds(Dip(RoleIndent), y, Math.Max(Dip(100), contentW - Dip(RoleIndent)), Dip(ThinkingHeaderH));
                y += Dip(ThinkingHeaderH);
            }
            if (thinkingBoxShown)
            {
                // 先按最终宽度换行再量高度：宽度不同时 TextHeight 会变，高度算在小宽度上会把正文挤出本行
                if (txtThinking.Width != contentW)
                {
                    txtThinking.Width = contentW;
                    _ = txtThinking.Handle;
                    txtThinking.NeedRecalc(true, true);
                }
                int th = ThinkingBoxHeight();
                // 判断依据是「内容比可用空间高」，而不是当前 TextHeight（滚动条会占宽、改变换行，
                // 用当前 TextHeight 反推会出现「一旦出现就再也关不掉」的粘滞滚动条）。
                bool scrolls = ThinkingNeedHeight() > th;
                if (txtThinking.ShowScrollBars != scrolls) txtThinking.ShowScrollBars = scrolls;
                txtThinking.ScrollSelf = scrolls;
                txtThinking.SetBounds(0, y, contentW, th);
                y += th + Dip(4);
            }
            int bodyW = Math.Max(Dip(20), contentW);
            int bodyH = Math.Max(Dip(20), Height - y - Dip(RowVPad) / 2);
            txt.SetBounds(0, y, bodyW, bodyH);
#if NET8_0_OR_GREATER
            _mdWrap.SetBounds(0, y, Dip(FoldGutterWidth), bodyH);
            md.SetBounds(Dip(FoldGutterWidth), y, Math.Max(Dip(20), bodyW - Dip(FoldGutterWidth)), bodyH);
            _mdWrap.BringToFront();
            md.BringToFront();
            // markdown 模式下 md 与代码段 FCTB 纵向堆叠（各自高度=内容高度）
            if (markdownMode)
            {
                int stackY = y;
                int boxW = Math.Max(Dip(20), bodyW - Dip(FoldGutterWidth));
                int mdH = Math.Max(Dip(10), md.GetPreferredContentHeight() + Dip(4));
                md.SetBounds(Dip(FoldGutterWidth), stackY, boxW, mdH);
                stackY += mdH;
                foreach (var box in _codeBoxes)
                {
                    if (!box.Visible) continue;
                    box.SetBounds(0, stackY, bodyW, Math.Max(Dip(10), box.TextHeight + box.Paddings.Vertical + Dip(4)));
                    stackY += box.Height + Dip(4);
                    box.BringToFront();
                }
                _mdWrap.SetBounds(0, y, Dip(FoldGutterWidth), Math.Max(Dip(10), stackY - y));
            }
#endif

            int size = Dip(ActionSize);
            int gap = Dip(ActionGap);
            int count = (canUndo ? 1 : 0) + (canCopy ? 1 : 0);
            int total = count * size + (count > 1 ? gap : 0);
            int bx = Math.Max(0, contentW - total);
            int by = Math.Max(0, Height - Dip(RowVPad) - size - Dip(2));
            if (canUndo)
            {
                btnUndo.SetBounds(bx, by, size, size);
                bx += size + gap;
            }
            if (canCopy) btnCopy.SetBounds(bx, by, size, size);
            btnUndo.BackColor = owner.BackColor;
            btnCopy.BackColor = owner.BackColor;

            if (markdownMode)
            {
#if NET8_0_OR_GREATER
                md.BringToFront();
#endif
            }
            else
            {
                txt.BringToFront();
            }
            btnUndo.BringToFront();
            btnCopy.BringToFront();
        }
    }

    /// <FCTB 子类：滚轮全部转发父容器，禁止行内滚动/缩放。</summary>
    public class RowTextBox : FastColoredTextBox
    {
        public event Action<int, bool> MouseWheelForwarded;

        /// <summary>左侧折叠 gutter 内点击后触发（FCTB 已内部完成展开/收起，父容器据此重测行高）。</summary>
        public event Action FoldingToggled;

        /// <summary>点击可点击链接（URL / 本地文件路径）后触发；宿主决定怎么处理。</summary>
        public event Action<ChatTextStyler.ChatLinkKind, string> LinkClicked;

        /// <summary>右键请求菜单（参数为屏幕坐标）。</summary>
        public event Action<Point> ContextMenuRequested;

        /// <summary>当前文本中的可点击链接（ChatTextStyler 赋样式时一并算出，供命中测试）。</summary>
        internal List<ChatTextStyler.ChatLink> LinkSpans;

        /// <summary>true：滚轮滚动本控件自身（内容封顶时用）；false：转发给聊天容器。</summary>
        public bool ScrollSelf { get; set; }

        /// <summary>非链接区域的常规光标（FCTB 构造期设为 IBeam，在此捕获后按需还原）。</summary>
        private readonly Cursor plainCursor;

        public RowTextBox()
        {
            plainCursor = Cursor ?? Cursors.IBeam;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (ScrollSelf)
            {
                base.OnMouseWheel(e);   // 内容封顶：滚轮滚动本控件自身
                return;
            }
            if (e is HandledMouseEventArgs hme) hme.Handled = true;
            MouseWheelForwarded?.Invoke(e.Delta, ModifierKeys == Keys.Control);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            // 无链接行（多数）早退：连 PointToPosition 都不算
            if ((LinkSpans == null || LinkSpans.Count == 0) && Cursor != Cursors.Hand) return;
            // FCTB 自身在 OnMouseMove 里会把光标重置为 defaultCursor；此处后置赋值，
            // 且经公开 setter 写回（FCTB 重写 Cursor，setter 同步 defaultCursor），悬浮态才稳定
            bool link = ChatTextStyler.LinkAt(LinkSpans, PointToPosition(e.Location)) != null;
            if ((Cursor == Cursors.Hand) != link)
                Cursor = link ? Cursors.Hand : plainCursor;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e); // FCTB 在此命中折叠标记并展开/收起
            if (e.Button == MouseButtons.Left && e.X < LeftIndent)
                FoldingToggled?.Invoke();
            else if (e.Button == MouseButtons.Left)
            {
                var link = ChatTextStyler.LinkAt(LinkSpans, PointToPosition(e.Location));
                if (link != null) LinkClicked?.Invoke(link.Value.Kind, link.Value.Target);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Right)
                ContextMenuRequested?.Invoke(PointToScreen(e.Location));
        }

        /// <summary>应用外部字体（克隆，避免共享实例被 FCTB 释放）。</summary>
        public void SetFontPreserveZoom(Font font)
            => Font = new Font(font, font.Style);

        /// <summary>赋文本并把光标收到末尾（供流式填充与滚动定位）。
        /// 注：FCTB 的 Text setter 现已自行收拢选区（Vax-FCTB ≥2.19 起，见 FctbSelectionTests），
        /// 这里的 PlaceCaretEnd 是**另有用处**：同值赋文本时 setter 会提前 return，靠它把光标仍落到末尾。</summary>
        public void SetTextCaretEnd(string text)
        {
            Text = text ?? string.Empty;
            PlaceCaretEnd();
        }

        /// <summary>光标置于最后字符，清除选区（不改变文本）。</summary>
        public void PlaceCaretEnd()
        {
            GoEnd();
            Selection.Start = Selection.End;
        }
    }

#if NET8_0_OR_GREATER
    internal sealed class MarkdownRowTextBox : MarkdownTextBox
    {
        private const int WM_CONTEXTMENU = 0x007B;
        private const int WM_MOUSEWHEEL = 0x020A;

        public event Action<int, bool> MouseWheelForwarded;

        /// <summary>点击可点击链接（URL / 本地文件路径）后触发；宿主决定怎么处理。
        /// new：MarkdownTextBox 自己关掉了 DetectUrls，继承的 RichTextBox.LinkClicked 永不触发，这里整条接过来。</summary>
        public new event Action<ChatTextStyler.ChatLinkKind, string> LinkClicked;

        private List<ChatTextStyler.ChatLink> linkSpans;
        private readonly Cursor plainCursor;
        private Font linkFont;

        public MarkdownRowTextBox()
        {
            plainCursor = Cursor ?? Cursors.Default;
            // MarkdownTextBox 渲染完再把 URL / 图片路径标成超链接外观（它自己关掉了 DetectUrls）
            TextChanged += (_, _) => { linkSpans = null; ApplyLinkStyle(); };
        }

        /// <summary>当前渲染文本中的可点击链接（按纯文本偏移，与 GetCharIndexFromPosition 同坐标系）。</summary>
        private List<ChatTextStyler.ChatLink> LinkSpans => linkSpans ??= ChatTextStyler.CollectLinks(Text);

        private void ApplyLinkStyle()
        {
            var links = LinkSpans;
            if (links.Count == 0) return;
            Color accent = ChatTheme.Current != null ? ChatTheme.Current.Accent : Color.FromArgb(0x00, 0x66, 0xCC);
            linkFont ??= new Font(Font, Font.Style | FontStyle.Underline);
            int selStart = SelectionStart, selLength = SelectionLength;
            foreach (var link in links)
            {
                if (link.Start < 0 || link.Start + link.Length > TextLength) continue;
                Select(link.Start, link.Length);
                SelectionColor = accent;
                SelectionFont = linkFont;
            }
            Select(selStart, selLength);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            linkFont?.Dispose();
            linkFont = null;
            base.OnFontChanged(e);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool overLink = ChatTextStyler.LinkAt(LinkSpans, GetCharIndexFromPosition(e.Location)) != null;
            if ((Cursor == Cursors.Hand) != overLink)
                Cursor = overLink ? Cursors.Hand : plainCursor;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            var link = ChatTextStyler.LinkAt(LinkSpans, GetCharIndexFromPosition(e.Location));
            if (link != null) LinkClicked?.Invoke(link.Value.Kind, link.Value.Target);
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_CONTEXTMENU) return;
            if (m.Msg == WM_MOUSEWHEEL)
            {
                int delta = (short)((long)m.WParam >> 16);
                MouseWheelForwarded?.Invoke(delta, ModifierKeys == Keys.Control);
                return;
            }
            base.WndProc(ref m);
        }
    }
#endif

    internal enum ChatActionKind
    {
        Undo,
        Copy
    }

    /// <summary>Antd 风格的右下角圆形悬停操作按钮（只画图标）。</summary>
    internal sealed class ChatActionButton : Control
    {
        private bool hover;

        public ChatActionButton(ChatActionKind kind)
        {
            Kind = kind;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer, true);
            Size = new Size(24, 24);
            Cursor = Cursors.Hand;
            TabStop = false;
        }

        public ChatActionKind Kind { get; }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var palette = ChatTheme.Current;
            Color icon = palette?.ForeDim ?? Color.FromArgb(0x5F, 0x6E, 0x7E);
            Color hoverBack = palette?.MenuHover ?? Color.FromArgb(0xF0, 0xF2, 0xF5);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            if (hover)
            {
                using var path = RoundedRect(new Rectangle(1, 1, Width - 3, Height - 3), 4);
                using var brush = new SolidBrush(hoverBack);
                e.Graphics.FillPath(brush, path);
            }
            DrawIcon(e.Graphics, icon);
        }

        private void DrawIcon(Graphics g, Color color)
        {
            float cx = Width / 2f;
            float cy = Height / 2f;
            using var pen = new Pen(color, 1.6f);
            if (Kind == ChatActionKind.Copy)
            {
                g.DrawRectangle(pen, cx - 5, cy - 5, 7, 9);
                g.DrawRectangle(pen, cx - 1, cy - 1, 7, 9);
                return;
            }

            g.DrawArc(pen, cx - 5, cy - 5, 10, 10, 35, 285);
            using var brush = new SolidBrush(color);
            g.FillPolygon(brush,
            [
                new PointF(cx - 0.5f, cy - 6.5f),
                new PointF(cx - 5.5f, cy - 4.5f),
                new PointF(cx - 1.5f, cy - 1.5f)
            ]);
        }

        private static GraphicsPath RoundedRect(Rectangle rect, int radius)
        {
            var path = new GraphicsPath();
            int d = Math.Max(1, Math.Min(radius * 2, Math.Min(rect.Width, rect.Height)));
            path.AddArc(rect.Left, rect.Top, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Top, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.Left, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
