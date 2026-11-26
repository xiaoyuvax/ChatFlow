using System.Drawing.Drawing2D;

namespace ChatFlow
{
    /// <summary>
    /// 「回到顶部 / 跳到末尾」浮动按钮的悬停菜单：列出视口之前（或之后）每一轮的用户输入，
    /// 点一行就跳到那一轮开头。
    /// 轮的顺序与起始位置直接复用滚动条刻度表（<see cref="EnsureScrollTicks"/>）；菜单项记的是
    /// <see cref="ChatTurn"/> 引用而不是偏移 —— 流式期间内容随时长高，按引用现算落点才不会跳错轮。
    /// </summary>
    public partial class ChatFlowControl
    {
        /// <summary>菜单项文本的截断长度（字符）：要够长才认得出是哪一轮，超出加省略号。</summary>
        internal const int JumpMenuChars = 90;

        /// <summary>菜单最多同时显示多少行；再多的靠滚轮翻。</summary>
        private const int JumpMenuMaxRows = 12;

        /// <summary>悬停菜单浮层。</summary>
        private readonly JumpMenuPanel jumpMenu = new();

        /// <summary>菜单当前服务哪个按钮：true = 顶部按钮（列之前的轮次），false = 末尾按钮（列之后的轮次）。</summary>
        private bool jumpMenuForTop;

        /// <summary>宿主发起的轮次选择（/fork 用）：选中后回调哪一轮；不在选择模式时为空。</summary>
        private Action<ChatTurn> turnPickerPick;

        /// <summary>当前菜单是宿主发起的轮次选择（不是悬停跳转菜单）：点选走回调，且不跟鼠标离开收起。</summary>
        private bool turnPickerMode;

        /// <summary>悬停轮询：按钮与菜单之间有缝，靠它判「鼠标还在这一带」。</summary>
        private readonly System.Windows.Forms.Timer jumpHoverPoll = new() { Interval = 120 };

        /// <summary>构造期调用（两个浮动按钮建好之后）：建浮层、挂悬停入口。</summary>
        private void InitializeJumpMenu()
        {
            jumpMenu.Owner = this;
            jumpMenu.Visible = false;
            Controls.Add(jumpMenu);

            jumpToTopButton.MouseEnter += (_, _) => BeginJumpMenuHover(true);
            jumpToEndButton.MouseEnter += (_, _) => BeginJumpMenuHover(false);
            jumpHoverPoll.Tick += (_, _) => PollJumpHover();
            VisibleChanged += (_, _) => { if (!Visible) HideJumpMenu(); };
        }

        /// <summary>菜单是否展开。</summary>
        internal bool JumpMenuVisible => jumpMenu.Visible;

        /// <summary>菜单当前可见项（自上而下），测试与宿主观察用。</summary>
        internal IReadOnlyList<string> JumpMenuTexts => jumpMenu.Texts;

        /// <summary>鼠标悬到某个浮动按钮：展开它的关联菜单（该方向上没有别的轮次就不弹）。</summary>
        internal void BeginJumpMenuHover(bool fromTop)
        {
            if (IsDisposed || !vbar.Visible) return;
            turnPickerMode = false;
            turnPickerPick = null;
            var entries = BuildJumpMenuItems(fromTop);
            if (entries.Count == 0)
            {
                HideJumpMenu();
                return;
            }
            jumpMenuForTop = fromTop;
            jumpTip.Hide(fromTop ? jumpToTopButton : jumpToEndButton);   // 悬停提示别压在新弹出的菜单上
            jumpMenu.Open(entries);
            LayoutJumpMenu();
            jumpHoverPoll.Stop();
            jumpHoverPoll.Start();
        }

        /// <summary>收起菜单（离开按钮与菜单这一带、控件隐藏、或点了某一项之后）。</summary>
        internal void HideJumpMenu()
        {
            jumpHoverPoll.Stop();
            if (jumpMenu.Visible) jumpMenu.Close();
            turnPickerMode = false;
            turnPickerPick = null;
        }

        /// <summary>
        /// 宿主用（/fork）：在聊天区底部弹一轮次选择框，点一行回调那一轮，不跳转也不动滚动跟随。
        /// 清单与滚动条刻度同一口径：只列有用户输入的轮，最近的在最上面（第一行 = 全部上下文）。
        /// </summary>
        public void ShowTurnPicker(Action<ChatTurn> pick)
        {
            if (IsDisposed || pick == null || DataSource == null) return;
            var turns = new List<ChatTurn>();
            foreach (var turn in DataSource.Turns)
                if (turn?.User != null) turns.Add(turn);
            if (turns.Count == 0) return;

            var entries = new List<(ChatTurn Turn, string Text)>(turns.Count);
            for (int i = turns.Count - 1; i >= 0; i--)
                entries.Add((turns[i], (i + 1) + "/" + turns.Count + " · " + JumpMenuLabel(turns[i])));

            turnPickerMode = true;
            turnPickerPick = pick;
            jumpMenuForTop = false;
            jumpTip.Hide(jumpToEndButton);
            jumpMenu.Open(entries);
            LayoutJumpMenu();
            jumpHoverPoll.Stop();   // 选择框等用户点选：鼠标移开不收起
        }

        /// <summary>点了菜单某一项：选择模式下回调宿主，悬停菜单模式下照旧跳到那一轮。</summary>
        private void ActivateTurnMenu(ChatTurn turn)
        {
            var pick = turnPickerMode ? turnPickerPick : null;
            if (pick != null)
            {
                HideJumpMenu();
                pick(turn);
                return;
            }
            JumpToTurn(turn);
        }

        /// <summary>点菜单第 index 项：跳到那一轮开头（测试与宿主也可直接调）。</summary>
        internal void ClickJumpMenuItem(int index) => jumpMenu.Activate(index);

        /// <summary>跳到某一轮的开头：暂停自动跟随，把那一轮第一条贴到视口顶部。</summary>
        internal void JumpToTurn(ChatTurn turn)
        {
            if (IsDisposed || turn == null) return;
            autoScrollToEnd = false;
            HideJumpMenu();
            int idx = items.FindIndex(i => ReferenceEquals(i.Turn, turn));
            if (idx < 0 || prefixSum.Length < idx + 1) return;   // 这一轮已被删掉：原地不动
            if (vbar.Visible)
            {
                SetScrollValueInternal(Math.Min(prefixSum[idx], MaxScrollValue));
                Relayout();   // 可见行重测后前面几行的高度可能变，按新值再对齐一次
                SetScrollValueInternal(Math.Min(prefixSum[idx], MaxScrollValue));
            }
            else Relayout();
            UpdateJumpToEndButton();
            UpdateJumpToTopButton();
        }

        /// <summary>
        /// 当前视口一侧的每一轮做成菜单项：靠得最近的排第一（顶部按钮取由近及远的历史，
        /// 末尾按钮取由近及远的后续）。没有用户输入的轮次不出现在刻度表里，也就不进菜单。
        /// </summary>
        private List<(ChatTurn Turn, string Text)> BuildJumpMenuItems(bool before)
        {
            var result = new List<(ChatTurn Turn, string Text)>();
            var ticks = EnsureScrollTicks();
            int current = vbar.Value;
            if (before)
            {
                for (int i = ticks.Count - 1; i >= 0; i--)
                    if (ticks[i].Offset < current) result.Add((ticks[i].Turn, JumpMenuLabel(ticks[i].Turn)));
            }
            else
            {
                for (int i = 0; i < ticks.Count; i++)
                    if (ticks[i].Offset > current) result.Add((ticks[i].Turn, JumpMenuLabel(ticks[i].Turn)));
            }
            return result;
        }

        /// <summary>菜单标题：用户输入的一行摘要（换行压平，超长截断加省略号）。</summary>
        private static string JumpMenuLabel(ChatTurn turn) => CompactPreview(turn?.UserText, JumpMenuChars);

        /// <summary>菜单定位：顶部按钮的菜单排在按钮下方、右边缘对齐；末尾按钮的排在按钮上方、水平居中。</summary>
        private void LayoutJumpMenu()
        {
            if (!jumpMenu.Visible) return;
            int contentW = Math.Max(1, ClientSize.Width - (vbar.Visible ? vbar.Width : 0));
            var anchor = jumpMenuForTop ? jumpToTopButton : jumpToEndButton;
            int width = Math.Min(jumpMenu.PreferredWidth, Math.Max(Dip(120), contentW - Dip(16)));
            int height = Math.Min(jumpMenu.PreferredHeight,
                Math.Max(jumpMenu.RowHeight + Dip(8), ClientSize.Height - Dip(24)));
            int x, y;
            if (turnPickerMode)
            {
                // 宿主轮次选择：底部居中上弹（末尾按钮这时可能不显示，不能拿它当锚点）
                x = Math.Max(Dip(8), (contentW - width) / 2);
                y = ClientSize.Height - Dip(8) - height;
            }
            else if (jumpMenuForTop)
            {
                x = Math.Max(Dip(8), Math.Min(anchor.Left + anchor.Width - width, contentW - width - Dip(8)));
                y = anchor.Bottom + Dip(6);
            }
            else
            {
                x = Math.Max(Dip(8), (contentW - width) / 2);
                y = anchor.Top - Dip(6) - height;
            }
            y = Math.Clamp(y, Dip(4), Math.Max(Dip(4), ClientSize.Height - height - Dip(4)));
            jumpMenu.SetBounds(x, y, width, height);
            jumpMenu.BringToFront();
        }

        /// <summary>悬停轮询：鼠标离开按钮与菜单这一带就收起（两者之间有缝，按并集外扩一圈判）。</summary>
        private void PollJumpHover()
        {
            if (IsDisposed || !jumpMenu.Visible)
            {
                jumpHoverPoll.Stop();
                return;
            }
            var anchor = jumpMenuForTop ? jumpToTopButton : jumpToEndButton;
            if (!anchor.Visible)
            {
                HideJumpMenu();
                return;
            }
            var band = Rectangle.Union(RectangleToScreen(anchor.Bounds), RectangleToScreen(jumpMenu.Bounds));
            band.Inflate(Dip(6), Dip(6));
            if (!band.Contains(Cursor.Position)) HideJumpMenu();
        }

        /// <summary>
        /// 轮次选择菜单：白底圆角、浅色悬停块（与右键菜单同一套观感），自绘、跟随主题。
        /// 行数超过上限时滚轮翻，右侧一根细指示条。
        /// </summary>
        private sealed class JumpMenuPanel : Control
        {
            private readonly Font font = new("Microsoft YaHei UI", 9F);
            private readonly List<(ChatTurn Turn, string Text)> items = [];
            private int hoverIndex = -1;
            private int scrollOffset;

            internal ChatFlowControl Owner { get; set; }

            internal JumpMenuPanel()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint, true);
                SetStyle(ControlStyles.Selectable, false);
                TabStop = false;
                Visible = false;
                Cursor = Cursors.Hand;
            }

            /// <summary>浮层不抢焦点：点菜单时聊天区（以及它的选区/滚动）保持原样。</summary>
            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= 0x08000000;   // WS_EX_NOACTIVATE
                    return cp;
                }
            }

            private int Dip(int value) => (int)Math.Round(value * DeviceDpi / 96f);

            internal int RowHeight => font.Height + Dip(10);

            /// <summary>按实际高度能画几行（宿主可能把高度压下来）。</summary>
            private int VisibleRows => Height <= Dip(8)
                ? Math.Max(1, Math.Min(items.Count, JumpMenuMaxRows))
                : Math.Max(1, Math.Min(Math.Min(items.Count, JumpMenuMaxRows), (Height - Dip(8)) / RowHeight));

            /// <summary>按行数算出的高度。</summary>
            internal int PreferredHeight => Math.Max(1, Math.Min(items.Count, JumpMenuMaxRows)) * RowHeight + Dip(8);

            /// <summary>按最长一条标题算出的宽度（调用方再按可用宽度钳制）。</summary>
            internal int PreferredWidth
            {
                get
                {
                    int widest = 0;
                    foreach (var item in items)
                        widest = Math.Max(widest, TextRenderer.MeasureText(item.Text, font,
                            new Size(int.MaxValue, int.MaxValue),
                            TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width);
                    return Math.Max(Dip(160), widest + Dip(28));
                }
            }

            internal IReadOnlyList<string> Texts
            {
                get
                {
                    var list = new List<string>(items.Count);
                    foreach (var item in items) list.Add(item.Text);
                    return list;
                }
            }

            internal void Open(List<(ChatTurn Turn, string Text)> entries)
            {
                if (Parent != null) BackColor = Parent.BackColor;   // 圆角外露出的边角与聊天区同色
                items.Clear();
                items.AddRange(entries);
                hoverIndex = -1;
                scrollOffset = 0;
                Visible = true;
                Invalidate();
            }

            internal void Close()
            {
                if (Visible) Visible = false;
                items.Clear();
                hoverIndex = -1;
                scrollOffset = 0;
            }

            internal void Activate(int index)
            {
                if (index < 0 || index >= items.Count) return;
                Owner?.ActivateTurnMenu(items[index].Turn);
            }

            private int IndexAt(int y)
            {
                if (items.Count == 0) return -1;
                int index = scrollOffset + (y - Dip(4)) / RowHeight;
                return index < 0 || index >= items.Count ? -1 : index;
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int index = IndexAt(e.Y);
                if (index == hoverIndex) return;
                hoverIndex = index;
                Invalidate();
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (hoverIndex < 0) return;
                hoverIndex = -1;
                Invalidate();
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                Activate(IndexAt(e.Y));
            }

            /// <summary>滚轮先翻本列表；已经到头就把滚轮交给聊天区，免得在浮层上滚不动。</summary>
            protected override void OnMouseWheel(MouseEventArgs e)
            {
                int maxOffset = Math.Max(0, items.Count - VisibleRows);
                if (maxOffset > 0)
                {
                    int lines = Math.Max(1, SystemInformation.MouseWheelScrollLines);
                    int next = Math.Clamp(scrollOffset - Math.Sign(e.Delta) * lines, 0, maxOffset);
                    if (next != scrollOffset)
                    {
                        scrollOffset = next;
                        Invalidate();
                        if (e is HandledMouseEventArgs handled) handled.Handled = true;
                        return;
                    }
                }
                Owner?.ForwardWheel(e.Delta, ModifierKeys == Keys.Control);
                if (e is HandledMouseEventArgs wheel) wheel.Handled = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                var palette = ChatTheme.Current;
                Color back = palette?.Control ?? Color.White;
                Color fore = palette?.Fore ?? Color.FromArgb(0x1F, 0x1F, 0x1F);
                Color hover = palette?.MenuHover ?? Color.FromArgb(0xF0, 0xF2, 0xF5);
                Color border = palette?.Border ?? Color.FromArgb(0xE8, 0xE8, 0xE8);
                Color dim = palette?.ForeDim ?? Color.Gray;

                g.SmoothingMode = SmoothingMode.AntiAlias;
                var rect = new Rectangle(0, 0, Math.Max(2, Width - 1), Math.Max(2, Height - 1));
                using (var path = RoundRectPath(rect, Math.Max(2, Dip(8))))
                {
                    using (var brush = new SolidBrush(back)) g.FillPath(brush, path);
                    using (var pen = new Pen(border)) g.DrawPath(pen, path);
                }
                g.SmoothingMode = SmoothingMode.Default;

                int pad = Dip(4);
                int rowH = RowHeight;
                int rows = VisibleRows;
                bool scrollable = items.Count > rows;
                int rowW = Math.Max(1, Width - pad * 2 - (scrollable ? Dip(6) : 0));
                for (int i = 0; i < rows; i++)
                {
                    int index = scrollOffset + i;
                    if (index >= items.Count) break;
                    var rowRect = new Rectangle(pad, pad + i * rowH, rowW, rowH);
                    if (index == hoverIndex)
                    {
                        using var path = RoundRectPath(rowRect, Math.Max(2, Dip(4)));
                        using var brush = new SolidBrush(hover);
                        g.FillPath(brush, path);
                    }
                    var textRect = new Rectangle(rowRect.X + Dip(6), rowRect.Y,
                        Math.Max(1, rowRect.Width - Dip(12)), rowRect.Height);
                    TextRenderer.DrawText(g, items[index].Text, font, textRect, fore,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                        | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }

                if (scrollable) PaintScrollThumb(g, dim, pad, rows, rowH);
            }

            /// <summary>右侧细指示条：显示当前翻到整张菜单的哪一段。</summary>
            private void PaintScrollThumb(Graphics g, Color color, int pad, int rows, int rowH)
            {
                int trackLen = rows * rowH;
                int thumbLen = Math.Max(Dip(16), (int)Math.Round(trackLen * (double)rows / items.Count));
                int maxOffset = Math.Max(1, items.Count - rows);
                int thumbTop = pad + (int)Math.Round((trackLen - thumbLen) * (double)Math.Min(scrollOffset, maxOffset) / maxOffset);
                using var brush = new SolidBrush(Color.FromArgb(120, color));
                g.FillRectangle(brush, Width - pad - Dip(3), thumbTop, Math.Max(1, Dip(3)), thumbLen);
            }

            protected override void Dispose(bool disposing)
            {
                if (disposing) font.Dispose();
                base.Dispose(disposing);
            }
        }
    }
}
