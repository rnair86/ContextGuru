using System.Diagnostics;
using ContextGuru.Core.Models;
using ContextGuru.Core.Storage;
using Microsoft.Data.Sqlite;

namespace ContextGuru.Tests;

public sealed class SqliteContextStoreTests : IDisposable
{
    const string Body = "## Goal\nbody text";
    static readonly DateTimeOffset T0 = new(2026, 11, 21, 10, 0, 0, TimeSpan.Zero);
    static readonly ProjectInfo P = new("app", @"C:\work\app", "key-app", ProjectSource.Argument, null);
    static readonly ProjectInfo Q = new("other", @"C:\work\other", "key-other", ProjectSource.Argument, null);

    readonly TempDir t = new();
    readonly FixedTimeProvider time = new() { Now = T0 };
    readonly string db;
    readonly SqliteContextStore store;

    public SqliteContextStoreTests()
    {
        db = Path.Combine(t.Path, "c.db");
        Migrations.Run(db);
        store = new SqliteContextStore(db, time);
    }

    public void Dispose() => t.Dispose();

    [Fact]
    public void Save_New_InsertsAndReportsNoPrevious()
    {
        var result = store.Save(P, "auth", "sum", Body, "claude");
        Assert.Equal(new SaveResult("auth", false, null, null), result);
        var rec = store.Get(P, "auth")!;
        Assert.Equal(("sum", Body, "claude", T0, T0), (rec.Summary, rec.Body, rec.SourceAgent, rec.CreatedAt, rec.UpdatedAt));
    }

    [Fact]
    public void Save_Overwrite_KeepsIdAndCreatedAtAndReportsPrevious()
    {
        time.Now = T0;
        store.Save(P, "auth", "old summary", Body, "claude");
        long idBefore = RowId("auth");
        time.Now = T0.AddMinutes(5);
        var second = store.Save(P, "auth", "new summary", Body, "codex");
        Assert.True(second.Overwritten);
        Assert.Equal(("old summary", T0), (second.PreviousSummary, second.PreviousUpdatedAt));
        var rec = store.Get(P, "auth")!;
        Assert.Equal((T0, T0.AddMinutes(5)), (rec.CreatedAt, rec.UpdatedAt));
        Assert.Equal(("new summary", "codex"), (rec.Summary, rec.SourceAgent));
        Assert.Equal(idBefore, RowId("auth"));
    }

    [Fact]
    public void Save_AutoName_UsesUtcStampThenSuffix()
    {
        time.Now = new DateTimeOffset(2026, 11, 21, 10, 30, 5, TimeSpan.Zero);
        Assert.Equal("20261121-103005", store.Save(P, null, "s", Body, null).Name);
        Assert.Equal("20261121-103005-2", store.Save(P, null, "s", Body, null).Name);
        Assert.Equal("20261121-103005-3", store.Save(P, null, "s", Body, null).Name);
    }

    [Fact]
    public void Save_AutoName_NeverOverwritesUserNamedContext()
    {
        time.Now = new DateTimeOffset(2026, 11, 21, 10, 30, 5, TimeSpan.Zero);
        store.Save(P, "20261121-103005", "mine", Body, null);
        var auto = store.Save(P, null, "auto", Body, null);
        Assert.Equal("20261121-103005-2", auto.Name);
        Assert.False(auto.Overwritten);
        Assert.Equal("mine", store.Get(P, "20261121-103005")!.Summary);
    }

    [Fact]
    public void Save_AutoName_UsesUtcWhenOffsetGiven()
    {
        time.Now = new DateTimeOffset(2026, 11, 21, 12, 30, 5, TimeSpan.FromHours(2));
        Assert.Equal("20261121-103005", store.Save(P, null, "s", Body, null).Name);
        Assert.Equal(new DateTimeOffset(2026, 11, 21, 10, 30, 5, TimeSpan.Zero), store.Get(P, "20261121-103005")!.CreatedAt);
    }

    [Fact]
    public void Save_WhileAnotherConnectionHoldsWriteLock_ThrowsDatabaseBusy_AfterAbout5s()
    {
        using var holder = Migrations.Open(db);
        using var begin = new SqliteCommand("BEGIN IMMEDIATE", holder);
        begin.ExecuteNonQuery();
        try
        {
            var sw = Stopwatch.StartNew();
            Assert.Throws<DatabaseBusyException>(() => store.Save(P, "auth", "s", Body, null));
            Assert.True(sw.Elapsed >= TimeSpan.FromSeconds(4), $"Returned after {sw.Elapsed}");
        }
        finally
        {
            using var rollback = new SqliteCommand("ROLLBACK", holder);
            rollback.ExecuteNonQuery();
        }
    }

    [Fact]
    public void Reads_WhileAnotherConnectionHoldsExclusiveLock_ThrowDatabaseBusy()
    {
        store.Save(P, "auth", "s", Body, null);
        // In WAL, readers are not blocked by a writer. An exclusive-mode connection that has written does block them.
        using var holder = Migrations.Open(db);
        Exec(holder, "PRAGMA locking_mode=EXCLUSIVE");
        Exec(holder, "BEGIN IMMEDIATE");
        Exec(holder, "UPDATE contexts SET summary = 'x'");
        try
        {
            Assert.Throws<DatabaseBusyException>(() => store.GetLatest(P));
            Assert.Throws<DatabaseBusyException>(() => store.Get(P, "auth"));
            Assert.Throws<DatabaseBusyException>(() => store.List(P));
        }
        finally
        {
            Exec(holder, "ROLLBACK");
        }
    }

    static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = new SqliteCommand(sql, c);
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Get_FoundAndNotFound()
    {
        store.Save(P, "auth", "s", Body, null);
        Assert.NotNull(store.Get(P, "auth"));
        Assert.Null(store.Get(P, "missing"));
    }

    [Fact]
    public void SameName_InTwoProjects_StaysSeparate()
    {
        store.Save(P, "auth", "in P", Body, null);
        store.Save(Q, "auth", "in Q", Body, null);
        Assert.Equal("in P", store.Get(P, "auth")!.Summary);
        Assert.Equal("in Q", store.Get(Q, "auth")!.Summary);
        Assert.Single(store.List(P));
    }

    [Fact]
    public void GetLatest_OrdersByUpdatedAt_ThenIdDesc()
    {
        Assert.Null(store.GetLatest(P));
        time.Now = T0;
        store.Save(P, "a", "s", Body, null);
        store.Save(P, "b", "s", Body, null);
        Assert.Equal("b", store.GetLatest(P)!.Name);
        time.Now = T0.AddMinutes(1);
        store.Save(P, "a", "s2", Body, null);
        Assert.Equal("a", store.GetLatest(P)!.Name);
    }

    [Fact]
    public void List_NewestFirst_NoBody()
    {
        time.Now = T0;
        store.Save(P, "first", "s1", Body, "claude");
        time.Now = T0.AddMinutes(1);
        store.Save(P, "second", "s2", Body, null);
        var list = store.List(P);
        Assert.Equal(["second", "first"], list.Select(i => i.Name));
        Assert.Equal(new ContextListItem("first", "s1", T0, "claude"), list[1]);
        Assert.Empty(store.List(Q));
    }

    [Fact]
    public void Remove_ReturnsRecordThenNull()
    {
        store.Save(P, "auth", "s", Body, "claude");
        var removed = store.Remove(P, "auth")!;
        Assert.Equal(("auth", "s", Body, "claude"), (removed.Name, removed.Summary, removed.Body, removed.SourceAgent));
        Assert.Null(store.Remove(P, "auth"));
        Assert.Null(store.Get(P, "auth"));
    }

    [Fact]
    public void Remove_OnlyAffectsOwnProject()
    {
        store.Save(P, "auth", "s", Body, null);
        Assert.Null(store.Remove(Q, "auth"));
        Assert.NotNull(store.Get(P, "auth"));
    }

    [Fact]
    public void ResolveDatabasePath_CreatesMissingFolder()
    {
        var path = Path.Combine(t.Path, "new", "deeper", "c.db");
        Assert.Equal(path, SqliteContextStore.ResolveDatabasePath(path));
        Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
    }

    [Fact]
    public void ResolveDatabasePath_NewFolder_WorksEndToEnd()
    {
        var path = SqliteContextStore.ResolveDatabasePath(Path.Combine(t.Path, "x", "y", "c.db"));
        Migrations.Run(path);
        var s = new SqliteContextStore(path, time);
        s.Save(P, "auth", "s", Body, null);
        Assert.NotNull(s.Get(P, "auth"));
    }

    [Fact]
    public void ResolveDatabasePath_Null_UsesLocalAppData()
    {
        var expected = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ContextGuru", "contexts.db");
        var path = SqliteContextStore.ResolveDatabasePath(null);
        Assert.Equal(expected, path);
        Assert.EndsWith(Path.Combine("ContextGuru", "contexts.db"), path, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveDatabasePath_Unix_FolderIs0700_FileIs0600()
    {
        if (OperatingSystem.IsWindows())
        {
            return;
        }
        var path = SqliteContextStore.ResolveDatabasePath(Path.Combine(t.Path, "data", "c.db"));
        Migrations.Run(path);
        new SqliteContextStore(path, time).Save(P, "auth", "s", Body, null);
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute, File.GetUnixFileMode(Path.GetDirectoryName(path)!));
        Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
    }

    long RowId(string name)
    {
        using var c = Migrations.Open(db);
        using var cmd = new SqliteCommand("SELECT id FROM contexts WHERE name = @n", c);
        cmd.Parameters.AddWithValue("@n", name);
        return (long)cmd.ExecuteScalar()!;
    }
}
