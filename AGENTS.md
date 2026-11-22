# AGENTS.md

> Guidance for AI coding agents working in this repo. Read this first.
> **Before building, changing, or debugging anything: verify live state** (read the actual
> code, schema, config, and deployed branch). Do not act on stale assumptions.

<!-- agents-md:begin id=communication -->
## Talking to the human

How to format replies. The reader works in a terminal and finds dense walls of text hard to parse.

- Lead with the answer in one sentence. Put the detail after it.
- Short paragraphs, 2 to 3 sentences max, with a blank line between them.
- Bold at most one thing per message. If everything is bold, nothing stands out.
- Three or more items become a list, not a packed paragraph.
- No em-dashes. Use a period or a comma.
- No emojis in replies unless the human asks for them.
- Plain language over jargon. Explain as if the reader is smart but not deep in this stack.
- Keep replies short. If a reply would run long, give the headline and ask before expanding.
- End with at most one question.
<!-- agents-md:end id=communication -->

<!-- agents-md:begin id=overview -->
## Overview
ContextGuru is a local MCP server (C#, .NET 10, stdio) that stores short coding-agent handoffs in a shared SQLite file, so work saved in Claude Code, Codex or Cursor can be read back in any of them. Four tools (save, get, list, remove) are called by four commands: `/cg-save-context [name]`, `/cg-get-context [name]`, `/cg-list-contexts`, `/cg-remove-context {name}`. It ships as a `dotnet tool` built locally.

Status: design approved, spike done, Tasks 0 to 3 done (skeleton, `HandoffTemplate`, `ProjectResolver`). The source of truth is `docs/designs/contextguru.md` (Status: Approved). Spike results are in `docs/spike.md`. The task list is `docs/superpowers/plans/2026-11-21-contextguru.md`, one GitHub issue per task. Next is Task 4, migrations and storage exceptions.
<!-- agents-md:end id=overview -->

<!-- agents-md:begin id=commands -->
## Commands
Build, test and format work today. The run, doctor, install and `local/` commands are planned (design's Verification Plan), so check they exist before running them.

- Install: `dotnet restore`
- Dev / run: `contextguru` (MCP stdio server), `contextguru --doctor` (diagnostics)
- Build: `dotnet build ContextGuru.slnx -c Release`
- Test: `dotnet test ContextGuru.slnx -c Release --no-build`
- Lint / format / typecheck: `dotnet format ContextGuru.slnx --verify-no-changes`
- Local build and install (Windows): `local/build.ps1`, `local/install.ps1`
- Register with an agent: `contextguru install --agent claude|codex|cursor`
- One test class: `dotnet test tests/ContextGuru.Tests -c Release --filter "FullyQualifiedName~<ClassName>"`

The spike confirmed the plain `dotnet test` forms above. `dotnet test --solution` and `--project` fail here.
<!-- agents-md:end id=commands -->

<!-- agents-md:begin id=testing -->
## Testing
xUnit v2 (`xunit` 2.9.3) in `tests/ContextGuru.Tests`, 78 tests so far. `TempDir` gives each test a throwaway folder. xUnit v2 has no dynamic skip, so a test that cannot set up (for example no link can be made) must fail, not return. Planned: Core store tests use a temporary SQLite file, and stdio tests start the built tool and drive it with an MCP client. All tests run on Windows. Linux and macOS are not tested in v1. Build, tests and format check must pass before a change is done. Warnings are errors.
<!-- agents-md:end id=testing -->

<!-- agents-md:begin id=structure -->
## Project structure
Current layout:

- `docs/designs/contextguru.md`: the design and the 34-item implementation plan (phases 0 to 5)
- `docs/designs/contextguru_rfc.md`: earlier RFC copy
- `docs/concepts_brief.md`, `docs/design_draft.md`: brief and first draft
- `docs/egm-reports/`: review reports from the design review
- `docs/spike.md`: observed spike results (agents, MCP SDK, SQLite, packages). Later tasks read their values from here
- `docs/superpowers/plans/2026-11-21-contextguru.md`: the implementation plan, tasks 0 to 15

Code layout. Built so far:

- `src/ContextGuru.Core`: class library, no MCP references. Has `Models/` (records), `Template/HandoffTemplate.cs` and `Projects/ProjectResolver.cs`
- `src/ContextGuru`: console app, still only `Program.cs`
- `tests/ContextGuru.Tests`: xUnit tests plus the `TempDir` helper

Planned (from the design):

- In Core: `IContextStore`, the SQLite store, migrations
- In `src/ContextGuru`: MCP host, four tools, doctor, installer, command file templates, packaged as a `dotnet tool`
- `local/`: `build.ps1` and `install.ps1`
- `spike/`: git-ignored scratch folder (the Task 0 spike code is already deleted)
<!-- agents-md:end id=structure -->

<!-- agents-md:begin id=conventions -->
## Code style & conventions

- C# on `net10.0`, nullable enabled, implicit usings, file-scoped namespaces. `.editorconfig` plus `dotnet format` enforce style.
- `AnalysisLevel` is `latest-recommended` and `TreatWarningsAsErrors` is on.
- Central package versions in `Directory.Packages.props`.
- Keep it simple and incremental. Do not overengineer or program defensively. Use the latest APIs and SDKs.
- Core has no MCP and no logging packages. The store is synchronous (`IContextStore`).
- Tool wire format: snake_case via `[JsonPropertyName]`, nulls sent as `null`, errors as `isError: true` with one text message.
- Return errors as `new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "..." }] }`. A thrown exception has its message rewritten by the SDK, so the exact error text would be lost.
- Pin package versions from the table in `docs/spike.md` (`ModelContextProtocol` 2.4.0, `Microsoft.Data.Sqlite` 11.0.0, `Serilog.Sinks.File` 7.0.0).
- Never log handoff content. In server mode nothing but the protocol may reach stdout.
- Keep README short.
<!-- agents-md:end id=conventions -->

<!-- agents-md:begin id=git -->
## Git & PR workflow
Default branch is `main`. Work for each task goes on a branch named `task-<N>-<short-name>` (for example `task-0-spike`) and ships by pull request. The PR body says `Closes #<issue>`. Commit messages are short sentences, for example "Record spike results". No PR template exists.
<!-- agents-md:end id=git -->

<!-- agents-md:begin id=gotchas -->
## Gotchas & hard-won lessons
Confirmed by the spike (details in `docs/spike.md`):

- `BeginTransaction()` takes the write lock at BEGIN. The wait on a held lock is roughly the larger of `Default Timeout` and `busy_timeout`, not the smaller. Both are set to 5 s and a test must still prove the write lock.
- A fresh SQLite connection has `busy_timeout` 0, so set the pragma on every connection.
- Only Claude Code supports MCP roots. Codex and Cursor start the server in the project folder, so `projectPath` and the working folder cover them.
- Codex commands are skills (`.agents/skills/<name>/SKILL.md`, run as `$<name>`), not `/name` prompts. Cursor and Claude Code use `/name` command files. Codex and Cursor have no argument placeholder, so a command body must say exactly what to pass to the tool.
- Codex MCP config is TOML (`~/.codex/config.toml`). No TOML package is needed. Decide in Task 13 between `codex mcp add` and a text append.

Carried over from the design:

- WAL needs a local disk. Network or synced folders can corrupt the database.
- Several agent processes share one database file, so every write is a short `BEGIN IMMEDIATE` transaction. Codes 5 and 6 map to `DatabaseBusyException`.
- Stored handoff text is untrusted. `get_context` wraps the body in `<<<HANDOFF {id} BEGIN/END>>>` with a random id and returns `agent_instructions`.
- Project identity uses the `projectPath` argument, then MCP roots, then the server working folder. The `.git` walk is done by hand, not by calling `git`.
<!-- agents-md:end id=gotchas -->

<!-- agents-md:begin id=security -->
## Security & secrets
No `.env` files. The only environment variable is `CONTEXTGURU_DB`, which overrides the database path. The database is plaintext and holds handoffs, so never put secrets in a handoff or in test data. On Linux and macOS the data folder is mode 0700 and the file 0600.
<!-- agents-md:end id=security -->

<!-- agents-md:begin id=boundaries -->
## Boundaries
The most important section. Keep it current.

- ✅ **Always**: read `docs/designs/contextguru.md` before implementing. Work in small steps and validate each one. Write tests in the same phase as the code. Follow the phase order and each phase's "Done when" check.
- ⚠️ **Ask first**: installing any package (each NuGet package needs approval, including Serilog packages and any TOML package). Deleting files. Changing the design, size caps, tool names or schema. Reversing a decision already made in the design. Adding GitHub Actions or publishing (deferred to v2).
- 🚫 **Never**: run destructive git commands (`reset --hard`, `push --force`). Commit secrets or real database files. Write anything but protocol to stdout in server mode. Log handoff content. Touch real agent configuration in tests (the installer takes a base folder).
<!-- agents-md:end id=boundaries -->

## Notes
<!-- Human-owned. Anything you write here is never touched by re-runs of agents-md. -->
- Work on 1 issue at a time
- Use the Github tools to read the Issue from Github
- When starting work on an issue move its status in github from `Todo` to `In Progress`
- Develop the feature - Do not skip any step from the 5 step red-green-refactor cycle
- Thoroughly test the feature with unit tests and integration tests and fix any issues
- Once the task is completed and properly tested, Submit a PR using your github tools
- Once the PR is merged move the Issue to Done with the a retrospective comment which should include.
  1. A brief description of what was done
  2. Any changes from what was planned in the ticket
  3. Any issues faced during the implementation along with what was done to fix it
- Ensure the Task is checked off in the Plan document
---
<!-- Generated and maintained by agents-md (https://github.com/eugeniughelbur/agents-md).
     Content INSIDE `agents-md:begin/end` markers is refreshed on re-run, unless you edited it:
     an edited section is kept as you left it. Empty a section (keep its markers) to regenerate it.
     Everything OUTSIDE the markers (including ## Notes) is yours and is preserved. -->
