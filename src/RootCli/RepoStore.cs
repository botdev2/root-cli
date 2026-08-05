using System.Text.Json;

namespace RootCli;

internal static class RepoStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static string PathFile =>
        System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "root-cli",
            "recent-repos.json");

    public static List<string> LoadRecent(int max = 12)
    {
        try
        {
            if (!File.Exists(PathFile))
            {
                return new List<string>();
            }

            var list = JsonSerializer.Deserialize<List<string>>(File.ReadAllText(PathFile), JsonOptions)
                       ?? new List<string>();
            return list
                .Where(p => !string.IsNullOrWhiteSpace(p) && Directory.Exists(p))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(max)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    public static void Remember(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        path = System.IO.Path.GetFullPath(path);
        var list = LoadRecent(32);
        list.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        list.Insert(0, path);
        list = list.Take(16).ToList();

        var dir = System.IO.Path.GetDirectoryName(PathFile);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        File.WriteAllText(PathFile, JsonSerializer.Serialize(list, JsonOptions));
    }
}
