using System.Diagnostics;
using ModelContextProtocol.Client;

namespace ContextGuru.Tests;

/// <summary>Runs the built tool (dotnet ContextGuru.dll from the test output folder) as a child process.</summary>
public sealed class ServerProcess : IAsyncDisposable
{
    static readonly string Dll = Path.Combine(AppContext.BaseDirectory, "ContextGuru.dll");

    readonly Process _process;

    ServerProcess(Process process) => _process = process;

    public StreamWriter StandardInput => _process.StandardInput;
    public StreamReader StandardOutput => _process.StandardOutput;
    public StreamReader StandardError => _process.StandardError;
    public int Id => _process.Id;

    public static ServerProcess Start(string dbPath, string[]? args = null, string? workingFolder = null)
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

    public Task<int> WaitForExitAsync() => _process.WaitForExitAsync().ContinueWith(_ => _process.ExitCode, TaskScheduler.Default);

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
