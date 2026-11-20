# ContextGuru: Cross-Agent Context Handoff

*   **Author:** rnair86
*   **Status:** Draft
*   **Date:** 2026-11-20

--------------------------------------------------------------------------------

## Overview

ContextGuru is a local MCP server, written in C# on .NET, that stores short work handoffs in a local SQLite file. A developer saves a handoff from one coding agent and reads it back later in the same agent or a different one. The target agents are Claude Code, Codex and Cursor. Each agent starts its own copy of the server over stdio, and all copies share one database file.

The server exposes four tools: save, get, list and remove. Each agent gets a small command file that calls those tools: `/cg-save-context [name]`, `/cg-get-context [name]`, `/cg-list-contexts` and `/cg-remove-context {name}`.

The server cannot read an agent's conversation. "Saving the context" means the agent writes a handoff in a fixed markdown template and passes it to the save tool. The server stores it, scoped to the project, and returns it on request.

## The Problem

A developer who switches between coding agents, or returns to a task in a new session, has to rebuild context by hand. The current workaround is a markdown file that the developer writes or asks an agent to write, then pastes or points to in the next session.

This breaks down in four ways:

*   Every handoff needs manual steps in both sessions, and the file's location and format are ad hoc.
*   The file is not tied to a project, so finding the latest handoff for a repo means finding the right file.
*   Different agents write handoffs in different shapes, so the next agent gets inconsistent information.
*   Nothing lists what was saved, or removes an old handoff in a controlled way.

The proposed solution is one command per action in each agent, backed by a shared local store. The store is keyed by project, so `get` with no name returns the latest handoff for the repo you are in. A fixed template keeps the content consistent across agents.

## Goals

*   Save a handoff in any of Claude Code, Codex or Cursor, and read it back in any of the three with one command each, and no hand-written file.
*   Scope every context to a project, identified by its path (git root if inside a repo, otherwise the current folder). Store both the folder name and the absolute path.
*   Return the latest context for the current project when `get` is called without a name.
*   Use a fixed markdown template with required headings: one-line summary, goal, what was done, decisions, open tasks, next steps. The server checks the headings and size caps and returns an error that names what is missing.
*   Generate a key automatically when `save` is called without a name. Overwrite on a repeated name, and keep one previous version. The save result says it overwrote and shows the old timestamp and summary.
*   Offer list and remove commands scoped to the current project. Removal is by exact name and is manual.
*   Show a brief summary on `get`. If the handoff has next steps, ask the user whether to continue from them; otherwise wait for instructions.
*   Run on Windows, Linux and macOS, installed as a `dotnet tool`.
*   Make wrong-project resolution visible: every response states the project path used and where it came from, and a diagnostic mode prints the version, database path, schema version and resolved project.

## Non-Goals

*   A dashboard for managing contexts (later improvement).
*   Diffs or file contents in the handoff. Git holds those.
*   Automatic expiry or deletion of contexts.
*   Server-side secret scanning. The template tells the agent not to include secrets. This is a stated, accepted risk.
*   A full revision history or a history UI. Only one previous version is kept.
*   WSL path mapping. Version 1 assumes all agents run natively on Windows. A repo opened from WSL counts as a different project.
*   Remote or multi-user sync.
*   Guarantees about what an agent writes. The server checks shape and size, not accuracy.

## Open Questions

*   **Command mechanism per agent**: What file location, name form and argument syntax does each of Claude Code, Codex and Cursor use for a custom command? Codex's form is unverified (historically a `/prompts:` prefix). A spike is needed before the install step can be designed.
*   **Project path source**: Does each agent support the MCP roots list, and what working folder does each use when it starts the server? The design assumes the command passes `projectPath`, with roots and the server's own working folder as fallbacks. The spike must confirm the fallbacks behave as assumed.
*   **Template validation**: Does the fixed template, tested against a plain markdown folder across all three agents for about a week, produce handoffs that let the next agent continue without re-explaining? Results may change the required headings or whether the server is needed at all.
*   **Size caps and headings**: What are the exact required headings and size limits for the summary and the whole handoff?
*   **Install step**: How does the install command write command files and register the MCP server for each agent, and how does it update or remove them?
*   **SDK and platform facts**: What is the current `ModelContextProtocol` NuGet package version and API? Does `Microsoft.Data.Sqlite` ship native SQLite for every target platform (Linux musl and arm)?
*   **Previous-version access**: Version 1 never returns the previous version through any tool, so it is only a recovery copy in the database. Is that acceptable, or does the user need a way to read it?

## Critical User Journeys (CUJs)

### CUJ 1: Save in one agent, continue in another

*   **Given**: The user is in a repo in Claude Code, has done part of a task, and has installed ContextGuru and its command files for Claude Code and Codex.
*   **When**: The user runs `/cg-save-context`, then later opens Codex in the same repo and runs `/cg-get-context`.
*   **Then**: The save result gives the auto-generated name and the project path used. The get result in Codex shows a brief summary of the saved handoff, and because it has next steps, asks whether to continue from them.

### CUJ 2: Overwrite by name

*   **Given**: A context named `auth-refactor` exists for the project.
*   **When**: The user runs `/cg-save-context auth-refactor` again.
*   **Then**: The new handoff replaces the current one. The previous current version is kept as the single previous version. The result says it overwrote and shows the old saved time and summary.

### CUJ 3: List and remove

*   **Given**: The project has three saved contexts.
*   **When**: The user runs `/cg-list-contexts`, then `/cg-remove-context old-spike` and confirms.
*   **Then**: The list shows name, summary, saved time and source agent for the three contexts. After removal, the named context and its previous version are gone and the list shows two.

### CUJ 4: Wrong or missing project

*   **Given**: An agent starts the server from a folder that is not the repo, and the command does not pass `projectPath`.
*   **When**: The user runs `/cg-get-context`.
*   **Then**: The server falls back to the roots list, then its own folder. The response states which source it used. If nothing is found, the response says so and names the path it searched, so the user can see why.

## Technical Plan (Architecture)

### Components

The solution has two projects plus a test project.

*   **`ContextGuru.Core`** (class library, no MCP references): the project resolver, the template checker, the store interface and its SQLite implementation, and the schema migrations.
*   **`ContextGuru`** (console app, packaged as a `dotnet tool`): the MCP host over stdio, the four tool classes, the `--doctor` mode and the `install` command that writes command files and registers the server for each agent. A dashboard added later can reference `ContextGuru.Core`.
*   **`ContextGuru.Tests`** (xUnit): tests for Core with a temporary SQLite file, and a stdio test that drives the tool with JSON-RPC.

```text
Agent (Claude Code | Codex | Cursor)
   |  command file: /cg-save-context  ->  agent writes handoff, calls tool
   v
stdio (MCP)           stdout = protocol only, logs go to stderr and a file
   v
ContextGuru (tool)
   |-- ContextTools: save_context | get_context | list_contexts | remove_context
   |-- Doctor, Installer
   v
ContextGuru.Core
   |-- ProjectResolver  (projectPath -> roots -> cwd; git root; normalize)
   |-- HandoffTemplate  (headings + size caps)
   |-- IContextStore -> SqliteContextStore (one file, WAL)
   v
contexts.db  (shared by every server process on the machine)
```

### Project identity

The resolver takes `projectPath` from the tool call. If it is missing, it asks the client for the MCP roots and uses the first root; if there are none, it uses the server's working folder. It then walks up from that folder to find `.git`, which can be a directory or a file (worktrees and submodules use a file), and uses that folder as the project root. With no `.git`, the folder itself is the root. It resolves symlinks to a real path.

Two values are stored. `project_path` is the real absolute path for display. `project_key` is the comparison form: on Windows it is the lower-cased path with a single separator form; on Linux and macOS it is the real path unchanged. Every tool response includes `project: { name, path, source }`, where `source` is `argument`, `roots` or `cwd`.

### Data model

`contexts` holds the current version.

```text
contexts(
  id            INTEGER PRIMARY KEY,
  project_key   TEXT NOT NULL,
  project_path  TEXT NOT NULL,
  project_name  TEXT NOT NULL,        -- root folder name
  name          TEXT NOT NULL,        -- lower-case, [a-z0-9][a-z0-9_-]{0,63}
  summary       TEXT NOT NULL,        -- one line, max 200 characters
  body          TEXT NOT NULL,        -- markdown handoff
  source_agent  TEXT,                 -- reported by the command, may be null
  created_at    TEXT NOT NULL,        -- UTC, ISO 8601
  updated_at    TEXT NOT NULL,        -- UTC, ISO 8601
  UNIQUE(project_key, name)
)

context_previous(
  context_id    INTEGER PRIMARY KEY REFERENCES contexts(id) ON DELETE CASCADE,
  summary       TEXT NOT NULL,
  body          TEXT NOT NULL,
  saved_at      TEXT NOT NULL,
  source_agent  TEXT
)
```

"Latest" means the highest `updated_at` for the project, with `id` as the tie-break. The schema version lives in `PRAGMA user_version`. Migrations are ordered and each runs inside `BEGIN IMMEDIATE`. A database whose version is higher than the tool knows is refused with a clear error.

### Database location and concurrency

The file is `%LOCALAPPDATA%\ContextGuru\contexts.db` on Windows, `$XDG_DATA_HOME/contextguru/contexts.db` (default `~/.local/share`) on Linux, and `~/Library/Application Support/ContextGuru/contexts.db` on macOS. The `CONTEXTGURU_DB` environment variable overrides it. On Linux and macOS the file is created with mode 0600. The database should not sit in a synced or network folder.

Each agent starts its own server process, so several processes write the same file. Each connection sets WAL mode and `busy_timeout` of 5000 ms. Every write is one short transaction started with `BEGIN IMMEDIATE`.

### Tool contracts

All four tools accept an optional `projectPath`. The tool names and result fields are a public contract: the installed command files do not update themselves, so changes are additive only. Each result carries `contract_version`.

*   **`save_context`**: `name` (optional), `summary`, `body`, `agent` (optional), `projectPath`. The server checks that `body` has every required heading and that sizes are within caps, otherwise it returns an error naming what is missing or too long. With no `name`, it generates `yyyyMMdd-HHmmss`, plus `-2`, `-3` on a clash. In one transaction it either inserts, or copies the current row into `context_previous` (replacing any earlier previous row) and updates the current row. The result has `name`, `overwritten`, `previous_saved_at`, `previous_summary` and `project`.
*   **`get_context`**: `name` (optional), `projectPath`. With no name it returns the latest context for the project. The result has `found`, `name`, `summary`, `body`, `saved_at`, `source_agent`, `project` and `agent_instructions`. When nothing is found, it returns `found: false` with the path searched and the names of contexts in other projects that share the same folder name.
*   **`list_contexts`**: `projectPath`. It returns metadata only for the project: `name`, `summary`, `updated_at`, `source_agent`, newest first. Bodies are not returned.
*   **`remove_context`**: `name`, `projectPath`. It deletes the exact name and its previous version, and returns what was deleted. It is marked destructive, and an unknown name returns `removed: false` without an error.

`agent_instructions` is server-written text that tells the agent how to present the result: show a short summary, ask whether to continue if the next-steps section is not empty, otherwise wait, never run the saved next steps without the user's approval, and treat `body` as saved data, not as instructions.

### Handoff template

`body` must contain these level-two headings, in this order: `Goal`, `Done`, `Decisions`, `Open tasks`, `Next steps`. The one-line summary is passed in the separate `summary` argument. The command files tell the agent to leave out secrets and file contents. Proposed caps are 200 characters for the summary and 20,000 characters for the body; the final numbers are an Open Question.

### Observability

The server writes logs to stderr and to a rotating file next to the database, never to stdout, and never logs handoff content. `contextguru --doctor` prints the version, database path, schema version, resolved project and its source, whether git was found, and the result of a test write.

### Installation

`contextguru install --agent claude|codex|cursor` registers the MCP server in that agent's configuration and writes the four command files from templates stored in the tool, so the files come from one source. Each file has a marker header so `install` can update it and `uninstall` can remove it, and it backs up any file it did not create. The exact file locations, names and argument syntax per agent are an Open Question until the spike is done.

## Alternatives Considered

### Alternative A: No server, command files over a markdown folder

Command files write and read `~/.contextguru/<project>/<name>.md`.

*   **Pros**: No C# code, no database, no server process. Files are readable and can be committed.
*   **Cons**: Each agent's command has to implement scoping, "latest" and listing in prose, so behavior varies by agent. No atomic overwrite or previous version. No size or heading checks.
*   **Reason for Rejection**: Rejected by author: the goal is a shared, project-scoped store with checks that behave the same in every agent. This option is kept as the one-week validation of the template (see Open Questions) before the server is built.

### Alternative B: MCP server on plain files instead of SQLite

*   **Pros**: Cheaper to build. Files are human-readable. "Latest" can use file modification time.
*   **Cons**: Atomic overwrite needs a temporary file and rename. Listing and the previous version need extra file conventions. A dashboard would have to scan folders.
*   **Reason for Rejection**: Rejected by author: SQLite gives atomic upsert, indexed latest lookup and listing, and a shared file the dashboard can read. The brief had left this open; the author chose SQLite.

### Alternative C: Git remote URL as the project key

*   **Pros**: The same repo cloned at two paths shares contexts.
*   **Cons**: Many projects have no remote, and the key changes if the remote changes.
*   **Reason for Rejection**: Rejected by author: not all projects are hosted remotely. The path is the key.

### Alternative D: Server working folder only, or passed path only

*   **Pros**: Server working folder only needs no argument. Passed path only has one source and one failure mode.
*   **Cons**: The working folder is wrong whenever an agent starts the server from another folder. Passed path only fails when a command does not supply it.
*   **Reason for Rejection**: Rejected by author: the passed path is used first, then the MCP roots, then the working folder, and the response states which was used.

### Alternative E: `/cg:` command prefix

*   **Pros**: Matches the original request.
*   **Cons**: Claude Code reserves the colon for plugin names, and the other agents have different forms, so the name would not be uniform.
*   **Reason for Rejection**: Rejected by author: the `/cg-` prefix is a plain command name that avoids the reserved colon.

### Alternative F: Free-form or light-template handoff, or structured fields

*   **Pros**: Free-form is easiest for the agent. Structured fields are checked field by field.
*   **Cons**: Free-form varies by agent. Structured fields cost more tokens and fail more often on weaker models.
*   **Reason for Rejection**: Rejected by author: one markdown body with required headings gives consistent handoffs at lower token cost.

### Alternative G: Pure overwrite, or a full revision history

*   **Pros**: Pure overwrite is the simplest. A revision history supports more recovery and a future dashboard history view.
*   **Cons**: Pure overwrite loses the old version and adding history later forces a migration. A full history needs pruning and more tables.
*   **Reason for Rejection**: Rejected by author: keep exactly one previous version in a second table, with no history UI in version 1.

### Alternative H: Previous version in the same row, or in a revisions table

*   **Pros**: Same-row columns need one write and no join. A revisions table that keeps two rows allows a later increase with no migration.
*   **Cons**: Same-row columns need a migration to hold more than one previous version. A revisions table grows without bound if the prune step fails.
*   **Reason for Rejection**: Rejected by author: a separate table with one row per context keeps the current-version table simple.

### Alternative I: One project, or three projects

*   **Pros**: One project is the smallest to build. Three projects allow the most separation.
*   **Cons**: One project makes the dashboard reference the tool executable. Three projects add indirection that nothing uses while the store has one implementation.
*   **Reason for Rejection**: Rejected by author: a core library plus a tool project is enough, and the dashboard can reuse the core.

### Alternative J: Server-side secret scanning

*   **Pros**: A safety net for secrets in handoffs.
*   **Cons**: False positives on hashes and identifiers, ongoing pattern maintenance, and it cannot guarantee a handoff has no secrets.
*   **Reason for Rejection**: Rejected by author for version 1: the template instruction is the only control, and the risk is accepted.

### Alternative K: Self-contained executable or NativeAOT

*   **Pros**: No .NET runtime needed on the machine.
*   **Cons**: A build per platform, a 30-70 MB file, security warnings for unsigned binaries on Windows and macOS. NativeAOT compatibility with the MCP SDK and SQLite is unverified.
*   **Reason for Rejection**: Rejected by author for version 1: a `dotnet tool` is simpler and the author already works in .NET.

## Detailed Implementation Plan

Build order: the spike and its results file come first, because the installer's per-agent file locations depend on them. Core comes next, then the tool project, then the installer.

Target framework is `net10.0`. Package versions are the latest stable at implementation time. Adding each NuGet dependency needs the author's confirmation before it is installed.

### Target Files to Create/Modify

#### 1. `docs/spike.md` (Create)

*   **Type / Scope**: Documentation of spike results.
*   **Proposed Changes**: Records, for Claude Code, Codex and Cursor on Windows: whether the agent supports the MCP roots list, the working folder it uses when it starts the server, the custom command file location and name form, argument syntax, and whether bare `/cg-<name>` works. Settles the Codex form and fills the Open Questions on commands and project path. The spike itself is a throwaway console server kept outside the source tree.

#### 2. `ContextGuru.slnx` (Create)

*   **Type / Scope**: Solution file.
*   **Proposed Changes**: Lists the three projects below.

#### 3. `Directory.Build.props` (Create)

*   **Type / Scope**: Build configuration.
*   **Proposed Changes**: Sets `net10.0`, nullable reference types, implicit usings, analyzers on, and warnings as errors for all projects.

#### 4. `.gitignore` (Create)

*   **Type / Scope**: Repository configuration.
*   **Proposed Changes**: Ignores `bin/`, `obj/`, test results and local database files.

#### 5. `src/ContextGuru.Core/ContextGuru.Core.csproj` (Create)

*   **Type / Scope**: Class library project.
*   **Proposed Changes**: References `Microsoft.Data.Sqlite` only. No MCP package, so Core stays free of MCP types.

#### 6. `src/ContextGuru.Core/Models/ContextRecord.cs` (Create)

*   **Type / Scope**: Data types.
*   **Proposed Changes**: Records for a stored context (name, summary, body, source agent, created and updated times, project fields), a previous-version summary, and the `ProjectInfo` record (name, path, key, source).

#### 7. `src/ContextGuru.Core/Projects/ProjectResolver.cs` (Create)

*   **Type / Scope**: Pure logic with file system calls.
*   **Proposed Changes**: `Resolve(projectPath?, rootsFromClient?, cwd)` returns `ProjectInfo`. Picks the source in the order argument, roots, cwd. Walks up to find `.git` as a directory or a file, resolves symlinks to a real path, and builds `project_key` (lower-cased path on Windows, real path elsewhere). Records the `source`.

#### 8. `src/ContextGuru.Core/Template/HandoffTemplate.cs` (Create)

*   **Type / Scope**: Validation logic.
*   **Proposed Changes**: Checks that `body` has the level-two headings `Goal`, `Done`, `Decisions`, `Open tasks`, `Next steps` in order, that `summary` is one line within its cap, and that the body is within its cap. Returns errors that name the missing heading or exceeded cap. Also reports whether the `Next steps` section has content.

#### 9. `src/ContextGuru.Core/Naming/ContextNames.cs` (Create)

*   **Type / Scope**: Pure logic.
*   **Proposed Changes**: Normalizes a user name to lower case and checks `[a-z0-9][a-z0-9_-]{0,63}`. Generates `yyyyMMdd-HHmmss` auto names and appends `-2`, `-3` on a clash.

#### 10. `src/ContextGuru.Core/Storage/IContextStore.cs` (Create)

*   **Type / Scope**: Interface.
*   **Proposed Changes**: `Save`, `GetLatest`, `Get(name)`, `List`, `Remove`, each taking a `ProjectInfo`. `Save` returns whether it overwrote and the previous saved time and summary.

#### 11. `src/ContextGuru.Core/Storage/SqliteContextStore.cs` (Create)

*   **Type / Scope**: SQLite implementation.
*   **Proposed Changes**: Opens a connection per call with WAL and `busy_timeout` 5000 ms. `Save` runs one `BEGIN IMMEDIATE` transaction that inserts a new row, or copies the current row into `context_previous` (replacing any earlier previous row) and updates the current row. `GetLatest` orders by `updated_at` descending, then `id`. `Remove` deletes the row and relies on `ON DELETE CASCADE` for the previous row. `List` returns metadata only.

#### 12. `src/ContextGuru.Core/Storage/Migrations.cs` (Create)

*   **Type / Scope**: Schema migrations.
*   **Proposed Changes**: An ordered list of migrations keyed by `PRAGMA user_version`, each run under `BEGIN IMMEDIATE`. Migration 1 creates `contexts`, `context_previous` and an index on `(project_key, updated_at)`. Refuses a database with a higher version than the tool knows.

#### 13. `src/ContextGuru.Core/Storage/DatabasePath.cs` (Create)

*   **Type / Scope**: Path logic.
*   **Proposed Changes**: Returns `CONTEXTGURU_DB` if set, otherwise the per-OS default from the Technical Plan. Creates the directory and sets mode 0600 on the file for Linux and macOS.

#### 14. `src/ContextGuru/ContextGuru.csproj` (Create)

*   **Type / Scope**: Console app and tool package.
*   **Proposed Changes**: `PackAsTool` true with `ToolCommandName` `contextguru`. References Core, the `ModelContextProtocol` package and `Microsoft.Extensions.Hosting`. Embeds the command file templates as resources.

#### 15. `src/ContextGuru/Program.cs` (Create)

*   **Type / Scope**: Entry point.
*   **Proposed Changes**: With no arguments, runs the MCP stdio server and registers the tool classes. Routes `--doctor`, `install` and `uninstall` to their handlers. Sends logging to stderr and the log file and nothing to stdout in server mode.

#### 16. `src/ContextGuru/Tools/ContextTools.cs` (Create)

*   **Type / Scope**: MCP tool class.
*   **Proposed Changes**: The four tools `save_context`, `get_context`, `list_contexts`, `remove_context` with descriptions and input schemas as in the Technical Plan. Each resolves the project (asking the client for roots when `projectPath` is missing), calls Core, and returns the result type below. `remove_context` is marked destructive.

#### 17. `src/ContextGuru/Tools/Results.cs` (Create)

*   **Type / Scope**: Result types.
*   **Proposed Changes**: Result records for the four tools, each with `contract_version` and `project`. Additive changes only.

#### 18. `src/ContextGuru/Tools/AgentInstructions.cs` (Create)

*   **Type / Scope**: Text constants.
*   **Proposed Changes**: The `agent_instructions` text returned by `get_context`, and the error text returned for template failures, so the wording lives in one place.

#### 19. `src/ContextGuru/Logging/FileLogger.cs` (Create)

*   **Type / Scope**: Logging.
*   **Proposed Changes**: A small logger that writes to a rotating file next to the database. It never logs handoff content.

#### 20. `src/ContextGuru/Doctor.cs` (Create)

*   **Type / Scope**: Diagnostic mode.
*   **Proposed Changes**: Prints version, database path, schema version, resolved project and its source, whether git was found, and the result of a write test.

#### 21. `src/ContextGuru/Install/Installer.cs` (Create)

*   **Type / Scope**: Install and uninstall logic.
*   **Proposed Changes**: For each agent, registers the MCP server in its configuration and writes the four command files from the embedded templates. Each file has a marker header. Existing files without the marker are backed up first. `uninstall` removes only marked files and the registration.

#### 22. `src/ContextGuru/Install/AgentTargets.cs` (Create)

*   **Type / Scope**: Per-agent data.
*   **Proposed Changes**: For Claude Code, Codex and Cursor: the config file, command folder, file extension, argument placeholder and how the tool name is addressed. Values come from `docs/spike.md`.

#### 23. `src/ContextGuru/Install/Templates/` (Create, four files)

*   **Type / Scope**: Command file templates, `save-context.md`, `get-context.md`, `list-contexts.md`, `remove-context.md`.
*   **Proposed Changes**: Each has an agent-neutral body plus placeholders filled per agent. The save template carries the handoff template, the instruction to omit secrets and file contents, and the instruction to pass the working folder as `projectPath`. The remove template tells the agent to confirm with the user before calling the tool.

#### 24. `tests/ContextGuru.Tests/ContextGuru.Tests.csproj` (Create)

*   **Type / Scope**: xUnit test project.
*   **Proposed Changes**: References both projects and the xUnit packages.

#### 25. `tests/ContextGuru.Tests/ProjectResolverTests.cs` (Create)

*   **Type / Scope**: Unit tests.
*   **Proposed Changes**: Covers the argument, roots and cwd order, `.git` as a directory and as a file, a symlinked folder, Windows case folding, and a folder with no `.git`.

#### 26. `tests/ContextGuru.Tests/HandoffTemplateTests.cs` (Create)

*   **Type / Scope**: Unit tests.
*   **Proposed Changes**: Covers a valid body, each missing heading, wrong order, summary over its cap or on several lines, body over its cap, and the next-steps empty check.

#### 27. `tests/ContextGuru.Tests/ContextNamesTests.cs` (Create)

*   **Type / Scope**: Unit tests.
*   **Proposed Changes**: Covers normalization, rejected names, auto-name format and clash suffixes.

#### 28. `tests/ContextGuru.Tests/SqliteContextStoreTests.cs` (Create)

*   **Type / Scope**: Integration tests with a temporary database file.
*   **Proposed Changes**: Covers insert, overwrite with previous copy, a second overwrite replacing the previous row, latest ordering, list scoping by project, remove deleting both rows, and two processes writing at once.

#### 29. `tests/ContextGuru.Tests/MigrationTests.cs` (Create)

*   **Type / Scope**: Integration tests.
*   **Proposed Changes**: Covers a new database, a repeated start, and refusal of a newer `user_version`. Includes a golden check of the schema.

#### 30. `tests/ContextGuru.Tests/StdioToolTests.cs` (Create)

*   **Type / Scope**: End-to-end tests over stdio.
*   **Proposed Changes**: Starts the tool, sends JSON-RPC calls for each of the four tools, and checks results, error text and that stdout contains only protocol messages.

#### 31. `.github/workflows/ci.yml` (Create)

*   **Type / Scope**: CI configuration.
*   **Proposed Changes**: Builds and tests on Windows, Linux and macOS.

#### 32. `README.md` (Create)

*   **Type / Scope**: Documentation.
*   **Proposed Changes**: Short: what it is, install with `dotnet tool install`, run `contextguru install --agent <name>`, the four commands, the database location and `--doctor`.

#### 33. `docs/designs/contextguru.md` (Modify)

*   **Type / Scope**: Design document.
*   **Proposed Changes**: Replaced by the finalized design when this draft is approved.

### Verification Plan

*   **Compilation/Build**: `dotnet build -warnaserror`
*   **Unit Tests**: `dotnet test`
*   **Static Analysis & Linting**: `dotnet format --verify-no-changes` and the analyzers enabled in `Directory.Build.props`
*   **Test Coverage Requirement**: No percentage gate. Every rule stated in the Technical Plan has a test.
*   **E2E/Integration**: `StdioToolTests` in CI on three operating systems. Manual acceptance on Windows: run CUJ 1 (save in one agent, get in another) across Claude Code, Codex and Cursor, run `contextguru --doctor`, and check that a get with the wrong folder explains where it searched.

--------------------------------------------------------------------------------

<!-- Provenance: Indicates initial structural drafting via EGM. -->

*Drafted with EGM*
