
using System.Collections;
using System.Text;
using RootCli.Core.Agent;
using RootCli.Core.Tools;

namespace RootCli.Core.Mcp;

public static class McpServerManager
{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, McpServerRuntime> Runtimes =
        new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> IndexedWorkspaces =
        new(StringComparer.OrdinalIgnoreCase);

    private static List<McpToolInfo> cachedTools = new();
    private static string startupSummary = "MCP not started.";

    public static string StartupSummary
    {
        get
        {
            lock (Sync)
            {
                return startupSummary;
            }
        }
    }

    public static IReadOnlyList<McpToolInfo> GetAvailableTools()
    {
        lock (Sync)
        {
            return cachedTools.ToList();
        }
    }

    public static IReadOnlyList<McpServerRuntime> GetRuntimes()
    {
        lock (Sync)
        {
            return Runtimes.Values.ToList();
        }
    }

    public static bool IsStarted
    {
        get
        {
            lock (Sync)
            {
                return Runtimes.Count > 0;
            }
        }
    }

    public static void StartEnabled(Action<string>? log = null)
    {
        lock (Sync)
        {
            ShutdownInternal();
            KillOrphanCodebaseMemoryProcesses();
            var document = McpServerStore.Load();
            var summaries = new List<string>();

            foreach (var definition in document.Servers)
            {
                if (definition == null || !definition.Enabled || string.IsNullOrWhiteSpace(definition.Id))
                {
                    continue;
                }

                var runtime = StartServer(definition);
                Runtimes[definition.Id] = runtime;
                if (runtime.IsConnected)
                {
                    summaries.Add(definition.Id + " (" + runtime.Tools.Count + " tools)");
                    log?.Invoke("mcp: connected " + definition.Id + " (" + runtime.Tools.Count + " tools)");
                }
                else
                {
                    summaries.Add(definition.Id + " offline: " + (runtime.LastError ?? "unknown error"));
                    log?.Invoke("mcp: offline " + definition.Id + " — " + (runtime.LastError ?? "unknown"));
                }
            }

            cachedTools = Runtimes.Values
                .Where(r => r.IsConnected && r.Tools != null)
                .SelectMany(r => r.Tools)
                .Where(t => IsMcpToolEnabled(t.ServerId, t.Name))
                .ToList();

            startupSummary = summaries.Count == 0
                ? "No MCP servers enabled."
                : string.Join("; ", summaries);
        }
    }

    public static void Shutdown()
    {
        lock (Sync)
        {
            ShutdownInternal();
            cachedTools = new List<McpToolInfo>();
            startupSummary = "MCP stopped.";
        }
    }

    public static List<Dictionary<string, object?>> GetEnabledNativeTools(AgentChatMode mode)
    {
        var tools = new List<Dictionary<string, object?>>();
        List<McpToolInfo> available;
        lock (Sync)
        {
            available = cachedTools.ToList();
        }

        foreach (var info in available)
        {
            if (!IsMcpToolEnabled(info.ServerId, info.Name))
            {
                continue;
            }

            if (!AgentModePolicy.AllowsTool(mode, ToolType.McpCall, info.Name))
            {
                continue;
            }

            var nativeName = AgentToolRegistry.BuildNativeToolKey(info.ServerId, info.Name);
            var description = string.IsNullOrWhiteSpace(info.Description)
                ? "MCP tool " + info.Name + " on server " + info.ServerId
                : info.Description;
            tools.Add(AgentToolRegistry.MakeFunction(nativeName, description, BuildMcpProperties(info)));
        }

        return tools;
    }

    public static string CallTool(string serverId, string toolName, Dictionary<string, object?>? arguments)
    {
        if (string.IsNullOrWhiteSpace(serverId) || string.IsNullOrWhiteSpace(toolName))
        {
            return "mcp_call requires server and name.";
        }

        McpServerRuntime? runtime;
        lock (Sync)
        {
            if (!Runtimes.TryGetValue(serverId.Trim(), out runtime) ||
                runtime == null ||
                !runtime.IsConnected ||
                runtime.Session == null)
            {
                return "MCP server '" + serverId + "' is not connected. Check mcp-servers.json or install the binary.";
            }
        }

        return TrimOutput(runtime.Session.CallTool(toolName.Trim(), arguments));
    }

    public static bool RequiresApproval(string? toolName)
    {
        var normalized = (toolName ?? "").Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return true;
        }

        return normalized.Contains("index") ||
               normalized.Contains("manage") ||
               normalized.Contains("write") ||
               normalized.Contains("delete") ||
               normalized.Contains("update") ||
               normalized.Contains("set_");
    }

    public static void TryAutoIndexWorkspace(string? workspacePath, Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(workspacePath) || !Directory.Exists(workspacePath))
        {
            return;
        }

        var normalized = Path.GetFullPath(workspacePath);
        lock (Sync)
        {
            if (IndexedWorkspaces.Contains(normalized))
            {
                return;
            }

            if (!Runtimes.TryGetValue("codebase-memory", out var runtime) ||
                runtime is not { IsConnected: true, Session: not null, Definition.AutoIndexOnWorkspaceOpen: true })
            {
                return;
            }

            IndexedWorkspaces.Add(normalized);
        }

        log?.Invoke("mcp: indexing workspace via codebase-memory...");
        try
        {
            CallTool(
                "codebase-memory",
                "index_repository",
                new Dictionary<string, object?> { ["path"] = normalized });
        }
        catch
        {

        }
    }

    public static void AppendToolInstructions(StringBuilder builder)
    {
        List<McpToolInfo> tools;
        lock (Sync)
        {
            tools = cachedTools.ToList();
        }

        if (tools.Count == 0)
        {
            builder.AppendLine("MCP servers: none connected. Install codebase-memory-mcp (desktop /root tools) or set ROOTCLI_MCP_CODEBASE_MEMORY.");
            return;
        }

        builder.AppendLine("MCP: prefer mcp__codebase-memory__* for architecture, symbols, call graphs before broad grepping.");
        var grouped = tools.GroupBy(t => t.ServerId ?? "unknown");
        foreach (var group in grouped)
        {
            builder.AppendLine("Server '" + group.Key + "':");
            foreach (var tool in group.Take(20))
            {
                var description = string.IsNullOrWhiteSpace(tool.Description)
                    ? ""
                    : " - " + TrimSingleLine(tool.Description, 140);
                builder.AppendLine("- mcp__" + group.Key + "__" + tool.Name + description);
            }

            if (group.Count() > 20)
            {
                builder.AppendLine("- ... and " + (group.Count() - 20) + " more");
            }
        }

        if (tools.Any(t => string.Equals(t.ServerId, "codebase-memory", StringComparison.OrdinalIgnoreCase)))
        {
            builder.AppendLine("Codebase Memory: list_projects/index_status, then search_graph, semantic_query, trace_path, or get_architecture.");
            builder.AppendLine("If the repo is not indexed, call mcp__codebase-memory__index_repository with {\"path\":\"<repo-root>\"}.");
        }
    }

    public static bool IsMcpToolEnabled(string serverId, string toolName)
    {
        var document = McpServerStore.Load();
        var server = document.Servers.FirstOrDefault(s =>
            string.Equals(s.Id, serverId, StringComparison.OrdinalIgnoreCase));
        if (server?.DisabledTools == null)
        {
            return true;
        }

        return !server.DisabledTools.Any(name =>
            string.Equals(name, toolName, StringComparison.OrdinalIgnoreCase));
    }

    private static McpServerRuntime StartServer(McpServerDefinition definition)
    {
        var runtime = new McpServerRuntime { Definition = definition };
        var resolved = McpPathResolver.ResolveExecutable(definition);
        runtime.ResolvedCommand = resolved ?? "";
        if (string.IsNullOrWhiteSpace(resolved))
        {
            runtime.LastError = string.Equals(definition.Id, "codebase-memory", StringComparison.OrdinalIgnoreCase)
                ? "codebase-memory-mcp.exe not found. Install desktop /root tools or set ROOTCLI_MCP_CODEBASE_MEMORY."
                : "Executable not found for server '" + definition.Id + "'.";
            return runtime;
        }

        var args = definition.Args == null || definition.Args.Count == 0
            ? ""
            : string.Join(" ", definition.Args.Select(QuoteArg));
        var session = new McpSession();
        if (!session.Connect(definition.Id, resolved, args, definition.WorkingDirectory))
        {
            runtime.LastError = session.LastError ?? "connect failed";
            session.Dispose();
            return runtime;
        }

        runtime.Session = session;
        runtime.IsConnected = true;
        runtime.Tools = session.ListTools();
        return runtime;
    }

    private static void ShutdownInternal()
    {
        foreach (var runtime in Runtimes.Values)
        {
            try
            {
                runtime.Session?.Dispose();
            }
            catch
            {

            }
        }

        Runtimes.Clear();
        IndexedWorkspaces.Clear();
    }

    public static void KillOrphanCodebaseMemoryProcesses()
    {
        try
        {
            foreach (var process in System.Diagnostics.Process.GetProcessesByName("codebase-memory-mcp"))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                    process.WaitForExit(2000);
                }
                catch
                {

                }
            }
        }
        catch
        {

        }
    }

    private static Dictionary<string, object?>[] BuildMcpProperties(McpToolInfo info)
    {
        if (info.InputSchema == null ||
            !info.InputSchema.TryGetValue("properties", out var propsObj) ||
            propsObj is not Dictionary<string, object?> properties ||
            properties.Count == 0)
        {
            return Array.Empty<Dictionary<string, object?>>();
        }

        var required = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (info.InputSchema.TryGetValue("required", out var requiredObj) &&
            requiredObj is IEnumerable requiredItems)
        {
            foreach (var item in requiredItems)
            {
                var name = Convert.ToString(item);
                if (!string.IsNullOrWhiteSpace(name))
                {
                    required.Add(name);
                }
            }
        }

        var result = new List<Dictionary<string, object?>>();
        foreach (var entry in properties)
        {
            var schema = entry.Value as Dictionary<string, object?>;
            var type = "string";
            if (schema != null && schema.TryGetValue("type", out var typeObj))
            {
                type = Convert.ToString(typeObj) ?? "string";

                if (typeObj is IList list && list.Count > 0)
                {
                    type = Convert.ToString(list[0]) ?? "string";
                }
            }

            result.Add(new Dictionary<string, object?>
            {
                ["name"] = entry.Key,
                ["type"] = type is "integer" or "number" or "boolean" or "object" or "array" ? type : "string",
                ["description"] = schema != null && schema.TryGetValue("description", out var d)
                    ? Convert.ToString(d) ?? ""
                    : "",
                ["required"] = required.Contains(entry.Key)
            });
        }

        return result.ToArray();
    }

    private static string QuoteArg(string arg)
    {
        if (string.IsNullOrEmpty(arg))
        {
            return "\"\"";
        }

        if (arg.Contains(' ') || arg.Contains('"'))
        {
            return "\"" + arg.Replace("\"", "\\\"") + "\"";
        }

        return arg;
    }

    private static string TrimOutput(string? text)
    {
        text ??= "";
        const int max = 24_000;
        return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
    }

    private static string TrimSingleLine(string text, int max)
    {
        text = (text ?? "").Replace('\r', ' ').Replace('\n', ' ').Trim();
        return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
    }
}
