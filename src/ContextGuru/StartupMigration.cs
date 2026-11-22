using ContextGuru.Core.Storage;

namespace ContextGuru;

/// <summary>Runs the schema migration on a background task so the server can answer initialize at once. Tools await Completion.</summary>
public sealed class StartupMigration
{
    public Task Completion { get; }

    public StartupMigration(string dbPath) => Completion = Task.Run(() => Migrations.Run(dbPath));
}
