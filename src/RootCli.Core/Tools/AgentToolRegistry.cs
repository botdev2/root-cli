using RootCli.Core.Agent;

namespace RootCli.Core.Tools;

public static class AgentToolRegistry
{
    public static List<Dictionary<string, object?>> BuildNativeTools(AgentChatMode mode = AgentChatMode.Agent)
    {
        var tools = new List<Dictionary<string, object?>>();

        Add(tools, mode, "read_file", "Read a repository file by relative path.",
            Prop("path", "string", "Repository-relative file path.", true));
        Add(tools, mode, "list_files", "List files under a repository folder.",
            Prop("path", "string", "Repository-relative folder path.", false));
        Add(tools, mode, "search_files", "Search file names/contents in the repository.",
            Prop("query", "string", "Text to search for.", true));
        Add(tools, mode, "write_file", "Create or replace a whole file (UTF-8, no BOM).",
            Prop("path", "string", "Repository-relative file path.", true),
            Prop("content", "string", "Full file contents.", true));
        Add(tools, mode, "edit_file", "Edit a file: pass content for full replace, or find+replace for a patch.",
            Prop("path", "string", "Repository-relative file path.", true),
            Prop("content", "string", "Optional full file contents.", false),
            Prop("find", "string", "Exact text to find.", false),
            Prop("replace", "string", "Replacement text.", false));
        Add(tools, mode, "apply_patch", "Replace the first exact occurrence of find with replace in a file.",
            Prop("path", "string", "Repository-relative file path.", true),
            Prop("find", "string", "Exact text to find.", true),
            Prop("replace", "string", "Replacement text.", true));
        Add(tools, mode, "delete_file", "Delete a file in the repository.",
            Prop("path", "string", "Repository-relative file path.", true));
        Add(tools, mode, "make_directory", "Create a directory (and parents) in the repository.",
            Prop("path", "string", "Repository-relative folder path.", true));
        Add(tools, mode, "run_command", "Run a shell command inside the repository (bash/sh on Linux, PowerShell on Windows).",
            Prop("command", "string", "Command line to run.", true),
            Prop("cwd", "string", "Optional repo-relative working directory.", false));
        Add(tools, mode, "change_repository", "Switch the active repository root to another folder.",
            Prop("path", "string", "Absolute folder path.", true));

        Add(tools, mode, "git_status", "Show short git status for the repository.");
        Add(tools, mode, "git_diff", "Show git diff (unstaged by default).",
            Prop("path", "string", "Optional repository-relative file path.", false),
            Prop("staged", "boolean", "If true, show staged diff.", false));
        Add(tools, mode, "git_log", "Show recent git commits.",
            Prop("count", "integer", "Number of commits (1-50, default 12).", false));
        Add(tools, mode, "git_branch", "Show current branch and list local branches.");
        Add(tools, mode, "git_checkout", "Switch or create a branch.",
            Prop("branch", "string", "Branch name.", true),
            Prop("create", "boolean", "If true, create the branch (git checkout -b).", false));
        Add(tools, mode, "git_commit", "Create a git commit. Prefer git_diff/git_status first.",
            Prop("message", "string", "Commit message.", true),
            Prop("all", "boolean", "If true (default), stage all changes before commit.", false));
        Add(tools, mode, "git_fetch", "Fetch from remotes.",
            Prop("remote", "string", "Remote name (default origin).", false));
        Add(tools, mode, "git_pull", "Fast-forward pull from a remote branch.",
            Prop("remote", "string", "Remote name (default origin).", false),
            Prop("branch", "string", "Remote branch (optional).", false));
        Add(tools, mode, "git_push", "Push the current branch to the remote.",
            Prop("remote", "string", "Remote name (default origin).", false),
            Prop("branch", "string", "Branch to push (optional).", false),
            Prop("set_upstream", "boolean", "If true, set upstream (-u).", false));
        Add(tools, mode, "create_pull_request", "Create a GitHub pull request (requires PAT / gh auth).",
            Prop("title", "string", "PR title.", false),
            Prop("body", "string", "PR body.", false),
            Prop("base", "string", "Base branch (default main).", false),
            Prop("head", "string", "Head branch (default current).", false),
            Prop("owner", "string", "GitHub owner (optional).", false),
            Prop("repo", "string", "GitHub repo name (optional).", false));

        Add(tools, mode, "github_status", "Check git install, GitHub auth, and workspace repo state.");
        Add(tools, mode, "github_login_pat", "Save a GitHub personal access token for RootCli.",
            Prop("token", "string", "GitHub PAT with repo scope.", true));
        Add(tools, mode, "github_logout", "Remove the saved RootCli GitHub token file.");
        Add(tools, mode, "github_auth_cli", "Authenticate GitHub CLI (gh) using saved/env token when possible.");
        Add(tools, mode, "github_repo_status", "Show branch, remote, and working tree for the workspace.");
        Add(tools, mode, "github_init", "Run git init in the workspace folder.");
        Add(tools, mode, "github_sync", "Fetch, pull, and optionally push to synchronize with remote.",
            Prop("push", "boolean", "Also push local commits (default true).", false));
        Add(tools, mode, "github_create_repo", "Create GitHub repo via gh and push (requires gh).",
            Prop("name", "string", "Repository name (default folder name).", false),
            Prop("visibility", "string", "private or public (default private).", false),
            Prop("owner", "string", "Owner/org (optional).", false));
        Add(tools, mode, "github_search_repositories", "Search GitHub repositories via API.",
            Prop("query", "string", "Search query.", true),
            Prop("limit", "integer", "Max results (default 10).", false));
        Add(tools, mode, "github_list_branches", "List remote branches via GitHub API.",
            Prop("owner", "string", "Owner (optional).", false),
            Prop("repo", "string", "Repo (optional).", false),
            Prop("limit", "integer", "Max branches.", false));
        Add(tools, mode, "github_list_commits", "List commits via GitHub API.",
            Prop("owner", "string", "Owner (optional).", false),
            Prop("repo", "string", "Repo (optional).", false),
            Prop("branch", "string", "Branch (optional).", false),
            Prop("limit", "integer", "Max commits.", false));
        Add(tools, mode, "github_get_repository", "Get GitHub repository metadata.",
            Prop("owner", "string", "Owner (optional).", false),
            Prop("repo", "string", "Repo (optional).", false));

        Add(tools, mode, "github_fetch", "Alias for git_fetch.",
            Prop("remote", "string", "Remote name.", false));
        Add(tools, mode, "github_pull", "Alias for git_pull.",
            Prop("remote", "string", "Remote name.", false),
            Prop("branch", "string", "Branch.", false));
        Add(tools, mode, "github_push", "Alias for git_push.",
            Prop("remote", "string", "Remote name.", false),
            Prop("branch", "string", "Branch.", false),
            Prop("set_upstream", "boolean", "Set upstream.", false));
        Add(tools, mode, "github_commit", "Alias for git_commit.",
            Prop("message", "string", "Commit message.", true),
            Prop("all", "boolean", "Stage all (default true).", false));
        Add(tools, mode, "github_checkout", "Alias for git_checkout.",
            Prop("branch", "string", "Branch name.", true),
            Prop("create", "boolean", "Create branch.", false));
        Add(tools, mode, "github_log", "Alias for git_log.",
            Prop("count", "integer", "Commit count.", false));
        Add(tools, mode, "github_create_pull_request", "Alias for create_pull_request.",
            Prop("title", "string", "PR title.", false),
            Prop("body", "string", "PR body.", false),
            Prop("base", "string", "Base branch.", false),
            Prop("head", "string", "Head branch.", false),
            Prop("owner", "string", "Owner.", false),
            Prop("repo", "string", "Repo.", false));

        Add(tools, mode, "internet_search", "Search the web or fetch a URL (DuckDuckGo / direct HTTP).",
            Prop("query", "string", "Search query or http(s) URL.", true));
        Add(tools, mode, "system_info", "Show local OS, time, user, and machine context.");

        return tools;
    }

    public static IReadOnlyList<string> ListToolNames() =>
        BuildNativeTools(AgentChatMode.Agent)
            .Select(t =>
            {
                if (t.TryGetValue("function", out var fn) &&
                    fn is Dictionary<string, object?> f &&
                    f.TryGetValue("name", out var n))
                {
                    return Convert.ToString(n) ?? "";
                }

                return "";
            })
            .Where(n => !string.IsNullOrWhiteSpace(n))
            .ToList();

    public static string BuildNativeToolKey(string serverId, string toolName) =>
        "mcp__" + Sanitize(serverId) + "__" + Sanitize(toolName);

    public static bool TryParseNativeToolKey(string? nativeName, out string serverId, out string toolName)
    {
        serverId = "";
        toolName = "";
        if (string.IsNullOrWhiteSpace(nativeName) ||
            !nativeName.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var parts = nativeName.Split(new[] { "__" }, StringSplitOptions.None);
        if (parts.Length < 3)
        {
            return false;
        }

        serverId = parts[1];
        toolName = string.Join("__", parts.Skip(2));
        return !string.IsNullOrWhiteSpace(serverId) && !string.IsNullOrWhiteSpace(toolName);
    }

    public static Dictionary<string, object?> MakeFunction(
        string name,
        string description,
        params Dictionary<string, object?>[] properties)
    {
        var props = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        var required = new List<string>();
        foreach (var p in properties)
        {
            var key = Convert.ToString(p["name"]) ?? "";
            props[key] = new Dictionary<string, object?>
            {
                ["type"] = p["type"],
                ["description"] = p["description"]
            };
            if (p.TryGetValue("required", out var req) && req is true)
            {
                required.Add(key);
            }
        }

        return new Dictionary<string, object?>
        {
            ["type"] = "function",
            ["function"] = new Dictionary<string, object?>
            {
                ["name"] = name,
                ["description"] = description,
                ["parameters"] = new Dictionary<string, object?>
                {
                    ["type"] = "object",
                    ["properties"] = props,
                    ["required"] = required.ToArray()
                }
            }
        };
    }

    private static void Add(
        List<Dictionary<string, object?>> tools,
        AgentChatMode mode,
        string name,
        string description,
        params Dictionary<string, object?>[] properties)
    {
        if (!AgentModePolicy.AllowsNativeToolName(mode, name))
        {
            return;
        }

        tools.Add(MakeFunction(name, description, properties));
    }

    private static Dictionary<string, object?> Prop(string name, string type, string description, bool required) =>
        new()
        {
            ["name"] = name,
            ["type"] = type,
            ["description"] = description,
            ["required"] = required
        };

    private static string Sanitize(string value) =>
        (value ?? "").Replace("\\", "_").Replace(" ", "_");
}
