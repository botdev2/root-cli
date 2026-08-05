namespace RootCli.Core.Tools;

public enum ToolType
{
    ReadFile,
    ListFiles,
    SearchFiles,
    WriteFile,
    ApplyPatch,
    DeleteFile,
    MakeDirectory,
    RunCommand,
    GitStatus,
    GitDiff,
    GitLog,
    GitBranch,
    GitCheckout,
    GitCommit,
    GitFetch,
    GitPull,
    GitPush,
    CreatePullRequest,
    ChangeWorkspace,
    InternetSearch,
    SystemInfo,
    GitHubStatus,
    GitHubLoginPat,
    GitHubLogout,
    GitHubAuthCli,
    GitHubCreateRepo,
    GitHubRepoStatus,
    GitHubInit,
    GitHubSync,
    GitHubSearchRepos,
    GitHubListBranches,
    GitHubListCommits,
    GitHubGetRepository,
    McpCall,
    Unknown
}

public sealed class ToolCall
{
    public ToolType Type { get; set; }
    public string ToolName { get; set; } = "";
    public string Path { get; set; } = "";
    public string Query { get; set; } = "";
    public string Find { get; set; } = "";
    public string Replace { get; set; } = "";
    public string Command { get; set; } = "";
    public string Cwd { get; set; } = "";
    public string FullContent { get; set; } = "";
    public string Risk { get; set; } = "low";
    public string McpServerId { get; set; } = "";
    public string McpToolName { get; set; } = "";
    public Dictionary<string, object?> Arguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
