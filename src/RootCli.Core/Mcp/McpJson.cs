using System.Text.Json;

namespace RootCli.Core.Mcp;

internal static class McpJson
{
    public static string Serialize(object value) =>
        JsonSerializer.Serialize(value);

    public static Dictionary<string, object?>? DeserializeObject(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return JsonElementToObject(doc.RootElement) as Dictionary<string, object?>;
    }

    public static object? JsonElementToObject(JsonElement el) =>
        el.ValueKind switch
        {
            JsonValueKind.Object => el.EnumerateObject()
                .ToDictionary(p => p.Name, p => JsonElementToObject(p.Value), StringComparer.OrdinalIgnoreCase),
            JsonValueKind.Array => el.EnumerateArray().Select(JsonElementToObject).ToList(),
            JsonValueKind.String => el.GetString(),
            JsonValueKind.Number => el.TryGetInt64(out var l) ? l : el.GetDouble(),
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Null => null,
            _ => el.ToString()
        };
}
