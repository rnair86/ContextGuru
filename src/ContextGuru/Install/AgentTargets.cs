namespace ContextGuru.Install;

public enum ConfigFormat
{
    Json,
    Toml,
}

public sealed record AgentTarget(string Agent, string ConfigPath, ConfigFormat ConfigFormat, string CommandFolder,
    string FileExtension, string ArgsPlaceholder, string ToolName, string MarkerLine, bool UsesSkillLayout = false)
{
    public string GetCommandPath(string commandName)
    {
        var name = CommandDefinition.For(commandName).InstalledName;
        return UsesSkillLayout
            ? Path.Combine(CommandFolder, name, "SKILL.md")
            : Path.Combine(CommandFolder, name + FileExtension);
    }

    public bool IsManagedCommand(string content)
    {
        using var reader = new StringReader(content);
        if (UsesSkillLayout)
        {
            if (reader.ReadLine() != "---")
            {
                return false;
            }

            string? line;
            do
            {
                line = reader.ReadLine();
            }
            while (line is not null and not "---");

            if (line is null)
            {
                return false;
            }
        }

        return reader.ReadLine() == MarkerLine;
    }
}

public static class AgentTargets
{
    internal const string ManagedMarker = "<!-- contextguru:managed v1 -->";

    public static IEnumerable<string> CommandNames => CommandDefinition.Names;

    public static AgentTarget For(string agent, string baseFolder) => agent switch
    {
        "claude" => new(agent, Path.Combine(baseFolder, ".claude.json"), ConfigFormat.Json,
            Path.Combine(baseFolder, ".claude", "commands"), ".md", "$ARGUMENTS", "mcp__contextguru__{0}", ManagedMarker),
        "codex" => new(agent, Path.Combine(baseFolder, ".codex", "config.toml"), ConfigFormat.Toml,
            Path.Combine(baseFolder, ".agents", "skills"), ".md", "the text after the skill name", "{0}",
            ManagedMarker, UsesSkillLayout: true),
        "cursor" => new(agent, Path.Combine(baseFolder, ".cursor", "mcp.json"), ConfigFormat.Json,
            Path.Combine(baseFolder, ".cursor", "commands"), ".md", "the text after the command name", "contextguru-{0}", ManagedMarker),
        _ => throw new ArgumentException("Unknown agent.", nameof(agent)),
    };
}
