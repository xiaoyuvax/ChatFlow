using System.Drawing.Drawing2D;

namespace ChatFlow
{
    /// <summary>轻量 Antd 风格右键菜单（不依赖 AntdUI）：白底圆角、浅色悬停块。</summary>
    internal static class ChatFlowMenu
    {
        public static void Show(ChatFlowControl owner, ChatFlowItem item, Point screen)
        {
            if (owner == null || item == null) return;
            bool undo = owner.CanUndo;
            bool copy = owner.CanCopy;
            if (!undo && !copy) return;

            var palette = ChatTheme.Current;
            Color back = palette?.Control ?? Color.White;
            Color fore = palette?.Fore ?? Color.FromArgb(0x1F, 0x1F, 0x1F);
            Color hover = palette?.MenuHover ?? Color.FromArgb(0xF0, 0xF2, 0xF5);
            Color border = palette?.Border ?? Color.FromArgb(0xE8, 0xE8, 0xE8);
            int pad = Dip(owner, 6);
            int radius = Dip(owner, 8);

            var menu = new ContextMenuStrip
            {
                ShowImageMargin = false,
                ShowCheckMargin = false,
                BackColor = back,
                ForeColor = fore,
                Font = new Font("Segoe UI", 9F),
                Padding = new Padding(pad),
                Renderer = new MenuRenderer(back, fore, hover, border, Dip(owner, 6))
            };
            menu.Opened += (_, _) => menu.Region = new Region(RoundedRect(menu.ClientRectangle, radius));
            if (undo) menu.Items.Add(Item("撤回", () => owner.RequestUndo(item)));
            if (copy) menu.Items.Add(Item("复制", () => owner.RequestCopy(item)));
            menu.Closed += (_, _) => menu.Dispose();
            menu.Show(screen);
        }

        private static ToolStripMenuItem Item(string text, Action click)
        {
            var item = new ToolStripMenuItem(text)
            {
                AutoSize = true,
                Padding = new Padding(12, 7, 22, 7)
            };
            item.Click += (_, _) => click();
            return item;
        }

        private static int Dip(Control c, int value) => (int)Math.Round(value * c.DeviceDpi / 96f);

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

        private sealed class MenuRenderer : ToolStripRenderer
        {
            private readonly Color back;
            private readonly Color fore;
            private readonly Color hover;
            private readonly Color border;
            private readonly int radius;

            public MenuRenderer(Color back, Color fore, Color hover, Color border, int radius)
            {
                this.back = back;
                this.fore = fore;
                this.hover = hover;
                this.border = border;
                this.radius = radius;
            }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
                => e.Graphics.Clear(back);

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e)
            {
                using var pen = new Pen(border);
                e.Graphics.DrawRectangle(pen, 0, 0, e.ToolStrip.Width - 1, e.ToolStrip.Height - 1);
            }

            protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
            {
                if (!e.Item.Selected || !e.Item.Enabled) return;
                var rect = new Rectangle(3, 1, e.Item.Width - 6, e.Item.Height - 2);
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using var path = RoundedRect(rect, radius);
                using var brush = new SolidBrush(hover);
                e.Graphics.FillPath(brush, path);
            }

            protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
            {
                e.TextColor = e.Item.Enabled ? fore : Color.Gray;
                base.OnRenderItemText(e);
            }
        }
    }
}
