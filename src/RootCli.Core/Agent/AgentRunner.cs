
using System.Text;
using RootCli.Core.Chat;
using RootCli.Core.Mcp;
using RootCli.Core.Ollama;
using RootCli.Core.Skills;
using RootCli.Core.Tools;
using RootCli.Core.Workspace;

namespace RootCli.Core.Agent;

public sealed class AgentRunOptions
{
    public string Prompt { get; set; } = "";
    public string Model { get; set; } = "";
    public int MaxSteps { get; set; } = 12;
    public AgentChatMode Mode { get; set; } = AgentChatMode.Agent;
    public bool McpEnabled { get; set; } = true;
    public bool AutoApproveNonHighRisk { get; set; }
    public Func<ToolCall, bool>? Approve { get; set; }
    public Action<string>? OnToken { get; set; }
    public Action<string>? OnLog { get; set; }

    public IReadOnlyList<ChatTurn>? History { get; set; }
}

public sealed class AgentRunResult
{
    public string Answer { get; set; } = "";
    public int Steps { get; set; }
    public List<string> ToolTraces { get; set; } = new();
    public string Mode { get; set; } = "agent";
    public string McpSummary { get; set; } = "";
    public AgentRunStats Stats { get; set; } = new();
}

public sealed class AgentRunner
{
    private readonly OllamaClient ollama;
    private readonly WorkspaceService workspace;

    public AgentRunner(OllamaClient ollama, WorkspaceService workspace)
    {
        this.ollama = ollama;
        this.workspace = workspace;
    }

    public AgentRunResult Run(AgentRunOptions options, CancellationToken token)
    {
        if (string.IsNullOrWhiteSpace(options.Prompt))
        {
            throw new InvalidOperationException("Prompt is empty.");
        }

        if (string.IsNullOrWhiteSpace(options.Model))
        {
            throw new InvalidOperationException("Model is required.");
        }

        var result = new AgentRunResult
        {
            Mode = AgentModePolicy.ToStorage(options.Mode)
        };

        try
        {
            if (options.McpEnabled)
            {
                McpServerManager.StartEnabled(options.OnLog);
                result.McpSummary = McpServerManager.StartupSummary;
                if (workspace.HasWorkspace)
                {
                    McpServerManager.TryAutoIndexWorkspace(workspace.RootPath, options.OnLog);
                }
            }

            var messages = new List<Dictionary<string, object?>>
            {
                TextHelpers.Message("system", BuildSystemPrompt(options))
            };

            if (options.History != null)
            {
                foreach (var turn in options.History)
                {
                    if (string.IsNullOrWhiteSpace(turn.Content))
                    {
                        continue;
                    }

                    var role = turn.Role is "assistant" or "system" or "user" ? turn.Role : "user";
                    messages.Add(TextHelpers.Message(role, turn.Content));
                }
            }

            messages.Add(TextHelpers.Message("user", options.Prompt.Trim()));

            var tools = BuildToolList(options.Mode, options.McpEnabled);
            var approve = options.Approve ?? (call => DefaultApprove(call, options.AutoApproveNonHighRisk));

            while (result.Steps < Math.Max(1, options.MaxSteps))
            {
                token.ThrowIfCancellationRequested();
                result.Steps++;
                options.OnLog?.Invoke("step " + result.Steps + " thinking…");

                var response = ollama.SendChatWithTools(
                    options.Model,
                    messages,
                    tools,
                    options.OnToken,
                    token);

                var visible = PlainTextFormatter.ToTerminalPlain(
                    TextHelpers.GetVisibleAssistantText(response.Content));
                if (!string.IsNullOrWhiteSpace(visible))
                {
                    result.Answer = visible;
                }

                var assistantMsg = new Dictionary<string, object?>
                {
                    ["role"] = "assistant",
                    ["content"] = response.Content ?? ""
                };
                if (response.ToolCalls.Count > 0)
                {
                    assistantMsg["tool_calls"] = response.ToolCalls.Select(tc => new Dictionary<string, object?>
                    {
                        ["function"] = new Dictionary<string, object?>
                        {
                            ["name"] = tc.Name,
                            ["arguments"] = tc.Arguments
                        }
                    }).ToArray();
                }

                messages.Add(assistantMsg);

                var calls = ToolProcessor.FromNativeToolCalls(response.ToolCalls);
                if (calls.Count == 0)
                {
                    options.OnLog?.Invoke("final response.");
                    return result;
                }

                options.OnLog?.Invoke("executing " + calls.Count + " tool(s)…");
                foreach (var call in calls)
                {
                    token.ThrowIfCancellationRequested();
                    options.OnLog?.Invoke("tool: " + call.ToolName);
                    var toolResult = ToolProcessor.Execute(call, workspace, approve, options.OnLog, options.Mode);
                    result.Stats.Record(call, toolResult);
                    result.ToolTraces.Add(call.ToolName + " → " + Truncate(toolResult, 240));
                    messages.Add(TextHelpers.ToolResultMessage(call.ToolName, toolResult));
                }
            }

            if (string.IsNullOrWhiteSpace(result.Answer))
            {
                result.Answer = "Stopped after " + result.Steps + " steps without a final prose answer.";
            }

            return result;
        }
        finally
        {
            if (options.McpEnabled)
            {
                McpServerManager.Shutdown();
            }
        }
    }

    private static List<Dictionary<string, object?>>? BuildToolList(AgentChatMode mode, bool mcpEnabled)
    {
        var tools = AgentToolRegistry.BuildNativeTools(mode);
        if (mcpEnabled)
        {
            tools.AddRange(McpServerManager.GetEnabledNativeTools(mode));
        }

        return tools.Count == 0 ? null : tools;
    }

    private string BuildSystemPrompt(AgentRunOptions options)
    {
        var builder = new StringBuilder();
        builder.AppendLine("You are Root CLI — a local terminal agent running against Ollama.");
        builder.AppendLine("Be precise. Prefer repository tools over guessing file contents.");
        builder.AppendLine("The user reads answers in a plain terminal that CANNOT render Markdown.");
        builder.AppendLine("Write plain text only: no **, *, _, `, ``` fences, # headings, []() links, or HTML.");
        builder.AppendLine("Use numbered lists (1. 2. 3.) and plain command lines. Indent snippets with spaces if needed.");
        builder.AppendLine("When finished, answer in plain prose (never markdown tool JSON in the final answer).");
        AgentModePolicy.AppendSystemPrompt(builder, options.Mode);
        builder.AppendLine(SkillsPrompt.BuildActiveSkillsSection());
        if (workspace.HasWorkspace)
        {
            builder.AppendLine("Repository root: " + workspace.RootPath);
            builder.AppendLine("Visible repository files:");
            builder.AppendLine(workspace.GetTreeSummary(40));
        }
        else
        {
            builder.AppendLine("No repository is selected. Answer from general knowledge only.");
        }

        ToolProcessor.AppendAgentToolInstructions(builder, options.Mode);
        if (options.McpEnabled)
        {
            McpServerManager.AppendToolInstructions(builder);
        }

        return builder.ToString();
    }

    private static bool DefaultApprove(ToolCall call, bool autoNonHigh)
    {
        if (string.Equals(call.Risk, "high", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return autoNonHigh || string.Equals(call.Risk, "low", StringComparison.OrdinalIgnoreCase);
    }

    private static string Truncate(string text, int max)
    {
        text ??= "";
        text = text.Replace("\r", " ").Replace("\n", " ").Trim();
        return text.Length <= max ? text : text.Substring(0, max - 1) + "…";
    }
}
