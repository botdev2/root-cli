
using System.Text;
using System.Text.Json;

namespace RootCli.Core.Mcp;

public static class McpServerStore
{
    private static readonly object Sync = new();
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string ConfigPath =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "root-cli",
            "mcp-servers.json");

    public static McpServersDocument Load()
    {
        lock (Sync)
        {
            EnsureDefaultConfig();
            try
            {
                var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                var document = JsonSerializer.Deserialize<McpServersDocument>(json, JsonOptions);
                if (document?.Servers == null)
                {
                    return BuildDefaultDocument();
                }

                if (MergeBundledServers(document))
                {
                    Save(document);
                }

                return document;
            }
            catch
            {
                return BuildDefaultDocument();
            }
        }
    }

    public static void Save(McpServersDocument document)
    {
        lock (Sync)
        {
            var dir = Path.GetDirectoryName(ConfigPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(
                ConfigPath,
                JsonSerializer.Serialize(document ?? BuildDefaultDocument(), JsonOptions),
                Encoding.UTF8);
        }
    }

    public static void EnsureDefaultConfig()
    {
        if (!File.Exists(ConfigPath))
        {
            Save(BuildDefaultDocument());
        }
    }

    private static bool MergeBundledServers(McpServersDocument document)
    {
        document.Servers ??= new List<McpServerDefinition>();
        var changed = false;
        if (!document.Servers.Any(s => string.Equals(s.Id, "codebase-memory", StringComparison.OrdinalIgnoreCase)))
        {
            document.Servers.Insert(0, BuildCodebaseMemoryDefinition());
            changed = true;
        }

        return changed;
    }

    private static McpServersDocument BuildDefaultDocument()
    {
        var document = new McpServersDocument();
        document.Servers.Add(BuildCodebaseMemoryDefinition());

        document.Servers.Add(new McpServerDefinition
        {
            Id = "blender-math",
            DisplayName = "Blender Math GLM",
            Command = "python",
            Enabled = false
        });
        document.Servers.Add(new McpServerDefinition
        {
            Id = "unity-math",
            DisplayName = "Unity Math GLM",
            Command = "python",
            Enabled = false
        });
        return document;
    }

    private static McpServerDefinition BuildCodebaseMemoryDefinition() =>
        new()
        {
            Id = "codebase-memory",
            DisplayName = "Codebase Memory",
            Command = "",
            Enabled = true,
            AutoIndexOnWorkspaceOpen = true
        };
}
