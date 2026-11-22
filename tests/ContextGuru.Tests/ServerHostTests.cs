using System.Globalization;
using System.Text.Json;
using ContextGuru.Core.Storage;
using Microsoft.Data.Sqlite;

namespace ContextGuru.Tests;

public class ServerHostTests
{
    static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    const string InitializeRequest =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""";

    static async Task Send(ServerProcess p, string line)
    {
        await p.StandardInput.WriteLineAsync(line);
        await p.StandardInput.FlushAsync();
    }

    /// <summary>Reads the reply to InitializeRequest and checks it is the result for request id 1.</summary>
    static async Task AssertInitializeResult(ServerProcess p)
    {
        var reply = await p.ReadLineAsync(Wait);
        Assert.NotNull(reply);
        using var doc = JsonDocument.Parse(reply);
        Assert.Equal(1, doc.RootElement.GetProperty("id").GetInt32());
        Assert.True(doc.RootElement.TryGetProperty("result", out _), reply);
    }

    [Fact]
    public async Task UnknownArgument_PrintsUsageToStderr_ExitCode2()
    {
        using var dir = new TempDir();
        await using var p = ServerProcess.Start(Path.Combine(dir.Path, "c.db"), args: ["bogus"]);
        var exit = await p.WaitForExitAsync();
        Assert.Equal(2, exit.ExitCode);
        Assert.Contains("Usage", exit.Stderr);
        Assert.Equal("", exit.Stdout);
    }

    [Fact]
    public async Task Server_Initializes_AndStdoutHoldsOnlyJsonRpc()
    {
        using var dir = new TempDir();
        await using var p = ServerProcess.Start(Path.Combine(dir.Path, "c.db"));
        await Send(p, InitializeRequest);
        await AssertInitializeResult(p);

        // Ending stdin stops the server, so stdout is complete once it exits.
        p.StandardInput.Close();
        var exit = await p.WaitForExitAsync();
        foreach (var line in exit.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        }
    }

    [Fact]
    public async Task Server_WritesLogFile_WithProcessId()
    {
        using var dir = new TempDir();
        await using var p = ServerProcess.Start(Path.Combine(dir.Path, "c.db"));
        await Send(p, InitializeRequest);
        await AssertInitializeResult(p);

        // Closing stdin ends the stdio server, which then exits on its own.
        p.StandardInput.Close();
        await p.WaitForExitAsync();

        var log = Path.Combine(dir.Path, "logs", $"contextguru-{DateTime.Now:yyyyMMdd}.log");
        Assert.Contains(p.Id.ToString(CultureInfo.InvariantCulture), await File.ReadAllTextAsync(log));
    }

    [Fact]
    public async Task Server_StartedAgainstNewerSchema_StaysAlive()
    {
        using var dir = new TempDir();
        var db = Path.Combine(dir.Path, "c.db");
        using (var c = Migrations.Open(db))
        {
            using var cmd = new SqliteCommand("PRAGMA user_version=99", c);
            cmd.ExecuteNonQuery();
        }
        await using var p = ServerProcess.Start(db);
        await Send(p, InitializeRequest);
        await AssertInitializeResult(p);
        await Task.Delay(500);
        Assert.False(p.HasExited);
    }

    [Fact]
    public async Task Server_WithUncreatableDatabaseFolder_StillAnswersInitialize()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "afile");
        await File.WriteAllTextAsync(file, "x");
        // The database folder would be a path below an existing file, so it cannot be created.
        await using var p = ServerProcess.Start(Path.Combine(file, "c.db"));
        await Send(p, InitializeRequest);
        await AssertInitializeResult(p);
        await Task.Delay(500);
        Assert.False(p.HasExited);
    }

    [Fact]
    public async Task Server_IgnoresMalformedAppSettingsInWorkingFolder()
    {
        using var dir = new TempDir();
        var work = dir.Sub("work");
        await File.WriteAllTextAsync(Path.Combine(work, "appsettings.json"), "{ not json");
        await using var p = ServerProcess.Start(Path.Combine(dir.Path, "c.db"), workingFolder: work);
        await Send(p, InitializeRequest);
        await AssertInitializeResult(p);
    }

    [Fact]
    public async Task Server_IgnoresLoggingEnvOverride_NoRequestTextOnStderr()
    {
        using var dir = new TempDir();
        await using var p = ServerProcess.Start(
            Path.Combine(dir.Path, "c.db"),
            env: new Dictionary<string, string> { ["Logging__Console__LogLevel__Default"] = "Trace" });
        await Send(p, InitializeRequest.Replace("\"capabilities\":{}", "\"capabilities\":{},\"x\":\"SECRETPAYLOAD\""));
        await AssertInitializeResult(p);
        p.StandardInput.Close();
        var exit = await p.WaitForExitAsync();
        Assert.DoesNotContain("SECRETPAYLOAD", exit.Stderr);
    }
}
