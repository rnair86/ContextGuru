using System.Text.RegularExpressions;
using ContextGuru.Core.Storage;
using Microsoft.Data.Sqlite;

namespace ContextGuru.Tests;

public class DoctorTests
{
    static readonly string[] ExpectedKeys =
    [
        "version",
        "database path",
        "schema version",
        "project name",
        "project path",
        "project source",
        ".git found",
        "write test",
    ];

    [Fact]
    public void Run_GoodDatabase_PrintsAllKeys_Exit0()
    {
        using var dir = new TempDir();
        var repo = dir.Sub("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        var db = Path.Combine(dir.Path, "c.db");
        Migrations.Run(db);
        using var writer = new StringWriter();

        var code = Doctor.Run(db, repo, writer);
        var output = writer.ToString();

        Assert.Equal(0, code);
        foreach (var key in ExpectedKeys)
        {
            Assert.Matches($"(?m)^{Regex.Escape(key)}: .+$", output);
        }
        Assert.Contains("project source: cwd", output);
        Assert.Contains($"schema version: {Migrations.Latest}", output);
        Assert.Contains("write test: ok", output);
    }

    [Fact]
    public void Run_MissingDatabase_PrintsNotCreatedAndSkipped_AndDoesNotCreateIt()
    {
        using var dir = new TempDir();
        var repo = dir.Sub("repo");
        var missingFolder = Path.Combine(dir.Path, "not-there");
        var db = Path.Combine(missingFolder, "c.db");
        using var writer = new StringWriter();

        var code = Doctor.Run(db, repo, writer);
        var output = writer.ToString();

        Assert.Equal(0, code);
        Assert.Contains("schema version: not created yet (created on first save)", output);
        Assert.Contains("write test: skipped", output);
        Assert.False(File.Exists(db));
        Assert.False(Directory.Exists(missingFolder));
    }

    [Fact]
    public void Run_NewerSchema_PrintsNewerThanThisToolWithoutThrowing_Exit1()
    {
        using var dir = new TempDir();
        var repo = dir.Sub("repo");
        var db = Path.Combine(dir.Path, "c.db");
        using (var c = Migrations.Open(db))
        {
            using var cmd = new SqliteCommand("PRAGMA user_version=99", c);
            cmd.ExecuteNonQuery();
        }
        using var writer = new StringWriter();

        var code = Doctor.Run(db, repo, writer);
        var output = writer.ToString();

        Assert.Equal(1, code);
        Assert.Matches(@"(?m)^schema version: .*99.*newer than this tool.*$", output);
    }

    [Fact]
    public void Run_HeldWriteLock_PrintsWriteTestFailed_Exit1()
    {
        using var dir = new TempDir();
        var repo = dir.Sub("repo");
        var db = Path.Combine(dir.Path, "c.db");
        Migrations.Run(db);
        using var holder = Migrations.Open(db);
        using var tx = holder.BeginTransaction();
        using var writer = new StringWriter();

        var code = Doctor.Run(db, repo, writer);
        var output = writer.ToString();

        Assert.Equal(1, code);
        Assert.Contains("write test: failed", output);
    }

    [Fact]
    public void Run_WithGitDirectory_PrintsGitFoundYes()
    {
        using var dir = new TempDir();
        var repo = dir.Sub("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        var nested = dir.Sub(Path.Combine("repo", "src", "app"));
        var db = Path.Combine(dir.Path, "c.db");
        using var writer = new StringWriter();

        var code = Doctor.Run(db, nested, writer);

        Assert.Equal(0, code);
        Assert.Contains(".git found: yes", writer.ToString());
    }

    [Fact]
    public void Run_WithGitFile_PrintsGitFoundYes()
    {
        using var dir = new TempDir();
        var repo = dir.Sub("worktree");
        File.WriteAllText(Path.Combine(repo, ".git"), "gitdir: ../main/.git/worktrees/worktree");
        var db = Path.Combine(dir.Path, "c.db");
        using var writer = new StringWriter();

        var code = Doctor.Run(db, repo, writer);

        Assert.Equal(0, code);
        Assert.Contains(".git found: yes", writer.ToString());
    }

    [Fact]
    public void Run_WithoutGit_PrintsGitFoundNo()
    {
        using var dir = new TempDir();
        var folder = dir.Sub("plain");
        var db = Path.Combine(dir.Path, "c.db");
        using var writer = new StringWriter();

        var code = Doctor.Run(db, folder, writer);

        Assert.Equal(0, code);
        Assert.Contains(".git found: no", writer.ToString());
    }

    [Fact]
    public void Run_PassingRun_RollsBackAndLeavesContextsTableEmpty()
    {
        using var dir = new TempDir();
        var repo = dir.Sub("repo");
        var db = Path.Combine(dir.Path, "c.db");
        Migrations.Run(db);
        using var writer = new StringWriter();

        var code = Doctor.Run(db, repo, writer);

        Assert.Equal(0, code);
        using var c = Migrations.Open(db);
        using var cmd = new SqliteCommand("SELECT count(*) FROM contexts", c);
        Assert.Equal(0L, (long)cmd.ExecuteScalar()!);
    }

    [Fact]
    public async Task Cli_DoctorFlag_PrintsToStdout_Exits0_DoesNotCreateMissingDb()
    {
        using var dir = new TempDir();
        var repo = dir.Sub("repo");
        Directory.CreateDirectory(Path.Combine(repo, ".git"));
        var missingFolder = Path.Combine(dir.Path, "missing");
        var db = Path.Combine(missingFolder, "c.db");

        await using var p = ServerProcess.Start(db, args: ["--doctor"], workingFolder: repo);
        var exit = await p.WaitForExitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(0, exit.ExitCode);
        Assert.Equal("", exit.Stderr);
        foreach (var key in ExpectedKeys)
        {
            Assert.Matches($"(?m)^{Regex.Escape(key)}: .+$", exit.Stdout);
        }
        Assert.Contains("schema version: not created yet (created on first save)", exit.Stdout);
        Assert.Contains("write test: skipped", exit.Stdout);
        Assert.False(File.Exists(db));
        Assert.False(Directory.Exists(missingFolder));
    }

    [Fact]
    public async Task Cli_DoctorWithExtraArg_PrintsUsageToStderr_Exits2()
    {
        using var dir = new TempDir();
        var db = Path.Combine(dir.Path, "c.db");

        await using var p = ServerProcess.Start(db, args: ["--doctor", "extra"]);
        var exit = await p.WaitForExitAsync(TimeSpan.FromSeconds(10));

        Assert.Equal(2, exit.ExitCode);
        Assert.Contains("Usage", exit.Stderr);
        Assert.Equal("", exit.Stdout);
    }
}
