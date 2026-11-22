using System.Text.RegularExpressions;
using ContextGuru.Core.Models;

namespace ContextGuru.Core.Template;

/// <summary>Checks the handoff summary, body and name against the template rules.</summary>
public static partial class HandoffTemplate
{
    public const int MaxSummaryLength = 200;
    public const int MaxBodyLength = 20_000;

    static readonly string[] Required = ["Goal", "Done", "Decisions", "Open tasks", "Next steps"];
    const string RequiredList = "Goal, Done, Decisions, Open tasks, Next steps";

    [GeneratedRegex("^[a-z0-9][a-z0-9_-]{0,63}$")]
    private static partial Regex NamePattern();

    [GeneratedRegex(@"^(n/a|none)\.?$", RegexOptions.IgnoreCase)]
    private static partial Regex PlaceholderPattern();

    public static IReadOnlyList<ValidationError> Validate(string summary, string body)
    {
        List<ValidationError> errors = [];

        var s = summary.Trim();
        if (s.Length == 0) errors.Add(new("summary", "Summary is empty."));
        else if (s.Contains('\n') || s.Contains('\r')) errors.Add(new("summary", "Summary must be one line."));
        else if (s.Length > MaxSummaryLength) errors.Add(new("summary", $"Summary is {s.Length} characters; the maximum is {MaxSummaryLength}."));

        var b = body.Trim();
        if (b.Length == 0)
        {
            errors.Add(new("body", "Body is empty."));
            return errors;
        }
        if (b.Length > MaxBodyLength) errors.Add(new("body", $"Body is {b.Length} characters; the maximum is {MaxBodyLength}."));

        var found = ParseHeadings(b).Select(h => Array.FindIndex(Required, r => r.Equals(h.Text, StringComparison.OrdinalIgnoreCase)))
            .Where(i => i >= 0).ToList();

        foreach (var (name, i) in Required.Select((n, i) => (n, i)))
        {
            var count = found.Count(f => f == i);
            if (count == 0)
                errors.Add(new("body", $"Missing required heading: \"## {name}\". Required headings, in order: {RequiredList}."));
            else if (count > 1)
                errors.Add(new("body", $"Heading \"## {name}\" appears more than once."));
        }

        // Order is judged on the first occurrence of each heading.
        var firsts = found.Distinct().ToList();
        for (var i = 0; i < firsts.Count; i++)
            if (firsts.Skip(i + 1).Any(later => later < firsts[i]))
                errors.Add(new("body", $"Heading \"## {Required[firsts[i]]}\" is out of order. Required order: {RequiredList}."));

        return errors;
    }

    public static bool NextStepsHasContent(string body)
    {
        var lines = SplitLines(body.Trim());
        var headings = ParseHeadings(body.Trim());
        var next = headings.FirstOrDefault(h => h.Text.Equals("Next steps", StringComparison.OrdinalIgnoreCase));
        if (next is null) return false;

        // The section runs to the next level-two heading, or the end of the body.
        var end = headings.Where(h => h.Line > next.Line).Select(h => h.Line).DefaultIfEmpty(lines.Length).Min();
        return lines[(next.Line + 1)..end].Any(l => !IsPlaceholder(l));
    }

    public static string NormalizeName(string name) => name.Trim().ToLowerInvariant();

    public static bool IsValidName(string name) => NamePattern().IsMatch(name);

    public static string BadNameMessage(string name) =>
        $"Name \"{name}\" is not valid. Use lower-case letters, digits, \"-\" and \"_\", starting with a letter or digit, up to 64 characters.";

    // A blank line or a list marker followed by none or n/a carries no content.
    static bool IsPlaceholder(string line)
    {
        var t = line.Trim().TrimStart('-', '*').Trim();
        return t.Length == 0 || PlaceholderPattern().IsMatch(t);
    }

    static string[] SplitLines(string text) => text.Split(["\r\n", "\n"], StringSplitOptions.None);

    sealed record Heading(string Text, int Line);

    // Level-two headings outside code fences. An unclosed fence hides the rest of the body.
    static List<Heading> ParseHeadings(string body)
    {
        List<Heading> headings = [];
        string? fence = null;
        var lines = SplitLines(body);
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var marker = line.StartsWith("```", StringComparison.Ordinal) ? "```" : line.StartsWith("~~~", StringComparison.Ordinal) ? "~~~" : null;
            if (fence is null)
            {
                if (marker is not null) fence = marker;
                else if (line.StartsWith("## ", StringComparison.Ordinal)) headings.Add(new(line[3..].Trim(), i));
            }
            else if (marker == fence) fence = null;
        }
        return headings;
    }
}
