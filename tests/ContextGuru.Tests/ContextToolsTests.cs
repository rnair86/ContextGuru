using ContextGuru.Core.Models;
using ContextGuru.Core.Storage;
using ContextGuru.Tools;
using Microsoft.Extensions.Logging.Abstractions;

namespace ContextGuru.Tests;

/// <summary>Calls the tool class directly, for cases the stdio client cannot provoke.</summary>
public class ContextToolsTests
{
    const string Valid = "## Goal\ng\n\n## Done\nd\n\n## Decisions\nx\n\n## Open tasks\no\n\n## Next steps\n- n\n";

    [Fact]
    public async Task Save_WithCancelledToken_ThrowsAndWritesNothing()
    {
        using var dir = new TempDir();
        var repo = dir.Sub("repo");
        var db = Path.Combine(dir.Path, "c.db");
        var migration = new StartupMigration(db);
        // The migration is already complete, so only the tool's own cancellation check can stop the save.
        await migration.Completion;
        var store = new SqliteContextStore(db, TimeProvider.System);
        var tools = new ContextTools(store, migration, NullLogger<ContextTools>.Instance);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            tools.SaveContext(null!, "s", Valid, "auth", null, repo, cts.Token));

        var project = new ProjectInfo("repo", repo, repo, ProjectSource.Argument, null);
        Assert.Empty(store.List(project));
    }
}
