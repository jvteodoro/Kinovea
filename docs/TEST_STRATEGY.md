# Verification and regression strategy

## Truthful verification labels
- `BUILD_ONLY`: compile succeeded; no functional claim.
- `TESTED`: declared tests passed on recorded platform.
- `PORTABLE_TESTED`: relevant tests passed on Linux and Windows.
- `REGRESSION_VERIFIED`: results demonstrably agree with known legacy oracle within documented tolerances.
- `NOT_RUN`, `BLOCKED`, `UNVERIFIED`: explicit unknowns; cannot be converted to green.

## Test levels
1. **Static/build:** all paths, project references, analyzers, target frameworks, OS-dependent API inventory.
2. **Unit:** geometry, conversions, calibration, timeline timebase, interpolation, data model serialization.
3. **Characterization:** deterministic I/O fixtures and oracles from trusted legacy behavior; use relative/absolute numerical tolerances with rationale.
4. **Integration:** actual FFmpeg native libraries and codec operations; file reader/writer; plugin discovery; camera adapters if device present.
5. **End-to-end:** open/play/seek/annotate/measure/save/reopen/export, on Linux and Windows.
6. **Nonfunctional:** memory, seek latency, decode throughput, synchronization, leaks under repeat; report platform-specific baselines rather than inventing thresholds.

## Golden master and oracle policy
- Prefer legacy test or saved output from a verified Windows baseline. Record legacy version, OS, runtime and command.
- If legacy execution cannot be performed, construct expectations from specs/invariants/independent tools and label `REFERENCE_VALIDATED`, not `REGRESSION_VERIFIED`.
- Keep fixture provenance, license, generation script, codec/format, checksums, expected results and tolerance under version control.
- For decoded pixel checks, exact equality only if pipeline is deterministic. Otherwise document color model, bit depth, decoder and a justified metric/tolerance; do not silently relax assertions.
- Numerical geometry checks need reference coordinates, units, rounding and edge cases; serialization needs round-trip and backwards-compatibility tests.

## Evidence record (one per slice and important run)
Store `docs/evidence/YYYY-MM-DD-<slice>.md` with:

```markdown
# Verification: <slice>
Commit: <sha>
Host: <OS/version/architecture>
SDK/runtime: <versions>
Native libraries: <names/versions>
Fixtures: <paths and SHA-256>
Legacy oracle: <source and version or UNAVAILABLE>
Commands:
- `<exact command>` => exit `<code>`; `<N passed, M failed, K skipped>`
Platform checks: Linux <result>; Windows <result or NOT_RUN>
Functional observations: <measured or explicitly not tested>
Regressions: <differences, tolerance and rationale>
Limitations: <what cannot be claimed>
```

Do not commit generated giant logs by default; link a CI artifact, store summarized failure codes and optionally small diagnostic snippets. Unavailable environment means `NOT_RUN`, not success.

## Per-PR gates
- Tests are linked to the same modified components.
- Negative paths and representative boundary cases covered.
- All executed commands and outputs recorded.
- Comparison oracle status specified.
- Windows and Linux outcomes separated.
- No new skipped tests without rationale and task reference.

## Timecode source characterization
`tests/Timecode.Characterization` is a package-free `net10.0` console assertion suite that compiles the exact `TimeHelper.cs`, `TimeCodeFormat.cs`, `TimeCodeType.cs`, and `TimestampMapper.cs` files from the legacy tree. Run it with:

```sh
dotnet run --project tests/Timecode.Characterization/Timecode.Characterization.csproj --configuration Release
```

The suite records 50 Linux observations/assertions across all format enum branches, type mapping, culture, rounding, negative and large values, normalization, timestamp narrowing and identity mapping. It is source characterization only: the suite does not establish that the legacy .NET Framework executable behaves identically on Windows. Keep parity `UNVERIFIED` until a Windows legacy oracle is captured and compared.

## Geometry source characterization
`tests/Geometry.Characterization` is a package-free `net10.0` assertion suite linking the exact `Vector3.cs`, `Matrix3x3.cs`, and `SVD.cs` sources. `SVD.cs` is included because `Matrix3x3`'s pseudo-inverse and SVD methods call it. Run it with:

```sh
dotnet run --project tests/Geometry.Characterization/Geometry.Characterization.csproj --configuration Release
```

The suite uses exact comparisons where values are exactly representable and a documented `1e-6` absolute tolerance for float vectors/matrices. It covers arithmetic, tie rules, normalization including zero, construction/indexing, rotations, determinant/inverse, singular and near-singular inputs, transpose products, pseudo-inverse, and SVD reconstruction. It is Linux source characterization only; Windows parity remains `UNVERIFIED`.

## Projective mapping source characterization
`tests/ProjectiveMapper.Characterization` links the exact `ProjectiveMapper.cs`, `Vector3.cs`, and `Ellipse.cs` sources under `net10.0`. The harness supplies a narrow `QuadrilateralF` substitute with the production vertex/indexer/constructor contract because production `QuadrilateralF` also depends on GDI+ drawing APIs, plus a no-op log4net surface because the mapper only constructs but does not call its logger during tested paths. Run:

```sh
dotnet run --project tests/ProjectiveMapper.Characterization/ProjectiveMapper.Characterization.csproj --configuration Release
```

The suite covers affine/projective vertex mapping, interior forward/backward round trips, quadrilateral overloads, default construction, invalid update behavior and homogeneous mapping. It deliberately pins the current homogeneous `Backward(Vector3)` Y result, whose implementation uses `mapMatrix[1,2]` where the other inverse terms use `unmapMatrix`; do not treat this characterization as approval to change or preserve that behavior without a legacy oracle. This source-linked harness does not execute production `QuadrilateralF` or calibration callers. Windows parity and application integration remain `UNVERIFIED`.

## Windows .NET Framework Bitmap oracle
`.github/workflows/bitmap-characterization.yml` runs on `windows-2022`, uses Visual Studio MSBuild, restores the packages needed by the actual `Kinovea.Video.Bitmap` project reference, checks for the .NET Framework 4.8 targeting pack, builds the headless harness, and invokes `Kinovea.Video.Bitmap.VideoReaderBitmap` through its public `Open`/navigation/geometry/summary APIs. The harness does not use `new Bitmap(path)` as a reader proxy. Run it locally from PowerShell with the same commands used by CI:

```powershell
nuget restore Kinovea.Services/packages.config -PackagesDirectory packages -NonInteractive -Verbosity normal
msbuild tests/Bitmap.Characterization/Kinovea.Bitmap.Characterization.csproj /t:Rebuild /m /nologo /verbosity:minimal /p:Configuration=Release /p:Platform=x64
tests/Bitmap.Characterization/bin/x64/Release/Kinovea.Bitmap.Characterization.exe --output artifacts/bitmap-characterization
```

The run produces `environment.json`, `source-hashes.json`, `fixture-manifest.json`, `runtime-metadata.json`, `test-results.json`, a text summary, generated fixture inputs, and native plus RGBA pixel files. Workflow artifacts upload under `if: always()`, including after restore, build, or characterization failures. The workflow runs only on manual `workflow_dispatch`, so pushes and pull requests do not consume Windows runner time. A build pass is not a behavior pass; only `test-results.json` with all assertions/cases passing is behavioral evidence.

Fixture inputs are generated in the artifact directory from a documented pixel formula: 7x5 BMP, transparent 13x9 PNG, 11x7 JPEG and JPEG EXIF orientation tags 1 through 9, plus malformed PNG. JPEG bytes use the Windows GDI+ encoder at a fixed quality and therefore record their fixture SHA-256 and encoder/runtime metadata on each run. The manifest marks every run `CANDIDATE_NOT_APPROVED`; generated inputs and outputs never write into the snapshot directories. A repeatable candidate from two same-SHA Windows runs is checked in at `tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f/`. This snapshot is evidence for review, not an approved expected-output baseline.

### Golden review and approval
1. Review a successful artifact from a specific workflow run and source commit. Confirm targeting-pack and runtime metadata, production and harness source hashes, every fixture hash, all test results, and pixel evidence files.
2. Repeat on a clean `windows-2022` run with the same source revision. Compare fixture and pixel hashes; investigate any difference before considering approval. Do not normalize away or change pixels to make runs match.
3. A reviewer approves a dedicated PR that adds an immutable versioned snapshot under `tests/Bitmap.Characterization/golden/approved/<baseline-id>/`, with provenance metadata naming the workflow run, commit, runner image, fixture hashes, source hashes, and reviewer. Keep this golden-data commit separate from harness/CI changes.
4. Future output changes are reviewed as diffs against that approved snapshot. Never auto-copy candidate artifacts over approved data or regenerate expected hashes during a test run. An intentional baseline update needs a separate explicit review and commit preserving the prior baseline or its history.

Windows runs `38175088092` and `38175219336` both passed on the same source SHA with 19 cases and 537 assertions. Comparing downloaded artifacts showed all 72 fixture and pixel files byte-for-byte identical; GDI+ version was `10.0.20348.5622` on both. The observed values and artifact hashes are recorded in `docs/evidence/2026-10-11-bitmap-characterization-infrastructure.md`. These are candidate outputs; no reviewer-approved golden snapshot exists yet. The scope does not cover other image formats, app UI integration, or cross-platform runtime behavior.

### Windows Bitmap to Portable PixelBuffer adapter
`.github/workflows/windows-bitmap-adapter.yml` keeps the legacy and portable checks separate. On Windows it builds and runs the net48 characterization harness against the production `VideoReaderBitmap`, then passes its versioned JSON report and hashed native/RGBA pixel files into the net10-windows adapter test executable. The latter reconstructs a short-lived GDI+ Bitmap from the captured actual-reader bytes and checks the adapter output against the independent logical RGBA capture; it also runs authored channel, alpha, padding, signed-stride, unsupported-format and lifetime cases. This transport tests the adapter against output produced by the real reader; it is not a shared in-process Bitmap and does not independently approve the candidate oracle. Linux and Windows separately run the `Kinovea.PortablePixels` contract tests.

The adapter currently supports only `Format24bppRgb`, `Format32bppRgb`, `Format32bppArgb` and `Format32bppPArgb`. It explicitly reorders BGR/BGRA into RGB24/RGBA32, drops the unused fourth byte for 32bpp RGB, and preserves straight versus premultiplied alpha without conversion. Its actual-reader comparisons establish the tested capture dimensions and logical RGB rows, plus straight alpha for ARGB captures. Direct authored tests establish channel mapping, alpha bytes/labels, positive and negative input strides, unlock behavior and source/output lifetime on the tested Windows runner. EXIF/mirror semantics, timestamp integration, unsupported GDI+ formats beyond the indexed negative case, Linux decoding and production frame/cache integration remain `NOT_ESTABLISHED`. See [adapter evidence](evidence/windows-bitmap-adapter-v1.md).

Latest full workflow run [38189832385](https://github.com/jvteodoro/Kinovea/actions/runs/38189832385), source SHA `e02fc457bbf371a456313e6ad59eb89135f6cfc8`, passed the net48 reader (19 cases/537 assertions), Windows adapter (38 checks/29 reader captures plus fixture hash validation), and the portable core (10 checks on Ubuntu and 10 on Windows). The run's source hash manifest includes all 29 configured code/workflow inputs with no missing paths. Intermediate target-framework and fixture-count/path corrections and failed runs are described in the evidence record.

For the additive PNG eXIf characterization, Windows Bitmap adapter workflow [38192763320](https://github.com/jvteodoro/Kinovea/actions/runs/38192763320) passed 40 adapter checks including 31 actual-reader captures; Portable Pixel Core passed 10/10 on Linux and Windows. Reader workflow [38192763379](https://github.com/jvteodoro/Kinovea/actions/runs/38192763379) passed 1,322 reader assertions on each OS, actual .NET Framework 4.8 reader characterization (20 cases/578 assertions), and 2,419 fresh-capture comparison assertions. Both workflows tested source SHA `8b75d148725762942f24ffe2c7c4624d4ad88294`. Per-fixture results and limits are in [reader evidence](evidence/portable-image-reader-v1.md).

## Portable BMP/PNG reader

`tests/PortableImageReader.Tests` runs a .NET 10 console assertion suite against the separate `Kinovea.PortableImageReader` library. Linux and Windows jobs verify immutable candidate BMP/PNG binary inputs and outputs plus independently authored expected pixel formulas. Tests cover transparent and partial-alpha pixels, a generated 24-bit BMP with row padding, errors, and output/file lifetime. A generated PNG eXIf orientation-6 case is compared with a fresh actual .NET Framework 4.8 reader capture. That Windows run observed that GDI+ does not expose the PNG orientation property and retains 13x9 dimensions and original raster order. This establishes the one fixture's result only; other PNG eXIf variants remain unverified. The old candidate directory remains unchanged and `CANDIDATE_NOT_APPROVED`.

### Portable reader shell navigation

`tests/PortableReaderShell.Tests` executes the same synchronous BMP/PNG navigation scenarios on Linux and Windows .NET 10. It covers initial empty frame, open metadata, first/sequential selection, repeated-current wrapper identity, forward/backward navigation, working-zone limits, PlayerRequest steps, close/reopen, corrupt-image recovery and repeated disposal. Windows CI first runs the actual net48 `VideoReaderBitmap` harness to produce `reader-state-traces.v1.json`, then supplies that trace to the shell test with `--legacy-trace`. The trace remains candidate evidence, not an approved golden. The test suite checks schema and selected actual behavior; explicitly documented safe differences are tested separately: idempotent close-before-open, no synthetic 4x4 corrupt-image frame, and fresh frame data after reopen at a stale timestamp. This shell remains detached from caches and does not establish cache ownership safety.

Local offline commands (Gentoo, SDK 10.0.111):

```sh
dotnet build tests/PortableReaderShell.Tests/PortableReaderShell.Tests.csproj -c Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -m:1
dotnet tests/PortableReaderShell.Tests/bin/Release/net10.0/PortableReaderShell.Tests.dll
```

Windows/Linux execution is recorded in [PortableReaderShell v1 evidence](evidence/portable-reader-shell-v1.md): final workflow `38243254812`, source SHA `fc007d36afe27a579666e5783b5a70d8b291aa0c`. The actual Windows reader passed 22 cases/644 assertions; the shell trace replay passed 411 assertions across navigation, failed-open recovery from both unopened and already-open states, and close-before-open observations. Its JSON trace SHA-256 is `a7372df8e03e916b49a33c08fdbedb52a27ce37ccb3959f7fc9e7cc101259697`. A warmed single-operation allocation sample measured 0 bytes for repeated-current and 80 bytes for changed-timestamp on both OSes. These are candidate results, not golden approval.

Run locally with:

```sh
dotnet build tests/PortableImageReader.Tests/PortableImageReader.Tests.csproj --configuration Release -m:1
dotnet tests/PortableImageReader.Tests/bin/Release/net10.0/PortableImageReader.Tests.dll --measure
dotnet run --project tests/PortablePixels.Tests/PortablePixels.Tests.csproj --configuration Release
```

The reader suite reports fixture/pixel comparisons individually and must not be summarized as overall Bitmap reader parity. Reader timing and managed allocation numbers are descriptive samples only. See [decoder decision](adr/0004-portable-bmp-png-decoder.md) and [versioned evidence](evidence/portable-image-reader-v1.md).


### Portable frame contract design

The minimal parallel migration proposal is in [ADR 0003](adr/0003-portable-frame-representation.md), with production source checks and review traceability in [the design evidence](evidence/2026-10-11-portable-frame-contract-design.md). It keeps existing Windows frame APIs and behavior while a small `net10.0` buffer contract and synthetic producer are tested independently. Two test categories must remain distinct:

- **Legacy compatibility regression:** execute the actual .NET Framework 4.8 reader/consumers on Windows. Strengthen raw channel-order, partial-alpha/premultiplication, independent EXIF raster, timestamp-boundary, generator/cache disposal, and exporter lifetime cases. Existing runs 38175088092/38175219336 reported 19 cases/537 assertions; candidate status is `PENDING_HUMAN_APPROVAL`, and those assertions do not yet independently establish all listed semantics.
- **Portable contract conformance:** exercise only the new contract on Linux and Windows with deterministic synthetic buffers. Check exact channel bytes, declared alpha modes, positive/negative stride plus row-zero offset, padding, bounds/overflow, and disposal lifetime. Passing this suite does not establish legacy parity.

No test from this design task ran. Linux reader parity remains `NOT_RUN`; do not conflate portable buffer tests with legacy compatibility.

### Portable image frame generator

`tests/PortableFrameGenerator.Tests` checks the standalone `net10.0` generator on Linux and Windows. It verifies open/generate/close/reopen/dispose states, `long` timestamp and previous timestamp values, shared decoded storage across frame wrappers, BMP/PNG dimensions, format/alpha labels, and deterministic logical pixels. Windows CI separately executes the real net48 `VideoReaderBitmap` characterization and compares both BMP/PNG outputs with the new generator. Those actual-reader results remain candidate comparisons and must be reported separately from portable contract tests.

The legacy characterization also verifies request boundaries, repeat-frame identity, close/reopen behavior, `DisposePrevious`, and the default `SingleFrame` eviction hazard. This is not cache integration approval: the portable generator is synchronous and standalone. It does not implement legacy `IFrameGenerator`, the reader shell's `[0,249]` range, or any cache ownership callback. Concurrent access, asynchronous frame retention and consumer/cache integration are unsupported.

### Portable SingleFrame ownership boundary

`tests/PortableSingleFrame.Tests` exercises `Kinovea.PortableSingleFrame` against the existing `PortableReaderShell` on Linux and Windows .NET 10. It verifies that cache `Clear` and cache `Dispose` only detach the slot, while reader `Close` invalidates new pixel access; it also checks timestamp/wrapper identity, storage reuse, working-zone results, failed-open cleanup, reopen, disposal, and rejected use after adapter disposal. The cache is non-owning and does not copy pixels. Its own `Close` clears before closing the supplied reader. `Dispose` leaves that caller-owned reader open, which the caller must close separately.

The Windows job first runs the actual .NET Framework 4.8 characterization harness against `VideoReaderBitmap`, `FrameGeneratorImageFile`, and `SingleFrame`. It uploads `single-frame-cache-traces.v1.json` and feeds it to the Windows .NET 10 suite for schema/behavior checks. The trace is candidate evidence, not approved golden data. The generator-owned Bitmap eviction defect is an isolated contract probe, not the production still-image reader flow. A broader source search found that `VideoReaderFFMpeg.cpp` constructs `SingleFrame`, `PreBuffer2`, and `Cache` with a native-buffer disposer; `VideoReaderGIF` constructs and uses `Cache`. See [production cache lifetime audit](evidence/production-video-cache-lifetimes-v1.md). This does not establish full FFmpeg playback behavior.

Supported synchronous lifetime: open through the adapter, navigate/read while the shell stays open, call cache `Clear` before dropping a cached reference, and use adapter `Close` to clear before releasing reader storage. Do not retain frames or borrowed `ReadOnlyMemory<byte>` beyond reader close. Calling the supplied shell's `Close` directly with a populated cache slot is unsupported; the wrapper remains cached but new pixel access throws `ObjectDisposedException`. Async consumers, `Cache`, `PreBuffer2`, and production playback are not integrated.

Local commands and results, ownership-option comparison, trace provenance and hashes are in [Portable SingleFrame cache evidence](evidence/portable-single-frame-cache-v1.md). GitHub Actions run [38247808240](https://github.com/jvteodoro/Kinovea/actions/runs/38247808240), source SHA `1762851bbeffd556452a46aa0c29b8fcf4184700`, passed the portable pixel/image/frame/shell/cache suites on Ubuntu 24.04 and Windows Server 2022. The actual net48 characterization passed 23 cases/674 assertions; the SingleFrame trace comparison passed 14 assertions. These results cover the reader and the direct SingleFrame probe, not FFmpeg or GIF playback.

### Production cache and consumer lifetime characterization

Keep cache tests separate from portable PixelBuffer contract tests and legacy image-reader tests. The PR #9 harness directly calls the actual `Kinovea.Video.Cache` and `PreBuffer2` implementations, recording add/acquire/trim/purge/shutdown behavior, disposal callbacks, current-frame identity and Bitmap usability. Windows run [38251549851](https://github.com/jvteodoro/Kinovea/actions/runs/38251549851), source `cbc3c16a7ad811e00b9b73cf153f1796f2d9adfa`, passed the net48 suite (24 cases/739 assertions); the production cache contract case passed 65 assertions. It observed the full-capacity `PreBuffer2.ForceAdd` eviction without a disposer call. This harness does not instantiate the FFmpeg C++/CLI reader or GIF reader, and it does not claim application playback. The inherited `SingleFrame` case contains a stale C#-only call-site observation; it is metadata, not an assertion, and the source-backed correction is documented in [production cache lifetime audit](evidence/production-video-cache-lifetimes-v1.md). That evidence includes the trace and complete downloaded artifact hash manifest. Actual FFmpeg/GIF playback and native AVBufferRef release remain `NOT_RUN`.

Local Linux commands (SDK 10.0.111):

```sh
dotnet build tests/PortableFrameGenerator.Tests/PortableFrameGenerator.Tests.csproj -c Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -m:1
dotnet tests/PortableFrameGenerator.Tests/bin/Release/net10.0/PortableFrameGenerator.Tests.dll
```

Windows workflow [38238423701](https://github.com/jvteodoro/Kinovea/actions/runs/38238423701), source SHA `3a40ecb06a56d565381efea8ad9567de82fb6031`, passed the portable generator (617 assertions), portable reader (1,322 assertions), and Pixel Core (10/10) on Ubuntu 24.04 and Windows Server 2022. On Windows, the actual net48 reader lifecycle suite passed 21 cases/621 assertions, and generator comparison against fresh actual-reader BMP/PNG captures passed 1,233 assertions. This comparison is limited to those two fixtures and the candidate evidence; no approved baseline, full playback parity, or cache integration is established. See [frame generator evidence](evidence/portable-frame-generator-v1.md).

## Experimental .NET 10 still-image host

`tests/PortableStillImageHost.Tests` is a `net10.0` diagnostic/test executable over the existing `PortableReaderShell`. Its assertion mode checks BMP/PNG dispatch, open/current/navigation/close, dimensions, timestamps, format and alpha labels, authored pixel values, repeated-current wrapper identity, shared decoded storage, and corrupt/unsupported inputs. Its `--host <path> [timestamp]` mode performs the same synchronous workflow and emits JSON containing source SHA, timestamps, dimensions, format, alpha mode, a logical-pixel SHA-256, an authored pixel probe, repeated wrapper identity and close invalidation. No second navigation or decoding implementation is introduced.

The Windows-only net48 characterization executable uses actual `VideoTypeManager` and `VideoReaderBitmap`; it does not stand in for `FrameServerPlayer` or WinForms. Run [38254889523](https://github.com/jvteodoro/Kinovea/actions/runs/38254889523), source SHA `f4ce321accb78789f714d8f72d3a2ea75a6a5223`, passed 47 assertions in that path. The same-run comparison passed BMP/PNG open status, dimensions, first timestamp pair, authored logical pixel probe, identity reuse within each process, and access invalidation after close. The combined run also passed portable host tests on Ubuntu 24.04 and Windows Server 2022 (43 host assertions per OS), plus existing generator/shell/cache/reader/pixel suites. Local Linux net48 remains `NOT_RUN` because targeting assemblies are absent. This is a narrow reader-path comparison, not UI or end-to-end playback evidence. Full commands, environment, trace/results, and artifact digests are in [host evidence](evidence/net10-still-image-host-v1.md).
### Wine desktop functional and visual observations

`tools/wine-oracle/` runs the installed Kinovea under a verified local Wine prefix using a disposable OverlayFS copy. Versioned scenario JSON separates fixture hashes, actions, expected observations, screenshots and limitations. It requires an interactive KWin/Xwayland desktop and captures only the Kinovea X11 window; it is not a headless CI test. Use `WINE_OBSERVED` only for the named, manually reviewed property. Keep open route, presentation, playback controls and navigation separate; screenshots do not establish decoded pixels, alpha, exact frame timing or Windows parity. Still-image open scenarios must check the visible player header after the dialog returns. A dialog closing or process exit alone is not a successful image open.

The v1 run used base SHA `8b73d5440908d5df954fda0e255fa3d79ed5694b`; its runner/scenario SHA was `214137a96d5c14703ecc3d2fb26b1c66e1aa640f`. A screenshot review correction established that the candidate BMP and PNG opened in player screens; the previous statement that those windows were blank was wrong. On source SHA `d4b3574892e133d0a1e322d1d4fd5a6169e8e4e4`, five local scenarios completed and passed 10 header OCR assertions: fitness PNG 276x398, generated PNG 1280x720, generated BMP 640x480, candidate BMP 7x5, and candidate PNG 13x9. `Ctrl+O` and the File menu sequence `Alt`, `Home`, `Enter`, `Enter` both opened images. All five headers showed `25.00 fps`; candidate 7x5/13x9 dimensions and fps match the Windows candidate characterization for those fixture hashes only. The candidate remains unapproved. See the [corrected Wine v1 record](evidence/wine-functional-oracle-v1.md), [v1 machine manifest](evidence/wine-functional-oracle-v1.json), and [still-image v2 manifest](evidence/wine-still-image-gui-v2.json).

Still-image limitations are explicit: right/left keys changed the file-browser selection rather than a verified player frame; Ctrl+F4 left the player header visible; the committed click guard requested `(1904,104)` and observed `(486,520)`, then refused to send a click. Player navigation, close/reopen, zoom, exact scaling, pixel values, alpha and EXIF remain `NOT_ESTABLISHED`. Internal file-browser double-click and drag/drop are source-inspected but `NOT_RUN`. Host `/tmp` inputs failed to produce a player in this prefix; identical, hash-verified fixtures under the home test-data directory did open. The cause is unknown. Wine source/binary correspondence is still `NOT_ESTABLISHED` (`2025.2.0.0` installed vs. `2026.1.0.0` checkout).

No GitHub workflow was added: this oracle requires a local installation, prefix and live desktop; hosted runs cannot reproduce those safely. The workflow files on this branch do not match `tools/wine-oracle/**` or the documentation paths, so this PR does not consume GitHub Actions minutes. Wine observations remain independent of Windows .NET Framework characterization and future native .NET 10 tests.

### Native still-image desktop presentation and GUI smoke

`src/Kinovea.PortableDesktop` is a small Avalonia 12.1.4 `net10.0` executable. `Kinovea.PortableDesktop.Presentation` owns the synchronous reader session, PixelBuffer-to-WriteableBitmap boundary, and double-precision image/viewport transform. `tests/Kinovea.PortableDesktop.Tests` checks raw destination rows, RGB/RGBA format and alpha labels, presentation lifetime, open/failure/step/repeat/seek/close/reopen, working-zone limits, and image/viewport round trips including anchored zoom. This is portable presentation conformance, not a Windows Bitmap reader regression suite.

On Gentoo, local-only scenario `tools/native-player/gui-interactions.py` launches the actual Avalonia process and uses its real `IStorageProvider` dialog. It selects a shared 1280×720 PNG, an authored 640×480 BMP, and a corrupt PNG; exercises Next/Previous, picker cancellation, invalid replacement retention, Ctrl+W close, same-process PNG/BMP reopen, and clean exit. The independent `tools/native-player/viewport-gestures.py` sends actual wheel and drag input, clicks Fit using reported control bounds, resizes the process-owned Xwayland window, then opens a new PNG after gestures and checks viewport reset. It samples known RGB/orientation landmarks and transparent/partial-alpha rendering from captured app-window screenshots. Scenario inputs are versioned under `tools/native-player/scenarios/`; outputs and screenshot hashes are in [native GUI interaction evidence](evidence/native-player-gui-interactions-v1.md).

The adapter copies logical PixelBuffer rows once into an independently owned `WriteableBitmap`; it retains no borrowed view or native pointer after conversion. Frame navigation adds no presentation copy. Session tests verify that a failed image replacement retains the current frame wrapper and presentation bitmap, while successful replacement and close dispose prior reader pixels and Avalonia resources. Actual file-picker cancel/reopen and viewport interactions now pass on this Gentoo desktop. Zoom-anchor math uses Avalonia's reported pointer location because the requested Xwayland coordinate differed in some runs. This is a local Linux GUI result, not Windows behavior, decoder pixel parity, or a universal screenshot comparison.

Local Linux x64 commands:

```sh
dotnet restore src/Kinovea.PortableDesktop/Kinovea.PortableDesktop.csproj -r linux-x64
dotnet build src/Kinovea.PortableDesktop/Kinovea.PortableDesktop.csproj -c Release -r linux-x64 --no-restore
dotnet restore tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj -r linux-x64
dotnet build tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj -c Release -r linux-x64 --no-restore -m:1
DISPLAY=:1 WAYLAND_DISPLAY=wayland-0 XDG_RUNTIME_DIR=/run/user/1000 dotnet tests/Kinovea.PortableDesktop.Tests/bin/Release/net10.0/linux-x64/Kinovea.PortableDesktop.Tests.dll
DISPLAY=:1 WAYLAND_DISPLAY=wayland-0 XDG_RUNTIME_DIR=/run/user/1000 python3 tools/native-player/smoke.py --assembly src/Kinovea.PortableDesktop/bin/Release/net10.0/linux-x64/Kinovea.PortableDesktop.dll --input <fixture> --output <local-evidence-dir> --source-sha <git-sha> --exercise-navigation-close
```

No hosted workflow ran. The reader package was aligned from SkiaSharp 4.153.1 to Avalonia's 3.119.4 backend because the managed/native ABI versions cannot coexist in this process. Linux reader tests passed after the change; Windows remains `NOT_RUN`. The draft PR HEAD uses `[skip ci]` to keep hosted Actions idle. See [native player v1 evidence](evidence/native-still-image-player-v1.md).

Video playback requires a separate regression path from the still-image shell. A decoder probe must compare timestamps from decoded frames (including `best_effort_timestamp` and stream timebase), dimensions, logical pixel values/channel order, row orientation, corrupt-input result and resource release against the actual Windows `VideoReaderFFMpeg` on the same synthetic fixture. Test the decoder first as a synchronous console boundary; it does not establish playback pacing, pause/seek, asynchronous prebuffering, cache ownership or UI-thread publication. Those require separate tests and ownership review before connecting a frame producer to Avalonia. The 2026-10-22 local assessment found no video file fixture in the checkout and ran no native Kinovea video decode; see [video playback boundary assessment](evidence/native-video-playback-boundary-v1.md).
