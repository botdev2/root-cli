using System.Text.RegularExpressions;

namespace RootCli.Core.Agent;

public static class PlainTextFormatter
{
    private static readonly Regex FenceOpen = new(@"^```[\w+-]*\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex FenceAny = new(@"```+", RegexOptions.Compiled);
    private static readonly Regex Heading = new(@"^#{1,6}\s+", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex BoldItalic = new(@"\*\*\*(.+?)\*\*\*", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex Bold = new(@"\*\*(.+?)\*\*", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ItalicStar = new(@"(?<![\w*])\*(?!\s)(.+?)(?<!\s)\*(?![\w*])", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex BoldUnder = new(@"__(.+?)__", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ItalicUnder = new(@"(?<![\w_])_(?!\s)(.+?)(?<!\s)_(?![\w_])", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex InlineCode = new(@"`([^`\n]+)`", RegexOptions.Compiled);
    private static readonly Regex Link = new(@"\[([^\]]+)\]\([^)]+\)", RegexOptions.Compiled);
    private static readonly Regex Image = new(@"!\[([^\]]*)\]\([^)]+\)", RegexOptions.Compiled);
    private static readonly Regex Hr = new(@"^\s*([-*_]){3,}\s*$", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex Blockquote = new(@"^>\s?", RegexOptions.Multiline | RegexOptions.Compiled);
    private static readonly Regex Strike = new(@"~~(.+?)~~", RegexOptions.Singleline | RegexOptions.Compiled);
    private static readonly Regex ExtraBlank = new(@"\n{3,}", RegexOptions.Compiled);

    public static string ToTerminalPlain(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var s = text.Replace("\r\n", "\n").Replace('\r', '\n');
        s = FenceOpen.Replace(s, "");
        s = FenceAny.Replace(s, "");
        s = Heading.Replace(s, "");
        s = Image.Replace(s, "$1");
        s = Link.Replace(s, "$1");
        s = BoldItalic.Replace(s, "$1");
        s = Bold.Replace(s, "$1");
        s = BoldUnder.Replace(s, "$1");
        s = ItalicStar.Replace(s, "$1");
        s = ItalicUnder.Replace(s, "$1");
        s = Strike.Replace(s, "$1");
        s = InlineCode.Replace(s, "$1");
        s = Blockquote.Replace(s, "");
        s = Hr.Replace(s, "");
        s = ExtraBlank.Replace(s, "\n\n");
        return s.Trim();
    }
}
