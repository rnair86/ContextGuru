namespace ContextGuru.Install;

internal sealed record CommandDefinition(string Name, string ToolName)
{
    private static readonly Dictionary<string, string> ToolNames = new(StringComparer.Ordinal)
    {
        ["save-context"] = "save_context",
        ["get-context"] = "get_context",
        ["list-contexts"] = "list_contexts",
        ["remove-context"] = "remove_context",
    };

    public string InstalledName => "cg-" + Name;

    public static IEnumerable<string> Names => ToolNames.Keys;

    public static CommandDefinition For(string commandName) =>
        ToolNames.TryGetValue(commandName, out var toolName)
            ? new(commandName, toolName)
            : throw new ArgumentException("Unknown command.", nameof(commandName));
}
