namespace RootCli.Core.Mcp;

public sealed class McpServerDefinition
{
    public string Id { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Command { get; set; } = "";
    public List<string> Args { get; set; } = new();
    public string WorkingDirectory { get; set; } = "";
    public bool Enabled { get; set; }
    public bool AutoIndexOnWorkspaceOpen { get; set; }
    public List<string> DisabledTools { get; set; } = new();
}

public sealed class McpToolInfo
{
    public string ServerId { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public Dictionary<string, object?>? InputSchema { get; set; }
}

public sealed class McpServersDocument
{
    public List<McpServerDefinition> Servers { get; set; } = new();
}

public sealed class McpServerRuntime
{
    public McpServerDefinition Definition { get; set; } = new();
    public McpSession? Session { get; set; }
    public List<McpToolInfo> Tools { get; set; } = new();
    public string LastError { get; set; } = "";
    public bool IsConnected { get; set; }
    public string ResolvedCommand { get; set; } = "";
}
