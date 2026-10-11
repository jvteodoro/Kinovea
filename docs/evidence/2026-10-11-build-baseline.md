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

## Follow-up investigation: parallel solution restore

The silent failure is reproducible when solution restore uses more than one MSBuild node, and does not reproduce when restore is serialized:

| Command | Exit | Result |
|---|---:|---|
| `dotnet restore Kinovea.VS2019.sln -m:1 -v:diag` | 0 | Restore graph completes. It emits four NU1503 warnings for the classic `Kinovea.Camera`, `Kinovea.Camera.DirectShow`, `Kinovea.Camera.HTTP`, and C++/CLI `PlayerServer` projects; no errors. |
| `dotnet restore Kinovea.VS2019.sln -m:2 -v:minimal` | 1 | No diagnostic output; graph stops at `_FilterRestoreGraphProjectInputItems`. |
| `dotnet restore Kinovea.VS2019.sln -m:4 -v:minimal` | 1 | No diagnostic output. |
| `dotnet restore Kinovea.VS2019.sln -m:8 -v:minimal` | 1 | No diagnostic output. |
| `dotnet restore Kinovea.VS2019.sln -m:2 -p:BuildInParallel=false -v:minimal` | 0 | Restore completes with the same four NU1503 warnings. |
| `dotnet restore Kinovea.VS2019.sln -m:2 -nr:false -v:minimal` | 1 | No output; disabling node reuse does not resolve the failure. |
| `dotnet restore Kinovea.VS2019.sln -m:2 -tl:off -v:minimal` | 1 | No output; disabling the terminal logger does not resolve the failure. |
| `dotnet restore Kinovea.Camera/Kinovea.Camera.csproj -m:2 -v:minimal` | 0 | Project-only restore succeeds. |
| `dotnet restore Kinovea.Camera.DirectShow/Kinovea.Camera.DirectShow.csproj -m:2 -v:minimal` | 0 | Project-only restore succeeds. |
| `dotnet restore /tmp/KinoveaRestoreProbe.sln -m:2 -v:minimal` | 1 | A two-project solution containing those two valid project paths/GUIDs fails with no output. |
| `dotnet restore /tmp/KinoveaRestoreProbe.sln -m:1 -v:minimal` | 0 | Same two-project solution serially emits two NU1503 warnings and completes. |
| `dotnet restore /tmp/KinoveaIndependentProbe.sln -m:2 -v:minimal` | 1 | A two-project probe containing independent, classic `Kinovea.Services` and `Tools/UnusedResources/Converter` projects (both with valid project GUIDs and no project references) also fails with no output. |
| `dotnet restore /tmp/KinoveaIndependentProbe.sln -m:1 -v:minimal` | 0 | Same independent probe completes. |
| `dotnet build Kinovea.VS2019.sln -m:1 -v:minimal` | 1 | Serialized restore succeeds, then the build reports 4 warnings and 20 errors. Errors: 15 MSB3644 (.NET Framework v4.8 targeting pack), 3 MSB4019 from `Kinovea.Camera.csproj:291` importing `$(MSBuildBinPath)/Microsoft.CSharp.Targets`, and 2 MSB4278 (missing Visual Studio C++ targets for PlayerServer). Warnings: four NU1503 as listed above. |
| `dotnet build Kinovea.VS2019.sln -m:2 -p:BuildInParallel=false -v:minimal` | 1 | Restore completes with four NU1503 warnings, but the solution build phase still exits 1 with 0 errors reported. The property is therefore confirmed only as a restore diagnostic; it is not a verified full-build workaround. |

`-m:1` changes MSBuild parallelism only: it does not remove projects, skip restore, suppress diagnostics, or retarget the build. Setting `BuildInParallel=false` also allows restore to complete at `-m:2`, but does not make the full build diagnostic or successful. The controlled probes show the immediate trigger is inner parallel project scheduling during solution restore: a single project restore works with `-m:2`, while minimal two-project solutions fail at `-m:2` and pass at `-m:1`, even when their projects have no references to each other. Disabling node reuse or terminal logging has no effect. The failure is therefore not explained by a missing path or a project-reference cycle. The MSBuild/NuGet task still emits no underlying exception or diagnostic in the parallel case, so the lower-level engine defect/root cause remains unverified. `/tmp/KinoveaRestoreProbe.sln` and `/tmp/KinoveaIndependentProbe.sln` were temporary diagnostics only and are not committed.

The serial full build exposes additional build-system defects that the default parallel invocation masks. `Kinovea.Camera.csproj:291` names `Microsoft.CSharp.Targets` under the SDK directory; that exact file is absent there, while `Microsoft.CSharp.CurrentVersion.targets` is present. This is a confirmed import failure for this Gentoo/.NET SDK environment, separate from the net48 reference-assembly and Visual Studio C++ toolchain blockers. No project or solution files were changed to work around these failures.

## Follow-up investigation: SDK-style minimal restore probe

Environment was rechecked on 2026-10-11: `/proc/self/status` reported real/effective/saved/filesystem UID all `1000`, `id -u` and `id -ru` both returned `1000`, `command -v fakeroot` returned no path, and neither `LD_PRELOAD` nor `FAKEROOTKEY` was set. This rules out the UID mismatch/fakeroot condition described in [MSBuild issue #9731](https://github.com/dotnet/msbuild/issues/9731) for this process; that report is similar but does not establish the cause here.

A fresh temporary solution at `/tmp/kinovea-cur-sdk-probe/Probe.sln` contains two independent trivial SDK-style `net10.0` projects. Each clean-state restore below removed both projects' `obj/` directories first:

| Command | Exit | Result |
|---|---:|---|
| `dotnet restore /tmp/kinovea-cur-sdk-probe/Probe.sln -m:2 -p:RestoreUseStaticGraphEvaluation=true -v:minimal` | 0 | Both projects restored from a clean state. |
| `dotnet restore /tmp/kinovea-cur-sdk-probe/Probe.sln -m:2 -v:minimal` | 1 | Silent failure from a clean state. A diagnostic log at `/tmp/kinovea-cur-sdk-m2.log` shows `_FilterRestoreGraphProjectInputItems` fails while invoking `_IsProjectRestoreSupported`, with 0 warnings and 0 reported errors. |
| `dotnet restore /tmp/kinovea-cur-sdk-probe/Probe.sln -m:1 -v:minimal` | 0 | Both projects restored. |
| `dotnet restore Kinovea.VS2019.sln -m:2 -p:RestoreUseStaticGraphEvaluation=true -v:minimal` | 0 | Solution restore completed; two NU1900 warnings reported that vulnerability metadata could not be fetched from NuGet due unavailable network. This is restore only; no project was built. |

This confirms the default parallel restore-graph path failure is not specific to Kinovea's legacy project evaluation, since it reproduces with independent minimal SDK-style projects. Static graph evaluation is a reproducible restore-only workaround in the current environment, including for the original solution. The underlying exception/root cause remains unknown; static graph results do not make the full build pass or validate runtime behavior. `/tmp` probe sources and logs are temporary and not committed. The result is tied to SDK 10.0.111/MSBuild 18.0.11 on this Gentoo environment and should be rechecked on another host before generalizing.
