using System.Reflection;
using System.Text;

namespace RootCli.Core.Skills;

public static class SkillsPrompt
{
    public const string AdhdSkillId = "i-have-adhd";

    public static string BuildActiveSkillsSection()
    {
        var body = LoadEmbeddedSkill("RootCli.Core.Skills.i-have-adhd.md");
        if (string.IsNullOrWhiteSpace(body))
        {
            body = FallbackAdhdBody;
        }

        var builder = new StringBuilder();
        builder.AppendLine("Active skills (enabled by default). When these conflict with general style rules, prefer the skill rules for the user-visible answer:");
        builder.AppendLine("--- I have ADHD ---");
        builder.AppendLine(body.Trim());
        builder.AppendLine();
        builder.AppendLine("Terminal note: the user reads answers in a plain console that cannot render Markdown.");
        builder.AppendLine("Follow the ADHD skill, but emit plain text only — no **, *, `, ```, # headings, or [links](url).");
        builder.AppendLine("Use numbered lists and plain command lines instead.");
        return builder.ToString().TrimEnd();
    }

    private static string LoadEmbeddedSkill(string resourceName)
    {
        var asm = Assembly.GetExecutingAssembly();
        using var stream = asm.GetManifestResourceStream(resourceName);
        if (stream == null)
        {

            foreach (var name in asm.GetManifestResourceNames())
            {
                if (name.EndsWith("i-have-adhd.md", StringComparison.OrdinalIgnoreCase))
                {
                    using var alt = asm.GetManifestResourceStream(name);
                    if (alt == null)
                    {
                        continue;
                    }

                    using var reader = new StreamReader(alt);
                    return reader.ReadToEnd();
                }
            }

            return "";
        }

        using (var reader = new StreamReader(stream))
        {
            return reader.ReadToEnd();
        }
    }

    private const string FallbackAdhdBody =
        """
        # I have ADHD

        Description: Action-first answers with numbered steps and no fluff — shaped for ADHD reading.

        Shape every response for an ADHD reader. Lead with concrete next actions, number multi-step work, externalize state across turns, suppress tangents, give specific time estimates, and make wins visible.

        ## Rules
        1. Lead with the next action (first line is something the reader can do).
        2. Number multi-step tasks.
        3. End with one concrete next action.
        4. Suppress tangents.
        5. Restate state every turn.
        6. Give specific time estimates.
        7. Make completed work visible.
        8. Matter-of-fact tone for errors.
        9. Cap lists at 5 items.
        10. No preamble, no recap, no closing pleasantries.
        """;
}
