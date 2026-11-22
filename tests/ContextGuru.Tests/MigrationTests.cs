using System.Text.RegularExpressions;
using ContextGuru.Core.Storage;
using Microsoft.Data.Sqlite;

namespace ContextGuru.Tests;

public sealed partial class MigrationTests
{
    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    static string NewDbPath(TempDir t) => Path.Combine(t.Path, "c.db");

    static object? Scalar(SqliteConnection c, string sql)
    {
        using var cmd = new SqliteCommand(sql, c);
        return cmd.ExecuteScalar();
    }

    /// <summary>The schema SQL from sqlite_master: whitespace collapsed, one statement per line, sorted by name.</summary>
    static string NormalizedSchema(SqliteConnection c)
    {
        using var cmd = new SqliteCommand("SELECT sql FROM sqlite_master WHERE sql IS NOT NULL ORDER BY name", c);
        using var r = cmd.ExecuteReader();
        var lines = new List<string>();
        while (r.Read())
        {
            lines.Add(Whitespace().Replace(r.GetString(0), " ").Trim());
        }
        return string.Join("\n", lines) + "\n";
    }

    static string ExpectedSchema()
    {
        using var s = typeof(MigrationTests).Assembly.GetManifestResourceStream("ContextGuru.Tests.Expected.schema-v1.sql")!;
        using var reader = new StreamReader(s);
        return reader.ReadToEnd().ReplaceLineEndings("\n");
    }

    [Fact]
    public void Run_NewDatabase_CreatesSchemaAtLatest()
    {
        using var t = new TempDir();
        var db = NewDbPath(t);
        Migrations.Run(db);
        using var c = Migrations.Open(db);
        Assert.Equal(Migrations.Latest, Migrations.ReadUserVersion(c));
        Assert.Equal("wal", Scalar(c, "PRAGMA journal_mode"));
    }

    [Fact]
    public void Run_NewerUserVersion_Throws()
    {
        using var t = new TempDir();
        var db = NewDbPath(t);
        Migrations.Run(db);
        using (var c = Migrations.Open(db))
        {
            Scalar(c, "PRAGMA user_version = 99");
        }
        var ex = Assert.Throws<SchemaTooNewException>(() => Migrations.Run(db));
        Assert.Equal((99, 1), (ex.Found, ex.Supported));
    }

    [Fact]
    public void Run_Twice_ChangesNothing()
    {
        using var t = new TempDir();
        var db = NewDbPath(t);
        Migrations.Run(db);
        string first;
        using (var c = Migrations.Open(db))
        {
            first = NormalizedSchema(c);
        }
        Migrations.Run(db);
        using var c2 = Migrations.Open(db);
        Assert.Equal(Migrations.Latest, Migrations.ReadUserVersion(c2));
        Assert.Equal(first, NormalizedSchema(c2));
    }

    [Fact]
    public void Run_NewDatabase_SchemaMatchesExpectedFile()
    {
        using var t = new TempDir();
        var db = NewDbPath(t);
        Migrations.Run(db);
        using var c = Migrations.Open(db);
        Assert.Equal(ExpectedSchema(), NormalizedSchema(c));
    }

    [Fact]
    public void Run_PathWithSemicolon_CreatesSchema()
    {
        using var t = new TempDir();
        var db = Path.Combine(t.Sub("team;notes"), "contexts.db");
        Migrations.Run(db);
        using var c = Migrations.Open(db);
        Assert.Equal(Migrations.Latest, Migrations.ReadUserVersion(c));
        Assert.Equal(1L, Scalar(c, "SELECT count(*) FROM sqlite_master WHERE name = 'contexts'"));
    }

    [Fact]
    public void Run_WalNotEnabled_Throws()
    {
        // An in-memory database cannot use WAL, so journal_mode stays "memory".
        var ex = Assert.Throws<InvalidOperationException>(() => Migrations.Run(":memory:"));
        Assert.Contains("memory", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Open_SetsBusyTimeout()
    {
        using var t = new TempDir();
        using var c = Migrations.Open(NewDbPath(t));
        Assert.Equal(5000L, Scalar(c, "PRAGMA busy_timeout"));
    }
}
