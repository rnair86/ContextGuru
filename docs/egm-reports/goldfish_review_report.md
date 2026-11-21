# Goldfish Annotated Review Report

**Status:** resolved

**Target:** `docs/designs/contextguru.md` (lines 1-251, FeatureRange)

Source findings: `critic_report.md` (Stage 7). Finding ids follow `critic_report.md` (B1, B2, M1-M10, nits). No finding is ESCALATE. Items that still need an author decision are marked **Decision**.

## Prioritized Findings & AI Suggestions

### [Issue 1] [`egm-design`, `egm-structural`] - Severity: Blocker (B1)
*   **Affected Scope:** [Lines 47-49, 153-168, 244](docs/designs/contextguru.md#L47-L168) (between Technical Plan and Alternatives)
*   **Discrepancy Description:** No Risks & Mitigations section. Risks are scattered (secrets, WSL, synced folder, injection), and several are not covered (growth, lock timeout, mistaken remove, SDK unknowns).
*   **Rationale:** The feature-spec standard requires failure modes, security, scalability limits and mitigations to be stated.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** The critique is valid; the document has no such section.
*   ***Proposed Edit:***
    *   Location: new `## Risks & Mitigations` after the Technical Plan (before `## Alternatives Considered`)
    *   Change: Add a table (Risk, Impact, Mitigation) with rows for: prompt injection through stored bodies; secrets stored in plaintext; database on a network or synced folder; concurrent writers and lock timeout; schema version skew between agents; unbounded growth with no expiry; mistaken remove; wrong or orphaned project resolution; SDK and platform unknowns. Each row points to the section that holds the detail.

### [Issue 2] [`egm-design`, `egm-structural`, `egm-style`] - Severity: Blocker (B2)
*   **Affected Scope:** [Lines 182-250](docs/designs/contextguru.md#L182-L250) (Alternatives A-K)
*   **Discrepancy Description:** Every alternative ends "Rejected by author" with no linked decision. A, B, D, G, H, I and K restate the chosen design or a preference instead of a trade-off. B cites "the brief", which the reader does not have.
*   **Rationale:** Rejection reasons must be concrete trade-offs, and attribution needs a link.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Valid. The author decisions are real, but the document records who, not why.
*   ***Proposed Edit:***
    *   Location: each `Reason for Rejection` line
    *   Change: Drop the "Rejected by author:" prefix. A: "Moves scoping, latest lookup and listing into command prose, so each agent can behave differently, and gives no atomic overwrite or checks. Kept as a pre-build trial of the template only." B: "Needs temp-file-and-rename for atomic overwrite and extra conventions for listing and the previous version; SQLite provides these in one file." D: "Passed path alone fails when a command omits it; working folder alone is wrong when the agent starts the server elsewhere." G: "Pure overwrite loses the old version and adding history later needs a migration; full history needs pruning and more tables." H: "Same-row columns need a migration for more than one previous version; a revisions table grows without bound if pruning fails." I: "Three projects add an abstractions project with one consumer; one project forces a future dashboard to reference the tool executable." K: "Self-contained builds need a binary per platform and trigger unsigned-binary warnings; NativeAOT compatibility is unverified." Remove the "brief" and "original request" references in B and E.

### [Issue 3] [`egm-design`, `egm-structural`, `egm-style`] - Severity: Major (M1)
*   **Affected Scope:** [Lines 53-61, 161, 190](docs/designs/contextguru.md#L53-L190) (Open Questions, Alternative A)
*   **Discrepancy Description:** Open Question 57 says validation may mean the server is not needed, yet the document builds it and keeps Alternative A as both rejected and a prerequisite. No go/no-go criteria, success metric or owner.
*   **Rationale:** A reader cannot tell what result means proceed, change headings, or drop the server.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Valid. **Decision:** the pass criteria below are my proposals; you must set the real thresholds.
*   ***Proposed Edit:***
    *   Location: new `### Decision gates` under Open Questions
    *   Change: State: (1) Spike passes if at least two of the three agents can run a custom command that passes the working folder to a tool. (2) Template trial passes if, over a week, the next agent can continue from a saved handoff without the user re-explaining in most attempts; failures name which of scoping, listing or atomicity hurt. (3) On pass, build the server and freeze the tool contract after the spike; on fail, change headings or use the plain-file design. Mark the Technical Plan as provisional until both gates pass. [numbers to be set by the author]

### [Issue 4] [`egm-design`, `egm-structural`, `egm-style`] - Severity: Major (M2)
*   **Affected Scope:** [Lines 35, 134, 163, 168, 172](docs/designs/contextguru.md#L35-L172)
*   **Discrepancy Description:** The template is described three ways (six headings including summary in Goals; five headings plus a separate summary argument in the template; "undecided" in Open Questions). "Empty" next steps is undefined. The 200-character cap is hard-coded in the schema while called proposed.
*   **Rationale:** The server validates these exactly, so the document needs one definition.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Valid; also matches comprehension findings 1 and 2.
*   ***Proposed Edit:***
    *   Location: Goals line 35; Handoff template section; schema comment line 134
    *   Change: In Goals write: "a one-line summary (separate argument) and a body with the headings Goal, Done, Decisions, Open tasks and Next steps". Define: next steps are empty when the section has no text other than whitespace, "none" or "n/a" (case-insensitive). Heading match is case-insensitive and ignores surrounding whitespace. Mark caps "proposed" in the schema comment. State that a rejected save returns an error naming the missing part and the agent retries once.

### [Issue 5] [`egm-design`, `egm-style`] - Severity: Major (M3)
*   **Affected Scope:** [Lines 47, 155, 168, 244](docs/designs/contextguru.md#L47-L244)
*   **Discrepancy Description:** Prompt injection through stored bodies is mitigated only by an advisory instruction. Secrets rely on the agent obeying the template; the accepted risk has no rationale, does not state plaintext storage, and Windows file permissions are not described.
*   **Rationale:** The saved body is replayed into another agent with tool access.
*   ***AI Suggestion:*** PROPOSE FIX (the secret-scanner suggestion is CONTESTED)
*   ***Justification:*** Documenting the threat is valid. The scanner suggestion contradicts a recorded author decision (Non-Goals line 47; Alternative J, line 244: template instruction only, risk accepted), so it is not applied.
*   ***Proposed Edit:***
    *   Location: Non-Goals line 47; tool contracts line 168; database section line 155
    *   Change: State: the database is plaintext; users must not put secrets in handoffs and the server does not enforce this; the user accepts this risk for a single-user local tool. State the injection threat plainly and have `get_context` wrap the body in clear delimiters. On Windows the file inherits the user profile folder's permissions.

### [Issue 6] [`egm-design`, `egm-style`] - Severity: Major (M4)
*   **Affected Scope:** [Lines 33, 49, 117-121, 164](docs/designs/contextguru.md#L33-L164)
*   **Discrepancy Description:** Project identity limits are undocumented: separate checkouts, worktrees and symlinks get different keys; a move or rename orphans contexts; multiple MCP roots are arbitrary; a server started in the home folder scopes everything there; the not-found response exposes other projects' names.
*   **Rationale:** These are silent failure modes of the key design.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Valid. The path-as-key decision stands; the fix documents its limits.
*   ***Proposed Edit:***
    *   Location: new `### Known limits` under Project identity
    *   Change: List the limits above and state version 1 accepts orphaning on move or rename, with no rebind tool. Multiple roots: use the first and report it in `source`. If the resolved root is the user's home folder or a filesystem root, the response includes a warning. State that the other-project names in the not-found response are local, same-user data, so no confidentiality boundary is crossed. Replace the dense clause at line 164 with a list of returned fields.

### [Issue 7] [`egm-design`] - Severity: Major (M5)
*   **Affected Scope:** [Lines 151, 157, 163](docs/designs/contextguru.md#L151-L163)
*   **Discrepancy Description:** The lock-timeout failure, concurrent migration, and version skew between a newer and an older tool are not specified. No expected load. The "latest" tie-break assumes a monotonic clock.
*   **Rationale:** Agents upgrade separately and share one file.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Valid.
*   ***Proposed Edit:***
    *   Location: Database location and concurrency
    *   Change: After 5000 ms the tool returns an error "database busy, retry". An older tool opening a newer database returns an error giving both schema versions and telling the user to update the older install. Expected scale: thousands of rows, kilobytes each, a few writes per day. "Latest" assumes the system clock does not go backward; a manual clock change can pick a different latest.

### [Issue 8] [`egm-design`, `egm-style`] - Severity: Major (M6)
*   **Affected Scope:** [Lines 143-157, 166](docs/designs/contextguru.md#L143-L166)
*   **Discrepancy Description:** Nothing prevents the database path from being a network or synced folder; WAL can corrupt there, and the doctor write test would not detect it. The reviewer also says `ON DELETE CASCADE` needs `PRAGMA foreign_keys=ON`.
*   **Rationale:** Corruption and orphan rows are plausible.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** The location risk is valid. The pragma claim is unverified: `Microsoft.Data.Sqlite` may already enable foreign keys by default; the fix is to state the setting explicitly so the behavior does not depend on that.
*   ***Proposed Edit:***
    *   Location: Database location and concurrency; Doctor
    *   Change: Say WAL needs shared memory, which network and some synced folders do not support, so the database must be on a local disk, and set `CONTEXTGURU_DB` to a local path if the default is redirected. `--doctor` warns when the path is under a known sync folder or a network path. Each connection sets `foreign_keys=ON`. State that a corrupt database is recovered by deleting the file, which loses all contexts.

### [Issue 9] [`egm-design`] - Severity: Major (M7)
*   **Affected Scope:** [Lines 37-38, 81, 166](docs/designs/contextguru.md#L37-L166)
*   **Discrepancy Description:** CUJ 3 says the user confirms a remove, but the contract has no server-side confirmation. Remove also deletes the previous version, which no tool can read, so the recovery goal is not met.
*   **Rationale:** Safety and recovery depend on prose, and the previous version has no reader.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Valid. **Decision:** the least-change edit below keeps your choices (one previous version, no reader in v1). Zoom-out (drop the table), the design critic and inventive-catalyst (add a reader) disagree; say if you want one of those instead.
*   ***Proposed Edit:***
    *   Location: Goals line 36; tool contracts line 166; CUJ 3
    *   Change: Reword the goal: the previous version is a recovery copy kept in the database and not readable through any tool in version 1. State that remove confirmation is agent-side only (the command file tells the agent to ask), and that "destructive" is an MCP tool annotation some clients ignore.

### [Issue 10] [`egm-design`] - Severity: Major (M8)
*   **Affected Scope:** [Lines 39, 60, 246-250](docs/designs/contextguru.md#L39-L250)
*   **Discrepancy Description:** Platform support is stated as fact while Linux musl and arm SQLite is open. `dotnet tool install` needs the .NET SDK, but the draft says the runtime and calls the tool route simpler than a self-contained build.
*   **Rationale:** Cursor and Codex users may have no SDK; the prerequisite is missing.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Valid and a factual correction.
*   ***Proposed Edit:***
    *   Location: Goals line 39; Alternative K
    *   Change: State the prerequisite: the .NET SDK to install the tool, the runtime to run it, and the supported .NET version. Mark Linux and macOS support as pending the spike. In K, say the self-contained build removes the SDK requirement at the cost of per-platform binaries.

### [Issue 11] [`egm-design`] - Severity: Major (M9)
*   **Affected Scope:** [Lines 97, 103, 176](docs/designs/contextguru.md#L97-L176)
*   **Discrepancy Description:** No log rotation size or retention; tests omit concurrency, migration races, Windows path normalization and per-agent end-to-end; no upgrade, rollback or uninstall plan.
*   **Rationale:** These are the riskiest areas.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Valid. Some test items belong in the Implementation range (lines 252+); this edit covers the feature-level statement only.
*   ***Proposed Edit:***
    *   Location: Observability; new `### Upgrade and rollback`
    *   Change: Log file rotates at a stated size (for example 1 MB, keep 3 files). Add: upgrading the tool runs `install` again to rewrite command files; uninstall removes only marked files and leaves the database; downgrade is blocked by the schema version check. Name the risky test areas (concurrent writers, migration race, Windows path normalization).

### [Issue 12] [`egm-style`] - Severity: Major (M10)
*   **Affected Scope:** [Lines 38, 161, 168](docs/designs/contextguru.md#L38-L168)
*   **Discrepancy Description:** "Brief summary" is unmeasurable; `contract_version` and "additive only" have no defined mechanism.
*   **Rationale:** Undefined terms in a public contract.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Valid. **Decision:** zoom-out (Stage 5) recommended dropping `contract_version`; this edit defines it instead. Say which you prefer.
*   ***Proposed Edit:***
    *   Location: Goals line 38; tool contracts line 161
    *   Change: "Brief summary" means the stored one-line `summary`. `contract_version` is an integer, 1 in v1, raised on any added field; clients ignore fields they do not know.

### [Issue 13] [`egm-design`] - Severity: Nit (alternatives)
*   **Affected Scope:** [Lines 182-250](docs/designs/contextguru.md#L182-L250)
*   **Discrepancy Description:** Alternatives D, F, G, H, I and K bundle several options; missing alternatives: a hosted store, each agent's native memory or `AGENTS.md`, and committing the handoff to the repo or a git note.
*   **Rationale:** The omitted ones are the nearest real competitors.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** The missing alternatives are valid and cheap to add; splitting bundled ones is DECLINED as low value.
*   ***Proposed Edit:***
    *   Location: after Alternative K
    *   Change: Add: native agent memory or `AGENTS.md` (per-agent and not shared across agents, so it does not meet the goal); a handoff file committed to the repo (puts handoff text into git history and diffs); a hosted store (breaks the local-only requirement and sends handoffs to a third party).

### [Issue 14] [`egm-structural`, `egm-style`, `egm-design`] - Severity: Nit (small edits)
*   **Affected Scope:** Lines 11-13, 24, 28, 44, 55, 93, 121, 126-149, 163-165, 196
*   **Discrepancy Description:** `saved_at` versus `updated_at`; `[name]` versus `{name}` unexplained; "later improvement" wording; `text` fence on SQL; "projects" ambiguity; "historically" for the Codex prefix; source term varies (`cwd`, "working folder").
*   **Rationale:** Minor inconsistencies in a public contract.
*   ***AI Suggestion:*** PROPOSE FIX
*   ***Justification:*** Cheap and valid.
*   ***Proposed Edit:***
    *   Location: as listed
    *   Change: Use `updated_at` in `get_context`. Add one line: `[name]` is optional, `{name}` is required. Replace "later improvement" with "out of scope for version 1". Label the data model fence `sql`. Say "C# projects". Replace "historically" with "in earlier Codex versions" and mark it unverified. Use "working folder" in prose and `cwd` only as the source value.

### [Issue 15] [`egm-structural`, `egm-style`, `egm-design`] - Severity: Nit (declined)
*   **Affected Scope:** Lines 7, 11, 17, 24, 28, 63-87, 89, 99-115, 166, 237, 249
*   **Discrepancy Description:** Section names versus the standard blueprint; separator line use; no diagram caption; restated sentences; long line 166; unsourced size figures; missing CUJs for error paths; subjective wording.
*   **Rationale:** Style and polish.
*   ***AI Suggestion:*** DECLINE
*   ***Justification:*** "The Problem" and "Technical Plan (Architecture)" follow the EGM design-doc template headings (`references/design_doc.md`). Remaining items are low value; the error-path CUJs can be added if the Risks section is accepted.
*   ***Proposed Edit:***
    *   Location: n/a
    *   Change: None.
