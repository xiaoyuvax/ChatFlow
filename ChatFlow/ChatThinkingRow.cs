using System.Drawing.Drawing2D;

namespace ChatFlow
{
    /// <summary>
    /// 聊天末尾的「思考中…」指示行：旋转弧线 + 文案，执行期间常驻在最后一条消息之后。
    /// 动画由独立 Timer 驱动，只重绘本行，不触发整表重排，避免流式输出时卡顿。
    /// </summary>
    internal sealed class ChatThinkingRow : Control
    {
        internal const int RowHeightDip = 40;
        private const string IndicatorText = "工作中…";

        private readonly System.Windows.Forms.Timer spin = new() { Interval = 55 };
        private readonly Font font = new("Microsoft YaHei UI", 9F);
        private int angle;

        public ChatThinkingRow()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer
                | ControlStyles.UserPaint, true);
            BackColor = Color.WhiteSmoke;
            TabStop = false;
            spin.Tick += (_, _) =>
            {
                angle = (angle + 30) % 360;
                Invalidate();
            };
        }

        private int S(float value) => Math.Max(1, (int)Math.Round(value * DeviceDpi / 96f));

        /// <summary>布局时调用：同步底色与动画状态，返回行高（像素）。</summary>
        public int Bind(int width)
        {
            BackColor = ChatTheme.Current?.Back ?? Parent?.BackColor ?? Color.WhiteSmoke;
            if (Visible && !spin.Enabled) spin.Start();
            Invalidate();
            return S(RowHeightDip);
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (Visible) spin.Start();
            else spin.Stop();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Color accent = ChatTheme.Current?.Accent ?? Color.FromArgb(0x16, 0x77, 0xFF);
            Color fore = ChatTheme.Current?.ForeDim ?? Color.FromArgb(0x5F, 0x6E, 0x7E);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int pad = S(10);
            int icon = S(14);
            int gap = S(8);
            Size text = TextRenderer.MeasureText(g, IndicatorText, font, Size.Empty, TextFormatFlags.NoPadding);
            int pillHeight = S(28);
            int pillWidth = pad * 2 + icon + gap + text.Width;
            int left = S(12);
            int top = Math.Max(0, (Height - pillHeight) / 2);

            // 淡色胶囊底：让指示条在深浅主题上都不会“浮”在正文里
            using (var path = Rounded(new Rectangle(left, top, pillWidth, pillHeight), pillHeight / 2))
            using (var fill = new SolidBrush(Color.FromArgb(16, accent)))
                g.FillPath(fill, path);

            // 旋转弧线：浅色轨道 + 加粗高亮弧，弧端圆头
            int size = icon - S(2);
            int cx = left + pad + icon / 2;
            int cy = top + pillHeight / 2;
            var circle = new Rectangle(cx - size / 2, cy - size / 2, size, size);
            float stroke = Math.Max(1.5f, S(1.5f));
            using (var track = new Pen(Color.FromArgb(48, accent), stroke))
                g.DrawEllipse(track, circle);
            using (var arc = new Pen(accent, stroke))
            {
                arc.StartCap = LineCap.Round;
                arc.EndCap = LineCap.Round;
                g.DrawArc(arc, circle, angle, 110);
            }

            TextRenderer.DrawText(g, IndicatorText, font,
                new Rectangle(left + pad + icon + gap, top, text.Width + 2, pillHeight), fore,
                TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                spin.Stop();
                spin.Dispose();
                font.Dispose();
            }
            base.Dispose(disposing);
        }

        private static GraphicsPath Rounded(Rectangle rect, int radius)
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
