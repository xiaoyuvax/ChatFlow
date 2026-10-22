using System.Drawing;

namespace ChatFlow
{
    /// <summary>
    /// 聊天区调色板。<see cref="ChatTheme.Current"/> 为 null 表示浅色主题：
    /// 控件回退到内置颜色，宿主无需提供任何值。
    /// </summary>
    public sealed class ChatPalette
    {
        public Color Back { get; set; }
        public Color Fore { get; set; }
        public Color ForeDim { get; set; }
        public Color Accent { get; set; }
        public Color Border { get; set; }
        public Color Control { get; set; }
        public Color MenuHover { get; set; }
        public Color InputBack { get; set; }
        public Color InputFore { get; set; }
        public Color ChatFore { get; set; }
        public Color ChatThinkingFore { get; set; }
    }

    /// <summary>
    /// 主题接缝：控件只认这里，不认识任何宿主的主题管理器。
    /// 宿主在切换主题时调用 <see cref="Set"/>，控件据此重绘。
    /// </summary>
    public static class ChatTheme
    {
        /// <summary>当前调色板；null = 浅色主题。</summary>
        public static ChatPalette Current { get; private set; }

        /// <summary>调色板变化通知（控件订阅后重建样式与底色）。</summary>
        public static event Action Changed;

        public static void Set(ChatPalette palette)
        {
            Current = palette;
            Changed?.Invoke();
        }
    }
}
