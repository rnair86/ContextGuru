# ContextGuru

A local MCP server that stores short coding-agent handoffs in a shared SQLite file. Save a handoff in Claude Code, Codex or Cursor and read it back in any of them.

It is a .NET tool (`contextguru`) that talks to the agent over stdio. The database stays on your machine. The agent may send a handoff to its model.

## Prerequisites

- Windows
- .NET 10 SDK
- PowerShell 7.4 or newer
- The .NET tools folder (`%USERPROFILE%\.dotnet\tools`) on PATH, so agents and `install.ps1` find `contextguru`
- Codex CLI (`codex`), for `--agent codex` and for the tests that `local/build.ps1` runs

## Build and install

```powershell
local/build.ps1     # restore, build, test, format check
local/install.ps1   # build.ps1, then pack and install as a global tool, then --doctor
```

Both scripts stop at the first failing step. `install.ps1` replaces any installed `ContextGuru` global tool with the version in `src/ContextGuru/ContextGuru.csproj`.

Before upgrading, close the agents that use ContextGuru. If a running server holds the tool, the uninstall step fails and the script stops. If any later step fails, fix the cause and run `install.ps1` again.

## Register with an agent

The agent is `claude`, `codex` or `cursor`.

```powershell
contextguru install --agent claude
contextguru uninstall --agent claude
```

`install` adds the MCP server entry and the four command files. Run it again after upgrading the tool. `uninstall` removes the `contextguru` server entry (whoever wrote it) and the marked command files, and leaves the database.

Both commands copy an existing agent configuration to `<file>.bak` (then `.bak.1`, `.bak.2`) before they change it. For Codex they run `codex mcp add` and `codex mcp remove`, which rewrite every `[mcp_servers.*]` table in `~/.codex/config.toml` and can drop comments and keys they do not know. The backup keeps the original.

## Commands

| Action | Claude Code and Cursor | Codex |
| :--- | :--- | :--- |
| Save a handoff | `/cg-save-context [name]` | `$cg-save-context [name]` |
| Read a handoff | `/cg-get-context [name]` | `$cg-get-context [name]` |
| List handoffs | `/cg-list-contexts` | `$cg-list-contexts` |
| Remove a handoff | `/cg-remove-context {name}` | `$cg-remove-context {name}` |

Handoffs belong to a project: the nearest git repository above the folder the agent was started in, or that folder if there is none.

## Database

The database is `%LOCALAPPDATA%\ContextGuru\contexts.db`. Set `CONTEXTGURU_DB` to use another file. Keep it on a local disk, not a network or synced folder.

Handoffs are stored as plaintext. Never put secrets in one.

```powershell
contextguru --doctor
```

`--doctor` prints the version, database path, schema version, the project it resolves from the current folder, and a write test. It exits 0 when the schema check and the write test pass, also when the database does not exist yet (the write test is then skipped). It exits 1 if either fails, for example a database that is newer than the tool.

## License

MIT, see `LICENSE`.
