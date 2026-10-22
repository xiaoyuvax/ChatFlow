using System.Drawing;

namespace ChatFlow.Tests
{
    internal static class TestPalette
    {
        public static readonly ChatPalette Dark = new()
        {
            Back = Color.FromArgb(32, 32, 32),
            Fore = Color.Gainsboro,
            ForeDim = Color.Gray,
            Accent = Color.DodgerBlue,
            Border = Color.FromArgb(64, 64, 64),
            Control = Color.FromArgb(45, 45, 45),
            MenuHover = Color.FromArgb(70, 70, 70),
            InputBack = Color.FromArgb(40, 40, 40),
            InputFore = Color.Gainsboro,
            ChatFore = Color.FromArgb(230, 230, 230),
            ChatThinkingFore = Color.FromArgb(150, 150, 150),
        };
    }
}
