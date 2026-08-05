using System.Windows.Forms;
using RootCli.Core.Git;

namespace RootCli;

internal static class RepoPicker
{
    public static string? Choose(string? current)
    {
        while (true)
        {
            Console.CursorVisible = true;
            try { Console.Clear(); } catch {  }

            TermUi.WriteLogo(compact: true);
            TermUi.WriteLine("  Choose a repository", TermUi.BrandStrong);
            Console.WriteLine();
            TermUi.Hint("Current: " + (string.IsNullOrWhiteSpace(current) ? "(none)" : current!));
            Console.WriteLine();

            var options = new List<(string Key, string Label, Func<string?> Action)>();
            options.Add(("1", "Browse…  (Windows folder picker)", BrowseFolder));
            options.Add(("2", "Create new project folder", () => CreateNewProject()));
            options.Add(("3", "Use this folder  (" + Short(Environment.CurrentDirectory) + ")",
                () => Accept(Environment.CurrentDirectory)));

            var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            options.Add(("4", "Documents  (" + Short(docs) + ")", () => BrowseUnder(docs)));
            options.Add(("5", "Desktop    (" + Short(desktop) + ")", () => BrowseUnder(desktop)));

            var recent = RepoStore.LoadRecent();
            var keyNum = 6;
            foreach (var path in recent.Take(8))
            {
                var captured = path;
                var k = keyNum <= 9 ? keyNum.ToString() : ((char)('a' + (keyNum - 10))).ToString();
                options.Add((k, "Recent    " + Short(captured), () => Accept(captured)));
                keyNum++;
            }

            options.Add(("p", "Type / paste a path", TypePath));
            options.Add(("0", "Cancel", () => null));

            foreach (var opt in options)
            {
                TermUi.Write("  [", TermUi.Dim);
                TermUi.Write(opt.Key, TermUi.Brand);
                TermUi.Write("]  ", TermUi.Dim);
                TermUi.WriteLine(opt.Label, ConsoleColor.Gray);
            }

            Console.WriteLine();
            TermUi.Hint("Pick a number (or letter). 1 = easiest.");
            Console.Write("  choose> ");
            var input = Console.ReadLine()?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(input))
            {
                continue;
            }

            var hit = options.FirstOrDefault(o => o.Key == input);
            if (hit.Action == null)
            {
                TermUi.Error("Unknown choice.");
                PauseBrief();
                continue;
            }

            var result = hit.Action();
            if (result == null)
            {

                if (input == "0")
                {
                    return current;
                }

                continue;
            }

            RepoStore.Remember(result);
            return result;
        }
    }

    private static string? BrowseFolder()
    {
        try
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select a project folder for RootCli",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true,
                InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
            };

            var result = dialog.ShowDialog(new ForegroundOwner());
            if (result != DialogResult.OK || string.IsNullOrWhiteSpace(dialog.SelectedPath))
            {
                return null;
            }

            return Accept(dialog.SelectedPath);
        }
        catch (Exception ex)
        {
            TermUi.Error("Folder picker failed: " + ex.Message);
            TermUi.Hint("Falling back to typed path…");
            PauseBrief();
            return TypePath();
        }
    }

    private static string? BrowseUnder(string root)
    {
        if (!Directory.Exists(root))
        {
            TermUi.Error("Folder not found: " + root);
            PauseBrief();
            return null;
        }

        Console.Clear();
        TermUi.WriteLine("  Folders in " + root, TermUi.BrandStrong);
        Console.WriteLine();
        TermUi.WriteLine("  [.]  use this folder", TermUi.Brand);
        TermUi.WriteLine("  [..] parent / cancel browse", TermUi.Dim);

        List<string> dirs;
        try
        {
            dirs = Directory.GetDirectories(root)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrWhiteSpace(n) && n![0] != '.')
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .Take(40)
                .Select(n => n!)
                .ToList();
        }
        catch (Exception ex)
        {
            TermUi.Error(ex.Message);
            PauseBrief();
            return null;
        }

        for (var i = 0; i < dirs.Count; i++)
        {
            Console.Write("  ");
            TermUi.Write("[" + (i + 1) + "]  ", TermUi.Brand);
            TermUi.WriteLine(dirs[i], ConsoleColor.Gray);
        }

        Console.WriteLine();
        Console.Write("  folder> ");
        var input = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(input) || input == "..")
        {
            return null;
        }

        if (input == ".")
        {
            return Accept(root);
        }

        if (int.TryParse(input, out var n) && n >= 1 && n <= dirs.Count)
        {
            return BrowseUnder(Path.Combine(root, dirs[n - 1]));
        }

        var joined = Path.Combine(root, input.Trim('"'));
        if (Directory.Exists(joined))
        {
            return BrowseUnder(joined);
        }

        TermUi.Error("Not found.");
        PauseBrief();
        return null;
    }

    private static string? CreateNewProject()
    {
        Console.Clear();
        TermUi.WriteLine("  Create new project", TermUi.BrandStrong);
        Console.WriteLine();
        TermUi.Hint("Parent folder defaults to Documents. Name becomes the project folder.");
        Console.WriteLine();

        var docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        Console.Write("  parent [" + docs + "]> ");
        var parent = Console.ReadLine()?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(parent))
        {
            parent = docs;
        }

        if (!Directory.Exists(parent))
        {
            TermUi.Error("Parent folder not found: " + parent);
            PauseBrief();
            return null;
        }

        Console.Write("  project name> ");
        var name = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        foreach (var c in Path.GetInvalidFileNameChars())
        {
            name = name.Replace(c, '-');
        }

        var target = Path.Combine(parent, name);
        try
        {
            Directory.CreateDirectory(target);
        }
        catch (Exception ex)
        {
            TermUi.Error("Could not create folder: " + ex.Message);
            PauseBrief();
            return null;
        }

        Console.Write("  git init here? [Y/n]> ");
        var git = Console.ReadLine()?.Trim();
        if (string.IsNullOrWhiteSpace(git) || !git.StartsWith("n", StringComparison.OrdinalIgnoreCase))
        {
            var init = GitRepositoryService.InitRepository(target);
            TermUi.Hint(init.Split('\n').FirstOrDefault() ?? init);
        }

        File.WriteAllText(
            Path.Combine(target, "README.md"),
            "# " + name + Environment.NewLine + Environment.NewLine + "Created with RootCli." + Environment.NewLine);

        TermUi.WriteLine("Created " + target, TermUi.Ok);
        PauseBrief();
        return Accept(target);
    }

    private static string? TypePath()
    {
        Console.WriteLine();
        TermUi.Hint("Paste a path, or drag a folder onto this window, then Enter.");
        Console.Write("  path> ");
        var path = Console.ReadLine()?.Trim().Trim('"');
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        if (!Directory.Exists(path))
        {
            TermUi.Error("Folder not found: " + path);
            PauseBrief();
            return null;
        }

        return Accept(path);
    }

    private static string? Accept(string path)
    {
        if (!Directory.Exists(path))
        {
            TermUi.Error("Folder not found: " + path);
            PauseBrief();
            return null;
        }

        return Path.GetFullPath(path);
    }

    private static string Short(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return "";
        }

        return path.Length <= 52 ? path : "…" + path[^49..];
    }

    private static void PauseBrief()
    {
        TermUi.Hint("Press any key…");
        Console.ReadKey(intercept: true);
    }

    private sealed class ForegroundOwner : IWin32Window
    {
        public IntPtr Handle
        {
            get
            {
                try
                {
                    return System.Diagnostics.Process.GetCurrentProcess().MainWindowHandle;
                }
                catch
                {
                    return IntPtr.Zero;
                }
            }
        }
    }
}
