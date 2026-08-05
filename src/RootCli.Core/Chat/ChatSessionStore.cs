using System.Text.Json;
using RootCli.Core.Agent;

namespace RootCli.Core.Chat;

public sealed class ChatTurn
{
    public string Role { get; set; } = "";
    public string Content { get; set; } = "";
    public DateTime TimeUtc { get; set; } = DateTime.UtcNow;
    public int CommandsRan { get; set; }
    public int FilesEdited { get; set; }
    public int LinesAdded { get; set; }
    public int LinesRemoved { get; set; }
}

public sealed class ChatSession
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N")[..12];
    public string Title { get; set; } = "New chat";
    public string Repo { get; set; } = "";
    public string Model { get; set; } = "";
    public string Mode { get; set; } = "ask";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
    public List<ChatTurn> Turns { get; set; } = new();

    public AgentChatMode ChatMode => AgentModePolicy.Parse(Mode);
}

public static class ChatSessionStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string SessionsRoot =>
        Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "root-cli",
            "sessions");

    public static List<ChatSession> List(int max = 40)
    {
        Directory.CreateDirectory(SessionsRoot);
        var result = new List<ChatSession>();
        foreach (var file in Directory.EnumerateFiles(SessionsRoot, "*.json"))
        {
            try
            {
                var session = JsonSerializer.Deserialize<ChatSession>(File.ReadAllText(file), JsonOptions);
                if (session != null)
                {
                    result.Add(session);
                }
            }
            catch
            {

            }
        }

        return result
            .OrderByDescending(s => s.UpdatedUtc)
            .Take(max)
            .ToList();
    }

    public static ChatSession? Load(string id)
    {
        var path = GetPath(id);
        if (!File.Exists(path))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<ChatSession>(File.ReadAllText(path), JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(ChatSession session)
    {
        Directory.CreateDirectory(SessionsRoot);
        session.UpdatedUtc = DateTime.UtcNow;
        if (string.IsNullOrWhiteSpace(session.Title) || session.Title == "New chat")
        {
            var firstUser = session.Turns.FirstOrDefault(t => t.Role == "user")?.Content;
            if (!string.IsNullOrWhiteSpace(firstUser))
            {
                session.Title = Truncate(firstUser.Replace("\r", " ").Replace("\n", " ").Trim(), 48);
            }
        }

        File.WriteAllText(GetPath(session.Id), JsonSerializer.Serialize(session, JsonOptions));
    }

    public static ChatSession Create(string repo, string model, AgentChatMode mode)
    {
        var session = new ChatSession
        {
            Repo = repo ?? "",
            Model = model ?? "",
            Mode = AgentModePolicy.ToStorage(mode)
        };
        Save(session);
        return session;
    }

    public static bool Delete(string id)
    {
        var path = GetPath(id);
        if (!File.Exists(path))
        {
            return false;
        }

        File.Delete(path);
        return true;
    }

    public static string SessionFilePath(string id) => GetPath(id);

    private static string GetPath(string id) =>
        Path.Combine(SessionsRoot, id.Trim() + ".json");

    private static string Truncate(string text, int max) =>
        text.Length <= max ? text : text[..(max - 1)] + "…";
}
