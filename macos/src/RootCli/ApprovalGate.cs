using RootCli.Core.Tools;

namespace RootCli;

internal sealed class ApprovalGate
{

    public bool PrefYes { get; set; }

    public bool AlwaysYes { get; set; }

    public bool Approve(ToolCall call)
    {
        if (string.Equals(call.Risk, "low", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (AlwaysYes)
        {
            return true;
        }

        var high = string.Equals(call.Risk, "high", StringComparison.OrdinalIgnoreCase);
        if (PrefYes && !high)
        {
            return true;
        }

        if (Console.IsInputRedirected)
        {
            return PrefYes && !high;
        }

        var detail = call.ToolName;
        if (!string.IsNullOrWhiteSpace(call.Command))
        {
            detail += " — " + Truncate(call.Command, 80);
        }
        else if (!string.IsNullOrWhiteSpace(call.Path))
        {
            detail += " — " + Truncate(call.Path, 80);
        }
        Console.Error.WriteLine();
        Console.Error.Write("  Allow " + detail + " [" + call.Risk + "]? [y/n/ay] ");
        var line = (Console.ReadLine() ?? "").Trim().ToLowerInvariant();

        if (line is "ay" or "always" or "a")
        {
            AlwaysYes = true;
            PrefYes = true;
            Console.Error.WriteLine("  always yes — won't ask again this session.");
            return true;
        }

        if (line is "y" or "yes")
        {
            return true;
        }

        Console.Error.WriteLine("  denied.");
        return false;
    }

    private static string Truncate(string text, int max)
    {
        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length <= max ? text : text[..(max - 1)] + "…";
    }
}
