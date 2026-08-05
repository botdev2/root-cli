

using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace RootCli.Core.Ollama;

public sealed class OllamaClient
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(25) };
    private readonly Dictionary<string, int> contextWindowCache = new(StringComparer.OrdinalIgnoreCase);

    public string BaseUrl { get; set; } = Environment.GetEnvironmentVariable("OLLAMA_HOST")?.Trim().TrimEnd('/')
                                          ?? "http://localhost:11434";

    public List<OllamaModelInfo> GetModelInfos(CancellationToken token = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, BaseUrl + "/api/tags");
        using var response = Http.Send(request, token);
        response.EnsureSuccessStatusCode();
        using var stream = response.Content.ReadAsStream(token);
        using var doc = JsonDocument.Parse(stream);
        var result = new List<OllamaModelInfo>();
        if (!doc.RootElement.TryGetProperty("models", out var models) || models.ValueKind != JsonValueKind.Array)
        {
            return result;
        }

        foreach (var item in models.EnumerateArray())
        {
            var name = item.TryGetProperty("name", out var n) ? n.GetString() : null;
            if (string.IsNullOrWhiteSpace(name))
            {
                continue;
            }

            var info = new OllamaModelInfo { Name = name };
            if (item.TryGetProperty("capabilities", out var caps) && caps.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in caps.EnumerateArray())
                {
                    var cap = c.GetString();
                    if (!string.IsNullOrWhiteSpace(cap))
                    {
                        info.Capabilities.Add(cap!);
                    }
                }
            }

            result.Add(info);
        }

        return result;
    }

    public List<string> GetModels(CancellationToken token = default) =>
        GetModelInfos(token).Where(m => !string.IsNullOrWhiteSpace(m.Name)).Select(m => m.Name).ToList();

    public string SendChat(
        string model,
        List<Dictionary<string, object?>> messages,
        Action<string>? onToken,
        CancellationToken token) =>
        SendChatWithTools(model, messages, null, onToken, token).Content;

    public OllamaChatResponse SendChatWithTools(
        string model,
        List<Dictionary<string, object?>> messages,
        List<Dictionary<string, object?>>? tools,
        Action<string>? onToken,
        CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            throw new InvalidOperationException("Model name is required.");
        }

        var payload = new Dictionary<string, object?>
        {
            ["model"] = model,
            ["messages"] = messages,
            ["stream"] = true,
            ["options"] = new Dictionary<string, object?>
            {
                ["temperature"] = 0.8,
                ["top_p"] = 0.95,
                ["num_ctx"] = GetContextWindowTokens(model)
            }
        };
        if (tools is { Count: > 0 })
        {
            payload["tools"] = tools;
        }

        var json = JsonSerializer.Serialize(payload);
        var response = new OllamaChatResponse { UsedNativeTools = tools is { Count: > 0 } };
        var contentBuilder = new StringBuilder();
        var toolCalls = new List<OllamaToolCall>();

        using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/api/chat");
        request.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var httpResponse = Http.Send(request, HttpCompletionOption.ResponseHeadersRead, token);
        httpResponse.EnsureSuccessStatusCode();
        using var stream = httpResponse.Content.ReadAsStream(token);
        using var reader = new StreamReader(stream);
        var thinkingOpen = false;
        while (!reader.EndOfStream)
        {
            token.ThrowIfCancellationRequested();
            var line = reader.ReadLine();
            if (string.IsNullOrWhiteSpace(line))
            {
                continue;
            }

            ProcessStreamLine(line, response, contentBuilder, toolCalls, ref thinkingOpen, onToken);
        }

        if (thinkingOpen)
        {
            contentBuilder.Append("</think>");
        }

        response.Content = contentBuilder.ToString();
        response.ToolCalls = toolCalls;
        response.ContextWindowTokens = GetContextWindowTokens(model);
        return response;
    }

    public int GetContextWindowTokens(string model)
    {
        if (string.IsNullOrWhiteSpace(model))
        {
            return 8192;
        }

        if (contextWindowCache.TryGetValue(model, out var cached) && cached >= 2048)
        {
            return cached;
        }

        var window = 8192;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, BaseUrl + "/api/show");
            request.Content = new StringContent(
                JsonSerializer.Serialize(new { name = model }),
                Encoding.UTF8,
                "application/json");
            using var response = Http.Send(request);
            if (response.IsSuccessStatusCode)
            {
                using var stream = response.Content.ReadAsStream();
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("model_info", out var info))
                {
                    foreach (var prop in info.EnumerateObject())
                    {
                        if (prop.Name.Contains("context_length", StringComparison.OrdinalIgnoreCase) &&
                            prop.Value.ValueKind == JsonValueKind.Number)
                        {
                            window = Math.Clamp(prop.Value.GetInt32(), 2048, 131072);
                            break;
                        }
                    }
                }
            }
        }
        catch
        {
        }

        contextWindowCache[model] = window;
        return window;
    }

    private static void ProcessStreamLine(
        string line,
        OllamaChatResponse response,
        StringBuilder contentBuilder,
        List<OllamaToolCall> toolCalls,
        ref bool thinkingOpen,
        Action<string>? onToken)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        if (root.TryGetProperty("prompt_eval_count", out var pec) && pec.ValueKind == JsonValueKind.Number)
        {
            response.PromptEvalCount = pec.GetInt32();
        }

        if (root.TryGetProperty("eval_count", out var ec) && ec.ValueKind == JsonValueKind.Number)
        {
            response.EvalCount = ec.GetInt32();
        }

        if (!root.TryGetProperty("message", out var message))
        {
            return;
        }

        if (message.TryGetProperty("thinking", out var thinking))
        {
            var thinkingText = thinking.GetString();
            if (!string.IsNullOrEmpty(thinkingText))
            {
                if (!thinkingOpen)
                {
                    contentBuilder.Append("<think>");
                    thinkingOpen = true;
                }

                contentBuilder.Append(thinkingText);
            }
        }

        if (message.TryGetProperty("content", out var contentProp))
        {
            var tokenText = contentProp.GetString();
            if (!string.IsNullOrEmpty(tokenText))
            {
                if (thinkingOpen)
                {
                    contentBuilder.Append("</think>");
                    thinkingOpen = false;
                }

                contentBuilder.Append(tokenText);
                onToken?.Invoke(tokenText);
            }
        }

        if (!message.TryGetProperty("tool_calls", out var calls) || calls.ValueKind != JsonValueKind.Array)
        {
            return;
        }

        foreach (var item in calls.EnumerateArray())
        {
            var parsed = ParseToolCall(item);
            if (parsed != null)
            {
                MergeToolCall(toolCalls, parsed);
            }
        }
    }

    private static OllamaToolCall? ParseToolCall(JsonElement callDict)
    {
        if (!callDict.TryGetProperty("function", out var function))
        {
            return null;
        }

        var name = function.TryGetProperty("name", out var n) ? n.GetString() : null;
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        var args = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (function.TryGetProperty("arguments", out var rawArgs))
        {
            if (rawArgs.ValueKind == JsonValueKind.Object)
            {
                foreach (var prop in rawArgs.EnumerateObject())
                {
                    args[prop.Name] = JsonElementToObject(prop.Value);
                }
            }
            else if (rawArgs.ValueKind == JsonValueKind.String)
            {
                var argText = rawArgs.GetString();
                if (!string.IsNullOrWhiteSpace(argText))
                {
                    try
                    {
                        using var argDoc = JsonDocument.Parse(argText);
                        if (argDoc.RootElement.ValueKind == JsonValueKind.Object)
                        {
                            foreach (var prop in argDoc.RootElement.EnumerateObject())
                            {
                                args[prop.Name] = JsonElementToObject(prop.Value);
                            }
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        return new OllamaToolCall { Name = name!, Arguments = args };
    }

    private static object? JsonElementToObject(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => el.GetRawText()
        };

    private static void MergeToolCall(List<OllamaToolCall> toolCalls, OllamaToolCall incoming)
    {
        for (var i = 0; i < toolCalls.Count; i++)
        {
            if (string.Equals(toolCalls[i].Name, incoming.Name, StringComparison.OrdinalIgnoreCase))
            {
                foreach (var kv in incoming.Arguments)
                {
                    toolCalls[i].Arguments[kv.Key] = kv.Value;
                }

                return;
            }
        }

        toolCalls.Add(incoming);
    }
}
