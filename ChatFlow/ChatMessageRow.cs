using FastColoredTextBoxNS;

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
        private readonly CheckBox chk;
        private readonly Label lblRole;
        private readonly Label lblThinking;     // 思考区折叠/展开头（无思考区时隐藏）
        private readonly RowTextBox txt;         // 正文
        private readonly RowTextBox txtThinking; // 思考区（可折叠，展开时显示在正文上方）
        private readonly ChatFlowControl owner;

        private bool _binding;
        private bool thinkingVisible;            // 当前条目是否有思考区

        /// <summary>思考区展开状态切换（由折叠头点击触发）。</summary>
        public event Action<ChatFlowItem> ThinkingToggled;

        /// <summary>勾选状态变化（参数为勾选后的状态），由容器联动整回话。</summary>
        public event Action<ChatFlowItem, bool> SelectionToggled;

        /// <summary>点击可点击链接（URL / 图像文件路径）后触发；由容器转交宿主。</summary>
        public event Action<ChatFlowItem, ChatTextStyler.ChatLinkKind, string> LinkClicked;

        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public ChatFlowItem Item { get; internal set; }

        private int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96f);

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
                BackColor = P != null ? P.Back : Color.FromArgb(243, 244, 246),   // 思考区：深色用更深的页面底色
                Visible = false,
                TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit, // 消锯齿（双缓冲面下 ClearType 不可用）
                AllowProportionalFont = true, // 思考区同正文：比例字体
            };
            txtThinking.MouseWheelForwarded += (delta, ctrl) => owner.ForwardWheel(delta, ctrl);
            txtThinking.FoldingToggled += () => owner.RequestRelayout(this);
            txtThinking.LinkClicked += (kind, target) => LinkClicked?.Invoke(Item, kind, target);

            Controls.Add(chk);
            Controls.Add(lblRole);
            Controls.Add(lblThinking);
            Controls.Add(txtThinking);
            Controls.Add(txt);
        }

        /// <summary>绑定数据（池化复用时调用）。返回该行实际需要的高度（含角色栏与留白）。
        /// 测量基于本行 FCTB 真实渲染（句柄已创建，WordWrap 生效）。</summary>
        public int Bind(ChatFlowItem item, Font font, int reservedWidth)
        {
            _binding = true;
            // 行表面与勾选框底色：主题切换只重绑（OnAppThemeChanged → Bind），不会重建控件，
            // 池化行上一次主题残留的底色（ChatTheme 对快照后新建的行无从还原）必须在这里重贴
            BackColor = Color.Transparent;
            chk.BackColor = P != null ? P.Back : Color.Transparent;
            Item = item;
            ContextMenuStrip = owner.ContextMenuStrip;
            chk.ContextMenuStrip = owner.ContextMenuStrip;
            lblRole.ContextMenuStrip = owner.ContextMenuStrip;
            txt.ContextMenuStrip = owner.ContextMenuStrip;
            lblRole.Text = item.IsUser ? "用户" : "AI";
            lblRole.ForeColor = P != null ? P.Accent : (item.IsUser ? Color.SteelBlue : Color.SeaGreen);
            txt.SetFontPreserveZoom(font);
            // 提示词块整段缩进后再上样式（缩进只作用于显示文本，模型原文不动）
            string body = ChatTextStyler.IndentPromptBlocks(item.Text);
            txt.SetTextCaretEnd(body);
            ChatTextStyler.ApplyBody(txt, body);
            chk.Checked = item.Turn.Selected;
            chk.Visible = _showSelection;
            txt.BackColor = P != null ? (item.IsUser ? P.InputBack : P.Control) : (item.IsUser ? Color.White : Color.FromArgb(248, 250, 252));
            txt.ForeColor = P != null ? P.ChatFore : Color.FromArgb(0x1B, 0x26, 0x33);
            txtThinking.ForeColor = P != null ? P.ChatThinkingFore : Color.FromArgb(0x5F, 0x6E, 0x7E);
            // 思考区底色同样按当前主题重贴（深色用更深的页面底色）：只在构造函数里定死的话，
            // 从深色切回浅色后思考区仍留着上次的深底（实测 bad case）。
            txtThinking.BackColor = P != null ? P.Back : Color.FromArgb(243, 244, 246);

            // 思考区：有内容或流式进行中才显示折叠头；展开时填充思考区文本
            bool streamingNoText = item.Turn.Streaming && string.IsNullOrEmpty(item.Text);
            thinkingVisible = item.ThinkingText != null || streamingNoText;
            lblThinking.Visible = thinkingVisible;
            lblThinking.Text = thinkingVisible && item.ThinkingText != null
                ? (item.ThinkingExpanded ? "▼ " + item.ThinkingHeaderText + "（点击折叠）"
                                         : "▶ " + item.ThinkingHeaderText + "（点击展开）")
                : "思考中……";
            lblThinking.ForeColor = P != null
                ? (thinkingVisible && item.ThinkingText != null ? P.ForeDim : P.Accent)
                : (thinkingVisible && item.ThinkingText != null ? Color.Gray : Color.DarkOrange);
            if (thinkingVisible && item.ThinkingExpanded && item.ThinkingText != null)
            {
                txtThinking.SetFontPreserveZoom(GetThinkingFont(font));
                txtThinking.SetTextCaretEnd(item.ThinkingText);
                ChatTextStyler.ApplyThinking(txtThinking, item.ThinkingText);
                txtThinking.ContextMenuStrip = owner.ContextMenuStrip;
            }
            txtThinking.Visible = thinkingVisible && item.ThinkingExpanded && item.ThinkingText != null;
            _binding = false;

            // 真实测量：本行 FCTB 在最终宽度下换行后的内容高度
            txt.Width = Math.Max(Dip(20), reservedWidth - Dip(TextRightSpace));
            _ = txt.Handle; // 强制创建句柄：FCTB 的 RecalcWordWrap 仅在 Created 时执行，否则换行被推迟
            txt.NeedRecalc(true, true);
            if (txtThinking.Visible)
            {
                txtThinking.Width = Math.Max(Dip(20), reservedWidth - Dip(TextRightSpace));
                _ = txtThinking.Handle;
                txtThinking.NeedRecalc(true, true);
            }
            LayoutContent(); // 主动布局：复用行尺寸未变时 OnSizeChanged 不触发，靠这里刷新子控件位置
            return MeasureRow();
        }

        /// <summary>本行所需高度（含角色栏与留白）；布局测量唯一出口。</summary>
        private int MeasureRow()
        {
            int contentH = txt.TextHeight + txt.Paddings.Vertical + Dip(4);
            if (lblThinking.Visible)
            {
                contentH += Dip(ThinkingHeaderH); // 折叠头始终占高（折叠时也显示）
                if (txtThinking.Visible)
                    contentH += txtThinking.TextHeight + txtThinking.Paddings.Vertical + Dip(4);
            }
            return contentH + Dip(RoleIndent) + Dip(2) + Dip(RowVPad);
        }

        public void ApplyFont(Font font)
        {
            txt.SetFontPreserveZoom(font);
            if (txtThinking.Visible) txtThinking.SetFontPreserveZoom(GetThinkingFont(font));
        }

        /// <summary>思考区专用字体：比正文小一号、斜体，与正式答复明确区分。</summary>
        private Font GetThinkingFont(Font body)
            => new(body.FontFamily, Math.Max(6f, body.Size - 1f), FontStyle.Italic);

        /// <summary>流式更新文本（不重置复选框/角色）。光标置末尾，无选区。</summary>
        public void UpdateText(string fullText)
        {
            _binding = true;
            string body = ChatTextStyler.IndentPromptBlocks(fullText);
            txt.SetTextCaretEnd(body);
            ChatTextStyler.ApplyBody(txt, body);
            _binding = false;
        }

        /// <summary>内部测试访问：正文/思考区 FCTB（供 STA 单测验证样式与折叠标记）。</summary>
        internal RowTextBox BodyBox => txt;
        internal RowTextBox ThinkingBox => txtThinking;

        /// <summary>流式更新思考区文本（可见性/摘要/展开内容变化后主动重布局）。</summary>
        public void UpdateThinkingText(string thinkingText, bool expanded, bool streaming = false)
        {
            if (Item == null) return;
            bool streamingNoText = streaming && string.IsNullOrEmpty(Item.Text);
            bool visible = !string.IsNullOrEmpty(thinkingText) || streamingNoText;
            thinkingVisible = visible;
            lblThinking.Visible = visible;
            if (!visible) { txtThinking.Visible = false; LayoutContent(); return; }
            lblThinking.Text = !string.IsNullOrEmpty(thinkingText)
                ? (expanded ? "▼ " + Item.ThinkingHeaderText + "（点击折叠）"
                            : "▶ " + Item.ThinkingHeaderText + "（点击展开）")
                : "思考中……";
            lblThinking.ForeColor = P != null
                ? (!string.IsNullOrEmpty(thinkingText) ? P.ForeDim : P.Accent)
                : (!string.IsNullOrEmpty(thinkingText) ? Color.Gray : Color.DarkOrange);
            txtThinking.Visible = visible && expanded && !string.IsNullOrEmpty(thinkingText);
            if (txtThinking.Visible)
            {
                _binding = true;
                txtThinking.SetTextCaretEnd(thinkingText);
                ChatTextStyler.ApplyThinking(txtThinking, thinkingText);
                _binding = false;
            }
            LayoutContent();
        }

        /// <summary>光标置于最后字符，清除选区（不改变文本）。</summary>
        public void PlaceCaretEnd() => txt.PlaceCaretEnd();

        /// <summary>在当前宽度/字体下重测本行高度（含角色栏与留白）。</summary>
        public int Remeasure()
        {
            _ = txt.Handle;
            txt.NeedRecalc(true, true);
            if (txtThinking.Visible)
            {
                _ = txtThinking.Handle;
                txtThinking.NeedRecalc(true, true);
            }
            return MeasureRow();
        }

        /// <summary>诊断：分配高度与 FCTB 需求高度的差值（正数=裁剪像素数，0=正常）。</summary>
        public int GetClipping()
        {
            _ = txt.Handle;
            txt.NeedRecalc(true, true);
            int need = txt.TextHeight + txt.Paddings.Vertical + Dip(4);
            if (txtThinking.Visible)
            {
                _ = txtThinking.Handle;
                txtThinking.NeedRecalc(true, true);
                need += txtThinking.TextHeight + txtThinking.Paddings.Vertical + Dip(ThinkingHeaderH) + Dip(4);
            }
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

        public string GetSelectedText() => txt.SelectedText;

        protected override void OnSizeChanged(EventArgs e)
        {
            base.OnSizeChanged(e);
            LayoutContent();
        }

        /// <summary>子控件布局：折叠头 →（展开时）思考区 → 正文，正文永远在最下且 z 序最顶。
        /// 必须显式调用（Bind / 尺寸变化 / 思考区可见性切换），不能依赖 OnSizeChanged。</summary>
        private void LayoutContent()
        {
            chk.SetBounds(Dip(2), Dip(2), Dip(20), Dip(20));
            lblRole.SetBounds(Dip(24), Dip(2), Dip(RoleIndent + 20), Dip(20));

            int y = Dip(RoleIndent) + Dip(2);
            if (thinkingVisible)
            {
                lblThinking.SetBounds(Dip(RoleIndent), y, Math.Max(Dip(100), Width - Dip(RoleIndent) - Dip(TextRightSpace)), Dip(ThinkingHeaderH));
                y += Dip(ThinkingHeaderH);
                if (txtThinking.Visible)
                {
                    int th = Math.Max(Dip(10), txtThinking.TextHeight + txtThinking.Paddings.Vertical + Dip(4));
                    txtThinking.SetBounds(0, y, Math.Max(Dip(20), Width - Dip(TextRightSpace)), th);
                    y += th + Dip(4);
                }
            }
            txt.SetBounds(0, y, Math.Max(Dip(20), Width - Dip(TextRightSpace)), Math.Max(Dip(20), Height - y - Dip(RowVPad) / 2));
            txt.BringToFront();
        }
    }

    /// <FCTB 子类：滚轮全部转发父容器，禁止行内滚动/缩放。</summary>
    public class RowTextBox : FastColoredTextBox
    {
        public event Action<int, bool> MouseWheelForwarded;

        /// <summary>左侧折叠 gutter 内点击后触发（FCTB 已内部完成展开/收起，父容器据此重测行高）。</summary>
        public event Action FoldingToggled;

        /// <summary>点击可点击链接（URL / 图像文件路径）后触发；宿主决定怎么处理。</summary>
        public event Action<ChatTextStyler.ChatLinkKind, string> LinkClicked;

        /// <summary>当前文本中的可点击链接（ChatTextStyler 赋样式时一并算出，供命中测试）。</summary>
        internal List<ChatTextStyler.ChatLink> LinkSpans;

        /// <summary>非链接区域的常规光标（FCTB 构造期设为 IBeam，在此捕获后按需还原）。</summary>
        private readonly Cursor plainCursor;

        public RowTextBox()
        {
            plainCursor = Cursor ?? Cursors.IBeam;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
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
}
