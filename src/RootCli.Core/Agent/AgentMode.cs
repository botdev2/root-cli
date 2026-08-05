using RootCli.Core.Mcp;
using RootCli.Core.Tools;

namespace RootCli.Core.Agent;

public enum AgentChatMode
{
    Agent = 0,
    Ask = 1,
    Plan = 2
}

public static class AgentModePolicy
{
    public static AgentChatMode Parse(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return AgentChatMode.Agent;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "ask" => AgentChatMode.Ask,
            "plan" => AgentChatMode.Plan,
            _ => AgentChatMode.Agent
        };
    }

    public static string ToStorage(AgentChatMode mode) =>
        mode switch
        {
            AgentChatMode.Ask => "ask",
            AgentChatMode.Plan => "plan",
            _ => "agent"
        };

    public static string ToDisplayLabel(AgentChatMode mode) =>
        mode switch
        {
            AgentChatMode.Ask => "Ask",
            AgentChatMode.Plan => "Plan",
            _ => "Agent"
        };

    public static string Describe(AgentChatMode mode) =>
        mode switch
        {
            AgentChatMode.Ask => "Ask — answer questions. Read-only; no edits or shell.",
            AgentChatMode.Plan => "Plan — design the approach. Read-only; no implementation.",
            _ => "Agent — do the work. Edit files, run commands, git, GitHub."
        };

    public static bool IsReadOnly(AgentChatMode mode) =>
        mode is AgentChatMode.Ask or AgentChatMode.Plan;

    public static bool AllowsTool(AgentChatMode mode, ToolType type, string? toolName)
    {
        if (mode == AgentChatMode.Agent)
        {
            return true;
        }

        return type switch
        {
            ToolType.ReadFile or ToolType.ListFiles or ToolType.SearchFiles => true,
            ToolType.GitStatus or ToolType.GitDiff or ToolType.GitLog or ToolType.GitBranch
                or ToolType.GitFetch or ToolType.GitPull => true,
            ToolType.InternetSearch or ToolType.SystemInfo => true,
            ToolType.GitHubStatus or ToolType.GitHubRepoStatus
                or ToolType.GitHubSearchRepos or ToolType.GitHubListBranches
                or ToolType.GitHubListCommits or ToolType.GitHubGetRepository => true,
            ToolType.McpCall => !McpServerManager.RequiresApproval(toolName),
            _ => false
        };
    }

    public static bool AllowsNativeToolName(AgentChatMode mode, string? toolName)
    {
        if (mode == AgentChatMode.Agent)
        {
            return true;
        }

        var name = (toolName ?? "").Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return false;
        }

        if (AgentToolRegistry.TryParseNativeToolKey(name, out _, out var mcpTool))
        {
            return AllowsTool(mode, ToolType.McpCall, mcpTool);
        }

        return AllowsTool(mode, ToolProcessor.MapToolName(name), name);
    }

    public static void AppendSystemPrompt(System.Text.StringBuilder builder, AgentChatMode mode)
    {
        builder.AppendLine("Active chat mode: " + ToDisplayLabel(mode) + ".");
        switch (mode)
        {
            case AgentChatMode.Ask:
                builder.AppendLine("ASK MODE (read-only):");
                builder.AppendLine("- Answer using read/search/git inspect/GitHub read/internet_search tools only.");
                builder.AppendLine("- Allowed: read_file, list_files, search_files, git_status/diff/log/branch/fetch/pull, github_status/repo_status/list_*/search/get_repository, internet_search, system_info, read MCP.");
                builder.AppendLine("- Forbidden: writes, deletes, shell, commits, push, PR create, mutating MCP.");
                builder.AppendLine("- Do not claim you edited files or ran commands.");
                break;
            case AgentChatMode.Plan:
                builder.AppendLine("PLAN MODE (read-only planning):");
                builder.AppendLine("- Explore with Ask-mode tools, then produce a concrete implementation plan.");
                builder.AppendLine("- Do not implement, edit, commit, push, or open PRs.");
                builder.AppendLine("- Include a numbered list (1. 2. 3. …) of actionable steps.");
                builder.AppendLine("- End by asking whether to switch to Agent mode to execute.");
                break;
            default:
                builder.AppendLine("AGENT MODE (full autonomy):");
                builder.AppendLine("- You may inspect, edit, run commands, use git_*/github_*, and create_pull_request.");
                builder.AppendLine("- Prefer git_* / github_* over raw git via run_command.");
                break;
        }
    }
}
