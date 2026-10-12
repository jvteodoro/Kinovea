# Task 001 — Inventory and reproducible Linux build baseline

## Goal
Document the checkout, exact dependency graph, platform coupling, and first safe migration candidate. Do not port implementation in this task.

## Existing evidence
Gentoo + SDK 10.0.111; path capitalization mismatch fixed locally; current build exposes MSB3644, MSB4278, NU1503. See MIGRATION_LOG.

## Deliverables
- Expand `docs/COMPONENT_MATRIX.md` to cover all source solution projects, target frameworks, project references, package refs and platform APIs.
- Save `docs/evidence/YYYY-MM-DD-build-baseline.md` including git SHA, working tree state, commands and exit codes.
- Reproduce build results without hiding errors; distinguish restore/build/toolchain failures.
- Produce prioritized shortlist (at least 2) of small portable migration slices, explaining direct and transitive dependencies and characterization approach.
- Update migration log; open ADR if proposing a new `.sln`.
- One or more coherent commits with meaningful verification and limitations.

## Suggested commands
```bash
pwd; git rev-parse HEAD; git status --short
dotnet --info
find . \( -name '*.sln' -o -name '*.csproj' -o -name '*.vcxproj' \) | sort
git grep -nE 'TargetFrameworkVersion|TargetFramework|ProjectReference|Reference Include|HintPath' -- '*.csproj'
git grep -nE 'System.Windows.Forms|System.Drawing|DllImport|Microsoft.Win32|DirectShow' -- '*.cs'
dotnet build Kinovea.VS2019.sln -v:minimal
```

## Acceptance criteria
- Every known project represented and paths verified on case-sensitive Linux.
- Logs prove the result, without claiming compilation success or runtime parity.
- Matrix cites concrete code/project files in relevant cells.
- Next slice selected with specific proposed tests, and no semantic source changes made.
