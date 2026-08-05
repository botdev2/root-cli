namespace RootCli;

internal static class TermUi
{
    public static readonly ConsoleColor Brand = ConsoleColor.Cyan;
    public static readonly ConsoleColor BrandStrong = ConsoleColor.White;
    public static readonly ConsoleColor Dim = ConsoleColor.DarkGray;
    public static readonly ConsoleColor Ok = ConsoleColor.DarkGreen;
    public static readonly ConsoleColor Warn = ConsoleColor.DarkYellow;
    public static readonly ConsoleColor Err = ConsoleColor.DarkRed;
    public static readonly ConsoleColor Ask = ConsoleColor.Blue;
    public static readonly ConsoleColor Plan = ConsoleColor.DarkYellow;
    public static readonly ConsoleColor Agent = ConsoleColor.DarkGreen;
    public static readonly ConsoleColor Mcp = ConsoleColor.Magenta;
    public static readonly ConsoleColor Tool = ConsoleColor.DarkCyan;

    public static int Width
    {
        get
        {
            try
            {
                return Math.Clamp(Console.WindowWidth, 48, 100);
            }
            catch
            {
                return 80;
            }
        }
    }

    public static void Write(string text, ConsoleColor color)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = prev;
    }

    public static void WriteLine(string text, ConsoleColor color)
    {
        Write(text, color);
        Console.WriteLine();
    }

    public static void Rule(char ch = '─')
    {
        WriteLine(new string(ch, Width), Brand);
    }

    public static void DoubleRule() => Rule('═');

    public static void BrandHeader(string subtitle)
    {
        WriteLogo();
        DoubleRule();
        Console.Write("  ");
        Write("ROOT", Brand);
        Write("CLI", BrandStrong);
        if (!string.IsNullOrWhiteSpace(subtitle))
        {
            Write("  ·  " + subtitle, Dim);
        }

        Console.WriteLine();
        Rule();
    }

    public static void WriteLogo(bool compact = false)
    {
        _ = compact;
    }

    public static void Section(string title)
    {
        Console.WriteLine();
        Write("  ▸ ", Brand);
        WriteLine(title, BrandStrong);
        Console.WriteLine();
    }

    public static void KeyValue(string key, string value, ConsoleColor? valueColor = null)
    {
        Write("  " + key.PadRight(10), Dim);
        WriteLine(value, valueColor ?? BrandStrong);
    }

    public static void Bullet(string text, ConsoleColor color)
    {
        Write("  • ", color);
        WriteLine(text, ConsoleColor.Gray);
    }

    public static void StatusDot(bool ok, string text)
    {
        Write(ok ? "  ● " : "  ○ ", ok ? Ok : Warn);
        WriteLine(text, ConsoleColor.Gray);
    }

    public static void Error(string message)
    {
        Write("  ✕ ", Err);
        WriteLine(message, Err);
    }

    public static void Hint(string text) => WriteLine("  " + text, Dim);

    public static void BoxTop()
    {
        WriteLine("  ╔" + new string('═', Math.Max(10, Width - 4)) + "╗", Brand);
    }

    public static void BoxRow(string content)
    {
        var inner = Math.Max(10, Width - 4);
        if (content.Length > inner)
        {
            content = content.Substring(0, inner - 1) + "…";
        }

        Write("  ║", Brand);
        Write(content.PadRight(inner), ConsoleColor.Gray);
        WriteLine("║", Brand);
    }

    public static void BoxBottom()
    {
        WriteLine("  ╚" + new string('═', Math.Max(10, Width - 4)) + "╝", Brand);
    }

    public static ConsoleColor ModeColor(string mode) =>
        mode.ToLowerInvariant() switch
        {
            "ask" => Ask,
            "plan" => Plan,
            "agent" => Agent,
            _ => Brand
        };

    public static void CapPill(string cap)
    {
        var color = cap.ToLowerInvariant() switch
        {
            "tools" or "tool" => Tool,
            "vision" or "image" => ConsoleColor.DarkMagenta,
            "thinking" or "reasoning" => Plan,
            "completion" or "chat" => Ok,
            _ => Dim
        };
        Write(" ", Dim);
        Write("⟨", color);
        Write(cap, color);
        Write("⟩", color);
    }

    public static void WriteRunStats(RootCli.Core.Agent.AgentRunStats stats)
    {
        if (stats == null || !stats.HasActivity)
        {
            return;
        }

        Console.WriteLine();
        Rule();
        Console.Write("  ");
        Write("recap", Dim);
        Console.Write("  ");

        Write(stats.CommandsRan.ToString(), BrandStrong);
        Write(" cmd", Dim);
        Write("  ·  ", Dim);

        Write(stats.FilesEdited.ToString(), BrandStrong);
        Write(" file" + (stats.FilesEdited == 1 ? "" : "s") + " edited", Dim);
        Write("  ·  ", Dim);

        Write("+" + stats.LinesAdded, Ok);
        Write(" ", Dim);
        Write("−" + stats.LinesRemoved, Err);

        if (stats.FilesExplored > 0 || stats.Searches > 0)
        {
            Write("  ·  ", Dim);
            Write(stats.FilesExplored + " read", Dim);
            Write(" / ", Dim);
            Write(stats.Searches + " search", Dim);
        }

        Console.WriteLine();
        if (stats.EditedPaths.Count > 0)
        {
            foreach (var path in stats.EditedPaths.Take(8))
            {
                Hint("✎ " + path);
            }
        }
    }

    public static void WriteAnswer(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            WriteAnswerLine(raw);
        }
    }

    private static readonly System.Text.RegularExpressions.Regex AnswerToken = new(
        @"(?<url>https?://[^\s]+)|(?<win>(?:[A-Za-z]:\\|\\\\)[^\s""']+)|(?<unix>(?:\.\.?/|[\w.-]+/)+[\w.-]+)|(?<cmd>\b(?:npm|npx|pnpm|yarn|dotnet|git|gh|cd|dir|copy|move|del|rm|mkdir|curl|wget|ollama|rootcli|python|py|node|cargo|go|docker|kubectl)\b)|(?<num>^\s*\d+\.(?=\s))",
        System.Text.RegularExpressions.RegexOptions.Compiled | System.Text.RegularExpressions.RegexOptions.Multiline);

    private static void WriteAnswerLine(string line)
    {
        if (line.Length == 0)
        {
            Console.WriteLine();
            return;
        }

        var matches = AnswerToken.Matches(line);
        if (matches.Count == 0)
        {
            WriteLine(line, BrandStrong);
            return;
        }

        var i = 0;
        foreach (System.Text.RegularExpressions.Match m in matches)
        {
            if (m.Index > i)
            {
                Write(line[i..m.Index], BrandStrong);
            }

            var color =
                m.Groups["url"].Success ? Tool
                : m.Groups["win"].Success || m.Groups["unix"].Success ? Ask
                : m.Groups["cmd"].Success ? Ok
                : m.Groups["num"].Success ? Brand
                : BrandStrong;

            Write(m.Value, color);
            i = m.Index + m.Length;
        }

        if (i < line.Length)
        {
            Write(line[i..], BrandStrong);
        }

        Console.WriteLine();
    }
}

