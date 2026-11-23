using System.Text.Json;
using System.Text.Json.Serialization;
using ContextGuru.Core.Models;
using ModelContextProtocol.Protocol;

namespace ContextGuru.Tools;

public sealed record ProjectResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("path")] string Path,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("warning")] string? Warning)
{
    public static ProjectResult From(ProjectInfo p) => new(p.Name, p.Path, p.Source.ToString().ToLowerInvariant(), p.Warning);
}

public sealed record SaveContextResult(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("overwritten")] bool Overwritten,
    [property: JsonPropertyName("previous_updated_at")] string? PreviousUpdatedAt,
    [property: JsonPropertyName("previous_summary")] string? PreviousSummary,
    [property: JsonPropertyName("project")] ProjectResult Project);

public sealed record GetContextFound(
    [property: JsonPropertyName("found")] bool Found,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("body")] string Body,
    [property: JsonPropertyName("updated_at")] string UpdatedAt,
    [property: JsonPropertyName("source_agent")] string? SourceAgent,
    [property: JsonPropertyName("project")] ProjectResult Project,
    [property: JsonPropertyName("agent_instructions")] string AgentInstructions);

public sealed record GetContextNotFound(
    [property: JsonPropertyName("found")] bool Found,
    [property: JsonPropertyName("searched_path")] string SearchedPath,
    [property: JsonPropertyName("project")] ProjectResult Project);

public sealed record ListEntry(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("updated_at")] string UpdatedAt,
    [property: JsonPropertyName("source_agent")] string? SourceAgent);

public sealed record ListContextsResult(
    [property: JsonPropertyName("project")] ProjectResult Project,
    [property: JsonPropertyName("contexts")] IReadOnlyList<ListEntry> Contexts);

public sealed record RemoveContextResult(
    [property: JsonPropertyName("removed")] bool Removed,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("project")] ProjectResult Project);

/// <summary>Builds the one-text-block tool results. Errors are returned, not thrown, so the SDK keeps the exact text.</summary>
public static class Results
{
    public static CallToolResult Ok<T>(T value) =>
        new() { Content = [new TextContentBlock { Text = JsonSerializer.Serialize(value) }] };

    public static CallToolResult Error(string message) =>
        new() { IsError = true, Content = [new TextContentBlock { Text = message }] };

    public static string Timestamp(DateTimeOffset t) => t.UtcDateTime.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'", System.Globalization.CultureInfo.InvariantCulture);
}
