# Stage 9: Implementation Readiness Report

* **Target:** `docs/designs/contextguru.md`, DetailedRange lines 250-411 (28 plan items and the Verification Plan), with the places in lines 1-249 that the plan depends on
* **Reviewers:** `egm-implementation-readiness` (implementation-readiness standard), `egm-codebase-details`, `egm-style` (no standard). All three completed; none "not run".
* **Date:** 2026-11-20
* **Source tags:** R = readiness, C = codebase details, S = style. Findings are deduplicated across reviewers.

No source tree exists, so nothing was checked against code. The codebase reviewer worked from its own knowledge of the .NET packages. Claims it could not verify are listed in the last section.

**Verdict:** FAIL against the Zero-Decision Rule. The file list is exact and the build order is sensible. The failures are: no phases with checkpoints, several interfaces and wire formats left to the implementer, rules without a precise definition, and tests listed as topics without assertions.

## Blockers

| # | Finding | Lines | Source |
|---|---------|-------|--------|
| B1 | **No phases or checkpoints.** The plan is one flat list of 28 items and a one-sentence build order. Nothing says each phase leaves the repo buildable and testable. Tests are listed last although each should be built with the code it covers. The order sentence also conflicts with the plan, because the installer is inside the tool project. Fix: add phases (spike and go/no-go, empty projects with a green build, Core with its tests, tool project, installer, CI and README), each with its files and exit command. State what happens to the installer items if the spike drops an agent. | 252, 256-396 | R-B1, S-F15 |
| B2 | **Core interface and types are undefined.** `IContextStore` lists method names only: no sync or async, no parameter types, no return types, no `Save` result shape, no way to report `removed: false`, `database busy` or `schema too new` (exception or result). Records have prose field lists with no types (`DateTime` or `DateTimeOffset`, an enum for `source`). `HandoffTemplate` returns "errors" with no error type and does not say whether it returns all errors or the first. Ownership of error text is split between items 8 and 14. Fix: give the C# signatures and record definitions, one validation error type, the exception types, and say which item owns the error wording. | 283-311, 326 | R-B2, S-F8 |
| B3 | **Body parsing rules are undefined.** Not stated: whether `##` inside a fenced code block counts, whether extra or duplicate headings are allowed, the heading line rule (`##Goal`, trailing `#`), where a section ends, and the exact "empty next steps" test (`None.`, `- none`). A naive check passes a body with the headings inside a code fence. Fix: write the parser rule and one test case per rule. | 173, 293-296 | R-B3, C-N4 |
| B4 | **Caps and units.** Values are proposals (200 and 20,000), but the plan does not mark them provisional or say where they live. "Characters" is undefined (UTF-16 units, scalars or bytes). "One line" is undefined. Empty summary or body is not addressed. Fix: define the unit (for example `string.Length` after trimming), define one line as no `\r` or `\n`, require non-empty values, put the caps in one constants class and state the numbers in item 8 and its tests. | 143, 173, 296 | R-B4, S-F5 |
| B5 | **"Latest" and overwrite semantics.** `id` tie-break has no direction. It does not say whether an overwrite keeps `id` and `created_at` (an `UPDATE`), so the `id` tie-break does not favor the newer save. No timestamp format, so ISO strings could sort wrongly. The index in item 11 is on `(project_key, updated_at)` but the Data model block shows no index. Fix: state `ORDER BY updated_at DESC, id DESC`, a fixed-width UTC format, overwrite as `UPDATE`, add the index to the schema block and name the queries it serves, and give the `Save` SQL. | 152, 306, 311 | R-B5, C-N3, S-F6, S-F7 |
| B6 | **Resolver contract has open decisions.** MCP roots arrive as `file://` URIs, but item 7 gives no types and no conversion rule (percent-decoding, Windows `file:///C:/...`, non-`file` schemes). Not stated: handling of a relative, missing or file `projectPath`, an empty `projectPath`, the exact Windows key rule ("single separator form", trailing separator, UNC, drive case), how the home-folder warning is computed and where it appears (the `project` object has no warning field but the Known limits say the response includes one), and what "nothing found" means in CUJ 4. `ResolveLinkTarget` resolves only the last path segment, so symlinks in parent folders give different keys. Fix: define `Resolve` with exact types (URI conversion in the tool layer), write the normalization algorithm, add a `warnings` field, and add a parent-symlink test. | 121-130, 288-291 | R-B6, C-F7, C-F8, S-F23 |
| B7 | **Tool wire format and error mechanism.** Item 15 says only "result records". The contract uses snake_case result fields, and the MCP C# SDK serializes camelCase by default, so positional records would emit different names. No `list_contexts` envelope, no nullability, no rule for how validation, busy and schema errors reach the client (`isError: true`, a JSON-RPC error, or an `error` field). Unclear whether `searched_path` is returned for a named miss. Fix: a table of exact JSON per tool for success and each failure, `[JsonPropertyName]` on every property, explicit tool names, and a wire-name test. | 164-167, 326-331 | R-B7, C-F1, S-F9 |
| B8 | **`agent_instructions` text and delimiters are not in the plan.** Item 14 says it holds them, but gives no literal text and no delimiter scheme, and a body that contains the delimiter itself is not handled. Error wording is also open although the stdio tests assert on it. Fix: put the literal text, the delimiter rule and the error templates in the plan. | 162, 169, 326 | R-B8, S-F8 |

## Majors

| # | Finding | Lines | Source |
|---|---------|-------|--------|
| M1 | **Connection and transaction setup.** The plan says "settings in Database location and concurrency" without listing them. Reviewers differ on the driver: R says `BeginTransaction()` may not issue `BEGIN IMMEDIATE` and wants raw SQL; C says it does by default and raw SQL fights the driver. C also says `Default Timeout` (30 s) can override a `busy_timeout` pragma, so the real wait could be 30 s, and `journal_mode=WAL` persists in the file and can fail on a concurrent first open. The migration must re-read `user_version` inside the write lock. Connection pooling keeps handles open and breaks temp-file tests on Windows. This disagreement cannot be settled without the package; add it to the spike. Fix: write the open sequence and the migration steps, set `Pooling=False` (or clear pools in teardown), map SQLITE_BUSY to "database busy, retry". | 154-158, 306, 311 | R-M2, C-F3, C-F4 |
| M2 | **Auto-name clash has no owner and is not atomic.** Item 8 generates the name and the suffix, but a clash check needs the store. Two processes saving in the same second can race. UTC or local is not stated. Fix: `HandoffTemplate` formats the base name only, `Save` picks the suffix inside its write transaction, and the clash test moves to the store tests. | 164, 296, 366 | R-M4, C-F2 |
| M3 | **Name validation across tools.** The rule is given for save only. Not stated for `get` and `remove`: trim, case (`ToLowerInvariant`), invalid versus unknown name. Fix: one rule for all three tools. | 160-167, 296 | R-M5 |
| M4 | **File mode 0600 has a creation race.** Mode is set after SQLite creates the file, and `-wal` and `-shm` are not covered. Directory mode is unstated. Fix: pre-create the file with a Unix create mode and the folder with 0700 before opening, and add a Linux-only test. | 156, 306 | R-M3, C-N2 |
| M5 | **Concurrency and migration tests are not testable as written.** "Two servers migrating together" has no test in item 24. No way is given to run two real processes in xUnit, how many writes, or the pass criterion. Windows-only tests (case folding) and symlink tests (need privilege on Windows) have no skip rule, so "three operating systems" is ambiguous. Fix: specify the process count, writes and assertions, add a concurrent-migration case, inject an OS flag or path normalizer into the resolver, and name which tests run where. | 361, 371, 376, 404 | R-M6, C-F6, C-F9, S-F11 |
| M6 | **Test lists lack assertions.** Items 21-25 list topics. Missing cases include: `Get` found and not found, unknown remove, tie-break, same name in two projects, multiple roots, home-folder warning, CRLF bodies, `isError` shape, destructive annotation, no handoff content in logs. "Golden check of the schema" is undefined. Installer, Doctor and agent targets have no tests, although the plan says every rule has a test. The SDK ships a client that could replace most raw JSON-RPC. Fix: add case tables per test file, an `InstallerTests` and `DoctorTests` (inject the base folder), define the golden as normalized `sqlite_master` text, and keep one raw-process test for "stdout is protocol only". | 358-381, 403 | R-M7, S-F24, S-F25, C-N5 |
| M7 | **Installer cannot be built from the plan.** Not defined: marker header format (and whether a comment is valid in each command file format), how MCP registration edits the agent config (merge, preserve other keys, invalid file, atomic write), the command and path to register, backup name and location, `uninstall` syntax, placeholder names, the template texts, how `agent` is passed, and CLI parsing. JSON and TOML handling are unnamed. Fix: define each, name a TOML option, and keep the template texts in the plan or an appendix. | 181, 338-351, 316-318 | R-M8, C-F11, S-F10, S-F21 |
| M8 | **`--doctor` is ambiguous.** "Resolved project" has no MCP client in a terminal, so the source is `cwd` only. "Whether git was found" conflicts with the resolver never calling `git`. "Test write" has no table or cleanup. No output format or exit code. Behavior with a newer schema is not stated. Fix: define each line, make the write a rolled-back transaction, and give the exit codes. | 177, 333-336 | R-M9, C-N7 |
| M9 | **Server mode, stdout and startup order.** The default console logger can write to stdout and the plan names no mechanism. Where the store is registered and when migrations run is not stated; if they run at host start, a too-new schema kills the session instead of returning a tool error. Argument routing and exit codes are not defined. Fix: clear providers and log everything to stderr, run migrations lazily or catch and return a tool error, and define routing. | 105, 152, 321 | R-M10, C-N6 |
| M10 | **CI and packaging.** Item 26 builds on three operating systems while Linux and macOS support depends on the spike. No steps, runner labels, setup version or rule for non-Windows failures. Pack metadata (`PackageId`, `Version`, `Description`, readme, license) is missing, CI never packs or installs the tool, and the official MCP package type is not considered. Fix: specify the matrix and steps, whether non-Windows jobs are required before the spike confirms support, and the pack properties. | 313-316, 383-386, 405 | R-M11, C-F12 |
| M11 | **Dependencies are unpinned.** "Latest stable" is not measurable, and the `ModelContextProtocol` package may be pre-release. The file logging package is unnamed (the usual choice is Serilog with the file sink). `xunit` could mean v2 or v3, which differ in runner setup. "Analyzers on" has no level, and the format check needs an `.editorconfig`. Fix: pin versions after the spike (and say what to do if only a pre-release exists), name the logging package, name the xUnit flavor, set the analyzer level. | 254, 271, 316, 354-356, 402 | R-M1, C-F10, N1, S-F2, S-F13 |
| M12 | **Shared log file across processes.** Several server processes write the same log file next to the database, and size-based rotation is unreliable when shared. Fix: per-process file names, or accept stderr and `--doctor` only. This reopens your Stage 8 decision to keep a log file, so it needs your call. | 177 | C-F5 |
| M13 | **Verification Plan wording.** `-warnaserror` duplicates `Directory.Build.props`. Commands lack the solution path. "Every rule has a test" cannot be checked without a rule-to-test list. The manual acceptance step says three agents but CUJ 1 uses two, and has no pass criteria or steps to produce the wrong-folder case. "Risk areas" is a requirement, not a check. Fix: use one build setting, full commands, a rule-to-test table or drop the claim, explicit agent pairs and pass conditions. | 398-405 | R-N1, S-F11-F13, S-F26, S-F12 |
| M14 | **Spike item mismatch.** Item 1 does not record pass or fail per agent, the two-of-three threshold, or the drop-or-fallback decision, and "bare `/cg-<name>`" is undefined. "Throwaway console server kept outside the source tree" has no location or deletion rule. Fix: align item 1 with the Spike section and state where the throwaway lives. | 63, 258-261 | S-F3, S-F4 |
| M15 | **Terms are inconsistent.** "Working folder", "cwd", "server working folder" and "project root", "git root", "root folder" mix meanings, and item 19 uses "working folder" for the agent's folder. Fix: define four terms once (server working folder, agent folder, project root, MCP roots) and use them everywhere. | 56, 121-123, 261, 291, 312, 351 | S-F1 |

## Nits

* **Item 28 is a self-reference** to the design under review. Remove it or change it to "set Status and update the Open Questions after the spike" (R-N2, S-F14; lines 393-396).
* **Files missing from the list:** `global.json`, `Directory.Packages.props`, `.editorconfig`, a logging provider file, `LICENSE`. The template folder is one entry, not four paths. The `.slnx` file can be generated by `dotnet new sln` (R-N3, C-N8, S-F16; lines 256-396).
* **Small SQL and wording points:** `Remove` has no filter stated and could use `DELETE ... RETURNING`; the `project` object should omit `key`; `project_name` for a filesystem root is empty (R-N4, C-N8).
* **Stdio test mechanics:** how the test finds the built executable is not stated (R-N5, C-N5).
* **Core logging:** Core references only SQLite, so say whether it logs (S-F20, C-N8).
* **macOS case-insensitive disks:** the key is the real path unchanged, so different casing gives different keys. State it in Known limits (C-N9).
* **Style:** "Also" chains in items 8 and 10, "Type / Scope" mixes meanings, "Pure logic with file system calls", "golden check", "Short:" lead-in and `<name>` clashing with the `[name]` and `{name}` notation, heading "Target Files to Create/Modify", ampersands in labels (S-F16-F18, F22, F24, F26).

## Tensions with earlier decisions (for the author)

* **Stage 8 versus Stage 9.** Stage 8 asked to condense the plan (33 to 28 items). Stage 9 asks for far more detail, including signatures, tables of exact JSON, case tables, and more files (`InstallerTests`, `DoctorTests`, `global.json`, `Directory.Packages.props`, `.editorconfig`). The plan will grow, and the zero-decision standard governs this range.
* **Logging (M11, M12).** Naming the Serilog file sink follows your Stage 8 answer. Dropping the file sink would reverse it. Adding any package needs your confirmation.
* **New dependencies suggested:** `System.CommandLine` and a TOML library for the installer (M7). Neither is in your dependency list.
* **Findings that depend on the spike** (command paths, argument syntax, config formats, platform support) are not repeated unless the plan makes an unmarked assumption.

## Claims the codebase reviewer could not verify

* Current `ModelContextProtocol` package version and API (`McpServer` versus `IMcpServer`, roots request, destructive annotation, structured-content defaults).
* Exact `Default Timeout` and busy-timeout interaction, and whether `BeginTransaction()` issues `BEGIN IMMEDIATE`, in the current `Microsoft.Data.Sqlite`.
* xUnit v3 and `dotnet test` opt-in details on .NET 10.
* What `Environment.GetFolderPath(LocalApplicationData)` returns on Linux and macOS (already deferred to the spike).
* Per-agent command locations, argument syntax and config formats (spike).
* Whether the SQLite native bundle covers Linux musl and arm (the reviewer recalls it does).

## Checked and accurate

`net10.0` and `.slnx` as the default solution format, `PackAsTool` with `ToolCommandName`, `PRAGMA user_version` for the schema version, WAL needing a local disk, a tool needing the SDK to install and the runtime to run, and the Hosting plus `AddMcpServer().WithStdioServerTransport()` pattern. The dependency names in the plan match the Components section, and the remove template matches the agent-side confirmation rule.

## Resolution (2026-11-20, author decisions applied to `docs/designs/contextguru.md`)

Status: resolved. All 24 breakdown items were decided and applied in one batch. The plan is now 34 items in six phases.

| Item | Finding | Decision |
|------|---------|----------|
| 1 | B1 phases | Accepted. Phase table with exit commands, tests in the same phase. |
| 2 | B2 Core types | Accepted. Synchronous store, exceptions for busy and newer schema, `Validate` returns every problem. |
| 3 | B3 body parsing | Accepted. Fence-aware, extra headings allowed, duplicate and out-of-order are errors. |
| 4 | B4 caps and units | Accepted. `string.Length` of trimmed text, caps are constants, values provisional. |
| 5 | B5 latest and overwrite | Accepted. Fixed-width UTC text, `UPDATE` keeps `id` and `created_at`, `contexts_latest` index. |
| 6 | B6 resolver | Accepted. Typed signature, `MakeKey(realPath, isWindows)`, `project.warning`. |
| 7 | B7 wire format | Accepted. snake_case table, `isError` for errors. |
| 8 | B8 texts | Accepted. Per-response random delimiter id, literal `agent_instructions`, error message table. |
| 9 | M1 connection setup | Accepted. `Pooling=False`, both timeouts 5 s. Migration runs as a background task at server start (author preference), with the error kept and returned if it fails. |
| 10 | M2 auto-name | Accepted. The store picks the suffix inside the save transaction. |
| 11 | M3 name rule | Accepted. One rule in all tools. |
| 12 | M4 file mode | Accepted. Pre-create folder 0700 and file 0600. |
| 13 | M5 concurrency tests | Accepted. Two real server processes in `StdioToolTests`. |
| 14 | M6 test cases | Accepted. Case lists, `InstallerTests`, `DoctorTests`, `ServerProcess`. |
| 15 | M7 installer | Accepted. Marker line, config merge, backups, placeholders, template texts, hand-written parser. |
| 16 | M8 doctor | Accepted. Defined lines, rolled-back write test, exit codes. |
| 17 | M9 server mode | Accepted with changes. Logging at Information with a file logger (author), migration at server start (author). |
| 18 | M10 CI and packaging | Changed by the author. No GitHub Actions or publishing in version 1. Local scripts `local/build.ps1` and `local/install.ps1`. CI moved to version 2. |
| 19 | M11 dependencies | Accepted. `Directory.Packages.props`, Serilog file sink named, analyzer level, `.editorconfig`. |
| 20 | M12 shared log file | Accepted. Daily rotation, 7 files, shared writing, process id on each line. |
| 21 | M13 Verification Plan | Accepted. Local commands, manual acceptance steps. |
| 22 | M14 spike item | Accepted. Pass or fail per agent, one question list, `spike/` throwaway folder. |
| 23 | M15 terms | Accepted. Agent folder, server working folder, MCP roots, project root. |
| 24 | Nits | Accepted. Item 28 removed, files added, wording fixed. |

Still open after this stage: the file logging package versions, the license (not needed while unpublished), the size caps (provisional), and everything the spike answers.
