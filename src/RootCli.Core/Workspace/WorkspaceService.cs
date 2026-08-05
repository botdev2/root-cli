
using System.Text;
using RootCli.Core.Guards;

namespace RootCli.Core.Workspace;

public sealed class WorkspaceService
{
    public string RootPath { get; private set; } = "";

    public bool HasWorkspace => RepositoryGuard.IsUsableRepository(RootPath);

    public string Name
    {
        get
        {
            if (!HasWorkspace)
            {
                return "(no repository)";
            }

            var name = Path.GetFileName(RootPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return string.IsNullOrWhiteSpace(name) ? RootPath : name;
        }
    }

    public bool TrySetRoot(string path, bool create, out string error)
    {
        error = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Choose a project folder.";
            return false;
        }

        if (RepositoryGuard.IsBlockedRoot(path, out error))
        {
            return false;
        }

        if (create)
        {
            Directory.CreateDirectory(path);
        }

        if (!Directory.Exists(path))
        {
            error = "Folder not found: " + path;
            return false;
        }

        RootPath = Path.GetFullPath(path);
        return true;
    }

    public void SetRoot(string path, bool create = false)
    {
        if (!TrySetRoot(path, create, out var error))
        {
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(error) ? "Repository path was rejected." : error);
        }
    }

    public List<WorkspaceEntry> ListEntries(string relativePath)
    {
        if (!HasWorkspace)
        {
            return new List<WorkspaceEntry>();
        }

        var folder = ResolveInsidePath(relativePath);
        var result = new List<WorkspaceEntry>();
        if (!Directory.Exists(folder))
        {
            return result;
        }

        try
        {
            foreach (var entry in Directory.EnumerateFileSystemEntries(folder))
            {
                try
                {
                    if (Directory.Exists(entry))
                    {
                        var info = new DirectoryInfo(entry);
                        if (ShouldIgnoreDirectory(info.Name) || IsReparsePoint(info))
                        {
                            continue;
                        }

                        result.Add(new WorkspaceEntry
                        {
                            Name = info.Name,
                            RelativePath = ToRelative(entry),
                            IsDirectory = true
                        });
                        continue;
                    }

                    if (!File.Exists(entry))
                    {
                        continue;
                    }

                    var file = new FileInfo(entry);
                    result.Add(new WorkspaceEntry
                    {
                        Name = file.Name,
                        RelativePath = ToRelative(entry),
                        IsDirectory = false,
                        Size = file.Length
                    });
                }
                catch
                {
                }
            }
        }
        catch
        {
            return result;
        }

        return result
            .OrderByDescending(x => x.IsDirectory)
            .ThenBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Take(160)
            .ToList();
    }

    public List<WorkspaceEntry> Search(string query)
    {
        var result = new List<WorkspaceEntry>();
        if (!HasWorkspace || string.IsNullOrWhiteSpace(query))
        {
            return result;
        }

        var terms = query.ToLowerInvariant().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        foreach (var file in SafeEnumerateFiles(RootPath).Take(5000))
        {
            var relative = ToRelative(file);
            var lower = relative.ToLowerInvariant();
            var matched = terms.All(t => lower.Contains(t));
            if (!matched && IsLikelyText(relative))
            {
                try
                {
                    var info = new FileInfo(file);
                    if (info.Length < 512 * 1024)
                    {
                        var content = File.ReadAllText(file).ToLowerInvariant();
                        matched = terms.All(t => content.Contains(t));
                    }
                }
                catch
                {
                }
            }

            if (matched)
            {
                var info = new FileInfo(file);
                result.Add(new WorkspaceEntry
                {
                    Name = info.Name,
                    RelativePath = relative,
                    IsDirectory = false,
                    Size = info.Length
                });
            }

            if (result.Count >= 80)
            {
                break;
            }
        }

        return result;
    }

    public string ReadText(string relativePath)
    {
        if (!HasWorkspace)
        {
            throw new InvalidOperationException("No repository selected.");
        }

        var path = ResolveInsidePath(relativePath);
        var info = new FileInfo(path);
        if (info.Length > 1024 * 1024)
        {
            return "File is larger than 1 MB. Narrow the request or open it externally.";
        }

        return File.ReadAllText(path);
    }

    public void WriteText(string relativePath, string content)
    {
        if (!HasWorkspace)
        {
            throw new InvalidOperationException("No repository selected.");
        }

        var path = ResolveInsidePath(relativePath);
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(path, content ?? "", new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    public bool TryDeleteFile(string relativePath, out string error)
    {
        error = "";
        if (!HasWorkspace)
        {
            error = "No repository selected.";
            return false;
        }

        try
        {
            var full = ResolveInsidePath(relativePath);
            if (Directory.Exists(full))
            {
                error = "Path is a directory. delete_file only removes files.";
                return false;
            }

            if (!File.Exists(full))
            {
                error = "File not found: " + relativePath;
                return false;
            }

            File.Delete(full);
            return true;
        }
        catch (Exception ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public bool TryApplyPatch(string relativePath, string find, string replace, out string error)
    {
        error = "";
        if (!HasWorkspace)
        {
            error = "No repository selected.";
            return false;
        }

        if (string.IsNullOrEmpty(find))
        {
            WriteText(relativePath, replace ?? "");
            return true;
        }

        var path = ResolveInsidePath(relativePath);
        if (!File.Exists(path))
        {
            error = "File not found: " + relativePath + ". Use write_file to create new files.";
            return false;
        }

        var current = File.ReadAllText(path);
        var index = current.IndexOf(find, StringComparison.Ordinal);
        if (index < 0)
        {
            error = "Could not locate patch target text in " + relativePath;
            return false;
        }

        var updated = current.Substring(0, index) + (replace ?? "") + current.Substring(index + find.Length);
        WriteText(relativePath, updated);
        return true;
    }

    public string GetTreeSummary(int max)
    {
        var builder = new StringBuilder();
        if (!HasWorkspace)
        {
            builder.AppendLine("- no repository selected");
            return builder.ToString();
        }

        var entries = ListEntries(".");
        for (var i = 0; i < entries.Count && i < max; i++)
        {
            var entry = entries[i];
            builder.AppendLine("- " + (entry.IsDirectory ? "dir " : "file ") + entry.RelativePath);
        }

        if (builder.Length == 0)
        {
            builder.AppendLine("- empty repository");
        }

        return builder.ToString();
    }

    public string ResolveInsidePath(string relativePath)
    {
        if (!HasWorkspace)
        {
            throw new InvalidOperationException("No repository selected.");
        }

        var value = string.IsNullOrWhiteSpace(relativePath) ? "." : relativePath;
        var combined = Path.IsPathRooted(value) ? value : Path.Combine(RootPath, value);
        var full = Path.GetFullPath(combined);
        var root = Path.GetFullPath(RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var rootWithSep = root + Path.DirectorySeparatorChar;
        if (!full.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), root, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Path is outside the selected repository.");
        }

        PathAccessGuard.EnsureAllowed(full);
        return full;
    }

    private string ToRelative(string path)
    {
        var root = Path.GetFullPath(RootPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(path);
        if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase))
        {
            return full.Substring(root.Length);
        }

        return full;
    }

    private static bool IsLikelyText(string filePath)
    {
        var extension = Path.GetExtension(filePath).ToLowerInvariant();
        var textExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".bat", ".cmd", ".css", ".csv", ".env", ".html", ".js", ".json", ".jsx",
            ".log", ".md", ".mjs", ".ps1", ".py", ".rs", ".sh", ".toml", ".ts",
            ".tsx", ".txt", ".xml", ".yaml", ".yml", ".cs", ".xaml"
        };
        return textExtensions.Contains(extension) || string.IsNullOrWhiteSpace(extension);
    }

    private static bool ShouldIgnoreDirectory(string name) =>
        string.Equals(name, "node_modules", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, ".git", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "dist", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "bin", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(name, "obj", StringComparison.OrdinalIgnoreCase);

    private static bool IsReparsePoint(FileSystemInfo info)
    {
        try
        {
            return (info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }
        catch
        {
            return true;
        }
    }

    private static IEnumerable<string> SafeEnumerateFiles(string root)
    {
        var pending = new Stack<(string Path, int Depth)>();
        pending.Push((root, 0));
        while (pending.Count > 0)
        {
            var (path, depth) = pending.Pop();
            if (depth > 10)
            {
                continue;
            }

            string[] files = Array.Empty<string>();
            string[] dirs = Array.Empty<string>();
            try
            {
                files = Directory.GetFiles(path);
                dirs = Directory.GetDirectories(path);
            }
            catch
            {
            }

            foreach (var file in files)
            {
                yield return file;
            }

            foreach (var directoryPath in dirs)
            {
                var name = Path.GetFileName(directoryPath);
                if (ShouldIgnoreDirectory(name))
                {
                    continue;
                }

                var info = new DirectoryInfo(directoryPath);
                if (IsReparsePoint(info))
                {
                    continue;
                }

                pending.Push((directoryPath, depth + 1));
            }
        }
    }
}
