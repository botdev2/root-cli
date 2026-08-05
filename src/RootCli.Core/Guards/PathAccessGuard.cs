
using System;
using System.Collections.Generic;
using System.IO;

namespace RootCli.Core.Guards;

public static class PathAccessGuard
{
    private static readonly object Sync = new();
    private static List<string>? _extraBlocked;

    public static bool TryNormalizeAbsolute(string path, out string normalized, out string error)
    {
        normalized = "";
        error = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            error = "Path is empty.";
            return false;
        }

        try
        {
            normalized = Path.GetFullPath(path.Trim())
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            error = "That path is not valid.";
            return false;
        }

        if (normalized.Length == 3 && char.IsLetter(normalized[0]) && normalized[1] == ':' &&
            normalized[2] == Path.DirectorySeparatorChar)
        {
            error = "Cannot block an entire drive. Pick a folder inside it.";
            return false;
        }

        return true;
    }

    public static bool IsDeniedPath(string path, out string reason)
    {
        reason = "";
        if (!TryNormalizeAbsolute(path, out var normalized, out _))
        {
            return false;
        }

        foreach (var blocked in LoadBlocked())
        {
            if (string.IsNullOrWhiteSpace(blocked))
            {
                continue;
            }

            if (IsSameOrParent(blocked, normalized))
            {
                reason = "Path is blocked: " + blocked;
                return true;
            }
        }

        return false;
    }

    public static void EnsureAllowed(string path)
    {
        if (IsDeniedPath(path, out var reason))
        {
            throw new InvalidOperationException(reason);
        }
    }

    private static IEnumerable<string> LoadBlocked()
    {
        lock (Sync)
        {
            if (_extraBlocked != null)
            {
                return _extraBlocked;
            }

            _extraBlocked = new List<string>();
            try
            {
                var file = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "root-cli",
                    "blocked-paths.txt");
                if (File.Exists(file))
                {
                    foreach (var line in File.ReadAllLines(file))
                    {
                        var trimmed = line.Trim();
                        if (trimmed.Length == 0 || trimmed.StartsWith('#'))
                        {
                            continue;
                        }

                        if (TryNormalizeAbsolute(trimmed, out var norm, out _))
                        {
                            _extraBlocked.Add(norm);
                        }
                    }
                }
            }
            catch
            {
            }

            return _extraBlocked;
        }
    }

    private static bool IsSameOrParent(string parent, string child)
    {
        var p = parent.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var c = child.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        if (string.Equals(p, c, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var prefix = p + Path.DirectorySeparatorChar;
        return c.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
    }
}
