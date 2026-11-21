# Stage 5: Initial Zoom Out Report

* **Target:** `docs/designs/contextguru.md` (lines 1..437)
* **Reviewers:** `egm-zoom-out` (against `zoom-out-standard.md`), `egm-inventive-catalyst`
* **Date:** 2026-11-20

Both reviewers ran from a fresh context with no extra context files. Line numbers refer to the target file as reviewed.

## Part 1: Zoom Out (simplicity and scope)

**Verdict:** Context is sufficient (lines 9-28). The design does not pass the standard as written. The document says in several places that the server may be unnecessary, yet it commits to a full build.

The repo had no source code, so reuse findings are about platform and ecosystem choices. The reviewer did not find a root `README.md` or `context.md`, so it could not check project alignment.

| # | Severity | Finding | Lines |
|---|----------|---------|-------|
| 1 | Blocker | The server is built before its premise is validated. Open Question (57) and Alternative A (190) say the one-week markdown trial may change whether the server is needed, but the Implementation Plan schedules only a command-mechanism spike before building Core, the tool and the installer. Make the template trial an explicit gate before the build. Alternative A already covers most of the stated problem, and its rejection ("Rejected by author") is an assertion that contradicts line 57. | 57, 190, 254-256 |
| 2 | Major | The previous-version table has no consumer. No tool returns it (61), yet it adds a second table, copy logic, cascade delete, overwrite-report fields and tests. Build it when a read path exists, or return the overwrite warning without storing the old body. Alternatives G and H spend about 15 lines on a mechanism nobody can read. | 36, 48, 61, 142-148, 163, 226-232 |
| 3 | Major | Speculative project split. Core plus tool and `IContextStore` with one implementation are justified by the dashboard, which is a Non-Goal (44). Alternative I rejects three projects for the same reason the second project and the interface should go. Use one project and extract Core when the dashboard is real. | 93-96, 234-238, 44, 112 |
| 4 | Major | Versioning machinery for an unreleased v1: `contract_version`, additive-only changes, and a migrations framework. Re-running `install` after an upgrade rewrites command files. A single idempotent `CREATE TABLE IF NOT EXISTS` plus a `user_version` check is enough until the first schema change. | 161, 343, 151, 318 |
| 5 | Major | Altitude drops into the weeds. 33 file-level items including `.gitignore`, `Directory.Build.props`, the solution file, one test file per class, a constants file, and code-level name regex and clash-suffix rules. File lists belong in a task breakdown. | 252-423, 133, 163, 303 |
| 6 | Major | Scope creep in tools and install: the not-found response searching other projects (164), `--doctor` with a test write plus a bespoke rotating file logger (174-176, 350-353; use standard logging), and `agent_instructions` overlapping the command files so behavior wording lives in two places (168). | 35-38, 164, 168, 176, 178-180, 350-358 |
| 7 | Major | The design depends on unresolved facts (command locations, roots support, SDK version, SQLite native support), yet the plan specifies the three-level resolver, per-agent installer and `AgentTargets.cs` in detail. Narrow v1 to the agents the spike confirms, or keep these pieces light until the spike is done. | 55-61, 119, 180, 328 |
| 8 | Major | Hand-rolled platform behavior: symlink resolution, `.git` file-or-directory detection, case folding. `git rev-parse --show-toplevel` handles worktrees, submodules and symlinks when git exists. `Environment.GetFolderPath(SpecialFolder.LocalApplicationData)` already maps per OS. | 117-121, 155-157, 163, 320-323 |
| 9 | Nit | Alternatives J and K (secret scanning, NativeAOT) are Non-Goal level topics and can be compressed. | 240-251, 47 |
| 10 | Nit | Most alternatives say "Rejected by author" without a reason tied to the problem. The SQLite choice (196) reads as a preference, and a dashboard (a Non-Goal) was the main driver. | 190-244 |
| 11 | Nit | Process rules in a design doc (dependency confirmation, 256). The spike is "kept outside the source tree" (263) but its results go to `docs/spike.md`. | 256, 263 |

**What passes:** the objective, problem and goals are clear. The Non-Goals are disciplined. SQLite and the official `ModelContextProtocol` SDK are standard choices. "Remove requires an exact name" is simple and sound. Showing the project in every response addresses a real failure mode.

**Recommended actions from the reviewer:**
1. Make the markdown template trial an explicit gate before building.
2. Drop the previous-version table, or add a way to read it.
3. Merge into one project and drop `IContextStore`.
4. Drop `contract_version`, the migration framework and the bespoke file logger.
5. Shorten the Implementation Plan to components and interfaces.
6. Use `git rev-parse` and `SpecialFolder` instead of hand-rolled path logic.

## Part 2: Inventive Catalyst (contradictions the design accepts)

The reviewer treated these as resolved and not in need of change: SQLite WAL with `BEGIN IMMEDIATE` (157), the layered project source fallback (121), the one-slot previous version (226), and treating `body` as data in `agent_instructions` (168).

| # | Severity | False compromise | Proposed resolution | Secondary liabilities |
|---|----------|------------------|---------------------|-----------------------|
| 1 | Major | Project identity (33, 49, 117-121, 198-208): "path as key" is treated as the only option because not all repos have a remote. Clones, WSL, moved folders and symlink or case differences orphan contexts silently. Line 164 only partly mitigates this. | Compute a composite identity at save time: a git anchor when one exists (for example the root commit hash) plus the normalized path as fallback. Store both and match on either. Make the folder-name hint at 164 actionable, or add an "adopt contexts from path X" action. | Root-commit identity can merge forks or shallow clones, and repos with several root commits need a deterministic pick. Non-git folders still depend on the path. Needs a column and a migration. Harder to explain, so the response must state which key matched. |
| 2 | Major | Handoff template (35, 57, 163, 172, 216-220): consistency is bought by hard rejection, so every malformed handoff is a failed tool call the agent must retry. Headings are fixed while line 57 admits they are unvalidated. | Server owns the template: a `get_template` tool or the template in the save error, so command files do not embed a copy (373). Normalize slightly-off headings or accept them with a `warnings` field. Reject only on size caps and missing content. | Lenient parsing weakens the uniform shape. A new tool or round trip adds a contract surface. The alias list needs maintenance. Agents may ignore warnings. |
| 3 | Major | Tool contracts and installation (55, 59, 161, 180, 373): command files cannot update themselves, so changes are additive-only forever. Behavior is duplicated into three agent formats. | Make command files one-line stable stubs that tell the agent to call the tool and follow the returned instructions. Extend `agent_instructions` to save and remove. Have `--doctor` and server start compare the installed marker version and print "run contextguru install". Test MCP prompts in the spike as a replacement for command files. | Server-returned instructions are a prompt-injection and trust surface. Behavior depends on the model following returned text. A round trip on first call. MCP prompt support differs by agent, so a file fallback is still needed. |
| 4 | Major | Secrets (44, 47, 168, 240-244): the bar is perfect detection, so the answer is no control. Handoffs are plain text on disk and a dashboard or synced folder could expose them. | A narrow high-precision check for a small fixed set of signatures (private key blocks, known token prefixes). Return a non-fatal `warnings` entry or reject unless `acknowledge_risk` is set. | Pattern maintenance. False confidence. The override argument adds to the contract. Warnings interrupt flow. |
| 5 | Nit | Previous version (61, 142-149, 226-232) is stored but unreadable. | A revisions table with retention N (N=1 now), pruned inside the save transaction, plus a `version: "previous"` argument on `get_context`. Otherwise drop the table or document it as internal. | A more complex save transaction and index. Storage grows with N. One more contract argument. |

Reviewer priority order: finding 3 first (settle it before the spike, since it sets installer size), then 1, 2 and 4, then 5.

## Conflicts between the two reviewers

* **Previous version:** zoom-out says drop it (finding 2); inventive-catalyst says make it readable (finding 5). Both agree the current write-only form is the weak point.
* **Contract and installer:** zoom-out says drop `contract_version` and shorten the installer plan; inventive-catalyst says keep contracts stable and move more behavior to the server so command files stay stubs. Both want less logic in the per-agent files.
* **Secrets:** zoom-out calls the accepted risk acceptable (finding 9); inventive-catalyst recommends a narrow scanner (finding 4). Author decision stands (template instruction only) unless changed.
* **Project identity:** zoom-out suggests `git rev-parse` for resolution; inventive-catalyst suggests a git-derived anchor in the key. These are compatible.
