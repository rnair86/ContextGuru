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

Status: design only. No code exists yet. The source of truth is `docs/designs/contextguru.md` (Status: Draft). Implementation starts with phase 0, the spike (`docs/spike.md`).
<!-- agents-md:end id=overview -->

<!-- agents-md:begin id=commands -->
## Commands
No code exists yet. These are the planned commands from the design's Verification Plan. Check they exist before running.

- Install: `dotnet restore`
- Dev / run: `contextguru` (MCP stdio server), `contextguru --doctor` (diagnostics)
- Build: `dotnet build ContextGuru.slnx -c Release`
- Test: `dotnet test ContextGuru.slnx -c Release --no-build`
- Lint / format / typecheck: `dotnet format ContextGuru.slnx --verify-no-changes`
- Local build and install (Windows): `local/build.ps1`, `local/install.ps1`
- Register with an agent: `contextguru install --agent claude|codex|cursor`
<!-- agents-md:end id=commands -->

<!-- agents-md:begin id=testing -->
## Testing
Planned: xUnit in `tests/ContextGuru.Tests`. Core tests use a temporary SQLite file. Stdio tests start the built tool and drive it with an MCP client. All tests run on Windows. Linux and macOS are not tested in v1. Build, tests and format check must pass before a change is done. Warnings are errors.
<!-- agents-md:end id=testing -->

<!-- agents-md:begin id=structure -->
## Project structure
Current layout:

- `docs/designs/contextguru.md`: the design and the 34-item implementation plan (phases 0 to 5)
- `docs/designs/contextguru_rfc.md`: earlier RFC copy
- `docs/concepts_brief.md`, `docs/design_draft.md`: brief and first draft
- `docs/egm-reports/`: review reports from the design review

Planned layout (from the design):

- `src/ContextGuru.Core`: class library, no MCP references (project resolver, handoff template checker, `IContextStore`, SQLite store, migrations)
- `src/ContextGuru`: console app packaged as a `dotnet tool` (MCP host, four tools, doctor, installer, command file templates)
- `tests/ContextGuru.Tests`: xUnit tests
- `local/`: `build.ps1` and `install.ps1`
- `spike/`: throwaway spike code, git-ignored, deleted after `docs/spike.md` is written
<!-- agents-md:end id=structure -->

<!-- agents-md:begin id=conventions -->
## Code style & conventions

- C# on `net10.0`, nullable enabled, implicit usings, file-scoped namespaces. `.editorconfig` plus `dotnet format` enforce style.
- `AnalysisLevel` is `latest-recommended` and `TreatWarningsAsErrors` is on.
- Central package versions in `Directory.Packages.props`.
- Keep it simple and incremental. Do not overengineer or program defensively. Use the latest APIs and SDKs.
- Core has no MCP and no logging packages. The store is synchronous (`IContextStore`).
- Tool wire format: snake_case via `[JsonPropertyName]`, nulls sent as `null`, errors as `isError: true` with one text message.
- Never log handoff content. In server mode nothing but the protocol may reach stdout.
- Keep README short.
<!-- agents-md:end id=conventions -->

<!-- agents-md:begin id=git -->
## Git & PR workflow
Default branch is `main`. Current work is on `master`. Commit messages so far are short, sentence-style, for example "update design docs" and "EGM Draft". No branch naming or PR convention is recorded.
<!-- agents-md:end id=git -->

<!-- agents-md:begin id=gotchas -->
## Gotchas & hard-won lessons
From the design, still to be confirmed by the spike:

- Whether `Microsoft.Data.Sqlite` `BeginTransaction()` issues `BEGIN IMMEDIATE`, and how `Default Timeout` interacts with `busy_timeout`. Both timeouts are set to 5 s and a test must prove the write lock.
- WAL needs a local disk. Network or synced folders can corrupt the database.
- Several agent processes share one database file, so every write is a short `BEGIN IMMEDIATE` transaction. Codes 5 and 6 map to `DatabaseBusyException`.
- Stored handoff text is untrusted. `get_context` wraps the body in `<<<HANDOFF {id} BEGIN/END>>>` with a random id and returns `agent_instructions`.
- Project identity uses the `projectPath` argument, then MCP roots, then the server working folder. The `.git` walk is done by hand, not by calling `git`.
- Codex's command form is unverified. Do not fix the installer or tool names before the spike.
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

---
<!-- Generated and maintained by agents-md (https://github.com/eugeniughelbur/agents-md).
     Content INSIDE `agents-md:begin/end` markers is refreshed on re-run, unless you edited it:
     an edited section is kept as you left it. Empty a section (keep its markers) to regenerate it.
     Everything OUTSIDE the markers (including ## Notes) is yours and is preserved. -->
