# Spike results

Status: all answers observed on Windows on 2026-11-21.

## 1. Agents on Windows (Claude Code, Codex, Cursor)

Versions tested: Claude Code 2.1.330, Codex CLI 0.171.0, Cursor Agent CLI (`agent -p`, Cursor app 3.31.11). Each was driven non-interactively against `spike/McpSpike`, whose `probe` tool logs what the agent passed. One throwaway command per agent asked the model to call `probe` with the working folder and the command argument. The Cursor IDE and the interactive TUIs were not driven, only the CLIs.

| Question | Claude Code | Codex | Cursor |
|----------|-------------|-------|--------|
| Custom command passes the agent folder to a tool | PASS | PASS | PASS |
| MCP roots support | YES, `file:///D:/Projects/ContextGuru` | NO, no roots capability | NO, no roots capability |
| Server working folder at start | project folder | project folder | project folder |
| Command file location and name form | `.claude/commands/<name>.md` (project) or `~/.claude/commands`, run as `/<name>` | skill `.agents/skills/<name>/SKILL.md` (repo) or `~/.agents/skills`, run as `$<name>` | `.cursor/commands/<name>.md` (project) or `~/.cursor/commands`, run as `/<name>` |
| Argument syntax | `$ARGUMENTS` in the file | none: the model reads the text after the skill name | none: the model reads the text after the command name |
| Works without a prefix such as `/prompts:` | YES | YES (skills use `$`, no prefix) | YES |
| MCP config file path and format | `.mcp.json` (project), `~/.claude.json` (user), JSON `mcpServers`; `claude mcp add --scope` writes them | `~/.codex/config.toml`, TOML `[mcp_servers.<name>]`; `codex mcp add <name> -- <command>` writes it | `.cursor/mcp.json` (project), `~/.cursor/mcp.json` (user), JSON `mcpServers` |
| Marker comment placement | first line of the command file (`<!-- contextguru:managed -->`): command still worked | right after the SKILL.md front matter: skill still worked | first line of the command file: command still worked |

Observed `probe` lines (identical `projectPath` in all three):

- `client=claude-code/2.1.330 cwd=D:\Projects\ContextGuru rootsCapability=True roots=file:///D:/Projects/ContextGuru projectPath=D:\Projects\ContextGuru name=my-handoff`
- `client=codex-mcp-client/0.171.0 cwd=D:\Projects\ContextGuru rootsCapability=False roots=none projectPath=D:\Projects\ContextGuru name=skill-arg`
- `client=Cursor/1.0.0 cwd=D:\Projects\ContextGuru rootsCapability=False roots=none projectPath=D:\Projects\ContextGuru name=cursor-arg`

Notes that change later tasks:

- The model supplies tool arguments, so a command body must state exactly what to pass. One Claude Code run with a looser prompt sent `name=null`; three runs after that sent the name correctly.
- Codex custom prompts (`~/.codex/prompts`, `/prompts:<name>`) are deprecated in favour of skills per the official docs, and were not tested here. The installer should write a skill, not a prompt.
- Codex needs approval for MCP tools. The headless test used `-c mcp_servers.<name>.default_tools_approval_mode="approve"`. Interactive use will prompt unless the installed config sets it.
- Codex accepts `-c key=value` overrides, which let this spike run without editing `~/.codex/config.toml`.
- Roots exist only in Claude Code. For Codex and Cursor the identity comes from the `projectPath` argument, then the server working folder, which both agents set to the project folder.

Decision per failing agent: no agent failed the main check, so none is dropped. Codex and Cursor lack roots, so they rely on the `projectPath` argument and the working-folder fallback.

### Tool address probes (2026-11-23)

These Windows CLI probes used disposable project paths and empty databases. They left real agent configuration untouched.

- Claude Code emitted a `tool_use` named `mcp__contextguru__list_contexts` and returned `contexts: []`.
- Codex reported structured call fields `server: contextguru` and `tool: list_contexts`, and returned `contexts: []`. These fields identify the call in the CLI event. Separately, upstream Codex uses the raw tool name and server namespace for model-facing registration. The command instructs Codex to call `list_contexts` on the `contextguru` MCP server. [Upstream Codex MCP tool registration](https://github.com/openai/codex/blob/main/codex-rs/codex-mcp/src/rmcp_client.rs)
- Cursor's first native MCP event showed `args.name: contextguru-list_contexts`, `providerIdentifier: contextguru`, and `toolName: list_contexts`. Headless permission blocked that attempt. With `Mcp(contextguru:list_contexts)` allowed in the disposable project, Cursor invoked `contextguru-list_contexts` with `projectPath` and returned `contexts: []` with `isError: false`.

## 2. ModelContextProtocol

Evidence from `spike/McpSpike` (a server and a client in one project, driven over a real stdio process).

- Version: `ModelContextProtocol` 2.4.0, with `Microsoft.Extensions.Hosting` 11.0.0.
- Stdio server registration: `Host.CreateApplicationBuilder(args)`, `builder.Services.AddMcpServer().WithStdioServerTransport().WithTools<T>()`, `await builder.Build().RunAsync()`. Console logging must go to stderr (`LogToStandardErrorThreshold = LogLevel.Trace`).
- Tool attributes: `[McpServerToolType]` on the class, `[McpServerTool(Name = "...")]` on the method, `[Description]` on the method and parameters. A tool can take `McpServer server` and `CancellationToken` as injected parameters.
- Destructive annotation: `[McpServerTool(Destructive = true)]` shows as `ProtocolTool.Annotations.DestructiveHint = true` on the client. `ReadOnly = true` maps to `ReadOnlyHint`.
- Requesting roots: check `server.ClientCapabilities?.Roots is not null`, then `await server.RequestRootsAsync(new ListRootsRequestParams(), ct)` and read `.Roots[].Uri`. Worked against the test client, which supplies roots through `McpClientHandlers.RootsHandler` plus `Capabilities = new ClientCapabilities { Roots = new RootsCapability() }`. Roots were not yet checked against the real agents.
- Thrown exception to `isError`: any exception becomes `isError: true`, but the text is rewritten. A plain exception gives `An error occurred invoking 'boom'.` (message hidden). `McpException("Handoff not found")` gives `An error occurred invoking 'usererror': Handoff not found`. The spec needs exact error text, so tools must return `new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = "..." }] }`, which arrived unchanged as `Handoff not found`.
- Client class used by tests: `McpClient.CreateAsync(new StdioClientTransport(new StdioClientTransportOptions { Name, Command, Arguments }), new McpClientOptions { ... })`, then `ListToolsAsync()` and `CallToolAsync(name, args)`. Result content is `TextContentBlock`.
- Server working folder when started by the test client: the client's working folder (`D:\Projects\ContextGuru\spike\McpSpike`).

## 3. Microsoft.Data.Sqlite

Package `Microsoft.Data.Sqlite` 11.0.0. Evidence from `spike/SqliteSpike` (two connections, WAL, `Pooling=False`).

- `BeginTransaction()` issues `BEGIN IMMEDIATE`: YES. With connection A inside `BeginTransaction()` and no writes yet, connection B's insert failed with code 5. `BeginTransaction(IsolationLevel.Serializable, deferred: false)` behaved the same.
- `Default Timeout` and `busy_timeout` interaction: the wait is not the minimum of the two. A blocked writer on a held lock waited:

  | Default Timeout (s) | busy_timeout (ms) | Failed with code 5 after |
  |---------------------|-------------------|--------------------------|
  | 1 | unset | 1100 ms |
  | 1 | 3000 | 3356 ms |
  | 3 | 1000 | 3817 ms |
  | 3 | unset | 3116 ms |
  | 1 | 300 | 1526 ms |

  Reading (inferred from these timings, not from the driver source): the driver retries busy calls until `Default Timeout` has passed, and each retry waits up to `busy_timeout`. So the total is roughly the larger of the two, rounded up to a whole retry. With both at 5 s expect a failure after about 5 to 6 s.
- Bundled SQLite version: 3.53.4 (needs 3.35 or later). `DELETE ... RETURNING` is available.
- Transaction-start decision: use `connection.BeginTransaction()`, which takes the write lock at BEGIN. Keep both timeouts at 5 s. Task 10 still proves the lock with a test.
- Default `PRAGMA busy_timeout` on a fresh connection is 0, so the pragma must be set on every connection.

## 4. xUnit

Observed with the active SDK 11.0.100 (no `global.json` yet; SDK 10.0.115 is also installed).

- Generation produced by `dotnet new xunit`: xUnit v2 (`xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.4, `Microsoft.NET.Test.Sdk` 17.14.1, `coverlet.collector` 6.0.4), run through VSTest.
- Working `dotnet test` command forms (both passed 1 of 1):
  - All tests: `dotnet test ContextGuru.slnx -c Release`
  - One class: `dotnet test tests/ContextGuru.Tests -c Release --filter "FullyQualifiedName~<ClassName>"`
- Forms that do not work here: `dotnet test --solution ...` and `dotnet test --project ...` fail with an MSBuild switch error, so the plan's native Microsoft Testing Platform alternative does not apply.

## 5. Serilog.Sinks.File

Packages `Serilog.Sinks.File` 7.0.0, `Serilog.Extensions.Hosting` 11.0.0, `Microsoft.Extensions.Hosting` 11.0.0.

- `shared: true` with daily rolling across two processes: YES for concurrent writing. Two processes each wrote 2000 lines to `spike-.log` with `rollingInterval: Day, shared: true`. Result: one file `spike-20261121.log`, 4000 lines, 2000 from each process, lines interleaved, none lost. The actual midnight roll was not exercised, only the dated file name and shared opening.
- Logging-sink decision: use `Serilog.Sinks.File` with `shared: true` and `rollingInterval: Day`.

## 6. Codex MCP config

- Needs TOML: YES. The Codex MCP config is `~/.codex/config.toml` with `[mcp_servers.<name>]` tables (an existing `[mcp_servers.github]` entry confirms the shape). A TOML package is not required: `codex mcp add <name> -- <command> <args>` exists to write the entry, and a section can also be appended as text. Not run against the real config in this spike, so the exact `codex mcp add` output is unverified.
- Decision for Task 13: do not add a TOML package. The installer runs `codex mcp add contextguru -- contextguru` and `codex mcp remove contextguru`, with `CODEX_HOME` always set to the folder of the target config (a temp folder in tests, never the real one).
- Observed with Codex CLI 0.171.0 and a temp `CODEX_HOME`:
  - `codex mcp get contextguru --json` exits 0 with JSON (`transport.type`, `command`, `args`) when registered and 1 when missing. It can print a WARNING on stderr for a home under the temp folder, so parse stdout only.
  - `add` writes `[mcp_servers.contextguru]` with `command = "contextguru"` and no args key. A second add gives the same bytes. It drops extra keys (approval mode, timeout, env) of an existing entry, rewrites CRLF to LF, and drops a comment inside an existing `[mcp_servers.*]` table. Top-level comments and other tables stay, and the new table is placed before later non-mcp tables.
  - Broken TOML or a wrong value type: `add` and `remove` exit 1 with the reason on stderr and the file is unchanged. `remove` of a missing entry exits 0 and leaves the file unchanged.
  - `CODEX_HOME` must exist or codex exits 1. Rewriting the config drops a custom Windows ACL. Accepted.
- Install skips `add` when the entry is already a stdio server running `contextguru` with no args, so extra keys stay. The config is backed up before `add` or `remove`, and the backup is removed again when codex left the bytes unchanged.

## 7. Package versions to pin

Active SDK was 11.0.100 with no `global.json`; SDK 10.0.115 is also installed. Task 1 should add a `global.json` to pin SDK 10 if the build must target it. All spike projects targeted `net10.0`.

| Package | Version |
|---------|---------|
| ModelContextProtocol | 2.4.0 |
| Microsoft.Data.Sqlite | 11.0.0 |
| Microsoft.Extensions.Hosting | 11.0.0 |
| Serilog.Extensions.Hosting | 11.0.0 |
| Serilog.Sinks.File | 7.0.0 |
| xunit | 2.9.3 |
| xunit.runner.visualstudio | 3.1.4 |
| Microsoft.NET.Test.Sdk | 17.14.1 |
| coverlet.collector | 6.0.4 |
