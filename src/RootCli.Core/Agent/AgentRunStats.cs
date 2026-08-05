
using RootCli.Core.Tools;

namespace RootCli.Core.Agent;

public sealed class AgentRunStats
{
    private readonly HashSet<string> exploredPaths = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> editedPaths = new(StringComparer.OrdinalIgnoreCase);

    public int FilesEdited { get; private set; }
    public int FilesExplored { get; private set; }
    public int Searches { get; private set; }
    public int CommandsRan { get; private set; }
    public int LinesAdded { get; private set; }
    public int LinesRemoved { get; private set; }
    public int ToolCalls { get; private set; }

    public bool HasActivity =>
        FilesEdited > 0 || FilesExplored > 0 || Searches > 0 || CommandsRan > 0 ||
        LinesAdded > 0 || LinesRemoved > 0 || ToolCalls > 0;

    public IReadOnlyCollection<string> EditedPaths => editedPaths;

    public void Record(ToolCall call, string result)
    {
        if (call == null)
        {
            return;
        }

        ToolCalls++;
        switch (call.Type)
        {
            case ToolType.ReadFile:
            case ToolType.ListFiles:
                if (TrackExplored(call.Path))
                {
                    FilesExplored++;
                }

                break;
            case ToolType.SearchFiles:
            case ToolType.InternetSearch:
            case ToolType.GitHubSearchRepos:
                Searches++;
                break;
            case ToolType.WriteFile:
            case ToolType.ApplyPatch:
            case ToolType.DeleteFile:
            case ToolType.MakeDirectory:
                if (IsSuccessfulFileMutation(call, result) && TrackEdited(call.Path))
                {
                    FilesEdited++;
                }

                if (IsSuccessfulFileMutation(call, result))
                {
                    RecordLineDelta(call);
                }

                break;
            case ToolType.RunCommand:
            case ToolType.GitCheckout:
            case ToolType.GitCommit:
            case ToolType.GitFetch:
            case ToolType.GitPull:
            case ToolType.GitPush:
            case ToolType.CreatePullRequest:
            case ToolType.GitHubSync:
            case ToolType.GitHubCreateRepo:
            case ToolType.GitHubInit:
                CommandsRan++;
                break;
            case ToolType.McpCall:
                RecordMcp(call);
                break;
        }
    }

    private void RecordMcp(ToolCall call)
    {
        var name = (call.McpToolName ?? "").ToLowerInvariant();
        if (name.Contains("search") || name.Contains("query") || name.Contains("semantic"))
        {
            Searches++;
        }
        else if (name.Contains("index") || name.Contains("list") || name.Contains("trace") ||
                 name.Contains("architecture") || name.Contains("snippet"))
        {
            FilesExplored++;
        }
        else
        {
            CommandsRan++;
        }
    }

    private void RecordLineDelta(ToolCall call)
    {
        switch (call.Type)
        {
            case ToolType.WriteFile:
                LinesAdded += CountLines(call.FullContent);
                break;
            case ToolType.ApplyPatch:
                if (!string.IsNullOrWhiteSpace(call.FullContent))
                {
                    LinesAdded += CountLines(call.FullContent);
                }
                else
                {
                    LinesRemoved += CountLines(call.Find);
                    LinesAdded += CountLines(call.Replace);
                }

                break;
            case ToolType.DeleteFile:

                break;
        }
    }

    public static bool IsSuccessfulFileMutation(ToolCall call, string result)
    {
        if (call == null || string.IsNullOrWhiteSpace(result))
        {
            return false;
        }

        var text = result.Trim();
        if (text.StartsWith("Rejected", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("failed", StringComparison.OrdinalIgnoreCase) ||
            text.Contains("requires", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return call.Type switch
        {
            ToolType.WriteFile or ToolType.ApplyPatch =>
                text.StartsWith("Wrote ", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Patched ", StringComparison.OrdinalIgnoreCase),
            ToolType.DeleteFile => text.StartsWith("Deleted ", StringComparison.OrdinalIgnoreCase),
            ToolType.MakeDirectory => text.StartsWith("Created directory ", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private bool TrackExplored(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return exploredPaths.Add(Normalize(path));
    }

    private bool TrackEdited(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        return editedPaths.Add(Normalize(path));
    }

    private static string Normalize(string path)
    {
        try { return Path.GetFullPath(path.Trim()); }
        catch { return path.Trim(); }
    }

    private static int CountLines(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return 0;
        }

        var n = 1;
        foreach (var c in text)
        {
            if (c == '\n')
            {
                n++;
            }
        }

        return n;
    }
}
