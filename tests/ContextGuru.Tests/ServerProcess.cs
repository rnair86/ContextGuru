using System.Diagnostics;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace ContextGuru.Tests;

/// <summary>Runs the built tool (dotnet ContextGuru.dll from the test output folder) as a child process.</summary>
public sealed record ExitResult(int ExitCode, string Stdout, string Stderr);

public sealed class ServerProcess : IAsyncDisposable
{
    static readonly string Dll = Path.Combine(AppContext.BaseDirectory, "ContextGuru.dll");

    readonly Process _process;

    ServerProcess(Process process) => _process = process;

    public StreamWriter StandardInput => _process.StandardInput;
    public StreamReader StandardOutput => _process.StandardOutput;
    public StreamReader StandardError => _process.StandardError;
    public int Id => _process.Id;

    public static ServerProcess Start(string dbPath, string[]? args = null, string? workingFolder = null, IDictionary<string, string>? env = null)
    {
        var info = new ProcessStartInfo("dotnet")
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            WorkingDirectory = workingFolder ?? AppContext.BaseDirectory,
        };
        info.ArgumentList.Add(Dll);
        foreach (var a in args ?? [])
        {
            info.ArgumentList.Add(a);
        }
        info.Environment["CONTEXTGURU_DB"] = dbPath;
        foreach (var (k, v) in env ?? new Dictionary<string, string>())
        {
            info.Environment[k] = v;
        }
        return new ServerProcess(Process.Start(info)!);
    }

    public static StdioClientTransport Transport(string dbPath, string? workingFolder = null) =>
        new(new StdioClientTransportOptions
        {
            Name = "contextguru-test",
            Command = "dotnet",
            Arguments = [Dll],
            WorkingDirectory = workingFolder ?? AppContext.BaseDirectory,
            EnvironmentVariables = new Dictionary<string, string?> { ["CONTEXTGURU_DB"] = dbPath },
        });

    public bool HasExited => _process.HasExited;

    /// <summary>Reads one stdout line. On timeout the child is killed and the test fails.</summary>
    public async Task<string?> ReadLineAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            return await StandardOutput.ReadLineAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Kill();
            throw Xunit.Sdk.FailException.ForFailure($"No stdout line within {timeout}.");
        }
    }

    /// <summary>Waits for exit while draining stdout and stderr, so a full pipe cannot block the child. On timeout the child is killed and the test fails.</summary>
    public async Task<ExitResult> WaitForExitAsync(TimeSpan? timeout = null)
    {
        var limit = timeout ?? TimeSpan.FromSeconds(30);
        var stdout = StandardOutput.ReadToEndAsync();
        var stderr = StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(limit);
        try
        {
            await _process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Kill();
            throw Xunit.Sdk.FailException.ForFailure($"Process did not exit within {limit}.");
        }
        return new ExitResult(_process.ExitCode, await stdout, await stderr);
    }

    /// <summary>Waits for exit without reading the pipes, for a caller that drains them itself. On timeout the child is killed and the test fails.</summary>
    public async Task WaitUntilExitedAsync(TimeSpan timeout)
    {
        using var cts = new CancellationTokenSource(timeout);
        try
        {
            await _process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            Kill();
            throw Xunit.Sdk.FailException.ForFailure($"Process did not exit within {timeout}.");
        }
    }

    public void Kill() => _process.Kill(entireProcessTree: true);

    public async ValueTask DisposeAsync()
    {
        if (!_process.HasExited)
        {
            Kill();
            await _process.WaitForExitAsync();
        }
        _process.Dispose();
    }
}

/// <summary>An MCP client paired with its server process, draining stderr and killing the process tree on dispose or timeout.</summary>
public sealed class ServerSession : IAsyncDisposable
{
    readonly ServerProcess _server;

    ServerSession(ServerProcess server, McpClient client)
    {
        _server = server;
        Client = client;
    }

    public McpClient Client { get; }

    /// <summary>Starts two server processes back-to-back against the same database, then connects both MCP clients in parallel.</summary>
    public static async Task<(ServerSession First, ServerSession Second)> StartPairAsync(
        string dbPath,
        string? workingFolderA = null,
        string? workingFolderB = null,
        TimeSpan? timeout = null)
    {
        var serverA = ServerProcess.Start(dbPath, workingFolder: workingFolderA);
        ServerProcess serverB;
        try
        {
            serverB = ServerProcess.Start(dbPath, workingFolder: workingFolderB);
        }
        catch
        {
            await serverA.DisposeAsync();
            throw;
        }

        var tA = ConnectAsync(serverA, timeout);
        var tB = ConnectAsync(serverB, timeout);
        try
        {
            var sessions = await Task.WhenAll(tA, tB);
            return (sessions[0], sessions[1]);
        }
        catch
        {
            if (tA.IsCompletedSuccessfully)
            {
                await tA.Result.DisposeAsync();
            }
            if (tB.IsCompletedSuccessfully)
            {
                await tB.Result.DisposeAsync();
            }
            throw;
        }
    }

    static async Task<ServerSession> ConnectAsync(ServerProcess server, TimeSpan? timeout)
    {
        _ = server.StandardError.ReadToEndAsync();
        using var cts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(30));
        try
        {
            var transport = new StreamClientTransport(server.StandardInput.BaseStream, server.StandardOutput.BaseStream);
            var client = await McpClient.CreateAsync(transport, cancellationToken: cts.Token);
            return new ServerSession(server, client);
        }
        catch
        {
            await server.DisposeAsync();
            throw;
        }
    }

    public async ValueTask DisposeAsync()
    {
        try
        {
            await Client.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10));
        }
        catch (TimeoutException)
        {
            // Falls through: the process is killed below.
        }
        finally
        {
            await _server.DisposeAsync();
        }
    }
}
