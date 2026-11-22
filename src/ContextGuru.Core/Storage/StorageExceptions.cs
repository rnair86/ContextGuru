namespace ContextGuru.Core.Storage;

public sealed class DatabaseBusyException(Exception inner) : Exception("Database busy, retry.", inner);

public sealed class SchemaTooNewException(int found, int supported)
    : Exception($"The database schema is version {found}, but this tool supports up to {supported}. Update this tool's install.")
{
    public int Found { get; } = found;

    public int Supported { get; } = supported;
}
