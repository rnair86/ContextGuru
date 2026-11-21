# Stage 6: Comprehension Test Report

* **Target:** `docs/designs/contextguru.md`
* **FeatureRange:** lines 1..251. **DetailedRange:** lines 252..437.
* **Reviewer:** `egm-comprehension` (fresh context, no standard, no extra context files)
* **Date:** 2026-11-20

## What this proposes

ContextGuru is a local MCP server (C# / .NET, packaged as a `dotnet tool`) that lets a developer save a short markdown "handoff" from one coding agent (Claude Code, Codex, Cursor) and read it back in the same or another agent. Each agent launches its own server process over stdio, and all processes share one SQLite file. Four tools (save, get, list, remove) are exposed, and per-agent command files (`/cg-save-context` etc.) call them. The agent writes the handoff in a fixed template, because the server cannot see the conversation. The server validates headings and size, stores it per project (git root or cwd), keeps one previous version on overwrite, and reports which project path it used and where that came from. The detailed plan lists build order (spike first, then Core, tools, installer), 33 files, tests and CI.

## How the existing system works, per this document

The document describes no existing system beyond a manual workaround. Developers write or ask an agent to write an ad hoc markdown file and paste or point to it in the next session. The file is not tied to a project, its format varies by agent, and nothing lists or removes old handoffs. It is a greenfield build (the repo contains only the design and a spike is still to be run).

## Findings

No Blocker. The paraphrase of each section succeeded. The Majors are places where parts do not connect.

| # | Severity | Scope (lines) | Finding |
|---|----------|---------------|---------|
| 1 | Major | Goals vs template (35, 172, 298) | Goals list headings as "one-line summary, goal, what was done, decisions, open tasks, next steps". The template requires `Goal`, `Done`, `Decisions`, `Open tasks`, `Next steps`, with the summary passed separately. State the mapping at the Goals line. |
| 2 | Major | Next-steps gating (38, 168, 298) | "If the handoff has next steps" and "if the next-steps section is not empty" are never defined. What counts as empty (blank, whitespace, a placeholder like "None")? The get flow depends on it. |
| 3 | Major | Overwrite and `source_agent` (36, 136, 163) | It is not stated how a command knows its agent name or whether values are a fixed set. On overwrite, handling of `source_agent`, `updated_at` and whether `created_at` is preserved is not stated. `get` returns `saved_at` but the column is `updated_at`; CUJ 3 says "saved time". Name one term. |
| 4 | Major | Project identity (121, 132, 164, 308, 398) | The not-found lookup of "contexts in other projects that share the same folder name" is not explained, and it is not in the store interface or the tests. |
| 5 | Major | Project path from the command (56, 173, 373) | It is unexplained how a markdown command file knows the working folder, and the agent must be trusted to supply it. The failure mode when an agent passes a wrong or non-absolute path is undefined. Partly an Open Question. |
| 6 | Major | Remove confirmation (81, 166, 373) | CUJ 3 and the remove template have the agent confirm, but the remove tool has no confirmation parameter, so the safety lives only in command prose. "Marked destructive" is not explained (an MCP tool annotation?). State that confirmation is agent-side only. |
| 7 | Major | Command surface (13, 180) | Goals do not mention `install`, `uninstall` or `--doctor` as part of the product surface. "Marker header" and "backs up any file it did not create" (location, naming) are not defined. |
| 8 | Major | Open Questions vs build (55-61, 172, 254) | Size caps and headings are an Open Question, but the plan and tests use specific values (200 and 20,000) and exact headings. The Open Question on whether the server is needed conflicts with a plan that builds everything. The template validation week is not placed in the build order at line 254. The document does not say what gates the build. |
| 9 | Major | Resolver details (118-119, 293) | The roots step "uses the first root" with no handling of `file://` URIs or multiple roots. A missing roots capability is not distinguished from an empty list. Behavior is unstated when `projectPath` is relative or does not exist. |
| 10 | Nit | Undefined terms (55, 196, 212, 44, 96, 263) | "The spike", "the brief", "the original request" and "the dashboard" refer to things the reader may not have. "Rejected by author" repeats with no date or forum. |
| 11 | Nit | Acronyms and tool names | MCP, "roots list", `PRAGMA user_version`, `BEGIN IMMEDIATE`, WAL and `busy_timeout` are used without definition. "MCP roots" is protocol-specific; add a one-line definition and a spec link. |
| 12 | Nit | `contract_version` (161, 343, 168, 288, 121) | Its value, format and change trigger are not stated. `agent_instructions` wording is only summarized. `ProjectInfo` has `key` but the tool result `project` omits it; say so deliberately. |
| 13 | Nit | Inconsistencies (13, 431) | (a) Goals say the tool runs on Windows, Linux and macOS, while Non-Goals assume native Windows agents and manual acceptance is Windows only. (b) "Two projects plus a test project" versus Alternative I's "one project, or three projects" (do tests count?). (c) `.slnx` is not explained. (d) `Microsoft.Extensions.Hosting` appears in no earlier section. (e) The output format of `docs/spike.md` is unspecified, though item 22 reads it. |
| 14 | Nit | Verification Plan (427-431) | "Every rule stated in the Technical Plan has a test" is not met. There are no tests for `DatabasePath`, `Installer` marker and backup, `Doctor`, the logger never logging handoff content, or the 0600 mode. List them or narrow the claim. |
