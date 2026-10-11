# AGENTS.md — Kinovea cross-platform migration

## Mission
Incrementally migrate the upstream Kinovea codebase from legacy .NET Framework/Windows-specific technology toward modern .NET running natively on Linux and Windows, preserving observable video-analysis behavior and upstream traceability. This is a port, not a greenfield rewrite. Prioritize reusable analysis domain logic for possible AEX/EEFE integration, but do not invent behavioral requirements.

## Current validated baseline (2026-10-11)
- Developer environment: Gentoo Linux, .NET SDK 10.0.111.
- Original solution: `Kinovea.VS2019.sln`; projects include legacy .NET Framework 4.8 and Visual Studio C++ infrastructure.
- The Linux case-sensitive path mismatch `Kinovea.Video.Gif` vs `Kinovea.Video.GIF` was locally corrected; check current branch before acting.
- After that correction `dotnet build Kinovea.VS2019.sln -v:minimal` reaches 17 errors and 2 warnings: primarily MSB3644 (net48 reference assemblies absent) and MSB4278 (`Microsoft.Cpp.Default.props` missing for PlayerServer.vcxproj); NU1503 for some restore targets.
- This baseline is a *build failure*, not proof that any Kinovea component runs on Linux or .NET 10.
- Revalidate all statements against the checkout before relying on them. Logs and upstream branches may differ.

## Mandatory workflow — every change
1. **Inspect first.** Read relevant `docs/` pages and code, trace project references, platform APIs, existing tests, runtime behavior, resource files and licenses. Record the baseline state and its reproducible commands.
2. **Scope narrowly.** One logical migration slice per task. Identify inputs, outputs, dependencies, expected equivalence, intentional differences, and explicitly excluded work.
3. **Write/extend tests before migrating when feasible.** Characterize legacy behavior from source or executable; for any unavailable baseline, mark it `UNVERIFIED`, never call it equivalent.
4. **Implement the smallest reversible change.** Preserve original source semantics and stable public contracts where possible. Isolate Windows dependencies behind interfaces; do not replace complex components with silent stubs.
5. **Verify honestly.** Run relevant builds, tests, integration checks, and smoke tests on available platforms. Save commands, outcomes, runtime, OS/SDK information and artifact hashes in `docs/evidence/`. If a platform is unavailable, explicitly mark `NOT_RUN`.
6. **Update documentation in the same change.** Maintain `docs/COMPONENT_MATRIX.md`, `docs/MIGRATION_LOG.md`, `docs/TEST_STRATEGY.md` when relevant, and an ADR for material architectural decisions.
7. **Commit deliberately.** Separate build hygiene, characterization tests, functional port, and architectural refactor where practical; preserve traceability. Never mix unrelated changes.
8. **End with a handoff.** Summarize what changed, exact test results, regressions/risks, limitations, commit hashes and the next smallest task.

## Non-negotiable quality gates
- A component is **PORTED** only if it builds under a modern cross-platform target (e.g. `net10.0`) and passes its declared acceptance tests on Linux. `net10.0-windows` is not cross-platform.
- **REGRESSION_VERIFIED** requires comparison against a credible baseline (legacy tests, approved fixtures/golden outputs, or documented behavioral characterization). Merely running unit tests is not equivalence evidence.
- Do not delete, skip, weaken, or change expected results of tests to make CI green without explaining and obtaining task-specific authorization; quarantine unavoidable nondeterminism with a recorded issue.
- Do not claim GUI/FFmpeg/capture compatibility from compile success alone. Functional checks must cover behavior through the real boundary.
- Do not globally retarget all projects, erase legacy solutions, or add unsupported platform shims without a documented decision.
- No binary or large copyrighted sample videos committed by default. Use small, licensed/synthetic fixtures, deterministic generators and SHA-256 hashes.
- Never invent measurements, test passes, platform coverage, coverage percentages or performance data.
- Keep code working for Windows where feasible; if unavailable, mark `NOT_RUN` and request CI evidence before asserting compatibility.
- Prefer platform-neutral .NET APIs. Treat WinForms, System.Drawing.Common/GDI+, DirectShow, Registry, Windows DLL imports and Visual Studio C++/CLI as explicit migration risks.

## Testing and evidence
- Use test pyramid: domain unit tests; characterization/golden master; platform integration tests (video decode, seek, timestamp/frame output, camera on available hardware); end-to-end smoke tests; performance baselines when applicable.
- For video: use deterministic fixtures and compare frame dimensions, timestamps, seek tolerance, color/channel handling, frame pixel results with justified tolerances, and corrupted-file behavior. Compare against legacy output *where available*.
- Preserve reproducible results: command, git SHA, host OS, SDK/runtime, native library versions, fixture IDs/hashes, exit code, summary and limitations.
- Recommended status values: `NOT_STARTED`, `BLOCKED`, `IN_PROGRESS`, `BUILD_ONLY`, `TESTED`, `PORTABLE_TESTED`, `REGRESSION_VERIFIED`; never promote status on assumptions.

## Architecture rules
- Prefer incremental boundaries: domain models / geometry / timebase / analysis first where genuinely decoupled; platform-specific UI, camera adapters and C++/CLI media last or behind adapters.
- A parallel port solution/project is allowed if it reduces risk; do not imply `Kinovea.Port.sln` already exists. Create it only in a dedicated documented task.
- Do not assume FFmpeg can be trivially replaced. Preserve seek/buffering/timing semantics, document native interop strategy (including packaging and ABI issues) before implementing.
- Keep vendored upstream code, license notices and credits intact; review dependency licenses before adding packages.

## Commits & collaboration
- Branch: `port/<slice>` or `test/<slice>` from project-designated integration branch.
- Conventional prefixes: `build:`, `test:`, `port:`, `refactor:`, `docs:`, `ci:`, `fix:`.
- Subject imperative, <=72 chars recommended, component in scope when helpful. Body includes `Baseline:`, `Change:`, `Verification:`, `Limitations:` and `Refs:` (task/ADR/issue); use `NOT_RUN` explicitly.
- Never rewrite or force-push published history. Avoid combining mechanical renames with semantic changes.
- If repository has uncommitted user work, do not overwrite or reset it. Inspect `git status`, preserve changes and mention conflicts.

## Definition of done for one slice
All required: (1) scope and baseline documented; (2) implementation reviewed for platform-dependent calls; (3) tests added/updated and genuinely executed; (4) evidence stored; (5) docs/matrix updated; (6) changes committed coherently; (7) remaining risks listed. A blocked investigation can complete as a **diagnostic task** but must not be labeled as a successful port.

## First assigned task (do not skip ahead)
Complete the build-system inventory in `tasks/001-build-baseline.md`. Do not attempt to port the UI or FFmpeg until the matrix and baseline evidence exist.

<!-- BEGIN BEADS INTEGRATION v:1 profile:minimal hash:46cd31e7 -->
## Beads Issue Tracker

This project uses **bd (beads)** for issue tracking. Run `bd prime` to see full workflow context and commands.

### Quick Reference

```bash
bd ready              # Find available work
bd show <id>          # View issue details
bd update <id> --claim  # Claim work
bd close <id>         # Complete work
```

### Rules

- Use `bd` for ALL task tracking — do NOT use TodoWrite, TaskCreate, or markdown TODO lists
- Run `bd prime` for detailed command reference and session close protocol
- Use `bd remember` for persistent knowledge — do NOT use MEMORY.md files

**Architecture in one line:** issues live in a local Dolt DB; sync uses `refs/dolt/data` on your git remote; `.beads/issues.jsonl` is a passive export. See https://github.com/gastownhall/beads/blob/main/docs/core-concepts/sync-concepts.md for details and anti-patterns.

## Agent Context Profiles

The managed Beads block is task-tracking guidance, not permission to override repository, user, or orchestrator instructions.

- **Conservative (default)**: Use `bd` for task tracking. Do not run git commits, git pushes, or Dolt remote sync unless explicitly asked. At handoff, report changed files, validation, and suggested next commands.
- **Minimal**: Keep tool instruction files as pointers to `bd prime`; use the same conservative git policy unless active instructions say otherwise.
- **Team-maintainer**: Only when the repository explicitly opts in, agents may close beads, run quality gates, commit, and push as part of session close. A current "do not commit" or "do not push" instruction still wins.

## Session Completion

This protocol applies when ending a Beads implementation workflow. It is subordinate to explicit user, repository, and orchestrator instructions.

1. **File issues for remaining work** - Create beads for anything that needs follow-up
2. **Run quality gates** (if code changed) - Tests, linters, builds
3. **Update issue status** - Close finished work, update in-progress items
4. **Handle git/sync by active profile**:
   ```bash
   # Conservative/minimal/default: report status and proposed commands; wait for approval.
   git status

   # Team-maintainer opt-in only, unless current instructions forbid it:
   git pull --rebase
   bd dolt push
   git push
   git status
   ```
5. **Hand off** - Summarize changes, validation, issue status, and any blocked sync/commit/push step

**Critical rules:**
- Explicit user or orchestrator instructions override this Beads block.
- Do not commit or push without clear authority from the active profile or the current user request.
- If a required sync or push is blocked, stop and report the exact command and error.
<!-- END BEADS INTEGRATION -->

<!-- BEGIN BEADS CODEX SETUP: generated by bd setup codex -->
## Beads Issue Tracker

Use Beads (`bd`) for durable task tracking in repositories that include it. Use the `beads` skill at `.agents/skills/beads/SKILL.md` (project install) or `~/.agents/skills/beads/SKILL.md` (global install) for Beads workflow guidance, then use the `bd` CLI for issue operations.

### Quick Reference

```bash
bd ready                # Find available work
bd show <id>            # View issue details
bd update <id> --claim  # Claim work
bd close <id>           # Complete work
bd prime                # Refresh Beads context
```

### Rules

- Use `bd` for all task tracking; do not create markdown TODO lists.
- Run `bd prime` when Beads context is missing or stale. Codex 0.129.0+ can load Beads context automatically through native hooks; use `/hooks` to inspect or toggle them.
- Keep persistent project memory in Beads via `bd remember`; do not create ad hoc memory files.

**Architecture in one line:** issues live in a local Dolt DB; sync uses `refs/dolt/data` on your git remote; `.beads/issues.jsonl` is a passive export. See https://github.com/gastownhall/beads/blob/main/docs/core-concepts/sync-concepts.md for details and anti-patterns.
<!-- END BEADS CODEX SETUP -->
