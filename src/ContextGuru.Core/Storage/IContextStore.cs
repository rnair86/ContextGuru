using ContextGuru.Core.Models;

namespace ContextGuru.Core.Storage;

public interface IContextStore
{
    SaveResult Save(ProjectInfo project, string? name, string summary, string body, string? sourceAgent);

    ContextRecord? GetLatest(ProjectInfo project);

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Naming", "CA1716", Justification = "Get is the specified API name. This is a C# only tool.")]
    ContextRecord? Get(ProjectInfo project, string name);

    /// <summary>Metadata only, newest first.</summary>
    IReadOnlyList<ContextListItem> List(ProjectInfo project);

    /// <summary>Returns the deleted record, or null if there was none.</summary>
    ContextRecord? Remove(ProjectInfo project, string name);
}
