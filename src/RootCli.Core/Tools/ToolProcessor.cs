
using System.Diagnostics;
using System.Text;
using RootCli.Core.Agent;
using RootCli.Core.Git;
using RootCli.Core.Mcp;
using RootCli.Core.Ollama;
using RootCli.Core.Workspace;

namespace RootCli.Core.Tools;

public static class ToolProcessor
{
    public static List<ToolCall> FromNativeToolCalls(IEnumerable<OllamaToolCall>? nativeCalls)
    {
        var result = new List<ToolCall>();
        if (nativeCalls == null)
        {
            return result;
        }

        foreach (var native in nativeCalls)
        {
            if (native == null || string.IsNullOrWhiteSpace(native.Name))
            {
                continue;
            }

            result.Add(FromNative(native.Name, native.Arguments));
        }

        return result;
    }

    public static ToolCall FromNative(string name, Dictionary<string, object?> args)
    {
        args ??= new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (AgentToolRegistry.TryParseNativeToolKey(name, out var serverId, out var mcpToolName))
        {
            return new ToolCall
            {
                Type = ToolType.McpCall,
                ToolName = name.Trim(),
                McpServerId = serverId,
                McpToolName = mcpToolName,
                Arguments = new Dictionary<string, object?>(args, StringComparer.OrdinalIgnoreCase),
                Risk = McpServerManager.RequiresApproval(mcpToolName) ? "medium" : "low"
            };
        }

        var call = new ToolCall
        {
            ToolName = name.Trim(),
            Path = First(Str(args, "path"), Str(args, "branch"), Str(args, "remote")) ?? "",
            Query = First(Str(args, "query"), Str(args, "count"), Str(args, "staged"), Str(args, "create"), Str(args, "all"), Str(args, "push")) ?? "",
            Find = First(Str(args, "find"), Str(args, "set_upstream")) ?? "",
            Replace = Str(args, "replace"),
            Command = Str(args, "command"),
            Cwd = Str(args, "cwd"),
            FullContent = First(Str(args, "content"), Str(args, "message"), Str(args, "title"), Str(args, "token")) ?? "",
            Arguments = new Dictionary<string, object?>(args, StringComparer.OrdinalIgnoreCase)
        };

        call.Type = MapToolName(call.ToolName);
        call.Risk = EstimateRisk(call);
        return call;
    }

    public static ToolType MapToolName(string? name) =>
        (name ?? "").Trim().ToLowerInvariant() switch
        {
            "read_file" => ToolType.ReadFile,
            "list_files" => ToolType.ListFiles,
            "search_files" => ToolType.SearchFiles,
            "write_file" => ToolType.WriteFile,
            "edit_file" or "apply_patch" => ToolType.ApplyPatch,
            "delete_file" => ToolType.DeleteFile,
            "make_directory" => ToolType.MakeDirectory,
            "run_command" => ToolType.RunCommand,
            "change_repository" or "change_workspace" => ToolType.ChangeWorkspace,
            "git_status" => ToolType.GitStatus,
            "git_diff" => ToolType.GitDiff,
            "git_log" or "github_log" => ToolType.GitLog,
            "git_branch" => ToolType.GitBranch,
            "git_checkout" or "github_checkout" => ToolType.GitCheckout,
            "git_commit" or "github_commit" => ToolType.GitCommit,
            "git_fetch" or "github_fetch" => ToolType.GitFetch,
            "git_pull" or "github_pull" => ToolType.GitPull,
            "git_push" or "github_push" => ToolType.GitPush,
            "create_pull_request" or "github_create_pull_request" => ToolType.CreatePullRequest,
            "internet_search" => ToolType.InternetSearch,
            "system_info" => ToolType.SystemInfo,
            "github_status" => ToolType.GitHubStatus,
            "github_login_pat" => ToolType.GitHubLoginPat,
            "github_logout" => ToolType.GitHubLogout,
            "github_auth_cli" => ToolType.GitHubAuthCli,
            "github_create_repo" => ToolType.GitHubCreateRepo,
            "github_repo_status" => ToolType.GitHubRepoStatus,
            "github_init" => ToolType.GitHubInit,
            "github_sync" => ToolType.GitHubSync,
            "github_search_repositories" => ToolType.GitHubSearchRepos,
            "github_list_branches" => ToolType.GitHubListBranches,
            "github_list_commits" => ToolType.GitHubListCommits,
            "github_get_repository" => ToolType.GitHubGetRepository,
            _ => ToolType.Unknown
        };

    public static string Execute(
        ToolCall call,
        WorkspaceService workspace,
        Func<ToolCall, bool> approve,
        Action<string>? log = null,
        AgentChatMode mode = AgentChatMode.Agent)
    {
        if (call == null)
        {
            return "Invalid tool call.";
        }

        if (call.Type == ToolType.Unknown)
        {
            return "Unknown tool: " + call.ToolName;
        }

        var allowName = call.Type == ToolType.McpCall ? call.McpToolName : call.ToolName;
        if (!AgentModePolicy.AllowsTool(mode, call.Type, allowName))
        {
            return "Rejected by " + AgentModePolicy.ToDisplayLabel(mode) +
                   " mode (read-only): " + call.ToolName;
        }

        if (!approve(call))
        {
            return "Rejected by approval policy: " + call.ToolName;
        }

        try
        {
            return call.Type switch
            {
                ToolType.ReadFile => workspace.ReadText(call.Path),
                ToolType.ListFiles => ExecuteListFiles(call, workspace),
                ToolType.SearchFiles => ExecuteSearchFiles(call, workspace),
                ToolType.WriteFile => ExecuteWrite(call, workspace),
                ToolType.ApplyPatch => ExecutePatch(call, workspace),
                ToolType.DeleteFile => ExecuteDelete(call, workspace),
                ToolType.MakeDirectory => ExecuteMkdir(call, workspace),
                ToolType.RunCommand => RunCommand(call, workspace, log),
                ToolType.ChangeWorkspace => ExecuteChangeWorkspace(call, workspace),
                ToolType.GitStatus => ExecuteGitStatus(workspace),
                ToolType.GitDiff => ExecuteGitDiff(call, workspace),
                ToolType.GitLog => ExecuteGitLog(call, workspace),
                ToolType.GitBranch => ExecuteGitBranch(workspace),
                ToolType.GitCheckout => ExecuteGitCheckout(call, workspace),
                ToolType.GitCommit => ExecuteGitCommit(call, workspace),
                ToolType.GitFetch => GitRepositoryService.Fetch(workspace.RootPath, Arg(call, "remote")),
                ToolType.GitPull => GitRepositoryService.Pull(workspace.RootPath, Arg(call, "remote"), Arg(call, "branch")),
                ToolType.GitPush => GitRepositoryService.Push(
                    workspace.RootPath,
                    Arg(call, "remote"),
                    Arg(call, "branch"),
                    IsTruthy(Arg(call, "set_upstream")) || string.Equals(call.Find, "upstream", StringComparison.OrdinalIgnoreCase)),
                ToolType.CreatePullRequest => GitHubService.CreatePullRequest(
                    Arg(call, "owner"), Arg(call, "repo"), Arg(call, "title") ?? call.FullContent,
                    Arg(call, "head"), Arg(call, "base"), Arg(call, "body"), workspace),
                ToolType.InternetSearch => InternetSearchService.Search(First(call.Query, Arg(call, "query")) ?? ""),
                ToolType.SystemInfo => ExecuteSystemInfo(workspace),
                ToolType.GitHubStatus => GitHubService.Status(workspace),
                ToolType.GitHubLoginPat => GitHubService.SavePersonalAccessToken(First(call.FullContent, Arg(call, "token")) ?? ""),
                ToolType.GitHubLogout => GitHubService.Logout(),
                ToolType.GitHubAuthCli => GitHubService.AuthCli(),
                ToolType.GitHubCreateRepo => GitHubService.CreateRepoViaGh(Arg(call, "name"), Arg(call, "visibility"), Arg(call, "owner"), workspace),
                ToolType.GitHubRepoStatus => GitRepositoryService.FormatSnapshot(GitRepositoryService.Inspect(workspace.RootPath)),
                ToolType.GitHubInit => GitRepositoryService.InitRepository(workspace.RootPath),
                ToolType.GitHubSync => GitRepositoryService.Sync(
                    workspace.RootPath,
                    call.Query is null or "" || IsTruthy(call.Query) || IsTruthy(Arg(call, "push"))),
                ToolType.GitHubSearchRepos => GitHubService.SearchRepositories(
                    First(call.Query, Arg(call, "query")) ?? "",
                    ParseInt(Arg(call, "limit"), 10)),
                ToolType.GitHubListBranches => GitHubService.ListBranches(
                    Arg(call, "owner"), Arg(call, "repo"), ParseInt(Arg(call, "limit"), 30), workspace),
                ToolType.GitHubListCommits => GitHubService.ListCommits(
                    Arg(call, "owner"), Arg(call, "repo"), Arg(call, "branch"),
                    ParseInt(Arg(call, "limit") ?? call.Query, 12), workspace),
                ToolType.GitHubGetRepository => GitHubService.GetRepository(Arg(call, "owner"), Arg(call, "repo"), workspace),
                ToolType.McpCall => ExecuteMcp(call, log),
                _ => "Unhandled tool: " + call.ToolName
            };
        }
        catch (Exception ex)
        {
            return "Tool error (" + call.ToolName + "): " + ex.Message;
        }
    }

    public static void AppendAgentToolInstructions(StringBuilder builder, AgentChatMode mode)
    {
        if (AgentModePolicy.IsReadOnly(mode))
        {
            builder.AppendLine("Read-only tools: read_file, list_files, search_files, git_status, git_diff, git_log, git_branch, git_fetch, git_pull, github_status/repo_status/list_*/search/get_repository, internet_search, system_info, read MCP.");
            builder.AppendLine("Forbidden: writes, deletes, shell, commits, push, PR create, mutating MCP.");
            return;
        }

        builder.AppendLine("Native tools include files, shell, git_*, create_pull_request, github_* assistants, internet_search, system_info, and MCP.");
        builder.AppendLine("Prefer git_status/git_diff/git_log over raw git via run_command. Prefer create_pull_request / github_* for GitHub.");
        builder.AppendLine("Auth: ROOTCLI_GITHUB_TOKEN / GITHUB_TOKEN, or github_login_pat, or gh auth.");
    }

    private static string EstimateRisk(ToolCall call) =>
        call.Type switch
        {
            ToolType.ReadFile or ToolType.ListFiles or ToolType.SearchFiles or ToolType.MakeDirectory
                or ToolType.GitStatus or ToolType.GitDiff or ToolType.GitLog or ToolType.GitBranch
                or ToolType.SystemInfo or ToolType.GitHubStatus or ToolType.GitHubRepoStatus
                or ToolType.GitHubListBranches or ToolType.GitHubListCommits or ToolType.GitHubGetRepository
                or ToolType.GitHubSearchRepos => "low",
            ToolType.WriteFile or ToolType.ApplyPatch or ToolType.GitFetch or ToolType.GitPull
                or ToolType.InternetSearch or ToolType.GitHubSync or ToolType.GitHubInit
                or ToolType.GitHubLoginPat or ToolType.GitHubAuthCli or ToolType.ChangeWorkspace => "medium",
            ToolType.DeleteFile or ToolType.RunCommand or ToolType.GitCheckout or ToolType.GitCommit
                or ToolType.GitPush or ToolType.CreatePullRequest or ToolType.GitHubCreateRepo
                or ToolType.GitHubLogout => "high",
            _ => "high"
        };

    private static string ExecuteListFiles(ToolCall call, WorkspaceService workspace)
    {
        var entries = workspace.ListEntries(string.IsNullOrWhiteSpace(call.Path) ? "." : call.Path);
        if (entries.Count == 0)
        {
            return "(empty)";
        }

        var sb = new StringBuilder();
        foreach (var e in entries)
        {
            sb.AppendLine((e.IsDirectory ? "dir  " : "file ") + e.RelativePath);
        }

        return sb.ToString().TrimEnd();
    }

    private static string ExecuteSearchFiles(ToolCall call, WorkspaceService workspace)
    {
        var hits = workspace.Search(call.Query);
        if (hits.Count == 0)
        {
            return "No matches for: " + call.Query;
        }

        var sb = new StringBuilder();
        foreach (var h in hits)
        {
            sb.AppendLine(h.RelativePath);
        }

        return sb.ToString().TrimEnd();
    }

    private static string ExecuteWrite(ToolCall call, WorkspaceService workspace)
    {
        workspace.WriteText(call.Path, call.FullContent);
        return "Wrote " + call.Path;
    }

    private static string ExecutePatch(ToolCall call, WorkspaceService workspace)
    {
        if (!string.IsNullOrWhiteSpace(call.FullContent) ||
            (string.IsNullOrWhiteSpace(call.Find) && !string.IsNullOrWhiteSpace(call.Replace)))
        {
            var content = !string.IsNullOrWhiteSpace(call.FullContent) ? call.FullContent : call.Replace;
            workspace.WriteText(call.Path, content);
            return "Wrote " + call.Path;
        }

        if (!workspace.TryApplyPatch(call.Path, call.Find, call.Replace, out var patchError))
        {
            return "apply_patch failed: " + patchError;
        }

        return "Patched " + call.Path;
    }

    private static string ExecuteDelete(ToolCall call, WorkspaceService workspace)
    {
        if (!workspace.TryDeleteFile(call.Path, out var deleteError))
        {
            return "delete_file failed: " + deleteError;
        }

        return "Deleted " + call.Path;
    }

    private static string ExecuteMkdir(ToolCall call, WorkspaceService workspace)
    {
        var full = workspace.ResolveInsidePath(call.Path);
        Directory.CreateDirectory(full);
        return "Created directory " + call.Path;
    }

    private static string ExecuteChangeWorkspace(ToolCall call, WorkspaceService workspace)
    {
        var path = First(call.Path, Arg(call, "path"));
        if (string.IsNullOrWhiteSpace(path))
        {
            return "change_repository requires path.";
        }

        if (!workspace.TrySetRoot(path, false, out var error))
        {
            return "change_repository rejected: " + error;
        }

        return "Changed repository: " + workspace.RootPath;
    }

    private static string ExecuteGitStatus(WorkspaceService workspace)
    {
        if (!workspace.HasWorkspace)
        {
            return "git_status failed: no repository selected.";
        }

        if (!GitRuntimeService.Run(workspace.RootPath, "status --short", 20000, out var output))
        {
            return "git_status failed:\n" + output;
        }

        return string.IsNullOrWhiteSpace(output) ? "git_status: clean working tree" : "git_status:\n" + output.Trim();
    }

    private static string ExecuteGitDiff(ToolCall call, WorkspaceService workspace)
    {
        if (!workspace.HasWorkspace)
        {
            return "git_diff failed: no repository selected.";
        }

        var staged = IsTruthy(Arg(call, "staged")) ||
                     string.Equals(call.Query, "staged", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(call.Query, "true", StringComparison.OrdinalIgnoreCase);
        var args = staged ? "diff --staged" : "diff";
        var path = First(Arg(call, "path"),
            string.Equals(call.Path, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(call.Path, "false", StringComparison.OrdinalIgnoreCase)
                ? null
                : call.Path);
        if (!string.IsNullOrWhiteSpace(path) &&
            !string.Equals(path, "origin", StringComparison.OrdinalIgnoreCase))
        {
            args += " -- \"" + path.Trim().Replace("\"", "") + "\"";
        }

        if (!GitRuntimeService.Run(workspace.RootPath, args, 60000, out var output))
        {
            return "git_diff failed:\n" + output;
        }

        if (string.IsNullOrWhiteSpace(output))
        {
            return staged ? "git_diff: no staged changes" : "git_diff: no unstaged changes";
        }

        var trimmed = output.Trim();
        if (trimmed.Length > 24000)
        {
            trimmed = trimmed[..24000] + "\n\n[git_diff truncated]";
        }

        return "git_diff" + (staged ? " (staged)" : "") + ":\n" + trimmed;
    }

    private static string ExecuteGitLog(ToolCall call, WorkspaceService workspace)
    {
        if (!workspace.HasWorkspace)
        {
            return "git_log failed: no repository selected.";
        }

        var count = ParseInt(First(Arg(call, "count"), call.Query), 12);
        return "git_log:\n" + GitRepositoryService.Log(workspace.RootPath, count).Trim();
    }

    private static string ExecuteGitBranch(WorkspaceService workspace)
    {
        if (!workspace.HasWorkspace)
        {
            return "git_branch failed: no repository selected.";
        }

        var snapshot = GitRepositoryService.Inspect(workspace.RootPath);
        var list = GitRepositoryService.ListLocalBranches(workspace.RootPath).Trim();
        var builder = new StringBuilder();
        builder.AppendLine("git_branch:");
        if (snapshot.IsRepository)
        {
            builder.AppendLine("current: " + (snapshot.CurrentBranch ?? "(detached)"));
        }

        builder.AppendLine(string.IsNullOrWhiteSpace(list) ? "(no local branches)" : list);
        return builder.ToString().Trim();
    }

    private static string ExecuteGitCheckout(ToolCall call, WorkspaceService workspace)
    {
        if (!workspace.HasWorkspace)
        {
            return "git_checkout failed: no repository selected.";
        }

        var branch = First(Arg(call, "branch"), call.Path);
        if (string.IsNullOrWhiteSpace(branch))
        {
            return "git_checkout requires branch.";
        }

        var create = IsTruthy(Arg(call, "create")) ||
                     string.Equals(call.Query, "create", StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(call.Query, "true", StringComparison.OrdinalIgnoreCase);
        return GitRepositoryService.Checkout(workspace.RootPath, branch, create);
    }

    private static string ExecuteGitCommit(ToolCall call, WorkspaceService workspace)
    {
        if (!workspace.HasWorkspace)
        {
            return "git_commit failed: no repository selected.";
        }

        var message = First(Arg(call, "message"), call.FullContent);
        if (string.IsNullOrWhiteSpace(message))
        {
            return "git_commit requires message.";
        }

        var all = string.IsNullOrWhiteSpace(Arg(call, "all")) && string.IsNullOrWhiteSpace(call.Query)
                  || IsTruthy(Arg(call, "all"))
                  || IsTruthy(call.Query)
                  || string.Equals(call.Query, "all", StringComparison.OrdinalIgnoreCase);
        return GitRepositoryService.Commit(workspace.RootPath, message, all);
    }

    private static string ExecuteSystemInfo(WorkspaceService workspace)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Time: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss zzz"));
        sb.AppendLine("UTC:  " + DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm:ss") + "Z");
        sb.AppendLine("OS:   " + Environment.OSVersion);
        sb.AppendLine("Machine: " + Environment.MachineName);
        sb.AppendLine("User: " + Environment.UserName);
        sb.AppendLine(".NET: " + Environment.Version);
        sb.AppendLine("CWD:  " + Environment.CurrentDirectory);
        if (workspace.HasWorkspace)
        {
            sb.AppendLine("Repo: " + workspace.RootPath);
        }

        return sb.ToString().Trim();
    }

    private static string ExecuteMcp(ToolCall call, Action<string>? log)
    {
        log?.Invoke("mcp://" + call.McpServerId + "/" + call.McpToolName);
        return McpServerManager.CallTool(call.McpServerId, call.McpToolName, call.Arguments);
    }

    private static string RunCommand(ToolCall call, WorkspaceService workspace, Action<string>? log)
    {
        if (string.IsNullOrWhiteSpace(call.Command))
        {
            return "run_command requires command.";
        }

        var cwd = workspace.RootPath;
        if (!string.IsNullOrWhiteSpace(call.Cwd))
        {
            cwd = workspace.ResolveInsidePath(call.Cwd);
        }

        log?.Invoke("$ " + call.Command);
        var (fileName, arguments) = BuildShellInvocation(call.Command);
        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            WorkingDirectory = cwd,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi);
        if (process == null)
        {
            return "Failed to start shell.";
        }

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(120_000);
        var sb = new StringBuilder();
        sb.AppendLine("exit=" + process.ExitCode);
        if (!string.IsNullOrWhiteSpace(stdout))
        {
            sb.AppendLine(stdout.TrimEnd());
        }

        if (!string.IsNullOrWhiteSpace(stderr))
        {
            sb.AppendLine("stderr:");
            sb.AppendLine(stderr.TrimEnd());
        }

        return sb.ToString().TrimEnd();
    }

    private static (string FileName, string Arguments) BuildShellInvocation(string command)
    {
        if (OperatingSystem.IsWindows())
        {
            return ("powershell.exe", "-NoProfile -NonInteractive -Command " + QuotePowerShell(command));
        }

        var shell = Environment.GetEnvironmentVariable("SHELL");
        if (string.IsNullOrWhiteSpace(shell) || !File.Exists(shell))
        {
            shell = File.Exists("/bin/bash") ? "/bin/bash"
                : File.Exists("/usr/bin/bash") ? "/usr/bin/bash"
                : File.Exists("/bin/sh") ? "/bin/sh"
                : "sh";
        }

        return (shell, "-lc " + QuoteUnixShell(command));
    }

    private static string QuotePowerShell(string command) =>
        "'" + command.Replace("'", "''") + "'";

    private static string QuoteUnixShell(string command) =>
        "'" + command.Replace("'", "'\\''") + "'";

    private static string Str(Dictionary<string, object?> args, string key)
    {
        if (!args.TryGetValue(key, out var value) || value == null)
        {
            return "";
        }

        if (value is bool b)
        {
            return b ? "true" : "false";
        }

        return Convert.ToString(value)?.Trim() ?? "";
    }

    private static string? Arg(ToolCall call, string key)
    {
        if (call.Arguments.TryGetValue(key, out var value) && value != null)
        {
            if (value is bool b)
            {
                return b ? "true" : "false";
            }

            var s = Convert.ToString(value)?.Trim();
            if (!string.IsNullOrWhiteSpace(s))
            {
                return s;
            }
        }

        return null;
    }

    private static string? First(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static bool IsTruthy(string? value) =>
        string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase);

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var n) && n > 0 ? n : fallback;
}
