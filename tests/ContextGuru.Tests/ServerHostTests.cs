using System.Text.Json;
using ContextGuru.Core.Storage;
using Microsoft.Data.Sqlite;

namespace ContextGuru.Tests;

public class ServerHostTests
{
    const string InitializeRequest =
        """{"jsonrpc":"2.0","id":1,"method":"initialize","params":{"protocolVersion":"2025-06-18","capabilities":{},"clientInfo":{"name":"t","version":"1"}}}""";

    [Fact]
    public async Task UnknownArgument_PrintsUsageToStderr_ExitCode2()
    {
        using var dir = new TempDir();
        await using var p = ServerProcess.Start(Path.Combine(dir.Path, "c.db"), args: ["bogus"]);
        Assert.Equal(2, await p.WaitForExitAsync());
        Assert.Contains("Usage", await p.StandardError.ReadToEndAsync());
        Assert.Equal("", await p.StandardOutput.ReadToEndAsync());
    }

    [Fact]
    public async Task Server_Initializes_AndStdoutHoldsOnlyJsonRpc()
    {
        using var dir = new TempDir();
        await using var p = ServerProcess.Start(Path.Combine(dir.Path, "c.db"));
        await p.StandardInput.WriteLineAsync(InitializeRequest);
        await p.StandardInput.FlushAsync();

        var lines = new List<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(2));
        try
        {
            while (await p.StandardOutput.ReadLineAsync(cts.Token) is { } line)
            {
                lines.Add(line);
            }
        }
        catch (OperationCanceledException)
        {
        }

        Assert.NotEmpty(lines);
        foreach (var line in lines)
        {
            using var doc = JsonDocument.Parse(line);
            Assert.Equal("2.0", doc.RootElement.GetProperty("jsonrpc").GetString());
        }
    }

    [Fact]
    public async Task Server_WritesLogFile_WithProcessId()
    {
        using var dir = new TempDir();
        var db = Path.Combine(dir.Path, "c.db");
        await using var p = ServerProcess.Start(db);
        await p.StandardInput.WriteLineAsync(InitializeRequest);
        await p.StandardInput.FlushAsync();
        await p.StandardOutput.ReadLineAsync();

        // Closing stdin ends the stdio server, which then exits on its own.
        p.StandardInput.Close();
        await p.WaitForExitAsync();

        var log = Path.Combine(dir.Path, "logs", $"contextguru-{DateTime.Now:yyyyMMdd}.log");
        Assert.Contains(p.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), await File.ReadAllTextAsync(log));
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
        await p.StandardInput.WriteLineAsync(InitializeRequest);
        await p.StandardInput.FlushAsync();
        var reply = await p.StandardOutput.ReadLineAsync();
        Assert.Contains("\"result\"", reply);
        await Task.Delay(500);
        Assert.False(p.WaitForExitAsync().IsCompleted);
    }

    [Fact]
    public async Task Server_WithUncreatableDatabaseFolder_StillAnswersInitialize()
    {
        using var dir = new TempDir();
        var file = Path.Combine(dir.Path, "afile");
        await File.WriteAllTextAsync(file, "x");
        // The database folder would be a path below an existing file, so it cannot be created.
        await using var p = ServerProcess.Start(Path.Combine(file, "c.db"));
        await p.StandardInput.WriteLineAsync(InitializeRequest);
        await p.StandardInput.FlushAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var reply = await p.StandardOutput.ReadLineAsync(cts.Token);
        Assert.Contains("\"result\"", reply);
        await Task.Delay(500);
        Assert.False(p.WaitForExitAsync().IsCompleted);
    }

    [Fact]
    public async Task Server_IgnoresMalformedAppSettingsInWorkingFolder()
    {
        using var dir = new TempDir();
        var work = dir.Sub("work");
        await File.WriteAllTextAsync(Path.Combine(work, "appsettings.json"), "{ not json");
        await using var p = ServerProcess.Start(Path.Combine(dir.Path, "c.db"), workingFolder: work);
        await p.StandardInput.WriteLineAsync(InitializeRequest);
        await p.StandardInput.FlushAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Contains("\"result\"", await p.StandardOutput.ReadLineAsync(cts.Token));
    }

    [Fact]
    public async Task Server_IgnoresLoggingEnvOverride_NoRequestTextOnStderr()
    {
        using var dir = new TempDir();
        await using var p = ServerProcess.Start(
            Path.Combine(dir.Path, "c.db"),
            env: new Dictionary<string, string> { ["Logging__Console__LogLevel__Default"] = "Trace" });
        await p.StandardInput.WriteLineAsync(InitializeRequest.Replace("\"capabilities\":{}", "\"capabilities\":{},\"x\":\"SECRETPAYLOAD\""));
        await p.StandardInput.FlushAsync();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        Assert.Contains("\"result\"", await p.StandardOutput.ReadLineAsync(cts.Token));
        p.StandardInput.Close();
        await p.WaitForExitAsync();
        Assert.DoesNotContain("SECRETPAYLOAD", await p.StandardError.ReadToEndAsync());
    }
}
