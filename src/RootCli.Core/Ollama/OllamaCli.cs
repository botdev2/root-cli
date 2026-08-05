using System.Diagnostics;

namespace RootCli.Core.Ollama;

public static class OllamaCli
{
    public static string? FindExecutable()
    {
        if (OperatingSystem.IsWindows())
        {
            return FindOnPath("ollama.exe") ?? FindOnPath("ollama");
        }

        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        foreach (var candidate in new[]
                 {
                     FindOnPath("ollama"),
                     "/usr/local/bin/ollama",
                     "/opt/homebrew/bin/ollama",
                     Path.Combine(home, ".local", "bin", "ollama"),
                     Path.Combine(home, "bin", "ollama")
                 })
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    public static int SignIn() => Run("signin");

    public static int SignOut() => Run("signout");

    public static int Run(string arguments)
    {
        var exe = FindExecutable();
        if (string.IsNullOrWhiteSpace(exe))
        {
            Console.Error.WriteLine("error: ollama not found on PATH. Install from https://ollama.com then retry.");
            return 1;
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = arguments ?? "",
                UseShellExecute = false
            };
            using var process = Process.Start(psi);
            if (process == null)
            {
                Console.Error.WriteLine("error: failed to start ollama.");
                return 1;
            }

            process.WaitForExit();
            return process.ExitCode;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 1;
        }
    }

    private static string? FindOnPath(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            return null;
        }

        if (File.Exists(fileName))
        {
            return Path.GetFullPath(fileName);
        }

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
