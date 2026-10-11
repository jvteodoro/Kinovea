# Migration log

Append chronological entries; do not erase unsuccessful experiments.

## 2026-10-11 — initial Linux build investigation
- Environment: Gentoo Linux; .NET SDK `10.0.111`.
- Initial build of `Kinovea.VS2019.sln`: missing `Kinovea.Video.GIF/Kinovea.Video.GIF.csproj` (`MSB3202`) owing to casing mismatch with `Kinovea.Video.Gif` directory and project file.
- Local remediation: `git mv` project directory and `.csproj` to uppercase `GIF`. Check working tree for any additional metadata/documentation edits; no commit hash provided.
- Next build: `2 warning(s), 17 error(s)`; `MSB3644` net48 reference assemblies unavailable on Linux; `MSB4278` Visual Studio C++ targets unavailable for `PlayerServer.vcxproj`; `NU1503` restore skipped for `Kinovea.Camera` and `PlayerServer`.
- Observation: path issue resolved; full solution does not compile. No function or regression test has been reported as passing.
- Next: inventory each project and establish a portable characterization seam.

## 2026-10-11 — Task 001 revalidation — commit hash in handoff
- Baseline: checkout `cce3013c4c327a9fcd22cdac0b827cac41d99bff`, branch `port/net10-linux`; Gentoo 2.18, Linux `6.18.50-gentoo-dist-bin` x86_64, .NET SDK `10.0.111`, MSBuild `18.0.11`, runtime `10.0.11`. The working tree already contained untracked migration docs/task files; they were preserved and the task artifacts updated in place.
- Change: inventoried all 20 C#/C++ project entries in `Kinovea.VS2019.sln`, checked every project path and every C# project reference with case-sensitive filesystem lookups, mapped targets/dependencies/packages/platform APIs, and added a dependency-aware shortlist and characterization plan. No application source or behavior changed.
- Verification: `dotnet build Kinovea.VS2019.sln -v:minimal` exited 1 during solution restore, showing `Build FAILED`, 0 warnings and 0 errors; diagnostic restore also exited 1 in `_FilterRestoreGraphProjectInputItems` with no emitted MSBuild/NuGet error explaining why. This differs from the earlier recorded `17 errors / 2 warnings`, so that count is not reproducible in this run. Direct `dotnet build Kinovea.Services/Kinovea.Services.csproj -v:minimal` exited 1 with MSB3644 (missing net48 reference assemblies). Direct `dotnet build Kinovea.Video.FFMpeg/PlayerServer/PlayerServer.vcxproj -v:minimal` exited 1 with MSB4278 (`Microsoft.Cpp.Default.props` unavailable). Direct GIF project build exited 1 with 0 warnings/errors. See `docs/evidence/2026-10-11-build-baseline.md` for exact commands and limits.
- Regression evidence: none. No tests or runtime workflows were run; no component is claimed to compile, run, or preserve behavior on Linux.
- Known limitation: full-solution restore fails before the known compile-stage blockers are reported, and the current diagnostic output does not expose its cause. Windows/MSVC, Windows runtime behavior, camera hardware, FFmpeg decode/seek, and legacy behavioral oracles were not available/run. Per-project full build outcomes are not inferred from the two direct diagnostic project builds.
- Follow-up: reproduce and explain the restore graph failure in a dedicated build-forensics issue; then add a source-isolated timecode characterization suite before any migration. Preserve the exact legacy outputs and culture/rounding behavior as the oracle.

## Append new entries in this format
`YYYY-MM-DD — <task/slice> — <commit>`
- Baseline:
- Change:
- Verification:
- Regression evidence:
- Known limitation:
- Follow-up:
