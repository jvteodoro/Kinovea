# Commit policy

Keep commits reviewable, bisectable, intentional and linked to task/evidence.

## Suggested sequence per slice
1. `test(<component>): characterize <behavior>` — fixture/oracle and failing/passing baseline; if test unavailable, document why.
2. `port(<component>): migrate <capability> to modern .NET` — single behavioral slice.
3. `test(<component>): verify <parity>` — regression tests where not included in port commit.
4. `docs(<component>): record migration evidence` — if it cannot reasonably accompany earlier commit.

Prefer tests + implementation + evidence in the same atomic commit when separation would leave the branch knowingly broken. No commit may claim passing verification if evidence is absent.

## Commit message example
```
port(video): separate portable frame metadata

Baseline: legacy frame metadata contract documented in task 014.
Change: introduce platform-neutral metadata type and adapter.
Verification: dotnet test tests/... --no-restore (Linux, 18 passed).
Limitations: Windows NOT_RUN; no decoding or visual equivalence claim.
Refs: tasks/014-frame-metadata.md, docs/adr/0002-...
```

## Rules
- Do not commit `bin/`, `obj/`, logs, secrets, machine paths, downloaded packages or gigabyte-size videos.
- Avoid commits that combine case renames + API refactors + media changes.
- Cite upstream commit SHA where behavior is derived.
- If test failure is pre-existing, provide old/new comparisons and label it.
- One agent owns one nonoverlapping slice/branch; integrate through reviewed PRs.
