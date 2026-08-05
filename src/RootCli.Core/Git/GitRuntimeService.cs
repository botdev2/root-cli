
using System.Diagnostics;
using System.Text;

namespace RootCli.Core.Git;

public sealed class GitRunResult
{
    public bool Success { get; set; }
    public int ExitCode { get; set; }
    public string Output { get; set; } = "";
}

public static class GitRuntimeService
{
    public static string? FindGitExecutable()
    {

        if (!OperatingSystem.IsWindows())
        {
            return FindOnPath("git");
        }

        var candidates = new[]
        {
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs", "root", "third-party", "git", "cmd", "git.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "root", "third-party", "git", "cmd", "git.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "cmd", "git.exe"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "cmd", "git.exe")
        };

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return FindOnPath("git.exe") ?? FindOnPath("git");
    }

    public static bool Run(string workingDirectory, string arguments, int timeoutMs, out string output)
    {
        var ok = Run(FindGitExecutable(), workingDirectory, arguments, timeoutMs, out var result);
        output = result.Output;
        return ok;
    }

    public static bool Run(
        string? gitExecutable,
        string workingDirectory,
        string arguments,
        int timeoutMs,
        out GitRunResult result)
    {
        result = new GitRunResult();
        if (string.IsNullOrWhiteSpace(gitExecutable) || !File.Exists(gitExecutable))
        {
            result.Output = OperatingSystem.IsWindows()
                ? "git executable not found. Install Git for Windows or ensure git is on PATH."
                : "git executable not found. Install git (e.g. apt/dnf/pacman) or ensure it is on PATH.";
            return false;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = gitExecutable,
                Arguments = arguments ?? "",
                WorkingDirectory = string.IsNullOrWhiteSpace(workingDirectory)
                    ? Environment.CurrentDirectory
                    : workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                result.Output = "Failed to start git.";
                return false;
            }

            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(timeoutMs))
            {
                try { process.Kill(entireProcessTree: true); } catch {  }
                result.Output = "git " + arguments + " timed out.";
                return false;
            }

            var stdout = stdoutTask.GetAwaiter().GetResult();
            var stderr = stderrTask.GetAwaiter().GetResult();
            result.ExitCode = process.ExitCode;
            result.Output = (stdout + "\n" + stderr).Trim();
            result.Success = process.ExitCode == 0;
            return result.Success;
        }
        catch (Exception ex)
        {
            result.Output = ex.Message;
            return false;
        }
    }

    public static string FormatStatus()
    {
        var path = FindGitExecutable();
        if (string.IsNullOrWhiteSpace(path))
        {
            return "Git: not installed (not on PATH).";
        }

        Run(path, Environment.CurrentDirectory, "version", 10000, out var version);
        return "Git: " + path + "\n" + (version.Output.Split('\n').FirstOrDefault() ?? "");
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
}
