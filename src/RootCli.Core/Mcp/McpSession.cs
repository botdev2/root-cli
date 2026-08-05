
using System.Collections;
using System.Text;

namespace RootCli.Core.Mcp;

public sealed class McpSession : IDisposable
{
    private readonly McpStdioTransport transport = new();
    private readonly Dictionary<int, PendingRequest> pending = new();
    private readonly object pendingLock = new();
    private int nextId;

    public string ServerId { get; private set; } = "";
    public string? LastError { get; private set; }
    public bool IsConnected { get; private set; }

    public bool Connect(string serverId, string command, string arguments, string? workingDirectory)
    {
        ServerId = serverId;
        LastError = null;
        IsConnected = false;

        transport.MessageReceived += HandleMessage;
        if (!transport.Start(command, arguments, workingDirectory))
        {
            LastError = transport.LastError ?? "Failed to start MCP transport.";
            return false;
        }

        try
        {
            var initParams = new Dictionary<string, object?>
            {
                ["protocolVersion"] = "2024-11-05",
                ["capabilities"] = new Dictionary<string, object?>(),
                ["clientInfo"] = new Dictionary<string, object?>
                {
                    ["name"] = "root-cli",
                    ["version"] = "0.1.0"
                }
            };

            var initResult = SendRequest("initialize", initParams, 30000);
            if (initResult == null)
            {
                LastError ??= "MCP initialize returned no result.";
                Disconnect();
                return false;
            }

            transport.WriteMessage(McpJson.Serialize(new Dictionary<string, object?>
            {
                ["jsonrpc"] = "2.0",
                ["method"] = "notifications/initialized"
            }));

            IsConnected = true;
            return true;
        }
        catch (Exception ex)
        {
            LastError = ex.Message;
            Disconnect();
            return false;
        }
    }

    public List<McpToolInfo> ListTools()
    {
        var result = SendRequest("tools/list", new Dictionary<string, object?>(), 30000);
        var tools = new List<McpToolInfo>();
        if (result == null || !result.TryGetValue("tools", out var raw) || raw is not IEnumerable rawTools)
        {
            return tools;
        }

        foreach (var item in rawTools)
        {
            if (item is not Dictionary<string, object?> toolDict ||
                !toolDict.TryGetValue("name", out var nameObj))
            {
                continue;
            }

            var info = new McpToolInfo
            {
                ServerId = ServerId,
                Name = Convert.ToString(nameObj) ?? "",
                Description = toolDict.TryGetValue("description", out var desc) ? Convert.ToString(desc) ?? "" : ""
            };

            if (toolDict.TryGetValue("inputSchema", out var schema) &&
                schema is Dictionary<string, object?> schemaDict)
            {
                info.InputSchema = schemaDict;
            }

            tools.Add(info);
        }

        return tools;
    }

    public string CallTool(string toolName, Dictionary<string, object?>? arguments)
    {
        if (!IsConnected)
        {
            return "MCP server '" + ServerId + "' is not connected.";
        }

        var parameters = new Dictionary<string, object?>
        {
            ["name"] = toolName,
            ["arguments"] = arguments ?? new Dictionary<string, object?>()
        };

        var result = SendRequest("tools/call", parameters, 180000);
        if (result == null)
        {
            return "MCP tool call failed: " + (LastError ?? "no response");
        }

        if (result.TryGetValue("isError", out var err) && IsTruthy(err))
        {
            return "MCP tool error:\n" + FormatToolContent(result);
        }

        return FormatToolContent(result);
    }

    public void Disconnect()
    {
        IsConnected = false;
        transport.MessageReceived -= HandleMessage;
        transport.Stop();
        lock (pendingLock)
        {
            foreach (var entry in pending.Values)
            {
                entry.Signal(null);
            }

            pending.Clear();
        }
    }

    public void Dispose()
    {
        Disconnect();
        transport.Dispose();
    }

    private Dictionary<string, object?>? SendRequest(
        string method,
        Dictionary<string, object?> parameters,
        int timeoutMs)
    {
        var id = Interlocked.Increment(ref nextId);
        var pendingRequest = new PendingRequest();
        lock (pendingLock)
        {
            pending[id] = pendingRequest;
        }

        var payload = new Dictionary<string, object?>
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["method"] = method,
            ["params"] = parameters
        };

        transport.WriteMessage(McpJson.Serialize(payload));
        if (!pendingRequest.Wait(timeoutMs))
        {
            lock (pendingLock)
            {
                pending.Remove(id);
            }

            LastError = "Timed out waiting for MCP response to " + method + ".";
            return null;
        }

        if (pendingRequest.Error != null)
        {
            LastError = pendingRequest.Error;
            return null;
        }

        return pendingRequest.Result;
    }

    private void HandleMessage(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return;
        }

        Dictionary<string, object?>? message;
        try
        {
            message = McpJson.DeserializeObject(json);
        }
        catch
        {
            return;
        }

        if (message == null || !message.TryGetValue("id", out var idObj) || !TryGetInt(idObj, out var requestId))
        {
            return;
        }

        PendingRequest? pendingRequest;
        lock (pendingLock)
        {
            if (!pending.TryGetValue(requestId, out pendingRequest))
            {
                return;
            }

            pending.Remove(requestId);
        }

        if (message.TryGetValue("error", out var errorObj))
        {
            pendingRequest.Error = FormatRpcError(errorObj);
            pendingRequest.Signal(null);
            return;
        }

        pendingRequest.Result = message.TryGetValue("result", out var resultObj) &&
                                resultObj is Dictionary<string, object?> resultDict
            ? resultDict
            : new Dictionary<string, object?>();
        pendingRequest.Signal(pendingRequest.Result);
    }

    private static string FormatToolContent(Dictionary<string, object?> result)
    {
        if (!result.TryGetValue("content", out var contentObj) || contentObj is not IEnumerable contentItems)
        {
            return McpJson.Serialize(result);
        }

        var builder = new StringBuilder();
        foreach (var item in contentItems)
        {
            if (item is not Dictionary<string, object?> block)
            {
                continue;
            }

            if (block.TryGetValue("text", out var text))
            {
                builder.AppendLine(Convert.ToString(text));
            }
            else
            {
                builder.AppendLine(McpJson.Serialize(block));
            }
        }

        var textOut = builder.ToString().Trim();
        return string.IsNullOrWhiteSpace(textOut) ? McpJson.Serialize(result) : textOut;
    }

    private static string FormatRpcError(object? errorObj)
    {
        if (errorObj is not Dictionary<string, object?> errorDict)
        {
            return Convert.ToString(errorObj) ?? "Unknown MCP error";
        }

        var message = errorDict.TryGetValue("message", out var msg)
            ? Convert.ToString(msg) ?? "Unknown MCP error"
            : "Unknown MCP error";
        if (errorDict.TryGetValue("data", out var data))
        {
            message += "\n" + Convert.ToString(data);
        }

        return message;
    }

    private static bool IsTruthy(object? value) =>
        value switch
        {
            null => false,
            bool b => b,
            _ => string.Equals(Convert.ToString(value), "true", StringComparison.OrdinalIgnoreCase)
        };

    private static bool TryGetInt(object? value, out int result)
    {
        result = 0;
        return value switch
        {
            int i => Assign(out result, i),
            long l when l is >= int.MinValue and <= int.MaxValue => Assign(out result, (int)l),
            _ => int.TryParse(Convert.ToString(value), out result)
        };

        static bool Assign(out int target, int value)
        {
            target = value;
            return true;
        }
    }

    private sealed class PendingRequest
    {
        private readonly ManualResetEventSlim waitHandle = new(false);

        public Dictionary<string, object?>? Result;
        public string? Error;

        public bool Wait(int timeoutMs) => waitHandle.Wait(timeoutMs);

        public void Signal(Dictionary<string, object?>? result)
        {
            Result = result;
            waitHandle.Set();
        }
    }
}
