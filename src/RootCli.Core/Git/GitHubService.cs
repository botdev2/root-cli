
using System.Diagnostics;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using RootCli.Core.Workspace;

namespace RootCli.Core.Git;

public static class GitHubService
{
    private static readonly HttpClient Http = CreateClient();

    public static string TokenPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "root-cli",
            "github-token.txt");

    public static string? GetAccessToken()
    {
        var env = Environment.GetEnvironmentVariable("ROOTCLI_GITHUB_TOKEN")
                  ?? Environment.GetEnvironmentVariable("GITHUB_TOKEN")
                  ?? Environment.GetEnvironmentVariable("GH_TOKEN");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        try
        {
            if (File.Exists(TokenPath))
            {
                var text = File.ReadAllText(TokenPath).Trim();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
            }
        }
        catch
        {

        }

        try
        {
            var gh = FindOnPath("gh.exe") ?? FindOnPath("gh");
            if (!string.IsNullOrWhiteSpace(gh))
            {
                var psi = new ProcessStartInfo
                {
                    FileName = gh,
                    Arguments = "auth token",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    var token = p.StandardOutput.ReadToEnd().Trim();
                    p.WaitForExit(10000);
                    if (p.ExitCode == 0 && !string.IsNullOrWhiteSpace(token))
                    {
                        return token;
                    }
                }
            }
        }
        catch
        {

        }

        return null;
    }

    public static string SavePersonalAccessToken(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
        {
            return "Token is empty.";
        }

        var dir = Path.GetDirectoryName(TokenPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(TokenPath, token.Trim(), Encoding.UTF8);
        return "Saved GitHub PAT to " + TokenPath;
    }

    public static string Logout()
    {
        try
        {
            if (File.Exists(TokenPath))
            {
                File.Delete(TokenPath);
            }
        }
        catch (Exception ex)
        {
            return "Logout failed: " + ex.Message;
        }

        return "Removed saved RootCli GitHub token (env tokens unchanged).";
    }

    public static string Status(WorkspaceService? workspace)
    {
        var builder = new StringBuilder();
        builder.AppendLine(GitRuntimeService.FormatStatus());
        var token = GetAccessToken();
        builder.AppendLine(string.IsNullOrWhiteSpace(token)
            ? "GitHub auth: not signed in (set ROOTCLI_GITHUB_TOKEN / GITHUB_TOKEN, or github_login_pat)."
            : "GitHub auth: token available (" + Mask(token) + ")");
        if (workspace is { HasWorkspace: true })
        {
            builder.AppendLine(GitRepositoryService.FormatSnapshot(GitRepositoryService.Inspect(workspace.RootPath)));
        }

        return builder.ToString().Trim();
    }

    public static string CreatePullRequest(
        string? owner,
        string? repo,
        string? title,
        string? head,
        string? @base,
        string? body,
        WorkspaceService workspace)
    {
        var token = GetAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return "create_pull_request failed: not signed in. Use github_login_pat or set ROOTCLI_GITHUB_TOKEN.";
        }

        var snapshot = GitRepositoryService.Inspect(workspace.RootPath);
        owner = FirstNonEmpty(owner, snapshot.Owner);
        repo = FirstNonEmpty(repo, snapshot.Repo);
        head = FirstNonEmpty(head, snapshot.CurrentBranch);
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            return "create_pull_request failed: owner/repo required (GitHub remote or args).";
        }

        if (string.IsNullOrWhiteSpace(head))
        {
            return "create_pull_request failed: head branch required.";
        }

        title = string.IsNullOrWhiteSpace(title) ? "Update from RootCli" : title.Trim();
        @base = string.IsNullOrWhiteSpace(@base) ? "main" : @base.Trim();

        var payload = new Dictionary<string, object?>
        {
            ["title"] = title,
            ["head"] = head,
            ["base"] = @base
        };
        if (!string.IsNullOrWhiteSpace(body))
        {
            payload["body"] = body;
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, "/repos/" + owner + "/" + repo + "/pulls");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            using var resp = Http.Send(req);
            var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                return "Pull request creation failed (" + (int)resp.StatusCode + "):\n" + Truncate(text, 2000);
            }

            using var doc = JsonDocument.Parse(text);
            var number = doc.RootElement.TryGetProperty("number", out var n) ? n.ToString() : "?";
            var url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : "";
            return "Created pull request #" + number + ": " + url;
        }
        catch (Exception ex)
        {
            return "Pull request creation failed: " + ex.Message;
        }
    }

    public static string SearchRepositories(string query, int limit)
    {
        var token = GetAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return "github_search_repositories requires auth (ROOTCLI_GITHUB_TOKEN).";
        }

        limit = Math.Clamp(limit <= 0 ? 10 : limit, 1, 30);
        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Get,
                "/search/repositories?q=" + Uri.EscapeDataString(query) + "&per_page=" + limit);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = Http.Send(req);
            var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                return "Search failed (" + (int)resp.StatusCode + "):\n" + Truncate(text, 1500);
            }

            using var doc = JsonDocument.Parse(text);
            if (!doc.RootElement.TryGetProperty("items", out var items))
            {
                return "No results.";
            }

            var sb = new StringBuilder();
            sb.AppendLine("Repositories:");
            foreach (var item in items.EnumerateArray())
            {
                var full = item.TryGetProperty("full_name", out var fn) ? fn.GetString() : "?";
                var desc = item.TryGetProperty("description", out var d) ? d.GetString() : "";
                var url = item.TryGetProperty("html_url", out var u) ? u.GetString() : "";
                sb.AppendLine("- " + full + (string.IsNullOrWhiteSpace(desc) ? "" : " — " + desc));
                if (!string.IsNullOrWhiteSpace(url))
                {
                    sb.AppendLine("  " + url);
                }
            }

            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            return "Search failed: " + ex.Message;
        }
    }

    public static string ListBranches(string? owner, string? repo, int limit, WorkspaceService workspace)
    {
        var token = GetAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return "github_list_branches requires auth.";
        }

        var snapshot = GitRepositoryService.Inspect(workspace.RootPath);
        owner = FirstNonEmpty(owner, snapshot.Owner);
        repo = FirstNonEmpty(repo, snapshot.Repo);
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            return "owner/repo required.";
        }

        limit = Math.Clamp(limit <= 0 ? 30 : limit, 1, 100);
        try
        {
            using var req = new HttpRequestMessage(
                HttpMethod.Get,
                "/repos/" + owner + "/" + repo + "/branches?per_page=" + limit);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = Http.Send(req);
            var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                return "List branches failed (" + (int)resp.StatusCode + "):\n" + Truncate(text, 1500);
            }

            using var doc = JsonDocument.Parse(text);
            var sb = new StringBuilder();
            sb.AppendLine("Remote branches (" + owner + "/" + repo + "):");
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var name = item.TryGetProperty("name", out var n) ? n.GetString() : "?";
                sb.AppendLine("- " + name);
            }

            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            return "List branches failed: " + ex.Message;
        }
    }

    public static string ListCommits(string? owner, string? repo, string? branch, int limit, WorkspaceService workspace)
    {
        var token = GetAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return "github_list_commits requires auth.";
        }

        var snapshot = GitRepositoryService.Inspect(workspace.RootPath);
        owner = FirstNonEmpty(owner, snapshot.Owner);
        repo = FirstNonEmpty(repo, snapshot.Repo);
        branch = FirstNonEmpty(branch, snapshot.CurrentBranch);
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            return "owner/repo required.";
        }

        limit = Math.Clamp(limit <= 0 ? 12 : limit, 1, 50);
        try
        {
            var url = "/repos/" + owner + "/" + repo + "/commits?per_page=" + limit;
            if (!string.IsNullOrWhiteSpace(branch))
            {
                url += "&sha=" + Uri.EscapeDataString(branch);
            }

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = Http.Send(req);
            var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                return "List commits failed (" + (int)resp.StatusCode + "):\n" + Truncate(text, 1500);
            }

            using var doc = JsonDocument.Parse(text);
            var sb = new StringBuilder();
            sb.AppendLine("Recent commits:");
            foreach (var item in doc.RootElement.EnumerateArray())
            {
                var sha = item.TryGetProperty("sha", out var s) ? s.GetString() ?? "" : "";
                var msg = "";
                if (item.TryGetProperty("commit", out var c) &&
                    c.TryGetProperty("message", out var m))
                {
                    msg = (m.GetString() ?? "").Split('\n')[0];
                }

                sb.AppendLine("- " + (sha.Length > 7 ? sha[..7] : sha) + " " + msg);
            }

            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            return "List commits failed: " + ex.Message;
        }
    }

    public static string GetRepository(string? owner, string? repo, WorkspaceService workspace)
    {
        var token = GetAccessToken();
        if (string.IsNullOrWhiteSpace(token))
        {
            return "github_get_repository requires auth.";
        }

        var snapshot = GitRepositoryService.Inspect(workspace.RootPath);
        owner = FirstNonEmpty(owner, snapshot.Owner);
        repo = FirstNonEmpty(repo, snapshot.Repo);
        if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
        {
            return "owner/repo required.";
        }

        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, "/repos/" + owner + "/" + repo);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var resp = Http.Send(req);
            var text = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            if (!resp.IsSuccessStatusCode)
            {
                return "Get repository failed (" + (int)resp.StatusCode + "):\n" + Truncate(text, 1500);
            }

            using var doc = JsonDocument.Parse(text);
            var root = doc.RootElement;
            var sb = new StringBuilder();
            sb.AppendLine("Repository: " + GetStr(root, "full_name"));
            sb.AppendLine("URL: " + GetStr(root, "html_url"));
            sb.AppendLine("Default branch: " + GetStr(root, "default_branch"));
            sb.AppendLine("Private: " + GetStr(root, "private"));
            sb.AppendLine("Description: " + GetStr(root, "description"));
            sb.AppendLine("Stars: " + GetStr(root, "stargazers_count") + "  Forks: " + GetStr(root, "forks_count"));
            return sb.ToString().Trim();
        }
        catch (Exception ex)
        {
            return "Get repository failed: " + ex.Message;
        }
    }

    public static string CreateRepoViaGh(string? name, string? visibility, string? owner, WorkspaceService workspace)
    {
        var gh = FindOnPath("gh.exe") ?? FindOnPath("gh");
        if (string.IsNullOrWhiteSpace(gh))
        {
            return "github_create_repo requires GitHub CLI (gh) on PATH.";
        }

        name = string.IsNullOrWhiteSpace(name) ? workspace.Name : name.Trim();
        visibility = string.Equals(visibility, "public", StringComparison.OrdinalIgnoreCase) ? "public" : "private";
        var args = "repo create " + Quote(name) + " --source=. --remote=origin --" + visibility + " --push";
        if (!string.IsNullOrWhiteSpace(owner))
        {
            args = "repo create " + Quote(owner.Trim() + "/" + name) + " --source=. --remote=origin --" + visibility + " --push";
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = gh,
                Arguments = args,
                WorkingDirectory = workspace.RootPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            using var p = Process.Start(psi);
            if (p == null)
            {
                return "Failed to start gh.";
            }

            var stdout = p.StandardOutput.ReadToEnd();
            var stderr = p.StandardError.ReadToEnd();
            p.WaitForExit(180000);
            return (p.ExitCode == 0 ? "Created repo.\n" : "github_create_repo failed.\n") +
                   (stdout + "\n" + stderr).Trim();
        }
        catch (Exception ex)
        {
            return "github_create_repo failed: " + ex.Message;
        }
    }

    public static string AuthCli()
    {
        var token = GetAccessToken();
        var gh = FindOnPath("gh.exe") ?? FindOnPath("gh");
        if (string.IsNullOrWhiteSpace(gh))
        {
            return "gh not found on PATH. Install GitHub CLI, or use github_login_pat.";
        }

        if (!string.IsNullOrWhiteSpace(token))
        {
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = gh,
                    Arguments = "auth login --with-token",
                    UseShellExecute = false,
                    RedirectStandardInput = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var p = Process.Start(psi);
                if (p != null)
                {
                    p.StandardInput.Write(token);
                    p.StandardInput.Close();
                    var stdout = p.StandardOutput.ReadToEnd();
                    var stderr = p.StandardError.ReadToEnd();
                    p.WaitForExit(60000);
                    if (p.ExitCode == 0)
                    {
                        return "Authenticated gh with saved/env token.\n" + (stdout + stderr).Trim();
                    }
                }
            }
            catch
            {

            }
        }

        return "Run interactively: gh auth login --web\nOr save a PAT with github_login_pat / ROOTCLI_GITHUB_TOKEN.";
    }

    private static HttpClient CreateClient()
    {
        var client = new HttpClient
        {
            BaseAddress = new Uri("https://api.github.com/")
        };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("RootCli/0.1");
        client.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return client;
    }

    private static string? FindOnPath(string fileName)
    {
        var path = Environment.GetEnvironmentVariable("PATH") ?? "";
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var full = Path.Combine(dir.Trim('"'), fileName);
                if (File.Exists(full))
                {
                    return full;
                }
            }
            catch
            {

            }
        }

        return null;
    }

    private static string Mask(string token) =>
        token.Length <= 8 ? "********" : token[..4] + "…" + token[^4..];

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v))?.Trim();

    private static string GetStr(JsonElement el, string name) =>
        el.TryGetProperty(name, out var p)
            ? p.ValueKind == JsonValueKind.True ? "true"
            : p.ValueKind == JsonValueKind.False ? "false"
            : p.ToString()
            : "";

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\\\"") + "\"";
}
