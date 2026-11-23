using ContextGuru;
using ContextGuru.Core.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

if (args.Length > 0)
{
    // --doctor, install and uninstall are added in Tasks 9 and 12.
    Console.Error.WriteLine("Usage: contextguru");
    return 2;
}

// A path that cannot be prepared must not stop the server: the failure surfaces through StartupMigration.Completion.
string? dbPath = null;
StartupMigration migration;
try
{
    dbPath = SqliteContextStore.ResolveDatabasePath(Environment.GetEnvironmentVariable("CONTEXTGURU_DB"));
    migration = new StartupMigration(dbPath);
}
catch (Exception e)
{
    migration = new StartupMigration(e);
}

// No default configuration sources: project settings and logging env overrides must not change the server.
var builder = Host.CreateEmptyApplicationBuilder(new HostApplicationBuilderSettings { Args = args });
ConfigureLogging(builder, dbPath);
builder.Services.AddMcpServer().WithStdioServerTransport();
if (dbPath is not null)
{
    builder.Services.AddSingleton(new SqliteContextStore(dbPath, TimeProvider.System));
}
builder.Services.AddSingleton(migration);

var host = builder.Build();
var logger = host.Services.GetRequiredService<ILogger<StartupMigration>>();
var migrationFailed = LoggerMessage.Define<string>(LogLevel.Error, 0, "Migration failed: {Message}");
// A failed migration (for example a newer schema) must not stop the server: tools report it.
_ = migration.Completion.ContinueWith(
    t => migrationFailed(logger, t.Exception!.GetBaseException().Message, null),
    TaskContinuationOptions.OnlyOnFaulted);
await host.RunAsync();
return 0;

// Console goes to stderr only: stdout carries the protocol. The file log sits next to the database, when there is one.
static void ConfigureLogging(HostApplicationBuilder builder, string? dbPath)
{
    builder.Logging.ClearProviders();
    builder.Logging.AddConsole(o => o.LogToStandardErrorThreshold = LogLevel.Trace);
    if (dbPath is null)
    {
        return;
    }
    var logPath = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(dbPath))!, "logs", "contextguru-.log");
    var serilog = new LoggerConfiguration()
        .MinimumLevel.Information()
        .Enrich.WithProperty("ProcessId", Environment.ProcessId)
        .WriteTo.File(
            logPath,
            formatProvider: System.Globalization.CultureInfo.InvariantCulture,
            outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] [{ProcessId}] {Message:lj}{NewLine}{Exception}",
            rollingInterval: RollingInterval.Day,
            fileSizeLimitBytes: null,
            retainedFileCountLimit: 7,
            shared: true)
        .CreateLogger();
    builder.Logging.AddSerilog(serilog, dispose: true);
}
