namespace RootCli.Core.Workspace;

public sealed class WorkspaceEntry
{
    public string Name { get; set; } = "";
    public string RelativePath { get; set; } = "";
    public bool IsDirectory { get; set; }
    public long Size { get; set; }
}
