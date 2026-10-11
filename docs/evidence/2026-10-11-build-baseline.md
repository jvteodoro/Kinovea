# Verification: Task 001 build-system inventory

Baseline commit: `cce3013c4c327a9fcd22cdac0b827cac41d99bff` (`port/net10-linux`)

## Environment

- Host: Gentoo Linux 2.18, kernel `6.18.50-gentoo-dist-bin`, x86_64 GNU/Linux.
- .NET SDK: `10.0.111`; MSBuild `18.0.11+e2f47b011`.
- Installed runtimes: `Microsoft.NETCore.App 10.0.11`, `Microsoft.AspNetCore.App 10.0.11`; no workloads installed.
- Native libraries: no FFmpeg or camera runtime version was established; none was exercised.
- Fixtures: none used.
- Legacy oracle: unavailable; Windows .NET Framework/MSVC execution and saved legacy outputs were not run.
- Initial working-tree state: branch `port/net10-linux`, HEAD above; `git status --short --untracked-files=all` showed pre-existing untracked `docs/` files (`COMMIT_POLICY.md`, `COMPONENT_MATRIX.md`, `MIGRATION_LOG.md`, `MIGRATION_PLAN.md`, `TEST_STRATEGY.md`, `adr/0001-incremental-port.md`) and `tasks/001-build-baseline.md`, `tasks/TEMPLATE.md`. These were preserved. No tracked application source changes were present.
- SHA-256 at baseline: `Kinovea.VS2019.sln` = `f9ceb3f0349c2b1f76bb91def49a2c6abf22f9d428aa75695483928f25ee295f`; `Kinovea.Video.GIF/Kinovea.Video.GIF.csproj` = `fb42764c83d3d84196aee5e59b07b1fa0144d45375df8b2160eaeb79a78f70b2`.

## Commands and outcomes

Commands ran from `/home/enoch/projects/Kinovea`:

| Command | Exit | Observed result |
|---|---:|---|
| `git rev-parse HEAD` | 0 | `cce3013c4c327a9fcd22cdac0b827cac41d99bff` |
| `git status --short --untracked-files=all` | 0 | Existing untracked documentation/task files listed above; no tracked code modifications. |
| `dotnet --info` | 0 | SDK 10.0.111, MSBuild 18.0.11, runtime 10.0.11 on Gentoo Linux x64. |
| `find . \( -name '*.sln' -o -name '*.csproj' -o -name '*.vcxproj' \) -print \| sort` | 0 | Found original solution, 20 project files used in it, two additional legacy test projects, and two utility projects. `Tools/UnusedResources/UnusedResources.sln` is a separate solution. |
| Case-sensitive path/reference inspection script over `Kinovea.VS2019.sln` and C# `<ProjectReference>` paths | 0 | 20 solution projects; zero missing/case-mismatched project or project-reference paths. |
| `dotnet build Kinovea.VS2019.sln -v:minimal` | 1 | `Build FAILED`, 0 warnings, 0 errors, 4.04 seconds. Diagnostic output shows solution `Restore` failed before project build; no actionable error was emitted at minimal verbosity. |
| `dotnet build Kinovea.VS2019.sln -v:normal -bl:/tmp/kinovea-task001.binlog` | 1 | Solution `Restore` target failed, 0 warnings, 0 errors, 4.81 seconds. The binlog was left in `/tmp`, not added to the repository. |
| `dotnet restore Kinovea.VS2019.sln -v:diag` | 1 | `_FilterRestoreGraphProjectInputItems` failed; diagnostic log showed no `MSB####`, `NU####`, error, or exception explaining the failure. Captured log is `/tmp/kinovea-task001-restore.log` and is not committed. |
| `dotnet build Kinovea.Services/Kinovea.Services.csproj -v:minimal` | 1 | Build reaches compile setup and fails with MSB3644: .NETFramework v4.8 reference assemblies are absent. 0 warnings, 1 error. |
| `dotnet build Kinovea.Video.FFMpeg/PlayerServer/PlayerServer.vcxproj -v:minimal` | 1 | Fails at project import with MSB4278: `$(VCTargetsPath)/Microsoft.Cpp.Default.props` is unavailable. 0 warnings, 1 error. |
| `dotnet build Kinovea.Video.GIF/Kinovea.Video.GIF.csproj -v:minimal` | 1 | `Build FAILED`, 0 warnings, 0 errors, 1.01 seconds; no diagnostic was emitted. This project-only invocation did not reproduce the solution's previously reported error set. |

## Interpretation

The full-solution command was run without changing its project list, target frameworks, restore behavior or expected failures. It currently stops during solution restore, so this run does **not** reproduce the earlier migration note's `17 errors / 2 warnings`, nor can that aggregate be treated as the current baseline. Direct project invocations independently confirm the known net48 targeting-pack and Visual Studio C++ infrastructure blockers. The no-diagnostic failures on solution restore and the GIF project remain unexplained. The complete inventory, project references, package declarations, and source-level platform markers are in `docs/COMPONENT_MATRIX.md`.

## Platform and behavior checks

- Linux full solution build: failed during restore; no successful compilation established.
- Linux direct Services build: failed with MSB3644.
- Linux direct PlayerServer build: failed with MSB4278.
- Windows builds: `NOT_RUN`.
- Unit/characterization/integration/end-to-end tests: `NOT_RUN`.
- Runtime GUI, camera, FFmpeg decode/seek, frame output, playback timing and behavioral parity: `NOT_RUN`.
- Regression status: `UNVERIFIED`; no legacy oracle or comparison was available.

## Limitations and follow-up

The diagnostic capture identifies the failed restore graph target but not its root cause. Keep the failed result visible; do not treat a zero-error summary as a successful build. Next, isolate and explain the restore graph failure on the same checkout and SDK. Then establish a source-only timecode characterization suite, with Windows/legacy oracle comparison requested before claiming regression verification. No application code or behavior was changed for this task.
