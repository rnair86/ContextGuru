# Stage 7: Critic Review Report

* **Target:** `docs/designs/contextguru.md`, FeatureRange lines 1..251
* **Reviewers:** `egm-design` (feature-spec standard), `egm-structural` (feature-spec standard), `egm-style` (no standard)
* **Status:** all three completed; none "not run"
* **Date:** 2026-11-20

Findings are deduplicated across reviewers. Source tags: D = design, S = structural, T = style.

## Blockers

### B1. No Risks & Mitigations section (D1, S2)

The feature-spec standard requires failure modes, security implications, scalability limits and mitigations. The document has none in one place. Risks are scattered: secrets (47, 244), WSL (49), synced folder (155), prompt injection (168). Uncovered or thin:
* Prompt injection through stored handoffs replayed into another agent.
* WAL on synced or network folders.
* Unbounded database growth, given no expiry (46) and no per-project count cap.
* Data loss from a mistaken remove.
* Concurrent writers and the lock-timeout error.
* SDK and platform unknowns (60).

Fix: add a Risks & Mitigations section (risk, impact, mitigation) and cross-reference the scattered text.

### B2. Alternatives are rejected by attribution, not trade-off (D2, S1, T1)

All eleven alternatives end "Rejected by author" (190-250) and no link is given. Several restate the choice instead of the reason:
* A (190): says the goal is a shared store, yet the same text keeps A as the pre-build validation.
* B (196): "the author chose SQLite" is a decision, not a reason. It also cites "the brief", which the reader does not have (T18).
* D (208), G (226), H (232), I (238): restate the chosen design or say "simple" or "enough".
* K (250): "the author already works in .NET" is a preference.

Fix: drop the "Rejected by author" prefix and state the deciding trade-off, with numbers where possible.

## Majors

### M1. The design commits to a build whose premise is a pending test (D7, S3, T2)
Open Question 57 says the validation week may show the server is not needed, Alternative A is both rejected and kept as a prerequisite, and no gate says what result means proceed, change headings, or drop the server. No success metric, owner or timeline exists. The `contract_version` and "additive only" promise (161) is made before the command syntax is known. Fix: state go/no-go criteria for the spike and the one-week test in one place, mark the Technical Plan provisional, and freeze the contract after the spike.

### M2. Handoff template defined three ways (D8, S4, T3)
Goals (35) list "one-line summary, goal, what was done, decisions, open tasks, next steps". The template (172) requires `Goal`, `Done`, `Decisions`, `Open tasks`, `Next steps` with the summary as a separate argument. Open Question 58 says headings are undecided, yet the schema (134) hard-codes 200 characters. "Empty" next-steps is undefined (168). Strict heading order may reject handoffs from weaker models, and the retry cost is unquantified. Fix: one authoritative definition referenced from Goals, define "empty", case and whitespace tolerance, and say what a rejection costs.

### M3. Prompt injection and secrets are only advisory (D3)
A stored body is replayed into another agent that has tool access. The only mitigation is server-written text telling the agent to treat it as data, and that text travels in the same channel as the attack. Secret handling relies on the agent obeying the template. The accepted-risk statement (47) gives no rationale and does not say the database is plaintext. Windows file permissions are not described (only mode 0600 on Linux and macOS). Style (T9) also asks who accepts the risk and what the user must do. Fix: document the injection threat, delimit the body in the response, state the plaintext and permission situation, and consider a non-blocking warning for obvious secret patterns.

### M4. Project identity has undocumented limits (D4)
Different checkouts, worktrees and symlinks of one repo get different keys. A worktree resolves to its own folder. Moving or renaming a repo orphans its contexts with no re-key path. The first MCP root is used, which is arbitrary for multi-root workspaces. A server started in the home folder silently scopes everything to the home folder. The not-found response (164) exposes metadata from other projects and no goal or security statement covers it (T15 also asks for a field list instead of the dense clause). Fix: list these as known limits, define the multi-root rule, warn or refuse when the resolved root is a home or filesystem root, and either add a rebind path or accept orphaning explicitly.

### M5. Concurrency and migration behavior under-specified (D5)
After the 5000 ms timeout the tool's return is not stated. Two servers starting against an older schema both migrate, and a newer tool sharing one file with an older tool is likely because agents upgrade separately; refusing a newer version (151) breaks the older agent with no documented remedy. No expected load is given. The "latest" tie-break assumes a monotonic UTC clock. Fix: state the lock-timeout error contract, version-skew behavior, expected scale (for example thousands of rows, kilobytes each) and the clock assumption.

### M6. Database location and per-connection settings (D6, T5)
Nothing stops `CONTEXTGURU_DB` or a redirected `%LOCALAPPDATA%` from pointing at a network or synced folder, where WAL can corrupt data; the doctor test write will not detect this. Style asks for the failure mode and what to do. The reviewer also says `ON DELETE CASCADE` needs `PRAGMA foreign_keys=ON` per connection and the design does not mention it. This pragma claim is unverified: `Microsoft.Data.Sqlite` may already enable foreign keys by default, so check before adding it. Fix: detect unsafe locations in doctor, state the failure mode, state corruption and recovery behavior, and confirm the foreign key setting.

### M7. Remove confirmation and the unreadable previous version (D9)
CUJ 3 says the user confirms, but the contract only marks the tool destructive and has no server-side confirmation. Not every client honors the hint. Remove also deletes the previous version, and no tool can read it, so the recovery goal is unmet in practice. Fix: expose previous-version retrieval in v1, or reword the goal. Document confirmation as agent-side only, per agent, or add a two-step confirm.

### M8. Cross-platform and installation claims (D10)
Goals state Windows, Linux and macOS support as fact while Linux musl and arm SQLite support is open (60). `dotnet tool install` needs the .NET SDK, not only the runtime, and the draft says "needs the .NET runtime on each machine" and calls the tool "simpler" than a self-contained build (Alternative K). Many Cursor and Codex users may have no SDK. Fix: state the SDK prerequisite and supported .NET version, reconcile Alternative K, and mark platform support as conditional on the spike. This is a factual correction to the draft.

### M9. Observability, testing and rollout are thin (D11)
No log rotation size or retention. Tests (97) omit multi-process concurrency, migration races, Windows path normalization and per-agent end-to-end. No upgrade, rollback or uninstall plan covering the database. Fix: add those test cases and an upgrade and rollback section.

### M10. Smaller consistency points raised as Major by style (T4, T6)
"Brief summary" is unmeasurable and line 168 says "short summary" for the same behavior; say whether it is the stored one-line `summary`. `contract_version` is never defined (161), so "additive only" has no mechanism.

## Nits

* Naming and order against the standard blueprint ("The Problem" and "Technical Plan (Architecture)" versus "Problem Statement" and "Proposed Architecture"); Open Questions sits before the proposal (S5).
* Inconsistent separator line use (S6). Diagram has no caption (S7). The data model fence is labeled `text`, not `sql` (S8).
* Missing CUJs for template rejection, lock timeout, a too-new database, and the not-found cross-project names; CUJ 1 depends on unverified Codex and Cursor syntax (S9, D12).
* `updated_at` (list) versus `saved_at` (get) naming in a public contract (D12, also comprehension finding 3).
* `[name]` versus `{name}` convention is unexplained (D12, T7). "Small" command file (T7). Restated sentences at lines 11, 13, 24, 28 (T8, T11).
* "(later improvement)" at line 44 should read "out of scope for version 1" (T10, D12).
* Line 166 mixes unrelated facts in long sentences (T12). Source term inconsistent: `cwd`, "working folder", "own folder" (T13). "Clear error" for a newer database should state its content (T14).
* "30-70 MB" and "security warnings for unsigned binaries" are unsourced (T16). "Indirection that nothing uses" is subjective (T17). "Two projects plus a test project" is ambiguous; say "C# projects" (T19). "Historically" for the Codex prefix is uncalibrated (T20).
* Bundled alternatives (D, F, G, H, I, K) make pros and cons hard to attribute. Missing alternatives: a hosted store, each agent's native memory or `AGENTS.md`, and committing the handoff to the repo or a git note (D13).

## Kept as calibrated (no change)

Open questions are concrete and each names a decision (55-60). "NativeAOT compatibility ... is unverified" is stated plainly. Non-goals and limits at lines 47, 49 and 61 are plain.

## Overlap with earlier stage reports

* B1, M1 and M7 overlap with Stage 5 zoom-out findings 1 and 2 and the inventive-catalyst findings on secrets and the previous version.
* M2 repeats Stage 6 comprehension findings 1 and 2.
* The previous-version contradiction (drop it per zoom-out, expose it per design critic and inventive-catalyst) now has three reviewers on record and needs one author decision.
