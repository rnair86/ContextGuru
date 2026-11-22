using Microsoft.Data.Sqlite;

namespace ContextGuru.Core.Storage;

/// <summary>Opens the database and brings its schema up to date, tracked in PRAGMA user_version.</summary>
public static class Migrations
{
    // Index i holds migration i + 1. Add migration 2 as one more entry.
    static readonly string[] Steps =
    [
        """
        CREATE TABLE contexts(
          id INTEGER PRIMARY KEY,
          project_key TEXT NOT NULL,
          project_path TEXT NOT NULL,
          project_name TEXT NOT NULL,
          name TEXT NOT NULL,
          summary TEXT NOT NULL,
          body TEXT NOT NULL,
          source_agent TEXT,
          created_at TEXT NOT NULL,
          updated_at TEXT NOT NULL,
          UNIQUE(project_key, name)
        );
        CREATE INDEX contexts_latest ON contexts(project_key, updated_at DESC, id DESC);
        """,
    ];

    public static int Latest => Steps.Length;

    /// <summary>Opens a new unpooled connection. A fresh connection has busy_timeout 0, so it is set here.</summary>
    public static SqliteConnection Open(string dbPath)
    {
        var builder = new SqliteConnectionStringBuilder { DataSource = dbPath, Pooling = false, DefaultTimeout = 5 };
        var c = new SqliteConnection(builder.ConnectionString);
        c.Open();
        Exec(c, "PRAGMA busy_timeout=5000");
        return c;
    }

    public static void Run(string dbPath)
    {
        using var c = Open(dbPath);

        // First open: WAL is stored in the file, so it is set once, outside the transaction.
        if (ReadUserVersion(c) == 0)
        {
            Exec(c, "PRAGMA journal_mode=WAL");
        }

        // BeginTransaction issues BEGIN IMMEDIATE, so a second starter waits here and then finds nothing to do.
        using var tx = c.BeginTransaction();
        var found = ReadUserVersion(c, tx);
        if (found > Latest)
        {
            throw new SchemaTooNewException(found, Latest);
        }
        for (var v = found; v < Latest; v++)
        {
            Exec(c, Steps[v], tx);
        }
        if (found < Latest)
        {
            Exec(c, $"PRAGMA user_version={Latest}", tx);
        }
        tx.Commit();
    }

    public static int ReadUserVersion(SqliteConnection c, SqliteTransaction? tx = null)
    {
        using var cmd = new SqliteCommand("PRAGMA user_version", c, tx);
        return Convert.ToInt32(cmd.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture);
    }

    static void Exec(SqliteConnection c, string sql, SqliteTransaction? tx = null)
    {
        using var cmd = new SqliteCommand(sql, c, tx);
        cmd.ExecuteNonQuery();
    }
}
