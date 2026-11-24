# ContextGuru Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build ContextGuru, a local C# MCP server that saves and reads short coding-agent handoffs in a shared SQLite file, with four tools, an installer for Claude Code, Codex and Cursor, and a doctor mode.

**Architecture:** Two projects plus tests. `ContextGuru.Core` (class library, no MCP, no logging) holds the project resolver, the handoff template checker, `IContextStore` with its SQLite store, and migrations. `ContextGuru` (console app packed as a `dotnet tool`) holds the MCP host over stdio, the four tools, `--doctor`, and `install`/`uninstall`. `ContextGuru.Tests` (xUnit) tests Core directly and the tool through a real stdio process.

**Tech Stack:** C#, .NET 10 (`net10.0`), `Microsoft.Data.Sqlite`, `ModelContextProtocol`, `Microsoft.Extensions.Hosting`, `Serilog.Extensions.Hosting`, `Serilog.Sinks.File`, xUnit.

**Spec:** `docs/designs/contextguru.md` (the approved EGM design). Read it with this plan. Where the plan says "per spec", the spec text is the contract.

## How every task runs (red, green, refactor)

Every task has five steps: **1 Red** (write the failing test), **2 Verify red** (run it, see the expected failure), **3 Green** (smallest code that passes), **4 Verify green** (run it, see it pass), **5 Refactor and commit** (clean up with tests still green, `dotnet format`, commit). Two tasks are not pure code (Task 0 the spike, Task 15 acceptance). Their "test" is a checklist whose items start unchecked or `UNKNOWN`, and green means every item has evidence.

Test commands used below (`<entry>` is `ContextGuru.slnx`):

*   All tests: `dotnet test <entry> -c Release`
*   One class: `dotnet test tests/ContextGuru.Tests -c Release --filter "FullyQualifiedName~<ClassName>"`
*   Format check: `dotnet format <entry> --verify-no-changes`

If the spike selects xUnit v3 with native Microsoft Testing Platform on SDK 10, the same commands become `dotnet test --solution <entry> -c Release` and `dotnet test --project tests/ContextGuru.Tests -c Release --filter-class "*<ClassName>"`. Record the working forms in `docs/spike.md` and use them from then on.

Commit messages are short sentences. End each with `Co-Authored-By: Claude Sonnet 5.5 <noreply@anthropic.com>`.

## Global Constraints

*   Target `net10.0`, nullable enabled, implicit usings, file-scoped namespaces. `AnalysisLevel` `latest-recommended`, `TreatWarningsAsErrors` true, NuGet audit warnings NU1901 to NU1904 not errors.
*   `ContextGuru.Core` references `Microsoft.Data.Sqlite` only: no MCP package, no logging package. `IContextStore` methods are synchronous.
*   **Package gate:** the author approves each package before it is installed (CLAUDE.md). Ask once at the start of Task 0 for the full list in Tech Stack plus the xUnit packages, and again before any TOML package (Task 13).
*   Names: trim, `ToLowerInvariant`, match `[a-z0-9][a-z0-9_-]{0,63}`.
*   Size caps by `string.Length` of trimmed text: summary 200, body 20,000. Constants in `HandoffTemplate.cs` only.
*   Required body headings, once each, in order: `Goal`, `Done`, `Decisions`, `Open tasks`, `Next steps`.
*   Timestamps: UTC text `yyyy-MM-ddTHH:mm:ss.fffZ`. The store reads time from `TimeProvider`. Latest is `ORDER BY updated_at DESC, id DESC`.
*   SQLite connections: `Pooling=False`, `Default Timeout=5`, then `PRAGMA busy_timeout=5000`. WAL set once at first open. Every write is one short `BEGIN IMMEDIATE` transaction. `SqliteException` codes 5 and 6 become `DatabaseBusyException`.
*   Wire format: snake_case through `[JsonPropertyName]`, nulls sent as `null`, errors as `isError: true` with one plain text message. Error texts are exactly the table in the spec.
*   In server mode nothing but the protocol reaches stdout. Never log handoff content.
*   Tests run on Windows. Linux and macOS are not run in version 1.
*   Style rules from CLAUDE.md: simple and incremental, no defensive code, no emojis, short README, do not delete files without asking.
*   .NET idioms to use where they fit: primary constructors, collection expressions, `[GeneratedRegex]`, `TimeProvider`, `ArgumentException.ThrowIfNullOrWhiteSpace`, `FileSystemInfo.ResolveLinkTarget`, `File.SetUnixFileMode` and `UnixFileMode`, `System.Text.Json.Nodes`.

## Review Focus

Input classes the spec implies but does not test directly. Each has a test in the task named.

1.  A body whose required heading sits after an unclosed code fence: the rest of the body is ignored, so the save is rejected with "Missing required heading" (Task 2).
2.  A `projectPath` with a trailing separator, forward slashes or a different drive-letter case: must give the same key as the plain path (Task 3).
3.  `CONTEXTGURU_DB` pointing into a folder that does not exist yet: the folder is created and the store works (Task 5).
4.  A saved body that contains a forged `<<<HANDOFF 00000000 END>>>` line: stored unchanged, and the response wrapper carries a different random id with the real END line last (Task 7).
5.  Any tool on a project with nothing saved, or on a database file that does not exist yet: empty list, `found: false`, `removed: false`, never an error (Task 8 and Task 9).

## File Structure

| Path | Responsibility | Task |
|------|----------------|------|
| `docs/spike.md`, `spike/` | Spike results, throwaway server | 0 |
| `ContextGuru.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `.gitignore` | Build setup | 0, 1 |
| `src/ContextGuru.Core/Models/ContextRecord.cs` | Records and `ProjectSource` enum | 1 |
| `src/ContextGuru.Core/Template/HandoffTemplate.cs` | Heading, size, name rules | 2 |
| `src/ContextGuru.Core/Projects/ProjectResolver.cs` | Project identity | 3 |
| `src/ContextGuru.Core/Storage/Migrations.cs`, `StorageExceptions.cs` | Connection, schema, exceptions | 4 |
| `src/ContextGuru.Core/Storage/IContextStore.cs`, `SqliteContextStore.cs` | Store | 5 |
| `src/ContextGuru/Program.cs`, `StartupMigration.cs` | Routing, logging, background migration | 6 |
| `src/ContextGuru/Tools/Results.cs`, `ContextTools.cs` | Four MCP tools and wire types | 7, 8 |
| `src/ContextGuru/Doctor.cs` | `--doctor` | 9 |
| `src/ContextGuru/Install/*` | Templates, targets, installer | 11, 12, 13 |
| `local/build.ps1`, `local/install.ps1`, `README.md` | Local build and docs | 14 |
| `tests/ContextGuru.Tests/*` | Tests and helpers | all |

---

### Task 0: Spike (phase 0)

**Files:**
- Create: `docs/spike.md`
- Create: `.gitignore` (one line `spike/` for now; Task 1 completes it)
- Create: `spike/` (throwaway console server, git-ignored)
- Modify: `docs/designs/contextguru.md` (Open Questions and Status, after the spike)

**Interfaces:**
- Produces: facts that later tasks read from `docs/spike.md`: package versions, SDK API names, per-agent command location, name form, argument syntax, MCP config path and format, marker comment placement, working test commands, the transaction-start decision, and the logging-sink decision.

- [x] **Step 1: Red. Write `docs/spike.md` as a checklist where every answer is `UNKNOWN`**

One section per question from spec item 1, each with `Result: UNKNOWN`:
  1. Per agent (Claude Code, Codex, Cursor) on Windows: pass or fail for "a custom command passes the agent folder to a tool", MCP roots support, server working folder at start, command file location and name form, argument syntax, works without a prefix such as `/prompts:`, MCP config file path and format, where a marker comment can go.
  2. `ModelContextProtocol` version and API: stdio server registration, tool attributes, the destructive annotation, requesting roots, how a thrown exception becomes `isError`, the client class used by tests.
  3. `Microsoft.Data.Sqlite`: does `BeginTransaction()` issue `BEGIN IMMEDIATE`; how `Default Timeout` and `busy_timeout` interact; bundled SQLite version (needs 3.35 or later for `DELETE ... RETURNING`).
  4. Which xUnit generation `dotnet new xunit` produces and which `dotnet test` command forms work.
  5. Does `Serilog.Sinks.File` support `shared: true` with daily rolling across two processes.
  6. Does Codex need TOML for its MCP config.
  7. Package versions to pin in `Directory.Packages.props`.

- [x] **Step 2: Verify red**

Run: `rg -c "UNKNOWN" docs/spike.md`
Expected: a count of 7 or more.

- [x] **Step 3: Green. Get package approval, then run the experiments**

Ask the author to approve the package list. Build the throwaway server in `spike/` and a throwaway command in each agent that calls it with the agent folder. Fill every `UNKNOWN` with an observed result and, for each failing agent, the decision to drop it or rely on the roots and working-folder fallbacks. Add the exact versions as a table.

- [x] **Step 4: Verify green**

Run: `rg -c "UNKNOWN" docs/spike.md`
Expected: `0` matches (the command prints nothing or 0). At least two of three agents are marked pass. If fewer, stop and take the result to the author.

- [x] **Step 5: Refactor and commit**

Ask the author before deleting `spike/`. Then update the Open Questions in `docs/designs/contextguru.md` to match the results and set Status as the author decides. Commit `docs/spike.md`, `.gitignore` and the design edit with "Record spike results".

---

### Task 1: Skeleton and Core types

**Files:**
- Create: `ContextGuru.slnx`, `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`
- Modify: `.gitignore` (add `bin/`, `obj/`, test results, local database files, `logs/`, `local/nupkg/`)
- Create: `src/ContextGuru.Core/ContextGuru.Core.csproj`
- Create: `src/ContextGuru.Core/Models/ContextRecord.cs`
- Create: `src/ContextGuru/ContextGuru.csproj`, `src/ContextGuru/Program.cs` (a `return 0;` body for now)
- Create: `tests/ContextGuru.Tests/ContextGuru.Tests.csproj`
- Test: `tests/ContextGuru.Tests/ModelsTests.cs`

**Interfaces:**
- Produces (namespace `ContextGuru.Core.Models`, all in `ContextRecord.cs`):

```csharp
public enum ProjectSource { Argument, Roots, Cwd }
public sealed record ProjectInfo(string Name, string Path, string Key, ProjectSource Source, string? Warning);
public sealed record ContextRecord(string Name, string Summary, string Body, string? SourceAgent, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record ContextListItem(string Name, string Summary, DateTimeOffset UpdatedAt, string? SourceAgent);
public sealed record SaveResult(string Name, bool Overwritten, DateTimeOffset? PreviousUpdatedAt, string? PreviousSummary);
public sealed record ValidationError(string Field, string Message);
```

- [x] **Step 1: Red. Scaffold and write the test**

Create the three projects and the slnx with `dotnet new` and `dotnet sln add` (follow the scaffold-dotnet-test-project skill: use the xUnit generation chosen in the spike, delete the template sample test, reference Core and the tool project from the test project, no package versions in csprojs because `Directory.Packages.props` supplies them). Write `ModelsTests`:

```csharp
[Fact] public void ProjectSource_HasThreeValuesInOrder() =>
    Assert.Equal(["Argument", "Roots", "Cwd"], Enum.GetNames<ProjectSource>());

[Fact] public void SaveResult_PreviousFieldsCanBeNull()
{
    var r = new SaveResult("a", false, null, null);
    Assert.False(r.Overwritten);
    Assert.Null(r.PreviousUpdatedAt);
}
```

- [x] **Step 2: Verify red**

Run: `dotnet build ContextGuru.slnx -c Release`
Expected: FAIL with CS0246 for `ProjectSource` and `SaveResult`.

- [x] **Step 3: Green. Add the records and the build files**

Write `ContextRecord.cs` exactly as the Interfaces block. Fill `Directory.Build.props` (items 4 of the spec: `net10.0`, nullable, implicit usings, `AnalysisLevel`, `TreatWarningsAsErrors`, `WarningsNotAsErrors` NU1901 to NU1904), `global.json` (10.0.x, `rollForward` `latestFeature`), `Directory.Packages.props` (`ManagePackageVersionsCentrally` true and the versions from `docs/spike.md`), `.editorconfig` (indentation, file-scoped namespaces, `var` use). Tool csproj: `PackAsTool` true, `ToolCommandName` `contextguru`, `PackageId` `ContextGuru`, `Version` `0.1.0`, references to Core, `ModelContextProtocol`, `Microsoft.Extensions.Hosting`, `Serilog.Extensions.Hosting`, `Serilog.Sinks.File`. Core csproj references `Microsoft.Data.Sqlite` only.

- [x] **Step 4: Verify green**

Run: `dotnet build ContextGuru.slnx -c Release` then `dotnet test ContextGuru.slnx -c Release`
Expected: build with 0 warnings, 2 tests pass.

- [x] **Step 5: Refactor and commit**

Run `dotnet format ContextGuru.slnx` and `dotnet format ContextGuru.slnx --verify-no-changes` (exit 0). Commit: "Add solution skeleton and Core types".

---

### Task 2: HandoffTemplate

**Files:**
- Create: `src/ContextGuru.Core/Template/HandoffTemplate.cs`
- Test: `tests/ContextGuru.Tests/HandoffTemplateTests.cs`

**Interfaces:**
- Consumes: `ValidationError` (Task 1).
- Produces:

```csharp
public static class HandoffTemplate
{
    public const int MaxSummaryLength = 200;
    public const int MaxBodyLength = 20_000;
    public static IReadOnlyList<ValidationError> Validate(string summary, string body); // Field is "summary", "body"
    public static bool NextStepsHasContent(string body);
    public static string NormalizeName(string name);   // trim + ToLowerInvariant
    public static bool IsValidName(string name);       // [GeneratedRegex] [a-z0-9][a-z0-9_-]{0,63}, on a normalized name
    public static string BadNameMessage(string name);  // the "Bad name" row of the spec table
}
```

- [x] **Step 1: Red. Write the tests**

```csharp
const string Valid = "## Goal\nG\n## Done\nD\n## Decisions\nX\n## Open tasks\nO\n## Next steps\nN\n";

[Fact] public void Validate_ValidBody_ReturnsNoErrors() => Assert.Empty(HandoffTemplate.Validate("s", Valid));

[Fact] public void Validate_MissingDecisions_NamesIt()
{
    var e = Assert.Single(HandoffTemplate.Validate("s", Valid.Replace("## Decisions\nX\n", "")));
    Assert.Equal("Missing required heading: \"## Decisions\". Required headings, in order: Goal, Done, Decisions, Open tasks, Next steps.", e.Message);
}

[Fact] public void Validate_DuplicateGoal_Reports() =>
    Assert.Contains(HandoffTemplate.Validate("s", Valid + "## Goal\nagain\n"), e => e.Message == "Heading \"## Goal\" appears more than once.");

[Fact] public void Validate_DoneBeforeGoal_IsOutOfOrder() =>
    Assert.Contains(HandoffTemplate.Validate("s", "## Done\nD\n## Goal\nG\n## Decisions\nX\n## Open tasks\nO\n## Next steps\nN\n"),
        e => e.Message == "Heading \"## Done\" is out of order. Required order: Goal, Done, Decisions, Open tasks, Next steps.");

[Fact] public void Validate_HeadingInsideFence_IsIgnored() =>   // both ``` and ~~~
    Assert.Contains(HandoffTemplate.Validate("s", Valid.Replace("## Done\nD\n", "```\n## Done\n```\n")), e => e.Message.StartsWith("Missing required heading: \"## Done\""));

[Fact] public void Validate_UnclosedFence_IgnoresRest() =>      // Review Focus 1
    Assert.Contains(HandoffTemplate.Validate("s", "## Goal\nG\n```\n## Done\nD\n## Decisions\nX\n## Open tasks\nO\n## Next steps\nN\n"),
        e => e.Message.StartsWith("Missing required heading: \"## Done\""));
```

Also, each as its own `[Fact]` or `[Theory]` with the exact outcome: extra level-two headings allowed; `###` headings do not count; padded and mixed-case heading (`"##  goal  "`) accepted; `\r\n` body accepted; summary `""` gives `Summary is empty.`; 200 chars ok; 201 chars gives `Summary is 201 characters; the maximum is 200.`; summary with `\n` gives `Summary must be one line.` (checked on the untrimmed text, so an edge newline counts, and reported together with the length error); body `""` gives `Body is empty.` plus a missing-heading error for each of the five headings; 20,000 ok; 20,001 gives `Body is 20001 characters; the maximum is 20000.`; `NextStepsHasContent` false for `none`, `n/a`, `- none`, `* N/A.`, `None.`, empty and true for `Run the tests` (the whole trimmed section is judged, so `none` and `n/a` on separate lines or `-- none` count as content); `NormalizeName("  Auth-Refactor ")` is `auth-refactor`; `IsValidName` true for a 64-char name and false for 65 chars, `-x`, `a b`, `café`, `""`; `BadNameMessage("A b")` is `Name "A b" is not valid. Use lower-case letters, digits, "-" and "_", starting with a letter or digit, up to 64 characters.`

- [x] **Step 2: Verify red**

Run: `dotnet test tests/ContextGuru.Tests -c Release --filter "FullyQualifiedName~HandoffTemplateTests"`
Expected: FAIL to build, `HandoffTemplate` does not exist.

- [x] **Step 3: Green. Implement `HandoffTemplate`**

One pass over lines with a fence flag, splitting on `\r?\n`. Fence opens on a line starting with ``` or ~~~ and closes on the same marker. Collect level-two headings (line starts `## `, trimmed text compared case-insensitively) outside fences, then check missing, duplicate and order, then sections for `NextStepsHasContent`. Messages are exactly the spec table, numbers from the constants. Settled in review: a heading must start at column 0 (an indented `## Goal` is not a heading); order is judged on first occurrences, and a repeated heading that returns after a later section also gets an out-of-order error next to the duplicate error; an empty body still reports every missing heading.

- [x] **Step 4: Verify green**

Run: the same filter command.
Expected: PASS, all tests in `HandoffTemplateTests`.

- [x] **Step 5: Refactor and commit**

Extract the line scan into a private `ParseHeadings(string body)` shared by `Validate` and `NextStepsHasContent`. Run the class tests again, then `dotnet format`. Commit: "Add handoff template checker".

---

### Task 3: ProjectResolver

**Files:**
- Create: `src/ContextGuru.Core/Projects/ProjectResolver.cs`
- Test: `tests/ContextGuru.Tests/ProjectResolverTests.cs`
- Create: `tests/ContextGuru.Tests/TempDir.cs` (helper: `IDisposable`, creates a unique folder under the temp path, `Path`, `Sub(string)`, deletes itself on dispose)

**Interfaces:**
- Consumes: `ProjectInfo`, `ProjectSource` (Task 1).
- Produces:

```csharp
public sealed class InvalidProjectPathException() : Exception("projectPath must be an absolute path to an existing folder.");
public static class ProjectResolver
{
    public static ProjectInfo Resolve(string? projectPath, IReadOnlyList<string> rootPaths, string serverWorkingFolder);
    public static string MakeKey(string realPath, bool isWindows);
    public static IReadOnlyList<string> RootPathsFromUris(IEnumerable<string> uris); // file: URIs only, via new Uri(uri).LocalPath
}
```

- [x] **Step 1: Red. Write the tests (temp folders from `TempDir`)**

```csharp
[Fact] public void Resolve_ArgumentBeatsRootsAndCwd()
{
    using var t = new TempDir();
    var a = t.Sub("a"); var b = t.Sub("b"); var c = t.Sub("c");
    var p = ProjectResolver.Resolve(a, [b], c);
    Assert.Equal(ProjectSource.Argument, p.Source);
    Assert.Equal("a", p.Name);
}

[Theory]
[InlineData(@"C:\Users\Me\Repo\", @"c:\users\me\repo")]
[InlineData(@"C:/Users/Me/Repo", @"c:\users\me\repo")]
[InlineData(@"C:\", @"c:\")]
[InlineData(@"\\Server\Share\Repo", @"\\server\share\repo")]
public void MakeKey_Windows(string path, string key) => Assert.Equal(key, ProjectResolver.MakeKey(path, true));   // Review Focus 2

[Fact] public void MakeKey_Linux_IsUnchanged() => Assert.Equal("/home/Me/Repo", ProjectResolver.MakeKey("/home/Me/Repo", false));
```

Also: roots beat cwd (`Roots`); first of several roots wins; a subfolder resolves to the folder that holds `.git` (directory and also a `.git` file); no `.git` gives the starting folder; whitespace-only `projectPath` counts as not given; a relative path, a missing folder and a file path each throw `InvalidProjectPathException`; `RootPathsFromUris(["file:///C:/My%20Repo", "https://x"])` returns one path ending in `My Repo`; the user profile folder gives `Warning` equal to `Project root is the home folder or a filesystem root, so contexts are scoped to it.` and the same for `Path.GetPathRoot(...)`, whose `Name` is the root path itself; a normal repo has `Warning` null; a symlinked folder and a symlink in a parent folder resolve to the real path (create the link in the test; skip dynamically if the OS refuses); trailing-separator and mixed-case spellings of one folder give the same `Key` on Windows.

- [x] **Step 2: Verify red**

Run: `dotnet test tests/ContextGuru.Tests -c Release --filter "FullyQualifiedName~ProjectResolverTests"`
Expected: FAIL to build, `ProjectResolver` does not exist.

- [x] **Step 3: Green. Implement per the spec's Project identity section**

Source order argument, first root, working folder. Validate `projectPath` with `Path.IsPathFullyQualified` and `Directory.Exists`. Resolve links segment by segment with `FileSystemInfo.ResolveLinkTarget(returnFinalTarget: true)`. Walk up with `Directory.Exists(.git) || File.Exists(.git)`, no `git` process. `MakeKey` takes `isWindows` as a parameter. Settled in review: trim a link target with `Path.TrimEndingDirectorySeparator` so a link to a filesystem root keeps its root; skip the home-folder check when the OS reports no profile folder; `RootPathsFromUris` keeps only strings that start with `file:`; the link tests fail, not pass quietly, when no link can be made (xUnit v2 has no dynamic skip). Known and deferred: on Unix, links inside a link target and `..` after a link are not resolved.

- [x] **Step 4: Verify green**

Run: the same filter command.
Expected: PASS (symlink tests may report skipped, nothing fails).

- [x] **Step 5: Refactor and commit**

Split link resolution into a private `ResolveRealPath` and the walk into `FindProjectRoot`. Re-run the class tests, `dotnet format`. Commit: "Add project resolver".

---

### Task 4: Migrations and storage exceptions

**Files:**
- Create: `src/ContextGuru.Core/Storage/Migrations.cs`, `src/ContextGuru.Core/Storage/StorageExceptions.cs`
- Test: `tests/ContextGuru.Tests/MigrationTests.cs`
- Create: `tests/ContextGuru.Tests/Expected/schema-v1.sql` (embedded resource in the test project)

**Interfaces:**
- Produces:

```csharp
public sealed class DatabaseBusyException(Exception inner) : Exception("Database busy, retry.", inner);
public sealed class SchemaTooNewException(int found, int supported)
    : Exception($"The database schema is version {found}, but this tool supports up to {supported}. Update this tool's install.")
{ public int Found { get; } = found; public int Supported { get; } = supported; }

public static class Migrations
{
    public const int Latest = 1;
    public static SqliteConnection Open(string dbPath);      // Pooling=False; Default Timeout=5; PRAGMA busy_timeout=5000; opened
    public static void Run(string dbPath);                   // first-open WAL, BEGIN IMMEDIATE, check, migrate, set user_version, commit
    public static int ReadUserVersion(SqliteConnection c);
}
```

- [x] **Step 1: Red. Write the tests (each uses a temp database path from `TempDir`)**

```csharp
[Fact] public void Run_NewDatabase_CreatesSchemaAtLatest()
{
    using var t = new TempDir(); var db = Path.Combine(t.Path, "c.db");
    Migrations.Run(db);
    using var c = Migrations.Open(db);
    Assert.Equal(Migrations.Latest, Migrations.ReadUserVersion(c));
    Assert.Equal("wal", new SqliteCommand("PRAGMA journal_mode", c).ExecuteScalar());
}

[Fact] public void Run_NewerUserVersion_Throws()
{
    // create db, set PRAGMA user_version = 99, then:
    var ex = Assert.Throws<SchemaTooNewException>(() => Migrations.Run(db));
    Assert.Equal((99, 1), (ex.Found, ex.Supported));
}
```

Also: running twice changes nothing (same `user_version`, same schema); the normalized SQL from `sqlite_master` (collapse whitespace, one statement per line, sorted by name) equals the embedded `schema-v1.sql`, which holds the `contexts` table and `contexts_latest` index from the spec's Data model.

- [x] **Step 2: Verify red**

Run: `dotnet test tests/ContextGuru.Tests -c Release --filter "FullyQualifiedName~MigrationTests"`
Expected: FAIL to build, `Migrations` does not exist.

- [x] **Step 3: Green. Implement**

`Open` sets the connection string and runs the busy-timeout pragma. `Run` follows the spec's migration steps in one `BEGIN IMMEDIATE` transaction; the WAL pragma runs before it, once, when the database has no `user_version` yet. Migration 1 creates the table and index from the spec. Use the transaction-start decision recorded in `docs/spike.md`. As built: `Migrations.Latest` is a static property (`Steps.Length`), not a `const`, so it cannot drift from the migration array; `ReadUserVersion` has an optional `SqliteTransaction? tx` so the read joins the transaction; an extra test checks that `Open` sets `busy_timeout` to 5000.

- [x] **Step 4: Verify green**

Run: the same filter command.
Expected: PASS.

- [x] **Step 5: Refactor and commit**

Make the migration list a private array of SQL strings indexed by version so adding migration 2 is one line. Re-run, `dotnet format`. Commit: "Add schema migrations".

---

### Task 5: Store

**Files:**
- Create: `src/ContextGuru.Core/Storage/IContextStore.cs`, `src/ContextGuru.Core/Storage/SqliteContextStore.cs`
- Test: `tests/ContextGuru.Tests/SqliteContextStoreTests.cs`
- Create: `tests/ContextGuru.Tests/FixedTimeProvider.cs` (a `TimeProvider` subclass with a settable `Now`, no package needed)

**Interfaces:**
- Consumes: Task 1 records, Task 4 `Migrations`, exceptions.
- Produces:

```csharp
public interface IContextStore
{
    SaveResult Save(ProjectInfo project, string? name, string summary, string body, string? sourceAgent);
    ContextRecord? GetLatest(ProjectInfo project);
    ContextRecord? Get(ProjectInfo project, string name);
    IReadOnlyList<ContextListItem> List(ProjectInfo project);        // newest first, no body
    ContextRecord? Remove(ProjectInfo project, string name);          // deleted record or null
}
public sealed class SqliteContextStore(string dbPath, TimeProvider time) : IContextStore
{
    public static string ResolveDatabasePath(string? envOverride); // envOverride is the CONTEXTGURU_DB value or null
}
```
The store does not migrate. Callers run `Migrations.Run` first.

- [x] **Step 1: Red. Write the tests (temp database, `Migrations.Run` in the fixture)**

```csharp
[Fact] public void Save_Overwrite_KeepsIdAndCreatedAtAndReportsPrevious()
{
    time.Now = T0; var first = store.Save(P, "auth", "old summary", Body, "claude");
    time.Now = T0.AddMinutes(5); var second = store.Save(P, "auth", "new summary", Body, "codex");
    Assert.True(second.Overwritten);
    Assert.Equal(("old summary", T0), (second.PreviousSummary, second.PreviousUpdatedAt));
    var rec = store.Get(P, "auth")!;
    Assert.Equal((T0, T0.AddMinutes(5)), (rec.CreatedAt, rec.UpdatedAt));
}

[Fact] public void Save_AutoName_UsesUtcStampThenSuffix()
{
    time.Now = new DateTimeOffset(2026, 11, 21, 10, 30, 5, TimeSpan.Zero);
    Assert.Equal("20261121-103005", store.Save(P, null, "s", Body, null).Name);
    Assert.Equal("20261121-103005-2", store.Save(P, null, "s", Body, null).Name);
}

[Fact] public void Save_AutoName_NeverOverwritesUserNamedContext() { /* user saves "20261121-103005", then auto save: name is "...-2", first row intact */ }

[Fact] public void Save_WhileAnotherConnectionHoldsWriteLock_ThrowsDatabaseBusy_AfterAbout5s()
{   // connection B runs BEGIN IMMEDIATE; assert Assert.Throws<DatabaseBusyException>, elapsed >= 4s
}

[Fact] public void ResolveDatabasePath_CreatesMissingFolder()   // Review Focus 3
{
    var path = Path.Combine(t.Path, "new", "deeper", "c.db");
    Assert.Equal(path, SqliteContextStore.ResolveDatabasePath(path));
    Assert.True(Directory.Exists(Path.GetDirectoryName(path)));
}
```

Also: `Get` found and not found; same name in two projects stays separate; `GetLatest` order with a fixed clock and the `id DESC` tie-break at equal `updated_at`; `List` newest first with no body; `Remove` returns the record then null on the second call; `ResolveDatabasePath(null)` ends with `ContextGuru/contexts.db` under local app data; on Unix only, folder mode 0700 and file mode 0600 after the first save (guard with `OperatingSystem.IsWindows()` and return early).

- [x] **Step 2: Verify red**

Run: `dotnet test tests/ContextGuru.Tests -c Release --filter "FullyQualifiedName~SqliteContextStoreTests"`
Expected: FAIL to build, `SqliteContextStore` does not exist.

- [x] **Step 3: Green. Implement per the spec's store item**

Connection per call via `Migrations.Open`. Writes use the transaction form chosen in the spike. Map `SqliteException` codes 5 and 6 to `DatabaseBusyException`. Format timestamps with `ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'")` on the UTC value. `Remove` uses `DELETE ... RETURNING` (SQLite 3.35 or later confirmed by the spike). `ResolveDatabasePath` creates the folder and, on Unix, applies `UnixFileMode` 0700 to the folder and creates the empty file with 0600.

- [x] **Step 4: Verify green**

Run: the same filter command.
Expected: PASS (the busy test takes about 5 seconds).

- [x] **Step 5: Refactor and commit**

Extract a private `ExecuteWrite<T>(Func<SqliteConnection, T>)` so the exception mapping lives in one place. As built: it is `Func<SqliteConnection, SqliteTransaction, T>`, because every command inside `BeginTransaction()` must be given the transaction; `IContextStore.Get` has a single-rule `SuppressMessage` for CA1716 (`Get` is the specified name). Re-run, `dotnet format`. Commit: "Add SQLite context store".

---

### Task 6: Tool host, logging and startup migration

**Files:**
- Modify: `src/ContextGuru/Program.cs`
- Create: `src/ContextGuru/StartupMigration.cs`
- Create: `tests/ContextGuru.Tests/ServerProcess.cs`
- Test: `tests/ContextGuru.Tests/ServerHostTests.cs`

**Interfaces:**
- Consumes: `Migrations`, `SqliteContextStore` (Tasks 4, 5).
- Produces:

```csharp
// tool project
public sealed class StartupMigration  // starts Migrations.Run on a background task in its constructor
{
    public Task Completion { get; }   // faulted if Run threw; tools await it
}
// test helper
public sealed class ServerProcess : IAsyncDisposable
{
    public static ServerProcess Start(string dbPath, string[]? args = null, string? workingFolder = null);
    public StreamWriter StandardInput { get; }  public StreamReader StandardOutput { get; }  public StreamReader StandardError { get; }
    public Task<int> WaitForExitAsync(); public void Kill();
    public static StdioClientTransport Transport(string dbPath, string? workingFolder = null); // for the SDK client
}
```
`ServerProcess` runs `dotnet ContextGuru.dll` from the test output folder, with `CONTEXTGURU_DB` set.

- [x] **Step 1: Red. Write the tests**

```csharp
[Fact] public async Task UnknownArgument_PrintsUsageToStderr_ExitCode2()
{
    await using var p = ServerProcess.Start(db, args: ["bogus"]);
    Assert.Equal(2, await p.WaitForExitAsync());
    Assert.Contains("Usage", await p.StandardError.ReadToEndAsync());
    Assert.Equal("", await p.StandardOutput.ReadToEndAsync());
}

[Fact] public async Task Server_Initializes_AndStdoutHoldsOnlyJsonRpc()
{   // send an initialize request line on stdin; read stdout lines for 2 s; every line parses as JSON with "jsonrpc":"2.0"
}

[Fact] public async Task Server_WritesLogFile_WithProcessId()
{   // after start and stop, logs/contextguru-<yyyyMMdd>.log exists next to the db and contains Environment.ProcessId of the child
}

[Fact] public async Task Server_StartedAgainstNewerSchema_StaysAlive()
{   // db has user_version 99; the SDK client connects (initialize succeeds), the process has not exited
}
```

- [x] **Step 2: Verify red**

Run: `dotnet test tests/ContextGuru.Tests -c Release --filter "FullyQualifiedName~ServerHostTests"`
Expected: FAIL (exit code is 0 not 2, no stdout JSON, no log file).

- [x] **Step 3: Green. Implement `Program.cs` and `StartupMigration`**

Hand-written routing: no arguments runs the host; `--doctor`, `install`, `uninstall` call handlers added in Tasks 9 and 12 (for now they print usage and return 2); anything else prints usage to stderr and returns 2. Host: clear logging providers, console logger with `LogToStandardErrorThreshold = LogLevel.Trace`, Serilog file output from the spike's decision (daily rolling, 7 files, shared writing, `Environment.ProcessId` in the line template), minimum level Information, `AddMcpServer().WithStdioServerTransport()`. Register `SqliteContextStore` and `StartupMigration` as singletons and resolve `StartupMigration` at start.

- [x] **Step 4: Verify green**

Run: the same filter command.
Expected: PASS.

- [x] **Step 5: Refactor and commit**

Move logging setup into a private `ConfigureLogging(HostApplicationBuilder)` method. Re-run, `dotnet format`. Commit: "Add MCP host, logging and startup migration".

As built: the newer-schema and log-file tests drive the child over raw stdio (not the SDK client) so they can see its process id and exit state. `ServerProcess.Id` was added. A failed migration is only logged and the server stays up, so the tools in Tasks 7 and 8 must await `StartupMigration.Completion` and report its fault.

---

### Task 7: save_context and get_context

**Files:**
- Create: `src/ContextGuru/Tools/Results.cs`, `src/ContextGuru/Tools/ContextTools.cs`
- Modify: `src/ContextGuru/Program.cs` (register `.WithTools<ContextTools>()`)
- Test: `tests/ContextGuru.Tests/StdioToolTests.cs`

**Interfaces:**
- Consumes: Tasks 2, 3, 5, 6.
- Produces (SDK attribute usage per `docs/spike.md`):

```csharp
public sealed record ProjectResult([property: JsonPropertyName("name")] string Name, /* path, source, warning */);
public sealed record SaveContextResult(/* name, overwritten, previous_updated_at, previous_summary, project */);
public sealed record GetContextFound(/* found: true, name, summary, body, updated_at, source_agent, project, agent_instructions */);
public sealed record GetContextNotFound(/* found: false, searched_path, project */);

[McpServerToolType]
public sealed class ContextTools(IContextStore store, StartupMigration migration, ILogger<ContextTools> log)
{
    [McpServerTool(Name = "save_context")]
    public Task<CallToolResult> SaveContext(McpServer server, string summary, string body, string? name = null, string? agent = null, string? projectPath = null, CancellationToken ct = default);
    [McpServerTool(Name = "get_context")]
    public Task<CallToolResult> GetContext(McpServer server, string? name = null, string? projectPath = null, CancellationToken ct = default);
}
```
Success is one JSON text content block of the result record. An error is `CallToolResult { IsError = true }` with one text block. `AgentInstructions` is a public constant holding the exact spec text.

- [x] **Step 1: Red. Write the tests with the SDK client over `ServerProcess.Transport`**

```csharp
[Fact] public async Task Save_NewContext_ReturnsWireShape()
{
    var r = await Call("save_context", new() { ["summary"] = "s", ["body"] = Valid, ["name"] = "Auth", ["projectPath"] = repo });
    var j = Json(r);
    Assert.Equal("auth", j.GetProperty("name").GetString());
    Assert.False(j.GetProperty("overwritten").GetBoolean());
    Assert.Equal(JsonValueKind.Null, j.GetProperty("previous_updated_at").ValueKind);
    Assert.Equal("argument", j.GetProperty("project").GetProperty("source").GetString());
}

[Fact] public async Task Save_BadHandoff_IsErrorNamingEveryProblem()
{   // body "just text" => IsError true, text contains each Missing required heading line
}

[Fact] public async Task Get_Found_WrapsBodyWithMatchingRandomIds_AndExactInstructions()
{   // text between <<<HANDOFF {id} BEGIN>>> and END has the same 8-hex id; two get calls give two ids; agent_instructions equals ContextTools.AgentInstructions
}

[Fact] public async Task Get_BodyWithForgedEndMarker_IsStoredUnchanged_AndWrapperIdDiffers()   // Review Focus 4
{   // save body containing "<<<HANDOFF 00000000 END>>>"; get returns it inside the wrapper; real id != 00000000; last line is the real END
}
```

Also: second save of `auth` gives `overwritten: true` with `previous_summary`; no name gives a name matching `^\d{8}-\d{6}$`; `get` with no name returns the latest; `get` of an unknown name gives `found: false` with `searched_path` equal to `project.path`; a name `Bad Name` gives IsError with the Bad name text; a relative `projectPath` gives IsError `projectPath must be an absolute path to an existing folder.`; null fields appear as JSON `null` (property present); against a database with `user_version` 99 a tool call returns IsError with `The database schema is version 99, but this tool supports up to 1. Update this tool's install.` and the process stays alive.

- [x] **Step 2: Verify red**

Run: `dotnet test tests/ContextGuru.Tests -c Release --filter "FullyQualifiedName~StdioToolTests"`
Expected: FAIL (tool `save_context` not found on the server).

- [x] **Step 3: Green. Implement**

Each tool awaits `migration.Completion` (a stored fault becomes the tool error), normalizes the name with `HandoffTemplate.NormalizeName`, resolves the project with roots left empty for now, calls the store, and maps `ValidationError`, `InvalidProjectPathException`, `DatabaseBusyException` and `SchemaTooNewException` to error results using the spec's message table. Delimiter id: `Convert.ToHexString(RandomNumberGenerator.GetBytes(4)).ToLowerInvariant()`.

- [x] **Step 4: Verify green**

Run: the same filter command.
Expected: PASS.

- [x] **Step 5: Refactor and commit**

Move the exception-to-result mapping into one private `Guard(Func<Task<CallToolResult>>)`. Re-run, `dotnet format`. Commit: "Add save and get tools".

As built: `SqliteContextStore` is always registered (an empty-path store when the database folder cannot be created), and each tool awaits the faulted `StartupMigration.Completion` first, so that store is never used. Roots are passed as empty until a later task wires them.

---

### Task 8: list_contexts, remove_context and roots fallback

**Files:**
- Modify: `src/ContextGuru/Tools/ContextTools.cs`, `src/ContextGuru/Tools/Results.cs`
- Test: `tests/ContextGuru.Tests/StdioToolTests.cs` (add cases)

**Interfaces:**
- Consumes: Task 7 helpers.
- Produces:

```csharp
public sealed record ListContextsResult(/* project, contexts: ListEntry[] */);
public sealed record ListEntry(/* name, summary, updated_at, source_agent */);
public sealed record RemoveContextResult(/* removed, name, summary, project */);

[McpServerTool(Name = "list_contexts")]
public Task<CallToolResult> ListContexts(McpServer server, string? projectPath = null, CancellationToken ct = default);
[McpServerTool(Name = "remove_context", Destructive = true)]
public Task<CallToolResult> RemoveContext(McpServer server, string name, string? projectPath = null, CancellationToken ct = default);
```
Roots: ask the client only if it advertises roots, convert with `ProjectResolver.RootPathsFromUris`, ignore a failed request.

- [x] **Step 1: Red. Write the tests**

```csharp
[Fact] public async Task List_EmptyProject_ReturnsEmptyArray_NotError()          // Review Focus 5
{
    var j = Json(await Call("list_contexts", new() { ["projectPath"] = repo }));
    Assert.Equal(0, j.GetProperty("contexts").GetArrayLength());
}

[Fact] public async Task Remove_WithoutName_IsError() =>
    Assert.Equal("A name is required to remove a context.", Text(await Call("remove_context", new() { ["name"] = "  ", ["projectPath"] = repo })));

[Fact] public async Task Remove_UnknownName_ReturnsRemovedFalse_SummaryNull() { /* removed false, summary JSON null, not IsError */ }

[Fact] public async Task ToolsList_MarksOnlyRemoveAsDestructive() { /* annotations: remove_context Destructive true, others not */ }

[Fact] public async Task Get_NoProjectPath_ClientRoots_UsesRootsSource() { /* client with a roots handler returning file:// repo => project.source "roots" */ }

[Fact] public async Task Get_NoProjectPathNoRoots_UsesServerWorkingFolder()      // CUJ 4
{   // ServerProcess working folder = notARepo; source "cwd"; found false; searched_path == notARepo
}
```

Also: list returns saved names newest first with `name`, `summary`, `updated_at`, `source_agent` and no `body` property; after remove the list shrinks by one and `remove` returns the deleted `summary`; a handoff text never appears in `logs/contextguru-*.log` after save, get, list and remove calls.

- [x] **Step 2: Verify red**

Run: `dotnet test tests/ContextGuru.Tests -c Release --filter "FullyQualifiedName~StdioToolTests"`
Expected: FAIL (`list_contexts` and `remove_context` not found).

- [x] **Step 3: Green. Implement the two tools and the roots lookup**

`RemoveContext` trims first, then returns the required-name error before touching the store. Roots lookup lives in a private `GetRootPathsAsync(McpServer)` used by all four tools. Pass `Environment.CurrentDirectory` as the working folder.

- [x] **Step 4: Verify green**

Run: the same filter command.
Expected: PASS.

- [x] **Step 5: Refactor and commit**

Remove duplicated project resolution by routing all four tools through one private `ResolveProjectAsync`. Re-run, `dotnet format`. Commit: "Add list and remove tools".

As built: the SDK marks its roots APIs obsolete (MCP9005), so `ContextTools.cs` and `StdioToolTests.cs` carry a file-level `#pragma warning disable MCP9005`. SDK 2.4.0 resolves roots inside the client's own call, so the "roots fail" and "roots never answered" tests use a raw JSON-RPC client. The server asks for roots with a 5 second timeout and treats a failed or hung request as no roots. `list_contexts` is marked read-only.

---

### Task 9: Doctor

**Files:**
- Create: `src/ContextGuru/Doctor.cs`
- Modify: `src/ContextGuru/Program.cs` (route `--doctor`)
- Test: `tests/ContextGuru.Tests/DoctorTests.cs`

**Interfaces:**
- Consumes: `Migrations.Open`, `Migrations.ReadUserVersion`, `ProjectResolver.Resolve`.
- Produces: `public static class Doctor { public static int Run(string dbPath, string workingFolder, TextWriter output); }` returns 0 or 1.

- [x] **Step 1: Red. Write the tests**

```csharp
[Fact] public void Run_GoodDatabase_PrintsAllKeys_Exit0()
{
    Migrations.Run(db);
    var code = Doctor.Run(db, repo, writer);
    Assert.Equal(0, code);
    foreach (var key in new[] { "version", "database path", "schema version", "project name", "project path", "project source", ".git found", "write test" })
        Assert.Matches($"(?m)^{Regex.Escape(key)}: .+$", writer.ToString());
    Assert.Contains("project source: cwd", writer.ToString());
}

[Fact] public void Run_MissingDatabase_PrintsNotCreatedAndSkipped_AndDoesNotCreateIt()   // Review Focus 5
{
    Assert.Equal(0, Doctor.Run(db, repo, writer));
    Assert.Contains("schema version: not created yet (created on first save)", writer.ToString());
    Assert.Contains("write test: skipped", writer.ToString());
    Assert.False(File.Exists(db));
}
```

Also: `user_version` 99 prints a schema line containing `99` and `newer than this tool` without throwing; a second connection holding `BEGIN IMMEDIATE` makes `write test: failed` and exit code 1; a working folder with a `.git` folder prints `.git found: yes`, without one `no`; after a passing run the `contexts` table is still empty (rolled back).

- [x] **Step 2: Verify red**

Run: `dotnet test tests/ContextGuru.Tests -c Release --filter "FullyQualifiedName~DoctorTests"`
Expected: FAIL to build, `Doctor` does not exist.

- [x] **Step 3: Green. Implement per the spec's Observability section**

Version from the assembly informational version. Never call `Migrations.Run`. Open with `Migrations.Open` only if the file exists. The write test is `BEGIN IMMEDIATE` then `ROLLBACK`.

- [x] **Step 4: Verify green**

Run: the same filter command.
Expected: PASS (the failed-write case takes about 5 seconds).

- [x] **Step 5: Refactor and commit**

Make each check a small private method returning its `key: value` line so `Run` only joins them. Re-run, `dotnet format`. Commit: "Add doctor mode".

As built: `SqliteContextStore.GetDatabasePath` was extracted so `--doctor` resolves `CONTEXTGURU_DB` without creating missing folders or files; a newer schema (`user_version > Migrations.Latest`) prints `(newer than this tool)` without throwing and exits 1.

---

### Task 10: Concurrency tests

**Files:**
- Test: `tests/ContextGuru.Tests/StdioToolTests.cs` (add cases)

**Interfaces:**
- Consumes: the complete server (Tasks 6 to 8).

- [x] **Step 1: Red. Write the two tests**

```csharp
[Fact] public async Task TwoProcesses_Write25EachToSameDatabase_AllRowsExist_NoBusyErrors()
{   // two clients on the same db path, each saves 25 names (a-0..a-24, b-0..b-24) in parallel
    // pass: no result text equals "Database busy, retry."; list_contexts shows 50 rows
}

[Fact] public async Task TwoProcessesStartedTogether_OnNewDatabase_BothSucceed_UserVersionLatest()
{   // start both at once against a path that does not exist; each calls list_contexts; both not IsError; PRAGMA user_version == Migrations.Latest
}
```

- [x] **Step 2: Verify red**

The code already exists, so prove the tests can fail: temporarily set `PRAGMA busy_timeout=0` and `Default Timeout=0` in `Migrations.Open`.
Run: `dotnet test tests/ContextGuru.Tests -c Release --filter "FullyQualifiedName~TwoProcesses"`
Expected: FAIL with "Database busy, retry." in at least one test.

- [x] **Step 3: Green. Revert the temporary change**

Restore the 5-second values in `Migrations.Open`. No other code change is expected. If a test still fails, find the root cause before changing anything and take it to the author.

- [x] **Step 4: Verify green**

Run: the same filter command.
Expected: PASS.

- [x] **Step 5: Refactor and commit**

Move the shared "start two clients" code into a helper in `ServerProcess.cs`. Re-run, `dotnet format`. Commit: "Add concurrency tests".

As built: `DefaultTimeout = 0` means infinite timeout in `Microsoft.Data.Sqlite` (not 0 ms), so Step 2 RED was verified with `DefaultTimeout = 1`, `PRAGMA busy_timeout=0`, and `BeginTransaction(deferred: true)` in `SqliteContextStore.ExecuteWrite` (which failed with `"Database busy, retry."`), then all `src/` changes were reverted. Added `ServerSession.StartPairAsync` in `ServerProcess.cs` and two controlled lock-overlap tests (`Save_WaitsForHeldWriteLock_AndSucceedsAfterRelease` and `TwoProcessesStartedTogether_WhenMigrationLockHeld_BothWaitAndSucceed`) alongside the two multi-process tests.

---

### Task 11: Command templates and agent targets

**Files:**
- Create: `src/ContextGuru/Install/Templates/save-context.md`, `get-context.md`, `list-contexts.md`, `remove-context.md` (embedded resources in `src/ContextGuru/ContextGuru.csproj`)
- Create: `src/ContextGuru/Install/AgentTargets.cs`, `src/ContextGuru/Install/TemplateRenderer.cs`
- Test: `tests/ContextGuru.Tests/TemplateTests.cs`

**Interfaces:**
- Produces (values come from `docs/spike.md`):

```csharp
public enum ConfigFormat { Json, Toml }
public sealed record AgentTarget(string Agent, string ConfigPath, ConfigFormat ConfigFormat, string CommandFolder,
    string FileExtension, string ArgsPlaceholder, string ToolName, string MarkerLine, bool UsesSkillLayout = false)
{
    public string GetCommandPath(string commandName);
    public bool IsManagedCommand(string content);
}
public static class AgentTargets
{
    public static IEnumerable<string> CommandNames { get; }
    public static AgentTarget For(string agent, string baseFolder);   // "claude" | "codex" | "cursor"
}
public static class TemplateRenderer
{
    public static string ReadTemplate(string commandName);            // "save-context", "get-context", "list-contexts", "remove-context"
    public static string Render(string commandName, AgentTarget target); // loads and renders an embedded command
}
```

- [x] **Step 1: Red. Write the tests**

```csharp
[Theory] [InlineData("save-context")] [InlineData("get-context")] [InlineData("list-contexts")] [InlineData("remove-context")]
public void Template_FirstLine_IsTheMarker(string name) =>
    Assert.StartsWith("<!-- contextguru:managed v1 -->\n", TemplateRenderer.ReadTemplate(name).ReplaceLineEndings("\n"));

[Fact] public void Render_Save_ReplacesAllPlaceholders_AndSetsAgent()
{
    var text = TemplateRenderer.Render("save-context", AgentTargets.For("claude", baseFolder));
    Assert.DoesNotContain("{{", text);
    Assert.Contains("`agent` set to `claude`", text);
}
```

Also: the four template bodies contain the exact texts from the spec's Installation section (assert one distinctive sentence from each: save `Leave out secrets and file contents.`, get `Follow the \`agent_instructions\` in the result.`, list `Show name, summary, updated time and source agent as a table.`, remove `Only after they confirm`); `AgentTargets.For("unknown", ...)` throws `ArgumentException`; each of the three targets has non-empty values for every field.

- [x] **Step 2: Verify red**

Run: `dotnet test tests/ContextGuru.Tests -c Release --filter "FullyQualifiedName~TemplateTests"`
Expected: FAIL to build, `TemplateRenderer` does not exist.

- [x] **Step 3: Green. Implement**

Templates hold the marker line, then the spec text with the three placeholders. `AgentTargets` is data only, one case per agent filled from the spike (the marker comment form follows the spike's marker placement). A drop-listed agent from the spike is removed here and from the tests.

- [x] **Step 4: Verify green**

Run: the same filter command.
Expected: PASS.

- [x] **Step 5: Refactor and commit**

Cache the embedded resource reads in a private static dictionary. Re-run, `dotnet format`. Commit: "Add command templates and agent targets".

Verification for issue #12: the initial filtered build failed with CS0234 and CS0246 because the install types did not exist. The issue's filter passed 58 tests after implementation. The exact class filter passed all 11 Task 11 cases after caching, including embedded rendering for all four commands on all three targets. The full Release build passed with zero warnings, all 183 tests passed, and the format check passed.

The raw templates begin with the managed marker. The Codex renderer places skill front matter before the marker, following the spike.

---

### Task 12: Installer

**Files:**
- Create: `src/ContextGuru/Install/Installer.cs`
- Modify: `src/ContextGuru/Program.cs` (route `install` and `uninstall`)
- Test: `tests/ContextGuru.Tests/InstallerTests.cs`

**Interfaces:**
- Consumes: `AgentTargets.CommandNames`, `AgentTarget.GetCommandPath`, `AgentTarget.IsManagedCommand`, and `TemplateRenderer.Render(commandName, target)` from Task 11.
- Produces:

```csharp
public sealed class Installer(string baseFolder)
{
    public void Install(string agent);     // writes marked command files, adds or replaces only the "contextguru" entry
    public void Uninstall(string agent);   // removes marked files and that entry only
    public static int Run(string[] args, string baseFolder, TextWriter output, TextWriter error); // args start at "install"/"uninstall"; 0 ok, 2 bad args, 1 failure
}
```
Backups are `<file>.bak`, then `.bak.1`, `.bak.2`. Config writes go to a temp file, then move. JSON uses `System.Text.Json.Nodes`.

- [ ] **Step 1: Red. Write the tests (all under a temp `baseFolder`)**

```csharp
[Fact] public void Install_WritesMarkedFiles_AndRegistersServer()
{
    new Installer(t.Path).Install("claude");
    var target = AgentTargets.For("claude", t.Path);
    foreach (var name in AgentTargets.CommandNames)
        Assert.True(target.IsManagedCommand(File.ReadAllText(target.GetCommandPath(name))));
    var entry = ReadConfig("claude")["mcpServers"]!["contextguru"]!;     // exact path and key per AgentTargets
    Assert.Equal("contextguru", (string)entry["command"]!);
}

[Fact] public void Install_Twice_ChangesNothing() { /* hash all files after first and second install; equal; no .bak files */ }

[Fact] public void Install_UnmarkedExistingFile_IsBackedUpToBak_ThenBak1()
{   // pre-create an unmarked save command file with text "mine"; install; .bak holds "mine"; change it again to "mine2", force reinstall via new unmarked file; .bak.1 holds "mine2"; .bak still "mine"
}

[Fact] public void Install_KeepsOtherConfigKeys() { /* existing config with another server "x"; after install and after uninstall "x" survives */ }

[Fact] public void Install_UnparseableConfig_ChangesNothing_AndReportsWhy() { /* config "{ not json"; Run returns 1; file unchanged; error text non-empty */ }

[Fact] public void Uninstall_RemovesOnlyMarkedFilesAndTheEntry() { /* an unmarked file with a command name survives; database file untouched */ }

[Theory] [InlineData("install")] [InlineData("install", "--agent")] [InlineData("install", "--agent", "vim")] [InlineData("uninstall")]
public void Run_MissingOrUnknownAgent_Exit2_WithUsageOnError(params string[] args) => Assert.Equal(2, Installer.Run(args, t.Path, new StringWriter(), err));
```

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/ContextGuru.Tests -c Release --filter "FullyQualifiedName~InstallerTests"`
Expected: FAIL to build, `Installer` does not exist.

- [ ] **Step 3: Green. Implement per the spec's Installation section**

Parse config first and stop before any write if it fails. Use the Task 11 APIs for command names, paths, rendering, and ownership checks during install and uninstall. Create the parent folder for each command file. Claude Code and Cursor use flat `cg-<name>.md` files with the marker on the first line. Codex uses `cg-<name>/SKILL.md` with the marker immediately after the closing YAML delimiter. A missing config is created containing only the `contextguru` entry (command `contextguru`, no arguments).

- [ ] **Step 4: Verify green**

Run: the same filter command.
Expected: PASS.

- [ ] **Step 5: Refactor and commit**

Extract `BackupIfUnmanaged(string path)` and `WriteAtomically(string path, string text)`. Re-run, `dotnet format`. Commit: "Add installer".

---

### Task 13: Codex TOML configuration (only if the spike says Codex needs TOML)

**Files:**
- Modify: `src/ContextGuru/Install/Installer.cs`, `Directory.Packages.props`, `src/ContextGuru/ContextGuru.csproj`
- Test: `tests/ContextGuru.Tests/InstallerTests.cs` (add cases)

If `docs/spike.md` says Codex uses JSON, or Codex was dropped, skip this task and note "skipped" in the commit of Task 14.

**Interfaces:**
- Consumes: `ConfigFormat.Toml`, `Installer` (Tasks 11, 12).
- Produces: the same `Install`/`Uninstall` behavior for a TOML config.

- [ ] **Step 1: Red. Write the tests**

Same cases as Task 12 for `codex` against a TOML file: install adds a `contextguru` server table, other tables survive install and uninstall, a second install changes nothing, an unparseable file changes nothing and exits 1.

- [ ] **Step 2: Verify red**

Run: `dotnet test tests/ContextGuru.Tests -c Release --filter "FullyQualifiedName~InstallerTests"`
Expected: the new Codex cases FAIL.

- [ ] **Step 3: Green. Get package approval, then implement**

Ask the author to approve the TOML package named in the spike. Add it to `Directory.Packages.props` and the tool csproj. Branch on `ConfigFormat` in `Installer`.

- [ ] **Step 4: Verify green**

Run: the same filter command.
Expected: PASS.

- [ ] **Step 5: Refactor and commit**

Hide JSON and TOML behind one private `IConfigEditor` with `Add(string text)` and `Remove(string text)`. Re-run, `dotnet format`. Commit: "Add Codex TOML registration".

---

### Task 14: Local scripts and README

**Files:**
- Create: `local/build.ps1`, `local/install.ps1`, `README.md`

**Interfaces:**
- Consumes: the complete solution.
- Produces: the two scripts, which exit non-zero on the first failing step.

- [ ] **Step 1: Red. Define the check before the scripts exist**

The test is running `local/build.ps1`, which must run restore, build, test and format in order and exit 0. No file is written in this step.

- [ ] **Step 2: Verify red**

Run: `pwsh -File local/build.ps1`
Expected: FAIL, file not found.

- [ ] **Step 3: Green. Write the scripts and README**

`build.ps1`: `dotnet restore`, `dotnet build ContextGuru.slnx -c Release`, `dotnet test ContextGuru.slnx -c Release --no-build`, `dotnet format ContextGuru.slnx --verify-no-changes`, stop on the first non-zero exit. `install.ps1`: run `build.ps1`, `dotnet pack` into `local/nupkg/`, `dotnet tool uninstall -g ContextGuru` if installed, `dotnet tool install -g ContextGuru --add-source local/nupkg`, run `contextguru --doctor`, then print the reminder to run `contextguru install --agent <agent>`. README (short): what it is, local build and install, `contextguru install --agent <agent>`, the four commands, database location and `--doctor`.

- [ ] **Step 4: Verify green**

Run: `pwsh -File local/build.ps1`
Expected: exit code 0. Ask the author before running `local/install.ps1`, because it installs a global tool; expected result is a doctor output with every line filled and exit code 0.

- [ ] **Step 5: Refactor and commit**

Make sure the README commands match the scripts and `contextguru --help`-style usage text. Commit: "Add local build scripts and README".

---

### Task 15: Manual acceptance and close-out

**Files:**
- Create: `docs/acceptance.md`
- Modify: `docs/designs/contextguru.md` (Status), `AGENTS.md` (replace the planned commands with the real ones)

**Interfaces:**
- Consumes: the installed tool and the agents that passed the spike.

- [ ] **Step 1: Red. Write `docs/acceptance.md` with every spec check unchecked**

Items: install with `local/install.ps1`; `contextguru install --agent <agent>` per passing agent; CUJ 1 for each ordered pair of passing agents (save result shows generated name and project path; get result shows the summary and asks whether to continue when next steps exist); wrong-folder check from a folder outside any repo (response names its source, `searched_path` shows the folder); `contextguru --doctor` fully filled with exit code 0.

- [ ] **Step 2: Verify red**

Run: `rg -c "\- \[ \]" docs/acceptance.md`
Expected: a count equal to the number of items.

- [ ] **Step 3: Green. Run each check by hand and tick it with the evidence beside it**

Record the observed save result, get result and doctor output in the file. A failing item is a bug: find the root cause before changing code.

- [ ] **Step 4: Verify green**

Run: `rg -c "\- \[ \]" docs/acceptance.md`
Expected: no matches.

- [ ] **Step 5: Refactor and commit**

Set the design Status as the author decides, re-run `/agents-md` so `AGENTS.md` lists the real commands, then run `dotnet test ContextGuru.slnx -c Release` and `dotnet format ContextGuru.slnx --verify-no-changes` once more. Commit: "Record acceptance results".

---

## Self-review

*   **Spec coverage:** spike (0); build files and Core types (1); template rules (2); project identity and known limits (3); data model, migration and concurrency (4, 5, 10); four tools, wire format, texts, error table (7, 8); observability and doctor (6, 9); installer, markers, backups, templates (11, 12, 13); local scripts and README (14); verification plan and manual acceptance (15). Spec items 1 to 34 map to Tasks 0 to 15. The CI workflow is out of scope per Non-Goals.
*   **Additions beyond the spec's file list:** `StorageExceptions.cs`, `StartupMigration.cs`, `TemplateRenderer.cs`, `TempDir.cs`, `FixedTimeProvider.cs`, `ServerHostTests.cs`, `TemplateTests.cs`, `docs/acceptance.md`, and the helpers `HandoffTemplate.BadNameMessage` and `ProjectResolver.RootPathsFromUris`. Each is small and has one consumer named in its task.
*   **Dependencies between tasks:** 1 before everything; 4 before 5; 5 and 6 before 7; 7 before 8, 9 and 10; 11 before 12; 12 before 13; the spike gates the SDK calls in 5 to 8 and every value in 11 to 13.
