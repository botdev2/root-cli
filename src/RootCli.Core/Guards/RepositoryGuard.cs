
using System;
using System.IO;

namespace RootCli.Core.Guards;

public static class RepositoryGuard
{
    public static bool IsBlockedRoot(string path, out string reason)
    {
        reason = "";
        if (string.IsNullOrWhiteSpace(path))
        {
            reason = "Choose a project folder, not an empty path.";
            return true;
        }

        string full;
        try
        {
            full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
        catch
        {
            reason = "That folder path is not valid.";
            return true;
        }

        if (OperatingSystem.IsWindows()
            && full.Length == 3
            && char.IsLetter(full[0])
            && full[1] == ':'
            && (full[2] == '\\' || full[2] == '/'))
        {
            reason = "Cannot use an entire drive (such as C:\\) as a repository. Pick a project folder inside it instead.";
            return true;
        }

        if (!OperatingSystem.IsWindows()
            && (full == "/" || full == Path.DirectorySeparatorChar.ToString()))
        {
            reason = "Cannot use the filesystem root (/) as a repository. Pick a project folder instead.";
            return true;
        }

        if (IsKnownSystemRoot(full))
        {
            reason = "That folder is too broad for RootCli to use safely. Pick a specific project directory.";
            return true;
        }

        if (PathAccessGuard.IsDeniedPath(full, out reason))
        {
            return true;
        }

        return false;
    }

    public static bool IsUsableRepository(string path)
    {
        return !string.IsNullOrWhiteSpace(path) &&
               Directory.Exists(path) &&
               !IsBlockedRoot(path, out _);
    }

    private static bool IsKnownSystemRoot(string full)
    {
        if (OperatingSystem.IsWindows())
        {
            var candidates = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.System),
                Environment.GetFolderPath(Environment.SpecialFolder.SystemX86)
            };

            foreach (var candidateRaw in candidates)
            {
                if (string.IsNullOrWhiteSpace(candidateRaw))
                {
                    continue;
                }

                var candidate = Path.GetFullPath(candidateRaw)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                if (string.Equals(full, candidate, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        var unixRoots = new[]
        {
            "/bin", "/boot", "/dev", "/etc", "/lib", "/lib64", "/proc", "/run",
            "/sbin", "/sys", "/usr", "/usr/bin", "/usr/lib", "/usr/local",
            "/var", "/var/lib", "/root"
        };

        foreach (var root in unixRoots)
        {
            if (string.Equals(full, root, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
