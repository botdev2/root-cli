
namespace RootCli.Core.Mcp;

public static class McpPathResolver
{
    public static string RootToolsDir =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "root",
            "tools");

    public static string? ResolveExecutable(McpServerDefinition definition)
    {
        if (definition == null)
        {
            return null;
        }

        if (!string.IsNullOrWhiteSpace(definition.Command) && File.Exists(definition.Command.Trim()))
        {
            return Path.GetFullPath(definition.Command.Trim());
        }

        if (string.Equals(definition.Id, "codebase-memory", StringComparison.OrdinalIgnoreCase))
        {
            return ResolveCodebaseMemoryExecutable();
        }

        if (!string.IsNullOrWhiteSpace(definition.Command))
        {
            return FindOnPath(definition.Command.Trim());
        }

        return null;
    }

    public static string? ResolveCodebaseMemoryExecutable()
    {
        var env = Environment.GetEnvironmentVariable("ROOTCLI_MCP_CODEBASE_MEMORY");
        var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var candidates = new List<string?>
        {
            env,
            Path.Combine(RootToolsDir, "codebase-memory-mcp"),
            Path.Combine(RootToolsDir, "codebase-memory-mcp", "codebase-memory-mcp"),
            Path.Combine(home, ".local", "bin", "codebase-memory-mcp"),
            Path.Combine(home, "bin", "codebase-memory-mcp"),
            "/usr/local/bin/codebase-memory-mcp",
            "/usr/bin/codebase-memory-mcp",
            "/opt/homebrew/bin/codebase-memory-mcp"
        };

        if (OperatingSystem.IsWindows())
        {
            candidates.Add(Path.Combine(RootToolsDir, "codebase-memory-mcp.exe"));
            candidates.Add(Path.Combine(RootToolsDir, "codebase-memory-mcp", "codebase-memory-mcp.exe"));
            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Programs",
                "codebase-memory-mcp",
                "codebase-memory-mcp.exe"));
            candidates.Add(Path.Combine(home, "scoop", "shims", "codebase-memory-mcp.exe"));
        }

        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        return FindOnPath("codebase-memory-mcp")
               ?? (OperatingSystem.IsWindows() ? FindOnPath("codebase-memory-mcp.exe") : null);
    }

    public static string? FindOnPath(string fileName)
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

                if (OperatingSystem.IsWindows()
                    && !fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
                {
                    var withExe = full + ".exe";
                    if (File.Exists(withExe))
                    {
                        return withExe;
                    }
                }
            }
            catch
            {

            }
        }

        return null;
    }
}
