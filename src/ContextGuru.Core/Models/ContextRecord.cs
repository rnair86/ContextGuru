namespace ContextGuru.Core.Models;

public enum ProjectSource { Argument, Roots, Cwd }

public sealed record ProjectInfo(string Name, string Path, string Key, ProjectSource Source, string? Warning);

public sealed record ContextRecord(string Name, string Summary, string Body, string? SourceAgent, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);

public sealed record ContextListItem(string Name, string Summary, DateTimeOffset UpdatedAt, string? SourceAgent);

public sealed record SaveResult(string Name, bool Overwritten, DateTimeOffset? PreviousUpdatedAt, string? PreviousSummary);

public sealed record ValidationError(string Field, string Message);
