# ADR 0001: incremental, evidence-first port

Status: PROPOSED (requires maintainer approval)
Date: 2026-10-11

## Context
The VS2019 solution targets legacy .NET Framework and includes Windows-specific C++ build infrastructure. Linux build currently fails before compile for many projects. Migrating everything simultaneously would conflate infrastructure and functional defects.

## Decision proposed
Preserve upstream solution and migrate narrow slices behind platform-neutral boundaries with independent characterization tests and a component matrix. Introduce parallel build solution only when task inventory justifies it. Keep original behavior testable on Windows where available.

## Consequences
More intermediate adapters and temporary dual-target arrangements; smaller diffs, lower risk, traceability and the ability to compare outputs. Some components will remain blocked until native backends/UI are designed.

## Alternatives
Big-bang retarget of all csproj (rejected due to high coupling); wholesale rewrite (rejected due to parity and provenance risk); Mono-only compatibility (not the desired modern cross-platform destination).
