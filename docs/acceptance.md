# Manual Acceptance Results (Task 15)

Date: 2026-11-24. Platform: Windows. Passing agents from `docs/spike.md`: Claude Code (`2.1.332`), Codex (`0.172.0`), Cursor (`1.0.0`).

## 1. Local Install and Agent Registration

- [x] Run `pwsh -File local/install.ps1` — Exit 0. `dotnet build ContextGuru.slnx -c Release` (0 warnings, 0 errors), `dotnet test ContextGuru.slnx -c Release --no-build` (325/325 passed), `dotnet format ContextGuru.slnx --verify-no-changes` (clean), packed `local/nupkg/ContextGuru.0.1.0.nupkg`, uninstalled and reinstalled global tool `contextguru` 0.1.0, and `contextguru --doctor` printed all 8 lines (`schema version: not created yet (created on first save)`, `write test: skipped`, exit 0).
- [x] Run `contextguru install --agent claude` — Exit 0. Output: `Installed ContextGuru for claude.`
- [x] Run `contextguru install --agent codex` — Exit 0. Output: `Installed ContextGuru for codex.`
- [x] Run `contextguru install --agent cursor` — Exit 0. Output: `Installed ContextGuru for cursor.`

## 2. CUJ 1: Save in One Agent, Get in Another (All 6 Ordered Pairs)

Each pair ran in its own fresh throwaway git repository under `%TEMP%\cg-accept\`.

- [x] Claude Code -> Codex (`p1-claude-codex`) — Claude Code `/cg-save-context` saved generated name `20261124-211318` (no overwrite). Codex `$cg-get-context` loaded it (`contextguru/get_context` completed), showed the summary, and asked: "Would you like to continue in this repository? If so, what would you like to work on?"
- [x] Claude Code -> Cursor (`p2-claude-cursor`) — Claude Code `/cg-save-context` saved generated name `20261124-211333`. Cursor `/cg-get-context` loaded `20261124-211333` (from Claude), displayed the summary and next steps, and asked: "Want me to continue from those next steps?"
- [x] Codex -> Claude Code (`p3-codex-claude`) — Codex `$cg-save-context` saved generated name `20261125-024034` for `p3-codex-claude`. Claude Code `/cg-get-context` loaded `20261125-024034` (from Codex), showed the summary and next step, and asked whether to continue or wait for instructions.
- [x] Codex -> Cursor (`p4-codex-cursor`) — Codex `$cg-save-context` saved generated name `20261125-024043` for `p4-codex-cursor`. Cursor `/cg-get-context` loaded `20261125-024043` (from Codex), displayed the summary and next step, and asked: "Want me to continue from that next step?"
- [x] Cursor -> Claude Code (`p5-cursor-claude`) — Cursor `/cg-save-context` saved generated name `20261124-211249` for `p5-cursor-claude`. Claude Code `/cg-get-context` loaded `20261124-211249` (saved from Cursor), showed the summary and next step, and asked: "What would you like to work on?"
- [x] Cursor -> Codex (`p6-cursor-codex`) — Cursor `/cg-save-context` saved generated name `20261124-211355` for `C:\Users\rohit\AppData\Local\Temp\cg-accept\p6-cursor-codex`. Codex `$cg-get-context` loaded `20261124-211355`, showed the summary, and asked: "Would you like to continue in this folder? If so, what should I work on?"

## 3. Wrong-Folder Check

- [x] Start an agent in a folder outside any git repository (`%TEMP%\cg-accept\no-repo`) and run `/cg-get-context` — Claude Code called `get_context` (`IsError = false` in `contextguru-20261124.log`) and reported no saved context for `no-repo` (`found: false` with `searched_path` set to that folder).

## 4. Doctor Check

- [x] Run `contextguru --doctor` with an existing database — Exit 0. Observed output:

```text
version: 0.1.0
database path: C:\Users\rohit\AppData\Local\ContextGuru\contexts.db
schema version: 1
project name: ContextGuru
project path: D:\Projects\ContextGuru
project source: cwd
.git found: yes
write test: ok
```
