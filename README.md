# ContextGuru

A local MCP server that stores short coding-agent handoffs in a shared SQLite file. Save a handoff in Claude Code, Codex or Cursor and read it back in any of them.

It is a .NET tool (`contextguru`) that talks to the agent over stdio. Nothing leaves your machine.

## Prerequisites

- Windows
- .NET 10 SDK
- PowerShell 7.4 or newer
- Codex CLI (`codex`), for `--agent codex` and for the tests that `local/build.ps1` runs

## Build and install

```powershell
local/build.ps1     # restore, build, test, format check
local/install.ps1   # build.ps1, then pack and install as a global tool, then --doctor
```

Both scripts stop at the first failing step. `install.ps1` replaces any installed `ContextGuru` global tool with the version in `src/ContextGuru/ContextGuru.csproj`.

## Register with an agent

```powershell
contextguru install --agent claude|codex|cursor
contextguru uninstall --agent claude|codex|cursor
```

`install` adds the MCP server entry and the four command files. Run it again after upgrading the tool. `uninstall` removes only what `install` wrote and leaves the database.

An existing agent configuration is copied to `<file>.bak` (then `.bak.1`, `.bak.2`) before it is changed. For Codex the installer runs `codex mcp add`, which rewrites every `[mcp_servers.*]` table in `~/.codex/config.toml` and can drop comments and keys it does not know. The backup keeps the original.

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

`--doctor` prints the version, database path, schema version, the project it resolves from the current folder, and a write test. It exits 0 on a good database and 1 on a failed write test.

## License

MIT, see `LICENSE`.
