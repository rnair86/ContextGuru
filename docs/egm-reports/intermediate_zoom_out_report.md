# Stage 8: Intermediate Zoom Out Report

* **Target:** `docs/designs/contextguru.md` (full file, after Stage 7 edits)
* **Reviewer:** `egm-zoom-out` against `zoom-out-standard.md`, with `zoom_out_report.md` as the earlier pass
* **Date:** 2026-11-20

No source code exists in the repo, so reuse findings are about platform and ecosystem choices. The reviewer did not check for a root `README.md` or `context.md`.

**Verdict:** Context is sufficient (lines 9-28). The design still does not fully pass. The revisions fixed the premise gate and several vague spots, but added a lot of scope, mostly prose. The design is now larger than the problem it solves.

## Findings: scope and complexity added by the revisions

| # | Severity | Finding | Lines |
|---|----------|---------|-------|
| 1 | Major | **Risks & Mitigations** restates Non-Goals, Tool contracts, Data model and Database location almost line for line. Seven of nine rows end "(see X)". It adds reading cost and a second place to keep in sync. Cut it, or keep only risks with no home elsewhere. The "Unbounded growth" row is a risk the document says is negligible. | 195-207 |
| 2 | Major | **Alternatives grew from about 6 to 12.** Several are Non-Goal restatements or process defenses: H (secret scanning), I (NativeAOT), G (one versus three projects), E and F (two framings of the previous-version question). C (`/cg:` prefix) is a command-name decision. J (agent native memory) and K (repo file) are substantive. L (hosted store) is a strawman. Compress E and F, and fold G, H, I and L into one line each. Reasons are now tied to the problem and "Rejected by author" is gone, but the volume is worse. | 209-281 |
| 3 | Major | **Speculative features:** the home-folder warning (129), sync-folder and network-path detection in `--doctor` (185, 391), and the `similar_contexts` list (130, 173). `similar_contexts` was an earlier finding and is still open. Sync-folder detection needs a maintained list. | 124-130, 185 |
| 4 | Major | **Contract machinery:** `contract_version`, "additive only" and "clients ignore unknown fields" for a v1 with no released clients (earlier finding 4, still open). `agent_instructions` plus delimiters plus a threat model is legitimate, but the document admits it is advisory, and the wording is duplicated in the command files (181, 406). Consider keeping only the "treat body as data" line and the delimiter. | 170, 176-177, 181, 406 |
| 5 | Major | **Upgrade and rollback** is short but duplicates line 160 on downgrade, and the "riskiest areas" sentence is a test-plan item. Merge into Data model and Verification Plan, and delete the section. | 191-193 |
| 6 | Major | **The previous-version table still has no read path** (earlier finding 2, not addressed). The document now says plainly that no tool can read it (36), yet it ships a second table, cascade delete, copy-on-overwrite and overwrite-report fields, and Alternatives E and F spend about 20 lines defending it. The overwrite report (timestamp and summary) could come from the current row before the update, with no second table. Without a read path, recovery needs raw SQL. | 36, 151-157, 172, 175 |
| 7 | Major | **Concurrency text** is reasonable and compact and answers a real multi-process risk. Leave as is. Remove the duplicate pragma list in the plan (346). | 162-166 |
| 8 | Nit | **Altitude:** the Implementation Plan still has 33 file-level items, including `.gitignore`, `Directory.Build.props`, the solution file, one test file per class and a constants file. Name-regex and clash-suffix rules repeat at 142, 172, 181 and 336. Condense to components, interfaces and test categories. | 285-464 |
| 9 | Nit | **Hand-rolled platform behavior** (earlier finding 8, still open): `.git` walk, symlink resolution, case folding and per-OS paths. `git rev-parse --show-toplevel` and `Environment.GetFolderPath` would remove most of it. The document's own "whether git was found" doctor line implies git is optional, so justify not using git. | 118-122, 326, 353-356 |
| 10 | Nit | **Bespoke rotating file logger** (1 MB, last 3 files). Use `Microsoft.Extensions.Logging` to stderr, which Hosting already provides. A file sink is a feature, not a need, for a few writes a day. | 185, 383-386 |
| 11 | Nit | **Process rule in the design:** "Adding each NuGet dependency needs the author's confirmation" is a working agreement, not design. | 289 |

## Status of earlier findings (from `zoom_out_report.md`)

| # | Earlier finding | Now |
|---|-----------------|-----|
| 1 Blocker | Server built before premise validated | Addressed in part. A decision gate (60-62) now exists, but it opens "The server will be built" and gates only on command-mechanism support, so it can never fail on need. Reword it as a gate on need, or drop the word "gate". |
| 2 Major | Previous table has no consumer | Open (finding 6 above). |
| 3 Major | Speculative project split | Open. Core is still justified by a dashboard that is a Non-Goal (97, 44), and `IContextStore` has one implementation (112, 338). |
| 4 Major | `contract_version` and migrations | Open. Migration text grew. Race handling is needed. Drop `contract_version`. |
| 5 Major | Weeds | Open (finding 8). |
| 6 Major | Tool and install creep | Open (findings 3, 4, 10). `--doctor` grew. |
| 7 Major | Depends on unresolved facts | Largely addressed. Open Questions are explicit and the plan is provisional until the spike passes. Installer detail still precedes the spike. |
| 8 Major | Hand-rolled path logic | Open (finding 9). |
| 9-10 Nits | Alternatives compress, give reasons | Reasons fixed, volume worse (finding 2). |
| 11 Nit | Process rules | Open. |

## What the revisions improved (acceptable additions)

* The secrets decision (47, 200, 253-257) is stated once with an owner and a reason.
* Concurrency (162-166) and migration races (160) are necessary correctness text for a multi-process SQLite design.
* Remove safety (175): exact name only, no wildcard.
* The template section (179-181) is compact, and the empty-next-steps rule (181) is concrete and testable.
* Non-Goals (42-51) remain disciplined.

## Recommended edits from the reviewer

1. Delete Risks & Mitigations or cut it to non-duplicated risks. Fold Upgrade and rollback into Data model.
2. Compress Alternatives from 12 to about 5 (project key, project source, template, previous version, native memory or `AGENTS.md`).
3. Remove `similar_contexts`, `contract_version`, sync-folder detection and the home-folder warning, or mark them as post-v1.
4. Either drop the previous-version table or add a read path.
5. Merge into one project and drop `IContextStore`, or state a reason other than the dashboard.
6. Use `git rev-parse`, `GetFolderPath` and standard logging.
7. Replace the 33-file plan with a component and test-category list.
8. Make the decision gate test need, or remove the word "gate".

## Conflicts with earlier decisions (for the author)

* Items 1, 2, 3 and 5 reverse edits made in Stage 7: the Risks & Mitigations section and the extra alternatives came from the critic review, and `similar_contexts`, the home-folder warning, sync-folder detection and the upgrade section were accepted fixes.
* Item 4 and finding 6 repeat the previous-version question the author settled (one previous version, recovery copy, not readable in v1).
* Finding 8 on the decision gate no longer applies as written: the author removed the markdown-folder trial and decided to build the server.
* This stage does not edit the document. Edits come from the author's choice at the next gate or from the Stage 9 resolution.

## Resolution (2026-11-20, author decisions applied to `docs/designs/contextguru.md`)

Status: resolved. Applied in one batch after all items were decided.

| # | Decision | Result |
|---|----------|--------|
| 1 | Accepted (contest on deletion) | Risks table kept as an index, "Unbounded growth" row removed, index sentence added. |
| 2 | Accepted | Alternatives merged to A to G plus a "Smaller decisions considered" list. Hosted store removed. |
| 3 | Accepted | `similar_contexts` and sync-folder detection removed. Home-folder warning kept. |
| 4 | Accepted | `contract_version` and "additive only" removed. `agent_instructions` kept, with no duplication in command files. |
| 5 | Accepted | Upgrade and rollback section removed. Facts moved to Installation and Verification Plan. |
| 6 | Option C | Previous-version table dropped. Save reports `overwritten`, `previous_updated_at`, `previous_summary` from the current row. `foreign_keys=ON` removed because no foreign key remains. |
| 7 | Accepted | Plan points to Database location and concurrency for connection settings. |
| 8 | Accepted | Plan files folded (names, database path, agent instructions) and name rule stated once in `save_context`. Plan is 28 items. |
| 9 | Accepted | Database path uses `GetFolderPath(LocalApplicationData)`. The `.git` walk stays, with its reason stated. |
| 10 | Option A | `Microsoft.Extensions.Logging` plus a standard file logging package. Custom `FileLogger.cs` removed. |
| 11 | Accepted | Dependency list replaces the process rule. |
| 12 | Accepted | Section renamed to Spike. Threshold is two of three agents. |
| 13 | Skipped by the author | Core and tool projects and `IContextStore` stay. Justification changed from a Non-Goal dashboard to separation of concerns and the dashboard as the planned next step. |
