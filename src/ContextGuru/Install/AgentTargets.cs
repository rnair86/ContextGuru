namespace ContextGuru.Install;

public enum ConfigFormat
{
    Json,
    Toml,
}

public sealed record AgentTarget(string Agent, string ConfigPath, ConfigFormat ConfigFormat, string CommandFolder,
    string FileExtension, string ArgsPlaceholder, string ToolName, string MarkerLine);

public static class AgentTargets
{
    private const string Marker = "<!-- contextguru:managed v1 -->";

    public static AgentTarget For(string agent, string baseFolder) => agent switch
    {
        "claude" => new(agent, Path.Combine(baseFolder, ".claude.json"), ConfigFormat.Json,
            Path.Combine(baseFolder, ".claude", "commands"), ".md", "$ARGUMENTS", "mcp__contextguru__{0}", Marker),
        "codex" => new(agent, Path.Combine(baseFolder, ".codex", "config.toml"), ConfigFormat.Toml,
            Path.Combine(baseFolder, ".agents", "skills"), ".md", "the text after the skill name", "mcp__contextguru__{0}", Marker),
        "cursor" => new(agent, Path.Combine(baseFolder, ".cursor", "mcp.json"), ConfigFormat.Json,
            Path.Combine(baseFolder, ".cursor", "commands"), ".md", "the text after the command name", "{0}", Marker),
        _ => throw new ArgumentException("Unknown agent.", nameof(agent)),
    };
}
