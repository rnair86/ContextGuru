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
        var template = reader.ReadToEnd();
        var lineEnd = template.IndexOf('\n');
        if (lineEnd < 0 || !template.AsSpan(0, lineEnd).TrimEnd('\r').SequenceEqual(AgentTargets.ManagedMarker))
        {
            throw new InvalidOperationException($"Embedded template '{commandName}' is missing the managed marker.");
        }

        return template;
    }

    public static string Render(string commandName, AgentTarget target)
    {
        var command = CommandDefinition.For(commandName);
        var template = ReadTemplate(command.Name);
        var address = string.Format(CultureInfo.InvariantCulture, target.ToolName, command.ToolName);
        var tool = target.Agent == "codex" ? $"`{address}` on the `contextguru` MCP server" : $"`{address}`";
        var args = target.Agent == "claude" ? $"`{target.ArgsPlaceholder}`" : target.ArgsPlaceholder;
        var body = template[AgentTargets.ManagedMarker.Length..]
            .Replace(ToolPlaceholder, tool, StringComparison.Ordinal)
            .Replace("{{args}}", args, StringComparison.Ordinal)
            .Replace("{{agent}}", target.Agent, StringComparison.Ordinal);
        var rendered = target.MarkerLine + body;

        if (target.UsesSkillLayout)
        {
            return $"---\nname: {command.InstalledName}\ndescription: Run ContextGuru {command.Name}.\n---\n" + rendered.ReplaceLineEndings("\n");
        }

        return rendered;
    }
}
