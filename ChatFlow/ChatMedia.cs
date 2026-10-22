namespace ChatFlow
{
    /// <summary>附件类型判断（图片扩展名）。用于决定链接是否按图片路径着色与打开。</summary>
    public static class ChatMedia
    {
        private static readonly HashSet<string> ImageExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".png", ".jpg", ".jpeg", ".gif", ".webp", ".bmp"
        };

        public static bool IsImage(string fileName)
        {
            if (string.IsNullOrWhiteSpace(fileName)) return false;
            return ImageExts.Contains(Path.GetExtension(fileName));
        }
    }
}
