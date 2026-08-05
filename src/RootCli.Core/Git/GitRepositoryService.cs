
using System.Text;
using System.Text.RegularExpressions;

namespace RootCli.Core.Git;

public sealed class GitRepositorySnapshot
{
    public bool IsRepository { get; set; }
    public string RootPath { get; set; } = "";
    public string? CurrentBranch { get; set; }
    public string? RemoteName { get; set; }
    public string? RemoteUrl { get; set; }
    public string? Owner { get; set; }
    public string? Repo { get; set; }
    public string? StatusShort { get; set; }
    public bool HasUncommittedChanges { get; set; }
}

public static class GitRepositoryService
{
    public static GitRepositorySnapshot Inspect(string rootPath)
    {
        var snapshot = new GitRepositorySnapshot { RootPath = rootPath };
        if (string.IsNullOrWhiteSpace(rootPath) || !Directory.Exists(rootPath))
        {
            return snapshot;
        }

        snapshot.IsRepository = Directory.Exists(Path.Combine(rootPath, ".git")) ||
                                File.Exists(Path.Combine(rootPath, ".git"));
        if (!snapshot.IsRepository)
        {
            return snapshot;
        }

        if (GitRuntimeService.Run(rootPath, "rev-parse --abbrev-ref HEAD", 15000, out var output))
        {
            snapshot.CurrentBranch = FirstLine(output);
        }

        if (GitRuntimeService.Run(rootPath, "remote", 15000, out output))
        {
            snapshot.RemoteName = SplitLines(output).FirstOrDefault() ?? "origin";
        }

        if (!string.IsNullOrWhiteSpace(snapshot.RemoteName) &&
            GitRuntimeService.Run(rootPath, "remote get-url " + snapshot.RemoteName, 15000, out output))
        {
            snapshot.RemoteUrl = FirstLine(output);
            ParseOwnerRepo(snapshot.RemoteUrl, out var owner, out var repo);
            snapshot.Owner = owner;
            snapshot.Repo = repo;
        }

        if (GitRuntimeService.Run(rootPath, "status -sb", 20000, out output))
        {
            snapshot.StatusShort = output;
            snapshot.HasUncommittedChanges = SplitLines(output).Skip(1).Any(l => !string.IsNullOrWhiteSpace(l));
        }

        return snapshot;
    }

    public static string InitRepository(string rootPath)
    {
        if (!GitRuntimeService.Run(rootPath, "init", 30000, out var output))
        {
            return "git init failed:\n" + output;
        }

        return "Initialized git repository in " + rootPath + ".\n" + output;
    }

    public static string Fetch(string rootPath, string? remote)
    {
        if (!GitRuntimeService.Run(rootPath, "fetch --all --prune", 120000, out var output))
        {
            return "git fetch failed:\n" + output;
        }

        return "Fetched remotes.\n" + output;
    }

    public static string Pull(string rootPath, string? remote, string? branch)
    {
        remote = string.IsNullOrWhiteSpace(remote) ? "origin" : remote.Trim();
        var args = string.IsNullOrWhiteSpace(branch)
            ? "pull --ff-only " + remote
            : "pull --ff-only " + remote + " " + branch.Trim();
        if (!GitRuntimeService.Run(rootPath, args, 180000, out var output))
        {
            return "git pull failed:\n" + output;
        }

        return "Pulled latest changes.\n" + output;
    }

    public static string Push(string rootPath, string? remote, string? branch, bool setUpstream)
    {
        remote = string.IsNullOrWhiteSpace(remote) ? "origin" : remote.Trim();
        branch = string.IsNullOrWhiteSpace(branch) ? "" : branch.Trim();
        var args = "push";
        if (setUpstream && !string.IsNullOrWhiteSpace(branch))
        {
            args += " -u " + remote + " " + branch;
        }
        else
        {
            args += " " + remote;
            if (!string.IsNullOrWhiteSpace(branch))
            {
                args += " " + branch;
            }
        }

        if (!GitRuntimeService.Run(rootPath, args, 180000, out var output))
        {
            return "git push failed:\n" + output +
                   "\nHint: for GitHub HTTPS, set ROOTCLI_GITHUB_TOKEN / GITHUB_TOKEN or use gh auth login.";
        }

        return "Pushed to remote.\n" + output;
    }

    public static string Sync(string rootPath, bool push)
    {
        var snapshot = Inspect(rootPath);
        if (!snapshot.IsRepository)
        {
            return "Not a git repository: " + rootPath;
        }

        var builder = new StringBuilder();
        builder.AppendLine(Fetch(rootPath, snapshot.RemoteName));
        builder.AppendLine(Pull(rootPath, snapshot.RemoteName, snapshot.CurrentBranch));
        if (push)
        {
            builder.AppendLine(Push(rootPath, snapshot.RemoteName, snapshot.CurrentBranch, false));
        }

        builder.AppendLine(FormatSnapshot(Inspect(rootPath)));
        return builder.ToString().Trim();
    }

    public static string Commit(string rootPath, string message, bool all)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "Commit message is required.";
        }

        if (all && !GitRuntimeService.Run(rootPath, "add -A", 60000, out var addOut))
        {
            return "git add failed:\n" + addOut;
        }

        var escaped = message.Replace("\"", "\\\"");
        if (!GitRuntimeService.Run(rootPath, "commit -m \"" + escaped + "\"", 120000, out var output))
        {
            return "git commit failed:\n" + output;
        }

        return "Committed changes.\n" + output;
    }

    public static string Checkout(string rootPath, string branch, bool create)
    {
        if (string.IsNullOrWhiteSpace(branch))
        {
            return "Branch name is required.";
        }

        var args = create
            ? "checkout -b \"" + branch.Trim() + "\""
            : "checkout \"" + branch.Trim() + "\"";
        if (!GitRuntimeService.Run(rootPath, args, 60000, out var output))
        {
            return "git checkout failed:\n" + output;
        }

        return "Checked out branch " + branch + ".\n" + output;
    }

    public static string ListLocalBranches(string rootPath)
    {
        if (!GitRuntimeService.Run(rootPath, "branch --list", 20000, out var output))
        {
            return "Could not list branches:\n" + output;
        }

        return output;
    }

    public static string Log(string rootPath, int count)
    {
        count = Math.Clamp(count <= 0 ? 12 : count, 1, 50);
        if (!GitRuntimeService.Run(rootPath, "log -n " + count + " --oneline --decorate", 30000, out var output))
        {
            return "Could not read commit log:\n" + output;
        }

        return output;
    }

    public static string FormatSnapshot(GitRepositorySnapshot snapshot)
    {
        if (snapshot is not { IsRepository: true })
        {
            return "Not a git repository.";
        }

        var builder = new StringBuilder();
        builder.AppendLine("Repository: " + snapshot.RootPath);
        builder.AppendLine("Branch: " + (snapshot.CurrentBranch ?? "(detached)"));
        if (!string.IsNullOrWhiteSpace(snapshot.RemoteUrl))
        {
            builder.AppendLine("Remote: " + snapshot.RemoteName + " → " + snapshot.RemoteUrl);
        }

        builder.AppendLine(snapshot.HasUncommittedChanges
            ? "Working tree:\n" + (snapshot.StatusShort ?? "")
            : "Working tree: clean");
        return builder.ToString().Trim();
    }

    public static void ParseOwnerRepo(string? remoteUrl, out string? owner, out string? repo)
    {
        owner = null;
        repo = null;
        if (string.IsNullOrWhiteSpace(remoteUrl))
        {
            return;
        }

        var url = remoteUrl.Trim();
        if (url.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
        {
            url = url[..^4];
        }

        var sshMatch = Regex.Match(url, @"[:/]+([^/]+)/([^/]+)$");
        if (sshMatch.Success)
        {
            owner = sshMatch.Groups[1].Value;
            repo = sshMatch.Groups[2].Value;
        }
    }

    private static string FirstLine(string? value) =>
        SplitLines(value).FirstOrDefault() ?? "";

    private static List<string> SplitLines(string? value) =>
        (value ?? "")
            .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(s => s.Trim())
            .Where(s => s.Length > 0)
            .ToList();
}
