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
- Verification: XML/YAML, source syntax and diff checks passed and the change was pushed. Run `38174949059` compiled the harness and started it, then failed during JPEG fixture generation before any reader case ran.
- Regression evidence: none; run 38174949059 recorded zero cases and zero assertions.
- Known limitation: no reader observations or golden output exist; behavior remains `UNVERIFIED`.
- Follow-up: correct the JPEG encoder parameter type and rerun the workflow.

## 2026-10-11 — Bitmap JPEG fixture encoder correction
- Baseline: run `38174949059` on `windows-2022`, source `851edb7315abb085710d4b7136628a53c7ed151f`.
- Change: pass JPEG quality as Int64 to GDI+ `Encoder.Quality` and print fatal setup errors into the job log. No production behavior changed.
- Verification: source/documentation validation pending before commit. The corrected Windows run has not started.
- Regression evidence: none. CI compiled successfully but fixture generation failed before reader characterization (zero cases/assertions).
- Known limitation: characterization remains unexecuted and no golden baseline exists.
- Follow-up: push and inspect the next Windows workflow run and generated report.

## 2026-10-11 — Windows Bitmap reader characterization baseline
- Baseline: characterization source `0a9e8a5f847b77e030355dd2f38a7de97a4a2572`, built from the actual legacy `Kinovea.Video.Bitmap` project targeting .NET Framework 4.8.
- Change: executed the fixed headless reader harness twice on clean `windows-2022` runners at the same source SHA. Captured manifests, environment/source hashes, 13 input fixtures and 59 pixel files per run; the temporary repeat branch was deleted after comparison. No production reader behavior changed and no golden files were silently generated.
- Verification: runs `38175088092` and `38175219336` each passed 19 cases and 537 assertions. Both recorded runner image `20261004.326.1`, CLR `4.0.30319.42000`, GDI+ `10.0.20348.5622`, 64-bit process, .NET Framework release `533325`, and MSBuild `17.14.60.43110`. Source-hash manifests were identical. All 72 files under `fixtures/` and `pixels/` had matching SHA-256 across runs. First artifact ZIP SHA-256 `27bc62de97ef0ce075954eddebbc8f904a56d175a1be562125ec728d520a08ef`; repeat artifact ZIP SHA-256 `1b0b3ed3a58563d947f6b2bc479b0d89ea83503dd250498f86cb4edff39696e5`. Detailed results are in `docs/evidence/2026-10-11-bitmap-characterization-infrastructure.md`.
- Regression evidence: measured only against explicit harness assertions for the inspected legacy code, not against another runtime or previously approved baseline. This supports a repeatable Windows oracle but is not a `REGRESSION_VERIFIED` migration claim.
- Known limitation: no approved golden snapshot or architectural review exists. Coverage is limited to the specified synthetic BMP/PNG/JPEG inputs and current Windows/GDI+ environment; Linux/.NET 10, GUI and broader image behavior remain unverified. NuGet restore emits NU1902 for log4net 2.0.14.
- Follow-up: reviewer should inspect candidate manifests and binary outputs, then add a separate approved golden-data commit if accepted. Conduct ADR 0002 architectural review separately; retain its `PROPOSED` status until then.

## 2026-10-11 — Preserve candidate Bitmap oracle and make CI manual
- Baseline: two clean `windows-2022` runs at source SHA `0a9e8a5f847b77e030355dd2f38a7de97a4a2572`, each passing 19 cases and 537 assertions; 72 fixture/pixel files matched byte-for-byte.
- Change: preserve the first run's synthetic input fixtures, legacy reader pixel files, manifests, metadata and report under `tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f/`. Change the workflow trigger to manual dispatch only. Document that this candidate is not approved golden data.
- Verification: verify snapshot file hashes against its recorded checksum manifest; validate workflow YAML and ensure `workflow_dispatch` is the sole trigger; run `git diff --check`. No reader build or test rerun is needed for this artifact/documentation and trigger-only change; the recorded Windows runs remain the behavioral evidence.
- Regression evidence: candidate report records 19 cases, 537 assertions and zero failures. Repeat run confirmed fixture/pixel reproducibility.
- Known limitation: candidate is not reviewer-approved; behavior outside its cases is `UNVERIFIED`. This change does not alter production code.
- Follow-up: conduct separate golden review before copying this candidate into `approved/`; keep ADR 0002 `PROPOSED` pending architectural review.


## 2026-10-11 — Portable frame contract architecture proposal
- Baseline: requested `port/net10-linux` head `125dbbb907f554162bc31e54f95090876a19773f`; Windows Bitmap evidence remains candidate data with status `PENDING_HUMAN_APPROVAL`.
- Change: drafted ADR 0003 describing an explicit format/stride/alpha/timebase contract, ownership and disposal rules, a no-pool initial policy, producer/consumer map, conversion boundaries, tradeoffs, test gates and migration stages. Updated the matrix and test strategy. No production API or behavior changed.
- Verification: source inspection only; `git diff --check` passed. No builds or behavior tests run.
- Regression evidence: Windows candidate observations are cited as measured but remain pending human approval. Raw channel order, alpha premultiplication, independent EXIF pixel transforms, negative stride and resource leak behavior remain `UNVERIFIED`; Linux parity is `NOT_RUN`.
- Limitations: implementation-wide Bitmap inventory remains incomplete for unrelated UI resources/drawing images; the design evidence identifies this boundary.
- Follow-up: review and resolve ADR 0003 questions, complete missing Windows characterization, then implement the approved contract in a separate task on `port/net10-linux`.

## 2026-10-11 — Revise ADR 0003 for minimal behavior-preserving migration
- Baseline: PR #2 review `5485255332`; branch `design/portable-frame-contract` at `e01dc1b41cab64a58acedd463ef189e526c16fd0`; target `port/net10-linux` at `125dbbb907f554162bc31e54f95090876a19773f`.
- Change: selected a parallel portable pixel seam while preserving legacy contracts. Added per-change compatibility classifications, Option A/B/C comparison, signed-stride and `ReadOnlyMemory` lifetime limits, legacy behavior guarantees, non-goals, and staged Windows/Linux regression and rollback criteria. Inspected actual reader/generator/cache/export/OpenCV code. Updated evidence, matrix and test strategy.
- Verification: PR reviews/comments and issue comments queried with `gh api`; one architectural review was returned and no inline/issue comments were returned. Source inspection and `git diff --check`; no runtime test/build run.
- Regression evidence: Windows characterization remains candidate `PENDING_HUMAN_APPROVAL`; no new behavior is asserted. Raw channels, premultiplication, EXIF pixel maps and disposal remain gaps; Linux parity `NOT_RUN`.
- Limitations: UI rendering library and eventual decoder/native interop remain unresolved. No production code changed.
- Follow-up: review the revised ADR, then implement only the isolated net10 pixel contract plus synthetic producer and conformance tests after approval.

## 2026-10-12 — Windows GDI+ PixelBuffer adapter v1
- Baseline: `port/net10-linux` at merge commit `a9c1e9c8fe66d71356afebdad50a5854937e9fc1`; Portable Pixel Core `net10.0` remains independently tested, and the Windows net48 Bitmap outputs remain candidate/unapproved.
- Change: add isolated `net10.0-windows10.0.17763.0` `Kinovea.PortablePixels.GdiPlus` using Option A (separate actual-reader net48 capture process and net10 Windows adapter tests). Supports GDI+ 24bpp RGB, 32bpp RGB, ARGB and PArgb storage; explicitly maps BGR/BGRA to RGB24/RGBA32 and preserves alpha association. Extend Windows source hash metadata to cover all adapter/test sources. No production reader, frame API, cache, UI, timestamp, FFmpeg, or portable core change.
- Verification: final Windows workflow run [38189832385](https://github.com/jvteodoro/Kinovea/actions/runs/38189832385), source SHA `e02fc457bbf371a456313e6ad59eb89135f6cfc8`, passed: legacy reader 19 cases/537 assertions; adapter 38 checks including 29 reader-generated captures and 14 verified fixture hashes; portable core 10/10 on Windows and Linux. Source manifest listed 29 configured files with none missing. Local Gentoo .NET SDK 10.0.111 passed the portable core 10/10 and built the adapter library; test host build/runtime were not run locally. See [adapter evidence](evidence/windows-bitmap-adapter-v1.md) for environment, fixture/report/source hashes, exact commands, property statuses and allocation samples.
- Regression evidence: adapter output dimensions, RGB values, captured logical rows and straight ARGB alpha matched outputs from the actual legacy reader for the compared cases. Direct fixtures verify authored BGR/channel values, straight/PArgb alpha labeling/bytes, padding, negative stride, ownership, lock release and unsupported indexed input. This is property-scoped comparison against candidate captures generated in that same CI run, not an approved golden comparison.
- Limitations: initial failed CI runs recorded workflow-action naming (`38188120691`, `38188133096`), target platform issues (`38188701325`, `38188866089`, `38188869090`), and fixture-count/path assumptions (`38189533763`, `38189681765`). Corrections produced the passing final run. EXIF/mirror semantics as an adapter responsibility, actual-reader PArgb output, timestamps at a frame integration boundary, production consumer lifecycle, Linux image decode and app parity remain `NOT_ESTABLISHED` or `NOT_RUN`. Performance values are repeated samples from hosted runners, not general guarantees. The adapter is not integrated into production; `Kinovea.Video.Bitmap` is not marked ported.
- Follow-up: review the adapter boundary and decide whether to add a narrow opt-in integration test connecting one legacy-produced frame to PixelBuffer while retaining the legacy production path. Add independent mirror/EXIF expectations and frame-ownership characterization before that integration.

## 2026-10-12 — Portable BMP/PNG reader v1
- Baseline: PR #4 merged into `port/net10-linux` at `8ed8666aae85ff8cf16d97cdd1629dc2043638ba`; original Windows candidate remains `CANDIDATE_NOT_APPROVED`.
- Change: add standalone `net10.0` SkiaSharp 4.153.1 BMP/PNG reader returning owned RGB24 or straight-alpha RGBA32. Keep legacy production reader and call sites unchanged. Add a generated PNG eXIf orientation-6 input to Windows characterization; actual GDI+ output exposes no orientation property and remains unrotated. Adjust existing adapter test guards to allow additional cases and fixtures while preserving minimum checks.
- Verification: final tested code SHA `8b75d148725762942f24ffe2c7c4624d4ad88294`. [Reader workflow 38192763379](https://github.com/jvteodoro/Kinovea/actions/runs/38192763379): Linux and Windows reader suites each passed 1,322 assertions; actual .NET Framework 4.8 reader passed 20 cases/578 assertions; fresh reader comparison passed 2,419 assertions. [Shared adapter/core workflow 38192763320](https://github.com/jvteodoro/Kinovea/actions/runs/38192763320): adapter passed 40 checks/31 reader captures; Portable Pixel Core passed 10/10 on Linux and Windows. Local Gentoo Linux/.NET SDK 10.0.111 build passed with no warnings/errors; reader suite passed 1,322 assertions; comparison against the final Windows artifact passed 2,419 assertions; Portable Pixel Core passed 10/10. A separate local Windows-only adapter build was `NOT_RUN` because NuGet restore failed with `NU1301` network permission denied.
- Regression evidence: exact pixel comparison passed for candidate BMP 7x5, candidate transparent PNG 13x9, and generated PNG eXIf orientation 6 against fresh output from actual `VideoReaderBitmap`. Candidate binary fixture/pixel hashes are checked, but candidate approval remains pending. Only these fixtures and properties are established; no global reader parity or approved baseline is claimed.
- Measurements: three warmups and seven samples per format/size on Gentoo, Ubuntu 24.04 CI, and Windows Server 2022 CI. Managed allocation counts and medians are in [reader evidence](evidence/portable-image-reader-v1.md); they exclude Skia native memory and are descriptive, not budgets. One explicit full-frame copy writes decoded native pixels into the final owned buffer.
- Limitations: other PNG eXIf encodings/orientations, JPEG, color profiles, and uncommon BMP/PNG variants are unverified or unsupported. No timestamps, `VideoFrame`, cache, UI, thumbnail/export, or production integration. Linux native packaging beyond x64 glibc is unverified. No legacy production code changed.
- Follow-up: design a narrow opt-in frame-generator boundary integration while retaining the Windows path. Characterize timestamp and ownership behavior before connecting the portable reader to Kinovea consumers.
