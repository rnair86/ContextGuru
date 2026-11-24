using System.Collections.Concurrent;
using System.Globalization;

namespace ContextGuru.Install;

public static class TemplateRenderer
{
    private const string ToolPlaceholder = "{{tool}}";
    private static readonly ConcurrentDictionary<string, string> Templates = new(StringComparer.Ordinal);

    public static string ReadTemplate(string commandName) =>
        Templates.GetOrAdd(commandName, LoadTemplate);

    private static string LoadTemplate(string commandName)
    {
        var resourceName = $"ContextGuru.Install.Templates.{commandName}.md";
        using var stream = typeof(TemplateRenderer).Assembly.GetManifestResourceStream(resourceName)
            ?? throw new ArgumentException("Unknown command.", nameof(commandName));
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    public static string Render(string template, AgentTarget target)
    {
        var toolStart = template.IndexOf(ToolPlaceholder, StringComparison.Ordinal) + ToolPlaceholder.Length;
        var toolEnd = template.IndexOf('`', toolStart);
        var tool = template[toolStart..toolEnd];
        var address = string.Format(CultureInfo.InvariantCulture, target.ToolName, tool);
        var args = target.Agent == "claude" ? $"`{target.ArgsPlaceholder}`" : target.ArgsPlaceholder;
        var rendered = template.Replace(ToolPlaceholder + tool, address, StringComparison.Ordinal)
            .Replace("{{args}}", args, StringComparison.Ordinal)
            .Replace("{{agent}}", target.Agent, StringComparison.Ordinal);

        if (target.Agent == "codex")
        {
            var commandName = tool.Replace('_', '-');
            return $"---\nname: cg-{commandName}\ndescription: Run ContextGuru {commandName}.\n---\n" + rendered.ReplaceLineEndings("\n");
        }

        return rendered;
    }
}
