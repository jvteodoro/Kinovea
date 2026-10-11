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

## 2026-10-11 — parallel restore graph diagnosis
- Baseline: Task 001 commit `327cfbea597cae48a8e3e29ec5c95b4434f7d25b`; same Gentoo/.NET SDK 10.0.111 environment.
- Change: added controlled restore-parallelism findings to the evidence record. No application source, solution, or project file changed.
- Verification: full-solution restore with `-m:1` or `-m:2 -p:BuildInParallel=false` exits 0 (4 NU1503 warnings); the same restore with `-m:2`, `-m:4`, and `-m:8` exits 1 silently. Project-only restore for Camera and DirectShow succeeds at `-m:2`. Two-project solutions fail at `-m:2` and pass at `-m:1`, including a probe with independent classic projects. Full `dotnet build Kinovea.VS2019.sln -m:1 -v:minimal` exits 1 with 4 warnings and 20 errors: 15 MSB3644, 3 MSB4019, 2 MSB4278. Full build with `-m:2 -p:BuildInParallel=false` still exits 1, with 4 warnings and 0 reported errors; this is not a validated build workaround.
- Regression evidence: none; no behavior or runtime tests were run.
- Known limitation: the immediate failure trigger is inner parallel restore-graph processing in this environment. MSBuild/NuGet still emits no underlying exception, so the lower-level engine cause is unresolved. The serial build also confirms the missing `Microsoft.CSharp.Targets` import in `Kinovea.Camera.csproj:291`.
- Follow-up: retain serial restore/build as a diagnostic command only; do not alter project files as part of this investigation. Add a narrowly scoped investigation if MSBuild's silent parallel failure must be fixed rather than avoided operationally. Timecode characterization remains the next application-facing task.

## 2026-10-11 — timecode source characterization
- Baseline: `Kinovea.Services/TimeHelper.cs` and its enum/delegate contracts in checkout `815c5579ddc2686914d29546e4bf606e84f7e353`; no formatter implementation was changed. Existing `Kinovea.Tests/Time/TimecodeFormatTest.cs` only logs outputs and has no assertions; its net48 project references the broad legacy UI/media graph.
- Change: added a package-free `net10.0` console characterization project that links those exact four source files. Added assertions for all `GetTimestring` enum branches, `GetTimecodeType` mappings, culture, frame-rate precision, negative/zero/large time, midpoint rounding/carry, normalization endpoints/zero duration/zero total frames, timestamp narrowing boundaries and identity mapping. Updated strategy, matrix and this log.
- Verification: `dotnet run --project tests/Timecode.Characterization/Timecode.Characterization.csproj --configuration Release` on Gentoo Linux with SDK `10.0.111` / runtime `10.0.11` exited 0: `PASS: 50 timecode characterization assertions`. `git diff --check` exited 0.
- Regression evidence: source-linked Linux observations only. No Windows .NET Framework execution or known output oracle was available; legacy parity is `UNVERIFIED`, not `REGRESSION_VERIFIED`.
- Known limitation: selected observations reflect .NET 10/Linux globalization and checked inputs; they do not establish Windows globalization behavior or application integration. The existing legacy demo was not run because its project references net48 and unavailable platform/media projects.
- Follow-up: use these assertions as a characterization gate before extracting or changing timecode code; capture and compare a Windows legacy oracle when available.

## 2026-10-11 — geometry source characterization
- Baseline: `Vector3.cs`, `Matrix3x3.cs`, and supporting `SVD.cs` in checkout `02b879d5`; all are AForge-derived and retain their upstream copyright headers. The ScreenManager project itself is net48 and has extensive unrelated UI/media dependencies.
- Change: added a package-free `net10.0` harness linking those exact three source files, with 319 assertions over vector and matrix contracts, including rotation conventions, tie indices, zero normalization, singular/near-singular inverse behavior, pseudo-inverse and SVD reconstruction. No source behavior or AForge header was edited.
- Verification: `dotnet run --project tests/Geometry.Characterization/Geometry.Characterization.csproj --configuration Release` on Gentoo Linux with .NET SDK `10.0.111` / runtime `10.0.11` exited 0: `PASS: 319 geometry characterization assertions`. Build emits 3 source warnings: two CS8765 nullability override warnings and CS8981 for the existing lowercase `svd` type. `git diff --check` passed.
- Regression evidence: linked source observations on .NET 10/Linux only. No Windows .NET Framework execution or verified baseline output is available; parity remains `UNVERIFIED`.
- Known limitation: float checks use `1e-6` absolute tolerance for arithmetic/rotation results; SVD/pseudo-inverse checks characterize one well-conditioned full-rank matrix and a diagonal rank-deficient matrix. This does not prove domain-level calibration or geometry workflow behavior.
- Follow-up: if extracting these contracts, preserve AForge attribution and compare the suite with Windows legacy results before claiming regression parity. Characterize `ProjectiveMapper` and calibration callers separately.

## 2026-10-11 — Bitmap adapter boundary diagnosis
- Baseline: commit `3fb66f01`; inspected `Kinovea.Video.Bitmap` and `Kinovea.Video` source/project files. Both target net48; Bitmap references Services and Video and a direct log4net assembly. The shared `VideoFrame.Image`, `IFrameGenerator.Generate`, and `VideoSummary.Thumbs` contracts expose `System.Drawing.Bitmap`.
- Change: documented the adapter boundary, observable behaviors, Linux build outcomes, and a platform/fixture characterization plan. No application or project source changed.
- Verification: direct `dotnet build Kinovea.Video.Bitmap/Kinovea.Video.Bitmap.csproj -v:minimal` exited 1 during restore with 0 reported warnings/errors; diagnostic `dotnet build Kinovea.Video.Bitmap/Kinovea.Video.Bitmap.csproj -m:1 -v:minimal` exited 1 with MSB3644 because net48 reference assemblies are unavailable. No runtime tests were run.
- Regression evidence: none. Bitmap decoding, pixel equivalence, EXIF behavior, rotation and frame lifecycle are `UNVERIFIED`.
- Known limitation: source inspection cannot establish GDI+ decoder output or Windows EXIF handling. Linux cannot execute the legacy net48 project in this environment; no port or portable runtime claim is made.
- Decision support: proposed ADR 0002 defers an in-place portable retarget because `System.Drawing.Common` is Windows-only on modern .NET and Bitmap relies on shared GDI+-typed Video contracts. Microsoft platform guidance and call-site evidence are linked from the ADR and evidence record. No image library or FFmpeg backend is selected.
- Follow-up: capture Windows legacy results with generated/licensed fixtures for supported formats and EXIF orientations, then design the portable pixel/frame boundary before attempting a portable reader.

## 2026-10-11 — parallel restore graph diagnosis follow-up
- Baseline: previous parallel restore investigation; Gentoo Linux, .NET SDK `10.0.111`, MSBuild `18.0.11`.
- Change: added a clean two-project SDK-style `net10.0` restore probe and checked process identity/shim indicators. No application or project files changed.
- Verification: with both probe `obj/` directories removed before each run, `dotnet restore /tmp/kinovea-cur-sdk-probe/Probe.sln -m:2 -v:minimal` exited 1 silently; `-m:1` and `-m:2 -p:RestoreUseStaticGraphEvaluation=true` each exited 0 and restored both projects. The original solution restored with `-m:2 -p:RestoreUseStaticGraphEvaluation=true` (exit 0, two NU1900 network warnings). Default parallel restore's diagnostic log stops in `_FilterRestoreGraphProjectInputItems`/`_IsProjectRestoreSupported` with zero emitted errors. UID checks found real/effective UID 1000; no fakeroot command or `LD_PRELOAD`/`FAKEROOTKEY` was present. `git diff --check` passed.
- Regression evidence: none; restore is not a code or runtime test. No application behavior changed.
- Known limitation: root exception remains hidden. The independent SDK-style repro rules out a Kinovea-specific project-evaluation cause, but it does not distinguish an SDK/MSBuild defect from an environment/runtime interaction. The static graph option is verified for restore only. The similar fakeroot report at MSBuild issue #9731 does not match observed process identity and is not claimed as the cause. Full build still fails; Windows and runtime tests are `NOT_RUN`.
- Follow-up: retain default failures in evidence; use `-p:RestoreUseStaticGraphEvaluation=true` only as a documented restore diagnostic on this host, and recheck with another SDK/host to localize the fault.

## 2026-10-11 — ProjectiveMapper source characterization
- Baseline: ProjectiveMapper, Vector3 and Ellipse sources in checkout `43c8fae5c0ccdaec7f94a42870661bc1ac70d2c3`; ScreenManager remains net48 and tied to GDI+/WinForms and other Windows dependencies.
- Change: added a `net10.0` harness linking the exact ProjectiveMapper, Vector3 and Ellipse source. The test project supplies a narrowly documented QuadrilateralF contract substitute and no-op logger to avoid bringing unrelated GDI+ drawing/log4net assemblies into this math-only harness. Updated the matrix, test strategy and evidence. Production code was unchanged.
- Verification: `dotnet run --project tests/ProjectiveMapper.Characterization/ProjectiveMapper.Characterization.csproj --configuration Release` on Gentoo Linux with SDK `10.0.111` / runtime `10.0.11` exited 0: `PASS: 40 ProjectiveMapper characterization assertions`. Build reports source nullable warnings; none were suppressed. `git diff --check` passed.
- Regression evidence: Linux execution of linked source only. No Windows .NET Framework oracle or calibration workflow output is available; parity remains `UNVERIFIED`.
- Known limitation: production QuadrilateralF, GDI+ operations, real log4net assembly and calibration callers were not run. The characterization pins the parameterless constructor's uninitialized state, invalid-input behaviors, ellipse fixture output, and a suspicious `Backward(Vector3)` Y expression/result; these observations are not an endorsement of the results.
- Follow-up: compare pinned behaviors against a Windows legacy build before changing/extracting ProjectiveMapper; test production QuadrilateralF and calibration integration when a suitable platform/runtime is available.

## 2026-10-11 — Windows Bitmap characterization infrastructure
- Baseline: ADR 0002 is `PROPOSED`; `Kinovea.Video.Bitmap` targets net48 and uses the existing GDI+ reader. Linux lacks .NET Framework 4.8 reference assemblies.
- Change: added a `windows-2022` GitHub Actions workflow, a headless net48 harness referencing the production Bitmap project, deterministic in-memory fixture generation (BMP/PNG/JPEG plus EXIF orientations 1–9), raw/normalized pixel evidence capture, runtime/source hashes, JSON manifests and explicit test reports. Documented golden review and separate approval process. Production reader code and behavior were not changed; no replacement imaging library or System.Drawing migration was made.
- Verification: project XML validation passed with `xmllint`, workflow YAML parsed and its `windows-2022`/target-pack/always-upload structure was checked, C# sources passed Roslyn 7.3 syntax parsing, and `git diff --check` passed. `dotnet msbuild tests/Bitmap.Characterization/Kinovea.Bitmap.Characterization.csproj -target:Build -property:Configuration=Release -property:Platform=AnyCPU -verbosity:minimal` exited 1 with MSB3644 because this Linux host lacks net48 reference assemblies. No Visual Studio MSBuild, PowerShell, Mono or Windows runner was available locally. Windows build, workflow, fixture generation and reader tests are `NOT_RUN`. See `docs/evidence/2026-10-11-bitmap-characterization-infrastructure.md`.
- Regression evidence: none yet. Harness assertions are source-derived expectations, not observed Windows outputs. No golden baseline has been approved; all Bitmap behavior remains `UNVERIFIED`.
- Known limitation: CI has been authored but not executed. NuGet restore, transitive legacy project build, runner pack check, JPEG/EXIF generation, pixel capture and artifact upload remain to be verified on GitHub `windows-2022`.
- Follow-up: run the workflow; review its complete artifact and repeat with the same source SHA. Resolve all failures and nondeterministic differences before adding an approved golden-data commit. Keep ADR 0002 `PROPOSED` until its required architectural review.

## Append new entries in this format
`YYYY-MM-DD — <task/slice> — <commit>`
- Baseline:
- Change:
- Verification:
- Regression evidence:
- Known limitation:
- Follow-up:

## 2026-10-11 — Bitmap characterization CI x64 configuration correction
- Baseline: workflow run `38174132063` on `windows-2022`, source `1d14a2722b6cbf4fda992ca760ec39d03931d844`.
- Change: configured the headless harness and workflow for `Release|x64`, matching the legacy `Kinovea.Services` configuration that enables unsafe blocks. Updated local command documentation, migration matrix, ADR evidence and CI evidence record. Production source and reader behavior were not changed.
- Verification: run `38174132063` confirmed the .NET Framework 4.8 targeting pack and NuGet restore, then failed with nine CS0227 errors while building Services under AnyCPU. Run `38174665514` built x64 Services successfully, then failed on harness compile errors recorded in the evidence file. The diagnostic artifacts were uploaded. Local XML/YAML/path assertions and diff checks passed; reader execution remains NOT_RUN.
- Regression evidence: none. The harness was skipped by CI; no reader tests, fixtures, or pixel results were generated. Decoder behavior remains `UNVERIFIED`.
- Known limitation: no local Visual Studio MSBuild is available, and the harness still has compile errors pending the follow-up fix below.
- Follow-up: rebuild and execute after correcting the harness project reference and type name collisions. Only after the executable passes should candidate fixtures be repeated and reviewed for approval.

## 2026-10-11 — Bitmap harness compile corrections
- Baseline: run `38174665514` on `windows-2022` at source `cd8d9869736d04914b1d6fb6212669da7948c273`.
- Change: add a direct `Kinovea.Services` project reference for the `ImageRotation` API type and alias `System.Drawing.Bitmap` in harness sources to prevent collision with the enclosing `Kinovea.Bitmap` namespace. Production projects are unchanged.
- Verification: source/documentation checks passed and the commit was pushed. Run `38174835118` confirmed the prior Services/type fixes, then failed with CS0012/CS0246 because `Kinovea.Video` API types lacked a direct project reference.
- Regression evidence: none; run 38174835118 failed during compilation and skipped the reader.
- Known limitation: no generated fixtures, reports, or pixel evidence from any Windows run; behavior remains `UNVERIFIED`.
- Follow-up: add the direct `Kinovea.Video` reference, then run the workflow again.

## 2026-10-11 — Bitmap harness Video reference correction
- Baseline: run `38174835118` on `windows-2022`, source `5a6dbee08aae5e918ad65d5ced4982e34150e611`.
- Change: add a direct `Kinovea.Video` project reference because old-style project references did not expose its public API types transitively to the harness. Production code is unchanged.
- Verification: XML/YAML, source syntax and diff checks pending before commit. Next Windows build is not yet run.
- Regression evidence: none; characterization executable remains skipped.
- Known limitation: no reader observations or golden output exist; behavior remains `UNVERIFIED`.
- Follow-up: push and inspect the next Windows build, then proceed to runtime characterization only if compilation succeeds.
