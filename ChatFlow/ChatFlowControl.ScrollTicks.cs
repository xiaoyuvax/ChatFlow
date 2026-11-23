using System.Drawing.Drawing2D;
using System.Text;

namespace ChatFlow
{
    /// <summary>
    /// 滚动条拖动时的「轮次刻度 + 预览浮窗」。
    /// 用户按住滚动条（拖动滑块、点箭头、点轨道）才亮起：滚动条轨道上每轮用户输入一条刻线，
    /// 浮窗用小字显示视口顶部那一轮的用户输入摘要；滑块靠近某条刻线时磁吸对齐，
    /// 可以直接拖到某一轮。
    /// 叠层只画不接管输入（WS_EX_TRANSPARENT，鼠标穿透到原生滚动条/正文），
    /// 既有滚动逻辑（跟随末尾、markdown 延迟、锚点还原）完全不受影响。
    /// </summary>
    public partial class ChatFlowControl
    {
        /// <summary>磁吸半径（DIP，按滚动条上的像素计）：滑块离某轮刻线这么近就贴上去。</summary>
        private const int ScrollMagnetDip = 5;

        /// <summary>
        /// 刻度窄槽宽度：正文右侧本来就留了一条空白带（<see cref="ChatMessageRow.TextRightSpace"/>），
        /// 刻度就画在这条空白带里 —— 既不遮挡原生滚动条（箭头与滑块照常可见可用），也不压到任何文字。
        /// </summary>
        private int TickGutterWidth => Math.Max(Dip(4), Dip(ChatMessageRow.TextRightSpace));

        /// <summary>浮窗里用户输入的截断长度（字符）：超出加省略号。</summary>
        private const int ScrollPreviewChars = 60;

        /// <summary>盖在滚动条上的刻度层（操作滚动条期间可见，自己画轨道、刻线、滑块）。</summary>
        private readonly ScrollTickOverlay scrollTicks = new();

        /// <summary>刻度浮窗（小字显示那一轮的用户输入）。</summary>
        private readonly ScrollTickPreview scrollPreview = new();

        /// <summary>正在操作滚动条：刻度、磁吸、浮窗只在此期间生效。</summary>
        private bool scrollDragging;

        /// <summary>这次拖动是鼠标操作滚动条引起的（松手要自动收尾）；显式发起的不自动收尾。</summary>
        private bool scrollDragFromMouse;

        /// <summary>结束兜底：原生滚动条没有「拖动结束」事件，靠轮询左键是否松开收摊。</summary>
        private readonly System.Windows.Forms.Timer scrollDragIdle = new() { Interval = 120 };

        /// <summary>轮次刻度缓存：每轮用户输入条目的起始偏移（内容坐标，与 vbar.Value 同一坐标系）。</summary>
        private readonly List<(int Offset, ChatTurn Turn)> scrollTickCache = [];

        /// <summary>刻度缓存的指纹（条目数 + 总高）：内容一变就重算。</summary>
        private int tickStampCount = -1;
        private int tickStampHeight = -1;

        private ChatTurn previewTurn;
        private string previewText = string.Empty;
        private string previewCaption = string.Empty;

        /// <summary>是否正在操作滚动条。</summary>
        internal bool ScrollDragActive => scrollDragging;

        /// <summary>刻度层是否可见（操作滚动条期间）。</summary>
        internal bool ScrollTicksVisible => scrollTicks.Visible;

        /// <summary>浮窗当前显示的轮次序号（形如「3/12」）；未显示时为空。</summary>
        internal string ScrollPreviewCaption => scrollPreview.Visible ? previewCaption : string.Empty;

        /// <summary>浮窗当前显示的摘要文本；未显示时为空。</summary>
        internal string ScrollPreviewText => scrollPreview.Visible ? previewText : string.Empty;

        /// <summary>各轮用户输入的起始偏移（递增）。</summary>
        internal IReadOnlyList<int> ScrollTickOffsets
        {
            get
            {
                var result = new List<int>();
                foreach (var tick in EnsureScrollTicks()) result.Add(tick.Offset);
                return result;
            }
        }

        /// <summary>构造函数调用：建叠层控件、挂滚动条操作的收尾判定。</summary>
        private void InitializeScrollTicks()
        {
            scrollTicks.Owner = this;
            scrollTicks.Visible = false;
            Controls.Add(scrollTicks);

            scrollPreview.Owner = this;
            scrollPreview.Visible = false;
            Controls.Add(scrollPreview);

            scrollDragIdle.Tick += (_, _) =>
            {
                if (!scrollDragging) { scrollDragIdle.Stop(); return; }
                // 鼠标驱动的拖动：左键松开（或在滚动条外松开、按键已被别处消费）就收摊。
                // 显式发起的拖动（宿主/单测调 BeginScrollDrag）不受这条兜底影响，得自己 EndScrollDrag。
                if (scrollDragFromMouse && (Control.MouseButtons & MouseButtons.Left) == 0) EndScrollDrag();
            };
            SizeChanged += (_, _) => { if (scrollDragging) UpdateScrollDragVisuals(); };
            VisibleChanged += (_, _) => { if (!Visible) EndScrollDrag(); };
        }

        /// <summary>
        /// 开始操作滚动条：亮起刻度与浮窗（期间内容可能变过，首次进入时重算刻度）。
        /// fromMouse=true 表示由滚动条自身的交互（WM_VSCROLL）引起，松手后自动收尾。
        /// </summary>
        internal void BeginScrollDrag(bool fromMouse)
        {
            if (IsDisposed || !vbar.Visible) return;
            if (!scrollDragging)
            {
                scrollDragging = true;
                previewTurn = null;
                tickStampCount = -1;
            }
            scrollDragFromMouse |= fromMouse;
            if (scrollDragFromMouse)
            {
                scrollDragIdle.Stop();
                scrollDragIdle.Start();
            }
            UpdateScrollDragVisuals();
        }

        /// <summary>显式开始（宿主/单测）：不按鼠标按键也算在操作，靠 EndScrollDrag 收尾。</summary>
        internal void BeginScrollDrag() => BeginScrollDrag(false);

        /// <summary>结束操作：收起刻度与浮窗，磁吸随之失效。</summary>
        internal void EndScrollDrag()
        {
            scrollDragIdle.Stop();
            if (!scrollDragging) return;
            scrollDragging = false;
            scrollDragFromMouse = false;
            if (scrollTicks.Visible) scrollTicks.Visible = false;
            if (scrollPreview.Visible) scrollPreview.Visible = false;
        }

        /// <summary>磁吸：滑块离某轮刻线足够近就贴上去（已经贴住时就交回正常流程）。</summary>
        private bool ApplyScrollMagnet()
        {
            if (!scrollDragging || !vbar.Visible || items.Count == 0) return false;
            var ticks = EnsureScrollTicks();
            int max = MaxScrollValue;
            if (ticks.Count == 0 || max <= 0) return false;

            // 阈值按轨道像素换算成内容像素：手感是「离刻线几像素」，长会话里也不会时刻被拽住
            var (_, thumbLen) = ThumbMetrics(barHeight: null, withPosition: false);
            int trackLen = Math.Max(1, vbar.Height - Math.Min(ScrollArrowHeight, Math.Max(0, vbar.Height / 3)) * 2);
            int travel = Math.Max(1, trackLen - thumbLen);
            int threshold = Math.Max(Dip(2), (int)Math.Round(Dip(ScrollMagnetDip) * (double)max / travel));

            int index = NearestTickIndex(out int bestDelta);
            if (index < 0) return false;
            int best = Math.Min(ticks[index].Offset, max);
            if (bestDelta > threshold || best == vbar.Value) return false;
            SetScrollValueInternal(best);   // 递归进来的那次回调负责重排，这里直接交回
            SnapToNearestTick(threshold);   // 行高由估算变实测会把刻度挪几像素：按新刻度再贴一次，落在同一轮头部
            return true;
        }

        /// <summary>按当前刻度表再贴一次（最多两轮）：实测高度让刻度移动后仍停在这一轮头部。</summary>
        private void SnapToNearestTick(int threshold)
        {
            for (int round = 0; round < 2; round++)
            {
                var ticks = EnsureScrollTicks();
                int max = MaxScrollValue;
                if (ticks.Count == 0 || max <= 0) return;
                int index = NearestTickIndex(out int bestDelta);
                if (index < 0) return;
                int best = Math.Min(ticks[index].Offset, max);
                // 阈值放宽一倍：重排只把刻度挪几像素，第一轮算出的阈值仍适用
                if (best == vbar.Value || bestDelta > threshold * 2) return;
                SetScrollValueInternal(best);
            }
        }

        /// <summary>轮次刻度：每轮第一条（带用户输入的）条目的起始偏移。内容变了（条数/总高变）就重算。</summary>
        private List<(int Offset, ChatTurn Turn)> EnsureScrollTicks()
        {
            if (tickStampCount == items.Count && tickStampHeight == totalHeight) return scrollTickCache;
            tickStampCount = items.Count;
            tickStampHeight = totalHeight;
            scrollTickCache.Clear();
            if (prefixSum.Length < items.Count + 1) return scrollTickCache;

            ChatTurn last = null;
            for (int i = 0; i < items.Count; i++)
            {
                var turn = items[i].Turn;
                if (turn == null || ReferenceEquals(turn, last)) continue;
                last = turn;
                if (turn.User == null) continue;   // 没有用户输入的轮（卡片/链接/末尾指示行）不画刻度
                scrollTickCache.Add((prefixSum[i], turn));
            }
            return scrollTickCache;
        }

        /// <summary>刻度缓存作废（条目或行高变了）。</summary>
        private void InvalidateScrollTicks()
        {
            tickStampCount = -1;
            tickStampHeight = -1;
        }

        /// <summary>
        /// 离当前滚动位置最近的那条刻线（高亮、浮窗、磁吸共用同一套判据）。
        /// 不按「视口顶部落在哪一轮」算：长正文里顶部常停在某一轮的中段，拖到末尾时用户要的是下一轮，
        /// 按顶部算会一直显示上面那一轮，与磁吸落点也对不上。
        /// </summary>
        private int NearestTickIndex(out int delta)
        {
            delta = int.MaxValue;
            var ticks = EnsureScrollTicks();
            if (ticks.Count == 0) return -1;
            int max = MaxScrollValue;
            int best = -1;
            for (int i = 0; i < ticks.Count; i++)
            {
                int target = Math.Min(ticks[i].Offset, max);
                int d = Math.Abs(vbar.Value - target);
                if (d >= delta) continue;   // 相等时保留靠前那一轮：贴着这一轮头部时不往下跳
                delta = d;
                best = i;
            }
            return best;
        }

        /// <summary>当前对准的一轮在刻度表里的下标（高亮用）；没有返回 -1。</summary>
        private int CurrentTickIndex() => NearestTickIndex(out _);

        /// <summary>按当前滚动位置刷新刻度层与浮窗（操作滚动条期间每次值变化都走这里）。</summary>
        private void UpdateScrollDragVisuals()
        {
            if (IsDisposed) return;
            if (!scrollDragging || !vbar.Visible)
            {
                if (scrollTicks.Visible) scrollTicks.Visible = false;
                if (scrollPreview.Visible) scrollPreview.Visible = false;
                return;
            }
            // 刻度画在滚动条左侧的窄槽里（不盖住原生滚动条：箭头与滑块照常可见可用）
            scrollTicks.Bounds = new Rectangle(
                Math.Max(0, vbar.Left - TickGutterWidth), vbar.Top, TickGutterWidth, vbar.Height);
            if (!scrollTicks.Visible) scrollTicks.Visible = true;
            scrollTicks.BringToFront();
            scrollTicks.Invalidate();
            UpdateScrollPreview();
        }

        /// <summary>浮窗：位置对着那一轮的刻线，内容只在换轮时重新截断。</summary>
        private void UpdateScrollPreview()
        {
            var ticks = EnsureScrollTicks();
            int index = NearestTickIndex(out _);
            var turn = index >= 0 && index < ticks.Count ? ticks[index].Turn : null;
            if (turn == null)
            {
                if (scrollPreview.Visible) scrollPreview.Visible = false;
                return;
            }

            previewCaption = $"{index + 1}/{ticks.Count}";
            if (!ReferenceEquals(previewTurn, turn))
            {
                previewTurn = turn;
                previewText = CompactPreview(turn.UserText, ScrollPreviewChars);
            }
            scrollPreview.Caption = previewCaption;
            scrollPreview.Body = previewText;

            var (thumbTop, thumbLen) = ThumbMetrics();
            int contentW = Math.Max(1, ClientSize.Width - vbar.Width);
            int width = Math.Min(Dip(300), Math.Max(Dip(120), contentW - Dip(24)));
            int height = Dip(26);
            int x = Math.Max(0, vbar.Left - TickGutterWidth - width - Dip(6));
            int y = index >= 0
                ? TrackYForOffset(ticks[index].Offset, vbar.Height, thumbLen) - height / 2
                : thumbTop + thumbLen / 2 - height / 2;
            y = Math.Clamp(y, Dip(2), Math.Max(Dip(2), ClientSize.Height - height - Dip(2)));
            scrollPreview.Bounds = new Rectangle(x, y, width, height);
            if (!scrollPreview.Visible) scrollPreview.Visible = true;
            scrollPreview.BringToFront();
            scrollPreview.Invalidate();
        }

        /// <summary>一行摘要：换行/连续空白压成单个空格，超长截断加省略号（只扫头部，长正文不整段处理）。</summary>
        internal static string CompactPreview(string text, int max)
        {
            if (string.IsNullOrWhiteSpace(text)) return "（无用户输入）";
            if (max <= 0) return string.Empty;
            var sb = new StringBuilder(Math.Min(text.Length, max + 1));
            bool space = false;
            for (int i = 0; i < text.Length; i++)
            {
                char raw = text[i];
                char ch = raw == '\r' || raw == '\n' || raw == '\t' ? ' ' : raw;
                if (ch == ' ')
                {
                    if (space) continue;
                    space = true;
                }
                else space = false;
                if (sb.Length >= max)
                {
                    sb.Append('…');
                    return sb.ToString();
                }
                sb.Append(ch);
            }
            return sb.ToString().TrimEnd();
        }

        /// <summary>滚动条两端的箭头高度（系统度量）；滚动条太矮时按三分之一收，免得轨道算成负数。</summary>
        private int ScrollArrowHeight
        {
            get
            {
                int arrow = Math.Max(0, SystemInformation.VerticalScrollBarArrowHeight);
                int h = Math.Max(0, vbar.Height);
                if (h > 0 && arrow * 3 > h) arrow = h / 3;
                return arrow;
            }
        }

        /// <summary>滑块在轨道上的位置与长度：刻线、滑块、磁吸都按这套度量对齐（滑块行程与内容偏移线性对应）。</summary>
        private (int Top, int Length) ThumbMetrics(int? barHeight = null, bool withPosition = true)
        {
            int h = barHeight ?? vbar.Height;
            int arrow = Math.Min(ScrollArrowHeight, Math.Max(0, h / 3));
            int trackLen = Math.Max(1, h - arrow * 2);
            int view = Math.Max(1, ClientSize.Height);
            int len = (int)Math.Round(trackLen * (double)view / Math.Max(1, totalHeight));
            len = Math.Clamp(len, Math.Min(trackLen, Math.Max(Dip(10), 6)), trackLen);
            int max = MaxScrollValue;
            double ratio = !withPosition || max <= 0 ? 0 : Math.Clamp(vbar.Value / (double)max, 0, 1);
            int top = arrow + (int)Math.Round((trackLen - len) * ratio);
            return (top, len);
        }

        /// <summary>内容偏移 → 轨道上的 y：这一轮贴到视口顶部时滑块所处的位置（含箭头偏移）。</summary>
        private int TrackYForOffset(int offset, int barHeight, int thumbLen)
        {
            int arrow = Math.Min(ScrollArrowHeight, Math.Max(0, barHeight / 3));
            int trackLen = Math.Max(1, barHeight - arrow * 2);
            int travel = Math.Max(1, trackLen - thumbLen);
            int max = MaxScrollValue;
            double ratio = max <= 0 ? 0 : Math.Clamp(offset / (double)max, 0, 1);
            return arrow + (int)Math.Round(travel * ratio);
        }

        /// <summary>
        /// 刻度层绘制：每轮一条刻线，贴在窄槽右边缘（紧邻滚动条）；当前对准的那一轮用强调色拉满整槽宽。
        /// 轨道与滑块由原生滚动条自己画，这里不重复。
        /// </summary>
        private void PaintScrollTicks(Graphics g, Size size)
        {
            var palette = ChatTheme.Current;
            using (var bg = new SolidBrush(palette?.Back ?? BackColor))
                g.FillRectangle(bg, 0, 0, size.Width, size.Height);
            if (!vbar.Visible || size.Width <= 0 || size.Height <= 2) return;

            Color fore = palette?.ForeDim ?? Color.Silver;
            Color accent = palette?.Accent ?? Color.DodgerBlue;
            var (_, thumbLen) = ThumbMetrics(size.Height, withPosition: false);
            var ticks = EnsureScrollTicks();
            int current = CurrentTickIndex();
            int lastY = int.MinValue;
            int right = size.Width - 1;
            float thin = Math.Max(1f, Dip(1));
            for (int i = 0; i < ticks.Count; i++)
            {
                int y = TrackYForOffset(ticks[i].Offset, size.Height, thumbLen);
                if (y == lastY) continue;   // 两轮挤在同一像素：只画一条，免得糊成实心块
                lastY = y;
                bool hot = i == current;
                int len = hot ? right : Math.Max(2, size.Width / 2);
                using var pen = new Pen(hot ? accent : fore, hot ? thin * 2f : thin);
                g.DrawLine(pen, right - len, y, right, y);
            }
        }

        /// <summary>浮窗绘制：圆角底 + 边框 + 轮次序号（灰）+ 用户输入摘要（单行省略）。</summary>
        private void PaintScrollPreview(Graphics g, Size size, Font captionFont, Font bodyFont)
        {
            var palette = ChatTheme.Current;
            Color back = palette?.Control ?? Color.White;
            Color fore = palette?.Fore ?? Color.FromArgb(0x1F, 0x1F, 0x1F);
            Color dim = palette?.ForeDim ?? Color.Gray;
            Color border = palette?.Border ?? Color.FromArgb(0xE8, 0xE8, 0xE8);

            g.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Math.Max(2, size.Width - 1), Math.Max(2, size.Height - 1));
            using (var path = RoundRectPath(rect, Math.Max(2, Dip(6))))
            {
                using (var brush = new SolidBrush(back)) g.FillPath(brush, path);
                using (var pen = new Pen(border)) g.DrawPath(pen, path);
            }
            g.SmoothingMode = SmoothingMode.Default;

            int pad = Dip(8);
            int captionW = 0;
            if (!string.IsNullOrEmpty(previewCaption))
            {
                var capRect = new Rectangle(pad, 0, Math.Max(1, size.Width - pad * 2), size.Height);
                TextRenderer.DrawText(g, previewCaption, captionFont, capRect, dim,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                    | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                captionW = TextRenderer.MeasureText(g, previewCaption, captionFont, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding).Width + Dip(6);
            }
            int textLeft = pad + captionW;
            var bodyRect = new Rectangle(textLeft, 0, Math.Max(1, size.Width - textLeft - pad), size.Height);
            TextRenderer.DrawText(g, previewText, bodyFont, bodyRect, fore,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter
                | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
        }

        /// <summary>圆角矩形路径（与 ChatFlowMenu 同一套算法）。</summary>
        private static GraphicsPath RoundRectPath(Rectangle rect, int radius)
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

        /// <summary>叠层基类：只画不接管输入（鼠标穿透到下面的滚动条与正文行）。</summary>
        private abstract class OverlaySurface : Control
        {
            public ChatFlowControl Owner { get; set; }

            protected OverlaySurface()
            {
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer
                    | ControlStyles.UserPaint, true);
                TabStop = false;
                Visible = false;
            }

            protected override CreateParams CreateParams
            {
                get
                {
                    var cp = base.CreateParams;
                    cp.ExStyle |= 0x20;          // WS_EX_TRANSPARENT：命中测试穿透
                    cp.ExStyle |= 0x08000000;    // WS_EX_NOACTIVATE：不抢焦点
                    return cp;
                }
            }
        }

        /// <summary>盖在滚动条上的刻度层。</summary>
        private sealed class ScrollTickOverlay : OverlaySurface
        {
            protected override void OnPaint(PaintEventArgs e) => Owner?.PaintScrollTicks(e.Graphics, ClientSize);
        }

        /// <summary>刻度浮窗：小字一行，轮次序号 + 那一轮的用户输入摘要。</summary>
        private sealed class ScrollTickPreview : OverlaySurface
        {
            private readonly Font captionFont = new("Microsoft YaHei UI", 7F);
            private readonly Font bodyFont = new("Microsoft YaHei UI", 7.5F);

            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public string Caption { get; set; } = string.Empty;

            [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
            public string Body { get; set; } = string.Empty;

            protected override void OnPaint(PaintEventArgs e)
                => Owner?.PaintScrollPreview(e.Graphics, ClientSize, captionFont, bodyFont);

            protected override void Dispose(bool disposing)
            {
                if (disposing)
                {
                    captionFont.Dispose();
                    bodyFont.Dispose();
                }
                base.Dispose(disposing);
            }
        }
    }
}
