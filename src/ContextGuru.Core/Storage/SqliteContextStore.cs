using System.Globalization;
using ContextGuru.Core.Models;
using Microsoft.Data.Sqlite;

namespace ContextGuru.Core.Storage;

/// <summary>Stores handoffs in SQLite. It does not migrate: callers run Migrations.Run first.</summary>
public sealed class SqliteContextStore(string dbPath, TimeProvider time) : IContextStore
{
    const string TimestampFormat = "yyyy-MM-ddTHH:mm:ss.fff'Z'";
    const string RecordColumns = "name, summary, body, source_agent, created_at, updated_at";

    /// <summary>Returns the database path and creates its folder. On Unix the folder is 0700 and the empty file 0600.</summary>
    public static string ResolveDatabasePath(string? envOverride)
    {
        var path = string.IsNullOrEmpty(envOverride)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextGuru", "contexts.db")
            : envOverride;

        // A bare file name has an empty folder: the current folder already exists.
        var folder = Path.GetDirectoryName(path)!;
        if (OperatingSystem.IsWindows())
        {
            if (folder.Length > 0)
            {
                Directory.CreateDirectory(folder);
            }
        }
        else
        {
            if (folder.Length > 0)
            {
                Directory.CreateDirectory(folder, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
            }
            if (!File.Exists(path))
            {
                using var _ = new FileStream(path, new FileStreamOptions
                {
                    Mode = FileMode.CreateNew,
                    Access = FileAccess.Write,
                    UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                });
            }
        }
        return path;
    }

    public SaveResult Save(ProjectInfo project, string? name, string summary, string body, string? sourceAgent) =>
        ExecuteWrite((c, tx) =>
        {
            var now = time.GetUtcNow();
            var stamp = Format(now);
            var finalName = name ?? FirstFreeAutoName(c, tx, project, now);

            using var find = new SqliteCommand("SELECT summary, updated_at FROM contexts WHERE project_key = @key AND name = @name", c, tx);
            find.Parameters.AddWithValue("@key", project.Key);
            find.Parameters.AddWithValue("@name", finalName);
            string? previousSummary = null;
            DateTimeOffset? previousUpdatedAt = null;
            using (var r = find.ExecuteReader())
            {
                if (r.Read())
                {
                    previousSummary = r.GetString(0);
                    previousUpdatedAt = Parse(r.GetString(1));
                }
            }

            // An overwrite keeps id and created_at.
            var sql = previousSummary is null
                ? "INSERT INTO contexts(project_key, project_path, project_name, name, summary, body, source_agent, created_at, updated_at) VALUES (@key, @path, @pname, @name, @summary, @body, @agent, @now, @now)"
                : "UPDATE contexts SET summary = @summary, body = @body, source_agent = @agent, updated_at = @now WHERE project_key = @key AND name = @name";
            using var write = new SqliteCommand(sql, c, tx);
            write.Parameters.AddWithValue("@key", project.Key);
            write.Parameters.AddWithValue("@path", project.Path);
            write.Parameters.AddWithValue("@pname", project.Name);
            write.Parameters.AddWithValue("@name", finalName);
            write.Parameters.AddWithValue("@summary", summary);
            write.Parameters.AddWithValue("@body", body);
            write.Parameters.AddWithValue("@agent", (object?)sourceAgent ?? DBNull.Value);
            write.Parameters.AddWithValue("@now", stamp);
            write.ExecuteNonQuery();

            return new SaveResult(finalName, previousSummary is not null, previousUpdatedAt, previousSummary);
        });

    public ContextRecord? GetLatest(ProjectInfo project) =>
        ExecuteRead(c =>
        {
            using var cmd = new SqliteCommand($"SELECT {RecordColumns} FROM contexts WHERE project_key = @key ORDER BY updated_at DESC, id DESC LIMIT 1", c);
            cmd.Parameters.AddWithValue("@key", project.Key);
            return ReadRecord(cmd);
        });

    public ContextRecord? Get(ProjectInfo project, string name) =>
        ExecuteRead(c =>
        {
            using var cmd = new SqliteCommand($"SELECT {RecordColumns} FROM contexts WHERE project_key = @key AND name = @name", c);
            cmd.Parameters.AddWithValue("@key", project.Key);
            cmd.Parameters.AddWithValue("@name", name);
            return ReadRecord(cmd);
        });

    public IReadOnlyList<ContextListItem> List(ProjectInfo project) =>
        ExecuteRead(c =>
        {
            using var cmd = new SqliteCommand("SELECT name, summary, updated_at, source_agent FROM contexts WHERE project_key = @key ORDER BY updated_at DESC, id DESC", c);
            cmd.Parameters.AddWithValue("@key", project.Key);
            using var r = cmd.ExecuteReader();
            var items = new List<ContextListItem>();
            while (r.Read())
            {
                items.Add(new ContextListItem(r.GetString(0), r.GetString(1), Parse(r.GetString(2)), r.IsDBNull(3) ? null : r.GetString(3)));
            }
            return (IReadOnlyList<ContextListItem>)items;
        });

    public ContextRecord? Remove(ProjectInfo project, string name) =>
        ExecuteWrite((c, tx) =>
        {
            using var cmd = new SqliteCommand($"DELETE FROM contexts WHERE project_key = @key AND name = @name RETURNING {RecordColumns}", c, tx);
            cmd.Parameters.AddWithValue("@key", project.Key);
            cmd.Parameters.AddWithValue("@name", name);
            return ReadRecord(cmd);
        });

    /// <summary>Runs one short write transaction. BeginTransaction takes the write lock at BEGIN.</summary>
    T ExecuteWrite<T>(Func<SqliteConnection, SqliteTransaction, T> work) =>
        ExecuteRead(c =>
        {
            using var tx = c.BeginTransaction();
            var result = work(c, tx);
            tx.Commit();
            return result;
        });

    /// <summary>Runs work on a new connection and maps busy and locked errors (codes 5 and 6) to DatabaseBusyException.</summary>
    T ExecuteRead<T>(Func<SqliteConnection, T> work)
    {
        try
        {
            using var c = Migrations.Open(dbPath);
            return work(c);
        }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        {
            throw new DatabaseBusyException(ex);
        }
    }

    /// <summary>The UTC stamp as a name, then -2, -3 and so on until the name is free.</summary>
    static string FirstFreeAutoName(SqliteConnection c, SqliteTransaction tx, ProjectInfo project, DateTimeOffset now)
    {
        var baseName = now.UtcDateTime.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        using var cmd = new SqliteCommand("SELECT count(*) FROM contexts WHERE project_key = @key AND name = @name", c, tx);
        var key = cmd.Parameters.Add("@key", SqliteType.Text);
        var nameParam = cmd.Parameters.Add("@name", SqliteType.Text);
        key.Value = project.Key;
        for (var n = 1; ; n++)
        {
            var candidate = n == 1 ? baseName : $"{baseName}-{n}";
            nameParam.Value = candidate;
            if ((long)cmd.ExecuteScalar()! == 0)
            {
                return candidate;
            }
        }
    }

    static ContextRecord? ReadRecord(SqliteCommand cmd)
    {
        using var r = cmd.ExecuteReader();
        return r.Read()
            ? new ContextRecord(r.GetString(0), r.GetString(1), r.GetString(2), r.IsDBNull(3) ? null : r.GetString(3), Parse(r.GetString(4)), Parse(r.GetString(5)))
            : null;
    }

    static string Format(DateTimeOffset t) => t.UtcDateTime.ToString(TimestampFormat, CultureInfo.InvariantCulture);

    static DateTimeOffset Parse(string s) =>
        DateTimeOffset.Parse(s, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal);
}
