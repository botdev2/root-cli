using System.Reflection;
using RootCli.Core.Agent;
using RootCli.Core.Chat;
using RootCli.Core.Ollama;

namespace RootCli;

internal sealed class InteractiveShell
{
    private string? repo;
    private string? model;
    private bool mcpEnabled = true;
    private readonly ApprovalGate approvals = new();
    private int selected;
    private ChatSession? activeChat;

    public InteractiveShell(string? initialRepo = null)
    {
        if (!string.IsNullOrWhiteSpace(initialRepo) && Directory.Exists(initialRepo))
        {
            repo = Path.GetFullPath(initialRepo);
            RepoStore.Remember(repo);
        }
    }

    private static readonly (string Key, string Title, string Hint)[] Items =
    {
        ("1", "Recent chats", "Continue one of your latest saved sessions"),
        ("2", "Browse all chats", "Full list of saved chats"),
        ("3", "Ask", "Read-only Q&A — inspect the repo, no edits"),
        ("4", "Plan", "Read-only plan — numbered steps, then stop"),
        ("5", "Agent", "Full agent — edit files, run commands, MCP"),
        ("6", "Repository", "Browse, create, or pick a recent project folder"),
        ("7", "Model", "Pick an Ollama model"),
        ("8", "MCP status", "Probe codebase-memory and other servers"),
        ("9", "Tools", "List built-in + MCP tools"),
        ("0", "Quit", "Exit RootCli"),
    };

    public int Run()
    {
        Console.OutputEncoding = System.Text.Encoding.UTF8;
        Console.Title = "RootCli";
        if (string.IsNullOrWhiteSpace(repo))
        {
            repo = Environment.GetEnvironmentVariable("ROOTCLI_REPO");
        }

        if (string.IsNullOrWhiteSpace(repo))
        {
            repo = RepoStore.LoadRecent(1).FirstOrDefault();
        }

        model = Environment.GetEnvironmentVariable("ROOTCLI_MODEL");

        while (true)
        {
            DrawHome();
            var key = Console.ReadKey(intercept: true);

            if (key.Key == ConsoleKey.UpArrow || key.Key == ConsoleKey.K)
            {
                selected = (selected + Items.Length - 1) % Items.Length;
                continue;
            }

            if (key.Key == ConsoleKey.DownArrow || key.Key == ConsoleKey.J)
            {
                selected = (selected + 1) % Items.Length;
                continue;
            }

            if (key.Key == ConsoleKey.Enter || key.Key == ConsoleKey.Spacebar)
            {
                if (!Dispatch(selected))
                {
                    return 0;
                }

                continue;
            }

            if (key.Key == ConsoleKey.Escape || key.Key == ConsoleKey.Q)
            {
                DrawFarewell();
                return 0;
            }

            var ch = key.KeyChar;
            if (ch is >= '0' and <= '9')
            {
                var index = Items.ToList().FindIndex(i => i.Key == ch.ToString());
                if (index >= 0)
                {
                    selected = index;
                    if (!Dispatch(index))
                    {
                        return 0;
                    }
                }
            }
        }
    }

    private bool Dispatch(int index)
    {
        return Items[index].Key switch
        {
            "1" => OpenRecentChats(),
            "2" => OpenChatsMenu(),
            "3" => StartChat(AgentChatMode.Ask),
            "4" => StartChat(AgentChatMode.Plan),
            "5" => StartChat(AgentChatMode.Agent),
            "6" => PickRepo(),
            "7" => PickModel(),
            "8" => ShowMcp(),
            "9" => ShowTools(),
            "0" => Quit(),
            _ => true
        };
    }

    private void DrawHome()
    {
        Console.CursorVisible = false;
        try
        {
            Console.Clear();
        }
        catch
        {

        }

        var width = Math.Clamp(SafeWindowWidth(), 60, 100);
        WriteBanner(width);
        WriteStatus(width);
        WriteRecentChatsPreview(width);
        Console.WriteLine();
        WriteMenu(width);
        Console.WriteLine();
        Dim("  ↑↓ / j k  move    Enter  select    0–9  jump    Esc  quit");
        Console.WriteLine();
    }

    private void WriteBanner(int width)
    {
        var version = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "0.1.0";
        TermUi.WriteLogo();
        AccentLine(new string('═', width));
        Console.Write("  ");
        ColorWrite("ROOT", ConsoleColor.Cyan);
        ColorWrite("CLI", ConsoleColor.White);
        Dim("  ·  Ollama agent  ·  v" + version);
        Console.WriteLine();
        Dim("  Root CLI 1.0.0 (Open Beta) — ask, plan, or act inside a repository.");
        Console.WriteLine();
        AccentLine(new string('─', width));
    }

    private void WriteStatus(int width)
    {
        Console.WriteLine();
        var ollama = ProbeOllama();
        var mcp = mcpEnabled ? "on" : "off";
        StatusRow("Ollama", ollama.Ok ? ollama.Text : ollama.Text, ollama.Ok);
        StatusRow("Model ", string.IsNullOrWhiteSpace(model) ? "(auto — first local model)" : model!, !string.IsNullOrWhiteSpace(model) || ollama.Ok);
        StatusRow("Repo  ", string.IsNullOrWhiteSpace(repo) ? "(not set — pick Repository or create)" : repo!, !string.IsNullOrWhiteSpace(repo));
        StatusRow("Chat  ", activeChat == null ? "(none — Ask/Plan/Agent starts one)" : activeChat.Title + "  [" + activeChat.Mode + "]", activeChat != null);
        var approveLabel = approvals.AlwaysYes ? "always yes"
            : approvals.PrefYes ? "non-high auto" : "ask y/n/ay";
        StatusRow("MCP   ", mcp + "   approve " + approveLabel, true);
        AccentLine(new string('─', width));
    }

    private void WriteRecentChatsPreview(int width)
    {
        var recent = ChatSessionStore.List(5);
        Console.WriteLine();
        ColorWriteLine("  Recent chats", ConsoleColor.White);
        if (recent.Count == 0)
        {
            Dim("  (none yet — Ask / Plan / Agent will create one)");
            AccentLine(new string('─', width));
            return;
        }

        foreach (var s in recent)
        {
            var mark = activeChat != null && s.Id == activeChat.Id ? "●" : "○";
            Console.Write($"  {mark}  ");
            ColorWrite(s.Title, ConsoleColor.Gray);
            var meta = $"  · {s.Mode} · {ShortPath(s.Repo)} · {s.UpdatedUtc.ToLocalTime():g}";
            if (("  " + s.Title + meta).Length > width)
            {
                meta = meta.Length > 24 ? meta.Substring(0, Math.Min(meta.Length, width - s.Title.Length - 6)) + "…" : meta;
            }

            Dim(meta);
        }

        AccentLine(new string('─', width));
    }

    private void WriteMenu(int width)
    {
        ColorWriteLine("  What do you want to do?", ConsoleColor.White);
        Console.WriteLine();
        for (var i = 0; i < Items.Length; i++)
        {
            var item = Items[i];
            var active = i == selected;
            var marker = active ? "▸" : " ";
            var titlePad = 18;
            var label = $"  {marker} [{item.Key}]  {item.Title.PadRight(titlePad)}  {item.Hint}";
            if (label.Length > width)
            {
                label = label.Substring(0, width - 1) + "…";
            }

            if (active)
            {
                var prev = Console.BackgroundColor;
                Console.BackgroundColor = ConsoleColor.DarkCyan;
                Console.ForegroundColor = ConsoleColor.Black;
                Console.WriteLine(label.PadRight(width));
                Console.BackgroundColor = prev;
                Console.ResetColor();
            }
            else
            {
                Console.Write($"  {marker} ");
                ColorWrite($"[{item.Key}]", ConsoleColor.DarkCyan);
                ColorWrite($"  {item.Title.PadRight(titlePad)}  ", ConsoleColor.Gray);
                ColorWriteLine(item.Hint, ConsoleColor.DarkGray);
            }
        }
    }

    private bool StartChat(AgentChatMode mode)
    {
        if (string.IsNullOrWhiteSpace(repo))
        {
            var picked = RepoPicker.Choose(repo);
            if (string.IsNullOrWhiteSpace(picked))
            {
                return true;
            }

            repo = picked;
        }

        string resolvedModel;
        try
        {
            resolvedModel = ResolveModelOrFirst(model);
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
            Pause();
            return true;
        }

        activeChat = ChatSessionStore.Create(repo!, resolvedModel, mode);
        model = resolvedModel;
        return ChatLoop(activeChat);
    }

    private bool OpenRecentChats() => OpenChatsMenu(max: 8, title: "Recent chats");

    private bool OpenChatsMenu(int max = 40, string title = "All saved chats")
    {
        while (true)
        {
            Console.CursorVisible = true;
            Console.Clear();
            TermUi.WriteLogo(compact: true);
            ColorWriteLine("  " + title, ConsoleColor.Cyan);
            Console.WriteLine();
            TermUi.Hint("Stored in ~/.local/share/root-cli/sessions/");
            Console.WriteLine();

            var sessions = ChatSessionStore.List(max);
            TermUi.WriteLine("  [n]  New chat (Ask)", TermUi.Brand);
            if (max < 40)
            {
                TermUi.WriteLine("  [a]  Browse all chats", TermUi.Brand);
            }

            TermUi.WriteLine("  [0]  Back", TermUi.Dim);
            Console.WriteLine();

            for (var i = 0; i < sessions.Count; i++)
            {
                var s = sessions[i];
                var mark = activeChat != null && s.Id == activeChat.Id ? "●" : "○";
                TermUi.Write("  " + mark + " ", TermUi.Brand);
                TermUi.Write("[" + (i + 1) + "]  ", TermUi.Brand);
                TermUi.Write(s.Title, TermUi.BrandStrong);
                TermUi.WriteLine(
                    "  · " + s.Mode + " · " + ShortPath(s.Repo) + " · " + s.UpdatedUtc.ToLocalTime().ToString("g"),
                    TermUi.Dim);
            }

            if (sessions.Count == 0)
            {
                TermUi.Hint("No chats yet — start with Ask / Plan / Agent.");
            }

            Console.WriteLine();
            Console.Write("  chat> ");
            var input = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(input) || input == "0" || input == "b")
            {
                return true;
            }

            if (input == "n")
            {
                return StartChat(AgentChatMode.Ask);
            }

            if (input == "a" && max < 40)
            {
                return OpenChatsMenu();
            }

            if (int.TryParse(input, out var n) && n >= 1 && n <= sessions.Count)
            {
                activeChat = sessions[n - 1];
                repo = activeChat.Repo;
                model = activeChat.Model;
                return ChatLoop(activeChat);
            }

            Warn("Unknown choice.");
            Pause();
        }
    }

    private bool ChatLoop(ChatSession session)
    {

        var answerOnScreen = false;
        DrawChatFrame(session, recentOnly: true);

        while (true)
        {
            Console.CursorVisible = true;
            Console.WriteLine();
            TermUi.Hint("Type a message.  g=agent  p=plan  q=ask  /model  /chats  empty = back");
            Console.Write("  " + session.Mode + "> ");
            var prompt = Console.ReadLine();
            if (string.IsNullOrWhiteSpace(prompt))
            {
                ChatSessionStore.Save(session);
                return true;
            }

            prompt = prompt.Trim();
            if (TrySwitchChatMode(session, prompt, out var switchedMode))
            {
                ChatSessionStore.Save(session);
                Console.Write("  mode → ");
                ColorWriteLine(switchedMode.ToUpperInvariant(), TermUi.ModeColor(switchedMode));
                if (!answerOnScreen)
                {
                    DrawChatFrame(session, recentOnly: true);
                }

                continue;
            }

            if (prompt.StartsWith("/model", StringComparison.OrdinalIgnoreCase))
            {
                var picked = PickModelInline(session.Model);
                if (!string.IsNullOrWhiteSpace(picked))
                {
                    session.Model = picked;
                    model = picked;
                    ChatSessionStore.Save(session);
                    if (!answerOnScreen)
                    {
                        DrawChatFrame(session, recentOnly: true);
                    }
                }

                continue;
            }

            if (prompt.Equals("/chats", StringComparison.OrdinalIgnoreCase))
            {
                ChatSessionStore.Save(session);
                return OpenChatsMenu();
            }

            if (string.IsNullOrWhiteSpace(session.Repo))
            {
                var picked = RepoPicker.Choose(repo);
                if (string.IsNullOrWhiteSpace(picked))
                {
                    if (!answerOnScreen)
                    {
                        DrawChatFrame(session, recentOnly: true);
                    }

                    continue;
                }

                session.Repo = picked;
                repo = picked;
            }

            string runModel;
            try
            {
                runModel = ResolveModelOrFirst(session.Model);
                session.Model = runModel;
                model = runModel;
            }
            catch (Exception ex)
            {
                Warn(ex.Message);
                Pause();
                if (!answerOnScreen)
                {
                    DrawChatFrame(session, recentOnly: true);
                }

                continue;
            }

            DrawChatFrame(session, recentOnly: true);
            answerOnScreen = false;
            Console.WriteLine();
            ColorWrite("  you  ", ConsoleColor.Blue);
            ColorWriteLine(prompt, ConsoleColor.Gray);
            Console.WriteLine();
            AccentLine(new string('─', Math.Clamp(SafeWindowWidth(), 40, 80)));
            Console.WriteLine();

            try
            {
                var thinking = new ThinkingPane();
                var history = session.Turns.ToList();
                var result = Program.RunChatTurn(
                    session.ChatMode,
                    prompt,
                    session.Repo,
                    runModel,
                    approvals,
                    mcpEnabled,
                    12,
                    history,
                    thinking);

                thinking.ReplaceWithAnswer();
                if (!string.IsNullOrWhiteSpace(result.Answer))
                {
                    TermUi.WriteAnswer(result.Answer);
                }

                TermUi.WriteRunStats(result.Stats);

                session.Turns.Add(new ChatTurn { Role = "user", Content = prompt, TimeUtc = DateTime.UtcNow });
                session.Turns.Add(new ChatTurn
                {
                    Role = "assistant",
                    Content = result.Answer ?? "",
                    TimeUtc = DateTime.UtcNow,
                    CommandsRan = result.Stats.CommandsRan,
                    FilesEdited = result.Stats.FilesEdited,
                    LinesAdded = result.Stats.LinesAdded,
                    LinesRemoved = result.Stats.LinesRemoved
                });
                ChatSessionStore.Save(session);
                activeChat = session;
                answerOnScreen = true;

            }
            catch (Exception ex)
            {
                Warn("error: " + ex.Message);
                answerOnScreen = false;
            }
        }
    }

    private static bool TrySwitchChatMode(ChatSession session, string prompt, out string modeStorage)
    {
        modeStorage = session.Mode;
        AgentChatMode? mode = prompt.ToLowerInvariant() switch
        {
            "g" or "/g" or "/agent" => AgentChatMode.Agent,
            "p" or "/p" or "/plan" => AgentChatMode.Plan,
            "q" or "/q" or "/ask" => AgentChatMode.Ask,
            _ => null
        };

        if (mode == null && prompt.StartsWith("/mode", StringComparison.OrdinalIgnoreCase))
        {
            var part = prompt.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
            if (part.Length >= 2)
            {
                mode = AgentModePolicy.Parse(part[1]);
            }
        }

        if (mode == null)
        {
            return false;
        }

        modeStorage = AgentModePolicy.ToStorage(mode.Value);
        session.Mode = modeStorage;
        return true;
    }

    private void DrawChatFrame(ChatSession session, bool recentOnly)
    {
        try
        {
            Console.Clear();
        }
        catch
        {

        }

        WriteModeHeader(session.ChatMode);
        ColorWriteLine("  " + session.Title, ConsoleColor.White);
        Console.WriteLine();
        TermUi.KeyValue("chat", session.Id, TermUi.Dim);
        TermUi.KeyValue("mode", session.Mode.ToUpperInvariant(), TermUi.ModeColor(session.Mode));
        TermUi.KeyValue("model", string.IsNullOrWhiteSpace(session.Model) ? "(auto)" : session.Model, TermUi.BrandStrong);
        TermUi.KeyValue("repo", session.Repo, TermUi.Tool);
        TermUi.KeyValue("turns", session.Turns.Count.ToString(), TermUi.Dim);
        Console.WriteLine();

        if (recentOnly)
        {
            if (session.Turns.Count > 0)
            {
                TermUi.WriteLine("  Previous:", TermUi.Dim);
                foreach (var turn in session.Turns.TakeLast(4))
                {
                    var preview = RootCli.Core.Agent.PlainTextFormatter.ToTerminalPlain(turn.Content)
                        .Replace("\r", " ")
                        .Replace("\n", " ")
                        .Trim();
                    if (preview.Length > 70)
                    {
                        preview = preview[..69] + "…";
                    }

                    TermUi.Write(turn.Role == "user" ? "  you  " : "  ai   ", turn.Role == "user" ? TermUi.Ask : TermUi.Ok);
                    TermUi.WriteLine(preview, ConsoleColor.Gray);
                }

                Console.WriteLine();
            }

            TermUi.WriteLine("  " + ChatSessionStore.SessionFilePath(session.Id), TermUi.Dim);
            Console.WriteLine();
        }
    }

    private string? PickModelInline(string? current)
    {
        Console.CursorVisible = true;
        Console.Clear();
        ColorWriteLine("Switch model", ConsoleColor.Cyan);
        Console.WriteLine();
        List<string> models;
        try
        {
            models = new OllamaClient().GetModels();
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
            Pause();
            return null;
        }

        if (models.Count == 0)
        {
            Warn("No models. ollama pull <name>");
            Pause();
            return null;
        }

        for (var i = 0; i < models.Count; i++)
        {
            var mark = string.Equals(models[i], current, StringComparison.OrdinalIgnoreCase) ? "●" : "○";
            Console.WriteLine($"  {mark} {i + 1,2}.  {models[i]}");
        }

        Console.WriteLine();
        Console.Write("  number (blank = keep)> ");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return current;
        }

        if (int.TryParse(input, out var n) && n >= 1 && n <= models.Count)
        {
            return models[n - 1];
        }

        return input;
    }

    private static string ResolveModelOrFirst(string? requested)
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
            throw new InvalidOperationException("No Ollama models found. Pull one or set Model in the menu.");
        }

        return models[0];
    }

    private static string ShortPath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "(no repo)";
        }

        return path.Length <= 40 ? path : "…" + path[^37..];
    }

    private bool PickRepo()
    {
        var picked = RepoPicker.Choose(repo);
        if (!string.IsNullOrWhiteSpace(picked))
        {
            repo = picked;
            Console.CursorVisible = true;
            Console.Clear();
            ColorWriteLine("Repository ready", ConsoleColor.DarkGreen);
            Console.WriteLine();
            ColorWriteLine("  " + repo, ConsoleColor.Cyan);
            Console.WriteLine();
            Dim("  You can Ask / Plan / Agent against this folder now.");
            Pause();
        }

        return true;
    }

    private bool PickModel()
    {
        Console.CursorVisible = true;
        Console.Clear();
        ColorWriteLine("Set Ollama model", ConsoleColor.Cyan);
        Console.WriteLine();
        List<string> models;
        try
        {
            models = new OllamaClient().GetModels();
        }
        catch (Exception ex)
        {
            Warn("Could not reach Ollama: " + ex.Message);
            Pause();
            return true;
        }

        if (models.Count == 0)
        {
            Warn("No models found. Run: ollama pull <name>");
            Pause();
            return true;
        }

        for (var i = 0; i < models.Count; i++)
        {
            var mark = string.Equals(models[i], model, StringComparison.OrdinalIgnoreCase) ? "●" : "○";
            Console.WriteLine($"  {mark} {i + 1,2}.  {models[i]}");
        }

        Console.WriteLine();
        Console.Write("  number (or name, blank = keep)> ");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(input))
        {
            return true;
        }

        if (int.TryParse(input, out var n) && n >= 1 && n <= models.Count)
        {
            model = models[n - 1];
        }
        else
        {
            model = input;
        }

        ColorWriteLine("Model set to " + model, ConsoleColor.DarkGreen);
        Pause();
        return true;
    }

    private bool ShowMcp()
    {
        Console.CursorVisible = true;
        Console.Clear();
        ColorWriteLine("MCP status", ConsoleColor.Cyan);
        Console.WriteLine();
        try
        {
            Program.CmdMcpPublic(Array.Empty<string>());
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
        }

        Console.WriteLine();
        Console.Write("  Toggle MCP for menu runs? [Y/n] (currently " + (mcpEnabled ? "on" : "off") + ")> ");
        var ans = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(ans))
        {
            mcpEnabled = !ans.StartsWith("n", StringComparison.OrdinalIgnoreCase);
        }

        Console.Write("  Auto-approve non-high-risk tools? [y/N] (currently " + (approvals.PrefYes ? "on" : "off") + ")> ");
        ans = Console.ReadLine()?.Trim();
        if (!string.IsNullOrWhiteSpace(ans))
        {
            approvals.PrefYes = ans.StartsWith("y", StringComparison.OrdinalIgnoreCase);
            if (!approvals.PrefYes)
            {
                approvals.AlwaysYes = false;
            }
        }

        Pause();
        return true;
    }

    private bool ShowModels()
    {
        Console.CursorVisible = true;
        Console.Clear();
        try
        {
            Program.CmdModelsPublic(Array.Empty<string>());
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
        }

        Pause();
        return true;
    }

    private bool ShowTools()
    {
        Console.CursorVisible = true;
        Console.Clear();
        try
        {
            var args = mcpEnabled ? Array.Empty<string>() : new[] { "--no-mcp" };
            Program.CmdToolsPublic(args);
        }
        catch (Exception ex)
        {
            Warn(ex.Message);
        }

        Pause();
        return true;
    }

    private bool ShowGuide()
    {
        Console.CursorVisible = true;
        Console.Clear();
        ColorWriteLine("RootCli guide", ConsoleColor.Cyan);
        Console.WriteLine();
        Console.WriteLine("  RootCli is a local coding agent. It talks to Ollama on this machine");
        Console.WriteLine("  and can read (and in Agent mode, change) files inside one repository.");
        Console.WriteLine();
        ColorWrite("  Ask", ConsoleColor.Blue);
        Console.WriteLine("   — questions only. Safe. No edits.");
        ColorWrite("  Plan", ConsoleColor.DarkYellow);
        Console.WriteLine("  — explore, then a numbered plan. No edits.");
        ColorWrite("  Agent", ConsoleColor.DarkGreen);
        Console.WriteLine(" — do the work: patch files, run shell, use MCP.");
        Console.WriteLine();
        Console.WriteLine("  Setup checklist");
        Console.WriteLine("  1. Ollama running (http://localhost:11434)");
        Console.WriteLine("  2. At least one model pulled  →  ollama pull <name>");
        Console.WriteLine("  3. A repository folder selected (menu → Repository)");
        Console.WriteLine("  4. Optional MCP: codebase-memory-mcp on PATH");
        Console.WriteLine();
        Console.WriteLine("  From a terminal you can also call commands directly:");
        Dim("    rootcli ask \"What is this?\" -r /path/to/repo");
        Console.WriteLine();
        Dim("    rootcli plan \"…\" -r /path/to/repo");
        Console.WriteLine();
        Dim("    rootcli agent \"…\" -r /path/to/repo");
        Console.WriteLine();
        Console.WriteLine("  Config lives under ~/.local/share/root-cli/");
        Pause();
        return true;
    }

    private bool Quit()
    {
        DrawFarewell();
        return false;
    }

    private void DrawFarewell()
    {
        Console.CursorVisible = true;
        Console.WriteLine();
        Dim("  Bye — run rootcli anytime, or pass a command for scripting.");
        Console.WriteLine();
    }

    private void WriteModeHeader(AgentChatMode mode)
    {
        var (label, color, blurb) = mode switch
        {
            AgentChatMode.Ask => ("ASK", ConsoleColor.Blue, "Read-only answers from the repo"),
            AgentChatMode.Plan => ("PLAN", ConsoleColor.DarkYellow, "Read-only implementation plan"),
            _ => ("AGENT", ConsoleColor.DarkGreen, "Edit, shell, MCP — with approval policy")
        };
        ColorWrite("  " + label, color);
        Dim("  ·  " + blurb);
        Console.WriteLine();
        Console.WriteLine();
    }

    private static (bool Ok, string Text) ProbeOllama()
    {
        try
        {
            var client = new OllamaClient();
            var models = client.GetModels();
            if (models.Count == 0)
            {
                return (false, client.BaseUrl + "  (reachable, no models — ollama pull …)");
            }

            return (true, client.BaseUrl + "  (" + models.Count + " model" + (models.Count == 1 ? "" : "s") + ")");
        }
        catch
        {
            return (false, "unreachable — start Ollama first");
        }
    }

    private static void StatusRow(string label, string value, bool ok)
    {
        Console.Write("  ");
        Dim(label.PadRight(6));
        Console.Write(" ");
        ColorWrite(ok ? "●" : "○", ok ? ConsoleColor.DarkGreen : ConsoleColor.DarkYellow);
        Console.Write(" ");
        Console.WriteLine(value);
    }

    private static void Pause()
    {
        Console.WriteLine();
        Dim("  Press any key to return to the menu…");
        Console.ReadKey(intercept: true);
    }

    private static void Warn(string message)
    {
        ColorWriteLine("  " + message, ConsoleColor.DarkYellow);
    }

    private static void AccentLine(string text)
    {
        ColorWriteLine(text, ConsoleColor.DarkCyan);
    }

    private static void Dim(string text)
    {
        ColorWrite(text, ConsoleColor.DarkGray);
        Console.WriteLine();
    }

    private static void ColorWrite(string text, ConsoleColor color)
    {
        var prev = Console.ForegroundColor;
        Console.ForegroundColor = color;
        Console.Write(text);
        Console.ForegroundColor = prev;
    }

    private static void ColorWriteLine(string text, ConsoleColor color)
    {
        ColorWrite(text, color);
        Console.WriteLine();
    }

    private static int SafeWindowWidth()
    {
        try
        {
            return Console.WindowWidth;
        }
        catch
        {
            return 80;
        }
    }
}
