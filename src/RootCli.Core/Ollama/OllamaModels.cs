namespace RootCli.Core.Ollama;

public sealed class OllamaModelInfo
{
    public string Name { get; set; } = "";
    public List<string> Capabilities { get; set; } = new();
}

public sealed class OllamaToolCall
{
    public string Name { get; set; } = "";
    public Dictionary<string, object?> Arguments { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class OllamaChatResponse
{
    public string Content { get; set; } = "";
    public List<OllamaToolCall> ToolCalls { get; set; } = new();
    public bool UsedNativeTools { get; set; }
    public int PromptEvalCount { get; set; }
    public int EvalCount { get; set; }
    public int ContextWindowTokens { get; set; }
}
