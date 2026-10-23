using System.Drawing.Drawing2D;

namespace ChatFlow
{
    /// <summary>可点击的导航条目：图标 + 标题（可带副标题），用于进入子会话等。</summary>
    public sealed class ChatLink
    {
        public string Id { get; set; } = "";
        public int AnchorTurnIndex { get; set; } = -1;
        public string Title { get; set; } = "";
        public string Subtitle { get; set; } = "";
        public string AccentHex { get; set; } = "#722ED1";

        public Color Accent
        {
            get
            {
                try { return ColorTranslator.FromHtml(AccentHex); }
                catch { return Color.FromArgb(114, 46, 209); }
            }
        }
    }

    public sealed class ChatLinkEventArgs : EventArgs
    {
        public ChatLinkEventArgs(ChatLink link) => Link = link;
        public ChatLink Link { get; }
    }

    /// <summary>图标 + 标题的一行，整行可点击。</summary>
    public sealed class ChatLinkRow : Control
    {
        private ChatLink _link;
        private bool _hover;

        public event Action<ChatLink> Activated;

        public ChatLinkRow()
        {
            DoubleBuffered = true;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.UserPaint, true);
        }

        private int S(float v) => Math.Max(1, (int)Math.Round(v * DeviceDpi / 96.0));

        public ChatLink Link => _link;

        public int Bind(ChatLink link, Font font, int width)
        {
            _link = link;
            return Height = S(30);
        }

        protected override void OnMouseEnter(EventArgs e) { _hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { _hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button == MouseButtons.Left) Activated?.Invoke(_link);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            Color fore = ChatTheme.Current?.ChatFore ?? Color.FromArgb(31, 31, 31);
            Color dim = ChatTheme.Current?.ForeDim ?? Color.FromArgb(120, 120, 120);
            Color back = ChatTheme.Current?.Control ?? Color.FromArgb(249, 247, 255);

            if (_hover)
                using (var hover = new SolidBrush(Color.FromArgb(40, _link?.Accent ?? Color.FromArgb(114, 46, 209))))
                    g.FillRectangle(hover, ClientRectangle);

            int pad = S(10);
            int icon = S(16);
            int y = (Height - icon) / 2;

            Color accent = _link?.Accent ?? Color.FromArgb(114, 46, 209);
            using (var brush = new SolidBrush(accent))
            using (var pen = new Pen(accent, Math.Max(1f, S(1))))
            {
                var body = new Rectangle(pad, y, icon, icon);
                using (var path = Round(body, S(4))) g.FillPath(brush, path);
                using (var eye = new SolidBrush(Color.White))
                {
                    g.FillEllipse(eye, pad + S(4), y + S(5), S(3), S(3));
                    g.FillEllipse(eye, pad + S(9), y + S(5), S(3), S(3));
                }
            }

            int textLeft = pad + icon + S(8);
            int textWidth = Math.Max(10, Width - textLeft - S(24));
            using (var f = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular))
            {
                TextRenderer.DrawText(g, _link?.Title ?? "", f,
                    new Rectangle(textLeft, 0, textWidth, Height), fore,
                    TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis);
            }
            using (var chevron = new Pen(dim))
            {
                int cx = Width - S(14), cy = Height / 2;
                g.DrawLines(chevron, [new Point(cx - S(3), cy - S(4)), new Point(cx + S(1), cy), new Point(cx - S(3), cy + S(4))]);
            }
            base.OnPaint(e);
        }

        private static GraphicsPath Round(Rectangle r, int radius)
        {
            var path = new GraphicsPath();
            int d = radius * 2;
            path.AddArc(r.X, r.Y, d, d, 180, 90);
            path.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            path.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            path.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
