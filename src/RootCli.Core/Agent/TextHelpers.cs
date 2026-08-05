using System.Text.RegularExpressions;

namespace RootCli.Core.Agent;

public static class TextHelpers
{
    private static readonly Regex ThinkBlock = new(
        @"<think>[\s\S]*?</think>",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static string GetVisibleAssistantText(string? raw)
    {
        if (string.IsNullOrEmpty(raw))
        {
            return "";
        }

        return ThinkBlock.Replace(raw, "").Trim();
    }

    public static Dictionary<string, object?> Message(string role, string content) =>
        new()
        {
            ["role"] = role,
            ["content"] = content ?? ""
        };

    public static Dictionary<string, object?> ToolResultMessage(string toolName, string content) =>
        new()
        {
            ["role"] = "tool",
            ["name"] = toolName,
            ["content"] = content ?? ""
        };
}
