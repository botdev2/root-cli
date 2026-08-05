using System.Text;
using RootCli.Core.Agent;
using RootCli.Core.Mcp;
using RootCli.Core.Ollama;
using RootCli.Core.Tools;
using RootCli.Core.Workspace;

namespace RootCli;

internal static class Program
{
    private static int Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0)
        {
            return new InteractiveShell().Run();
        }

        if (IsHelp(args[0]))
        {
            PrintHelp();
            return 0;
        }

        try
        {
            var command = args[0].Trim().ToLowerInvariant();
            var rest = args.Skip(1).ToArray();
            return command switch
            {
                "menu" or "ui" or "shell" => new InteractiveShell().Run(),
                "here" or "." or "open" => CmdHere(rest),
                "login" or "signin" => CmdOllamaAuth(rest, signIn: true),
                "logout" or "signout" => CmdOllamaAuth(rest, signIn: false),
                "ollama" => CmdOllama(rest),
                "models" => CmdModels(rest),
                "ask" => CmdRun(rest, AgentChatMode.Ask),
                "agent" => CmdRun(rest, AgentChatMode.Agent),
                "plan" => CmdRun(rest, AgentChatMode.Plan),
                "tools" => CmdTools(rest),
                "tool" => CmdTool(rest),
                "mcp" => CmdMcp(rest),
                _ => Fail("Unknown command: " + args[0] + ". Try: rootcli --help")
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("error: " + ex.Message);
            return 1;
        }
    }

    internal static int RunFromShell(
        AgentChatMode mode,
        string prompt,
        string repo,
        string? model,
        bool yes,
        bool mcpEnabled,
        int maxSteps)
    {
        var args = new List<string> { prompt, "-r", repo, "--max-steps", maxSteps.ToString() };
        if (!string.IsNullOrWhiteSpace(model))
        {
            args.Add("-m");
            args.Add(model!);
        }

        if (yes)
        {
            args.Add("--yes");
        }

        if (!mcpEnabled)
        {
            args.Add("--no-mcp");
        }

        return CmdRun(args.ToArray(), mode);
    }

    internal static int CmdModelsPublic(string[] args) => CmdModels(args);
    internal static int CmdToolsPublic(string[] args) => CmdTools(args);
    internal static int CmdMcpPublic(string[] args) => CmdMcp(args);

    private static int CmdModels(string[] args)
    {
        var json = HasFlag(args, "--json");
        var client = new OllamaClient();
        var models = client.GetModelInfos();
        if (json)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(models));
            return 0;
        }

        TermUi.BrandHeader("local models");
        TermUi.KeyValue("host", client.BaseUrl, TermUi.Brand);
        TermUi.KeyValue("count", models.Count.ToString(), models.Count > 0 ? TermUi.Ok : TermUi.Warn);
        Console.WriteLine();

        if (models.Count == 0)
        {
            TermUi.StatusDot(false, "No models. Is Ollama running?");
            TermUi.Hint("try:  ollama pull llama3.2");
            return 1;
        }

        TermUi.BoxTop();
        for (var i = 0; i < models.Count; i++)
        {
            var m = models[i];
            var n = (i + 1).ToString().PadLeft(2);
            TermUi.Write("  ║ ", TermUi.Brand);
            TermUi.Write(n + "  ", TermUi.Dim);
            TermUi.Write(m.Name, TermUi.BrandStrong);
            foreach (var cap in m.Capabilities)
            {
                TermUi.CapPill(cap);
            }

            Console.WriteLine();
        }

        TermUi.BoxBottom();
        TermUi.Hint("use:  rootcli ask \"…\" -r <repo> -m <name>");
        return 0;
    }

    private static int CmdRun(string[] args, AgentChatMode mode)
    {
        var prompt = TakePositional(args);
        if (string.IsNullOrWhiteSpace(prompt))
        {
            return Fail(AgentModePolicy.ToStorage(mode) + " requires a prompt string.");
        }

        var repo = ResolveRepoPath(
            GetOption(args, "-r", "--repo")
            ?? Environment.GetEnvironmentVariable("ROOTCLI_REPO")
            ?? ".");
        var model = ResolveModel(GetOption(args, "-m", "--model"));
        var yes = HasFlag(args, "--yes");
        var maxSteps = ParseInt(GetOption(args, "--max-steps"), 12);
        var mcpEnabled = !HasFlag(args, "--no-mcp");
        var workspace = new WorkspaceService();
        if (string.IsNullOrWhiteSpace(repo) || !Directory.Exists(repo))
        {
            return Fail(AgentModePolicy.ToStorage(mode) + " needs a repo folder. Use -r ., -r /path, or ROOTCLI_REPO.");
        }

        workspace.SetRoot(repo);

        var ollama = new OllamaClient();
        var runner = new AgentRunner(ollama, workspace);
        var modeName = AgentModePolicy.ToStorage(mode);
        TermUi.BrandHeader(modeName);
        TermUi.KeyValue("mode", modeName.ToUpperInvariant(), TermUi.ModeColor(modeName));
        TermUi.KeyValue("model", model, TermUi.BrandStrong);
        TermUi.KeyValue("repo", workspace.RootPath, TermUi.Tool);
        TermUi.KeyValue("mcp", mcpEnabled ? "on" : "off", mcpEnabled ? TermUi.Mcp : TermUi.Dim);
        TermUi.Rule();
        Console.WriteLine();

        var thinking = new ThinkingPane();
        thinking.Begin();
        var gate = new ApprovalGate { PrefYes = yes };
        var result = runner.Run(new AgentRunOptions
        {
            Prompt = prompt,
            Model = model,
            MaxSteps = maxSteps,
            Mode = mode,
            McpEnabled = mcpEnabled,
            AutoApproveNonHighRisk = yes,
            Approve = gate.Approve,
            OnToken = null,
            OnLog = thinking.Log
        }, CancellationToken.None);

        thinking.ReplaceWithAnswer();

        if (!string.IsNullOrWhiteSpace(result.Answer))
        {
            TermUi.WriteAnswer(result.Answer);
        }

        TermUi.WriteRunStats(result.Stats);
        return 0;
    }

    internal static AgentRunResult RunChatTurn(
        AgentChatMode mode,
        string prompt,
        string repo,
        string model,
        ApprovalGate gate,
        bool mcpEnabled,
        int maxSteps,
        IReadOnlyList<RootCli.Core.Chat.ChatTurn>? history,
        ThinkingPane thinking)
    {
        var workspace = new WorkspaceService();
        workspace.SetRoot(repo);
        var runner = new AgentRunner(new OllamaClient(), workspace);
        thinking.Begin();
        return runner.Run(new AgentRunOptions
        {
            Prompt = prompt,
            Model = model,
            MaxSteps = maxSteps,
            Mode = mode,
            McpEnabled = mcpEnabled,
            AutoApproveNonHighRisk = gate.PrefYes || gate.AlwaysYes,
            Approve = gate.Approve,
            OnToken = null,
            OnLog = thinking.Log,
            History = history
        }, CancellationToken.None);
    }

    private static int CmdTools(string[] args)
    {
        var json = HasFlag(args, "--json");
        var names = AgentToolRegistry.ListToolNames().ToList();
        var includeMcp = !HasFlag(args, "--no-mcp");
        if (includeMcp)
        {
            try
            {
                McpServerManager.StartEnabled();
                names.AddRange(McpServerManager.GetAvailableTools()
                    .Select(t => AgentToolRegistry.BuildNativeToolKey(t.ServerId, t.Name)));
            }
            finally
            {
                McpServerManager.Shutdown();
            }
        }

        if (json)
        {
            Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(names));
            return 0;
        }

        TermUi.BrandHeader("tools");
        TermUi.KeyValue("native", AgentToolRegistry.ListToolNames().Count.ToString(), TermUi.Tool);
        var mcpCount = names.Count - AgentToolRegistry.ListToolNames().Count;
        TermUi.KeyValue("mcp", includeMcp ? mcpCount.ToString() : "skipped", includeMcp ? TermUi.Mcp : TermUi.Dim);
        Console.WriteLine();
        TermUi.BoxTop();
        foreach (var name in names)
        {
            var isMcp = name.StartsWith("mcp__", StringComparison.OrdinalIgnoreCase);
            TermUi.Write("  ║ ", TermUi.Brand);
            TermUi.Write(isMcp ? "mcp  " : "core ", isMcp ? TermUi.Mcp : TermUi.Tool);
            TermUi.WriteLine(name, TermUi.BrandStrong);
        }

        TermUi.BoxBottom();
        return 0;
    }

    private static int CmdTool(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("tool requires a tool name. Try: rootcli tools");
        }

        var name = args[0];
        var repo = GetOption(args, "-r", "--repo") ?? Environment.GetEnvironmentVariable("ROOTCLI_REPO");
        if (string.IsNullOrWhiteSpace(repo))
        {
            return Fail("tool requires -r/--repo.");
        }

        var workspace = new WorkspaceService();
        workspace.SetRoot(repo);
        var map = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        for (var i = 1; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal) || a is "-r" or "--repo" or "-m" or "--model" or "--yes" or "--no-mcp")
            {
                continue;
            }

            var key = a.Substring(2);
            string value;
            var eq = key.IndexOf('=');
            if (eq >= 0)
            {
                value = key.Substring(eq + 1);
                key = key.Substring(0, eq);
            }
            else if (i + 1 < args.Length && !args[i + 1].StartsWith('-'))
            {
                value = args[++i];
            }
            else
            {
                value = "true";
            }

            if (key is "repo" or "model" or "yes" or "json" or "max-steps" or "no-mcp")
            {
                continue;
            }

            map[key] = value;
        }

        var call = ToolProcessor.FromNative(name, map);
        var yes = HasFlag(args, "--yes");
        var startedMcp = false;
        try
        {
            if (call.Type == ToolType.McpCall)
            {
                McpServerManager.StartEnabled();
                startedMcp = true;
            }

            var gate = new ApprovalGate { PrefYes = yes };
            var output = ToolProcessor.Execute(call, workspace, gate.Approve);
            Console.WriteLine(output);
            return 0;
        }
        finally
        {
            if (startedMcp)
            {
                McpServerManager.Shutdown();
            }
        }
    }

    private static int CmdMcp(string[] args)
    {
        var json = HasFlag(args, "--json");
        McpServerStore.EnsureDefaultConfig();
        try
        {
            McpServerManager.StartEnabled(line =>
            {
                TermUi.Write("  · ", TermUi.Dim);
                TermUi.WriteLine(line, TermUi.Dim);
            });
            var runtimes = McpServerManager.GetRuntimes();
            var tools = McpServerManager.GetAvailableTools();
            if (json)
            {
                Console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
                {
                    config = McpServerStore.ConfigPath,
                    summary = McpServerManager.StartupSummary,
                    servers = runtimes.Select(r => new
                    {
                        id = r.Definition.Id,
                        enabled = r.Definition.Enabled,
                        connected = r.IsConnected,
                        command = r.ResolvedCommand,
                        error = r.LastError,
                        tools = r.Tools.Select(t => t.Name).ToArray()
                    }),
                    tools = tools.Select(t => AgentToolRegistry.BuildNativeToolKey(t.ServerId, t.Name)).ToArray()
                }));
                return 0;
            }

            TermUi.BrandHeader("mcp");
            TermUi.KeyValue("config", McpServerStore.ConfigPath, TermUi.Dim);
            TermUi.KeyValue("summary", McpServerManager.StartupSummary, TermUi.Mcp);
            Console.WriteLine();
            if (runtimes.Count == 0)
            {
                TermUi.StatusDot(false, "No enabled MCP servers. Edit the config to enable servers.");
                return 1;
            }

            foreach (var runtime in runtimes)
            {
                TermUi.StatusDot(runtime.IsConnected, runtime.Definition.Id + (runtime.IsConnected ? "  connected" : "  offline"));
                if (!string.IsNullOrWhiteSpace(runtime.ResolvedCommand))
                {
                    TermUi.Hint(runtime.ResolvedCommand);
                }

                if (!runtime.IsConnected && !string.IsNullOrWhiteSpace(runtime.LastError))
                {
                    TermUi.Error(runtime.LastError);
                }

                foreach (var tool in runtime.Tools.Take(30))
                {
                    TermUi.Write("      ", TermUi.Dim);
                    TermUi.Write("mcp__", TermUi.Mcp);
                    TermUi.Write(tool.ServerId, TermUi.Dim);
                    TermUi.Write("__", TermUi.Dim);
                    TermUi.WriteLine(tool.Name, TermUi.BrandStrong);
                }

                if (runtime.Tools.Count > 30)
                {
                    TermUi.Hint("… +" + (runtime.Tools.Count - 30) + " more");
                }

                Console.WriteLine();
            }

            return runtimes.Any(r => r.IsConnected) ? 0 : 1;
        }
        finally
        {
            McpServerManager.Shutdown();
        }
    }

    private static int CmdOllama(string[] args)
    {
        if (args.Length == 0)
        {
            return Fail("usage: rootcli ollama login|logout");
        }

        var sub = args[0].Trim().ToLowerInvariant();
        return sub switch
        {
            "login" or "signin" => CmdOllamaAuth(args.Skip(1).ToArray(), signIn: true),
            "logout" or "signout" => CmdOllamaAuth(args.Skip(1).ToArray(), signIn: false),
            _ => Fail("unknown ollama subcommand. Use: login | logout")
        };
    }

    private static int CmdOllamaAuth(string[] _, bool signIn)
    {
        TermUi.WriteLine(signIn
            ? "  Opening Ollama sign-in (browser) for cloud models…"
            : "  Signing out of Ollama…", TermUi.Brand);
        return signIn ? OllamaCli.SignIn() : OllamaCli.SignOut();
    }

    private static int CmdHere(string[] args)
    {
        var pathArg = args.FirstOrDefault(a => !a.StartsWith("-", StringComparison.Ordinal));
        var path = ResolveRepoPath(string.IsNullOrWhiteSpace(pathArg) ? "." : pathArg);
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return Fail("Folder not found: " + (pathArg ?? "."));
        }

        RepoStore.Remember(path);
        return new InteractiveShell(path).Run();
    }

    private static string? ResolveRepoPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        path = path.Trim().Trim('"');
        if (path is "." or "./" or ".\\")
        {
            return Path.GetFullPath(Environment.CurrentDirectory);
        }

        if (path == "~")
        {
            return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        }

        if (path.StartsWith("~/") || path.StartsWith("~" + Path.DirectorySeparatorChar))
        {
            path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                path[2..]);
        }

        try
        {
            return Path.GetFullPath(path);
        }
        catch
        {
            return path;
        }
    }

    private static string ResolveModel(string? requested)
    {
        if (!string.IsNullOrWhiteSpace(requested))
        {
            return requested.Trim();
        }

        var env = Environment.GetEnvironmentVariable("ROOTCLI_MODEL");
        if (!string.IsNullOrWhiteSpace(env))
        {
            return env.Trim();
        }

        var models = new OllamaClient().GetModels();
        if (models.Count == 0)
        {
            throw new InvalidOperationException("No Ollama models found. Pull one or pass -m.");
        }

        return models[0];
    }

    private static void PrintHelp()
    {
        TermUi.BrandHeader("Root CLI 1.0.0 (Open Beta)");
        TermUi.Section("usage");
        TermUi.Bullet("rootcli                 interactive menu (no args)", TermUi.Brand);
        TermUi.Bullet("rootcli here            open menu using this folder (VS Code / cwd)", TermUi.Brand);
        TermUi.Bullet("rootcli .               same as here", TermUi.Brand);
        TermUi.Bullet("rootcli login           ollama signin (cloud models)", TermUi.Ok);
        TermUi.Bullet("rootcli logout          ollama signout", TermUi.Dim);
        TermUi.Bullet("rootcli ollama login    same as login", TermUi.Ok);
        TermUi.Bullet("rootcli menu            same interactive guide", TermUi.Brand);
        TermUi.Bullet("rootcli models [--json]", TermUi.Tool);
        TermUi.Bullet("rootcli ask   \"prompt\" [-r repo|.] [-m model] [--yes] [--no-mcp]", TermUi.Ask);
        TermUi.Bullet("rootcli plan  \"prompt\" [-r repo|.] [-m model] [--yes] [--no-mcp]", TermUi.Plan);
        TermUi.Bullet("rootcli agent \"prompt\" [-r repo|.] [-m model] [--yes] [--no-mcp]", TermUi.Agent);
        TermUi.Bullet("rootcli tools [--json] [--no-mcp]", TermUi.Tool);
        TermUi.Bullet("rootcli tool <name> -r repo [--arg value] [--yes]", TermUi.Tool);
        TermUi.Bullet("rootcli mcp [--json]", TermUi.Mcp);

        TermUi.Section("modes");
        Console.Write("  ");
        TermUi.Write("ASK  ", TermUi.Ask);
        TermUi.WriteLine("read-only Q&A", ConsoleColor.Gray);
        Console.Write("  ");
        TermUi.Write("PLAN ", TermUi.Plan);
        TermUi.WriteLine("read-only numbered plan", ConsoleColor.Gray);
        Console.Write("  ");
        TermUi.Write("AGENT", TermUi.Agent);
        TermUi.WriteLine(" edits + shell + MCP", ConsoleColor.Gray);

        TermUi.Section("paths / env");
        TermUi.KeyValue("config", "~/.local/share/root-cli/");
        TermUi.KeyValue("mcp cfg", "~/.local/share/root-cli/mcp-servers.json");
        TermUi.KeyValue("OLLAMA_HOST", "http://localhost:11434");
        TermUi.KeyValue("ROOTCLI_MODEL", "default model");
        TermUi.KeyValue("ROOTCLI_REPO", "default repository path");
        TermUi.Hint("VS Code terminal:  rootcli here     (uses this folder)");
        TermUi.Hint("install: ./scripts/install-rootcli-alias.sh   then: rootcli here");
    }

    private static bool IsHelp(string value) =>
        value is "-h" or "--help" or "help" or "/?";

    private static int Fail(string message)
    {
        TermUi.Error(message);
        return 1;
    }

    private static bool HasFlag(string[] args, string flag) =>
        args.Any(a => string.Equals(a, flag, StringComparison.OrdinalIgnoreCase));

    private static string? GetOption(string[] args, params string[] names)
    {
        for (var i = 0; i < args.Length; i++)
        {
            foreach (var name in names)
            {
                if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                {
                    return i + 1 < args.Length ? args[i + 1] : null;
                }

                var prefix = name + "=";
                if (args[i].StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    return args[i].Substring(prefix.Length);
                }
            }
        }

        return null;
    }

    private static string TakePositional(string[] args)
    {
        var parts = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (a.StartsWith('-'))
            {
                if (a is "-r" or "--repo" or "-m" or "--model" or "--max-steps")
                {
                    i++;
                }

                continue;
            }

            parts.Add(a);
        }

        return string.Join(" ", parts);
    }

    private static int ParseInt(string? value, int fallback) =>
        int.TryParse(value, out var n) && n > 0 ? n : fallback;
}
