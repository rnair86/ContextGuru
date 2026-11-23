using ContextGuru.Core.Storage;
using Microsoft.Data.Sqlite;

namespace ContextGuru;

/// <summary>Runs the schema migration on a background task so the server can answer initialize at once. Tools await Completion.</summary>
public sealed class StartupMigration
{
    readonly string? _dbPath;
    readonly Lock _gate = new();
    Task _completion;

    /// <summary>A busy failure is not kept: the next read starts the migration again.</summary>
    public Task Completion
    {
        get
        {
            lock (_gate)
            {
                if (_dbPath is not null && _completion.Exception?.GetBaseException() is DatabaseBusyException)
                {
                    _completion = Start(_dbPath);
                }
                return _completion;
            }
        }
    }

    public StartupMigration(string dbPath)
    {
        _dbPath = dbPath;
        _completion = Start(dbPath);
    }

    /// <summary>For a failure before migration could start, such as a database folder that cannot be created.</summary>
    public StartupMigration(Exception failure) => _completion = Task.FromException(failure);

    static Task Start(string dbPath) => Task.Run(() =>
    {
        try
        {
            Migrations.Run(dbPath);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new DatabaseBusyException(ex);
        }
    });
}
