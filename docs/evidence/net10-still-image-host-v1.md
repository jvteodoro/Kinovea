# .NET 10 still-image host v1

Status: **Portable host tested on Linux/Windows; scoped Windows legacy reader comparison passed.**

## Base and provenance

- Repository: `jvteodoro/Kinovea`.
- PR #9 merged to `port/net10-linux` at `8b73d5440908d5df954fda0e255fa3d79ed5694b` (merge parent includes audit commit `28ac15a32306637610aa899a725e89657731fd86`).
- Proposed task branch: `design/net10-still-image-host`.
- Existing Windows Bitmap outputs remain candidate evidence, not reviewer-approved golden data.
- Architecture was proposed before implementation in commit `b11fc586a13a98238c63fd0540b9e559d0dd161c`. Test implementation commit: `f55e7e3c`; focused PowerShell correction: `f4ce321a`. The passing CI head source SHA is `f4ce321accb78789f714d8f72d3a2ea75a6a5223`.

## Production-equivalent source path

```text
RootKernel
  -> VideoTypeManager.LoadVideoReaders(registered VideoReader types)
  -> FrameServerPlayer.Load(path)
  -> VideoTypeManager.GetVideoReader(Path.GetExtension(path))
  -> VideoReaderBitmap.Open(path)
  -> FrameGeneratorImageFile.Open / System.Drawing.Bitmap
  -> VideoReaderBitmap.MoveRequest(first=true) / Current VideoFrame
  -> FrameServerPlayer.CurrentImage (filter current image, else reader.Current.Image)
  -> PlayerScreenUserInterface2 surface paint
```

`VideoReaderBitmap.Open` initializes metadata and `[0,249]` working zone but leaves `Current` empty until navigation. First sequential request selects timestamp 0; with one timestamp per frame, `PreviousTimestamp` is -1. `FrameGeneratorImageFile` owns and reuses its GDI+ image; close disposes it. `FrameServerPlayer.CurrentImage` returns the current reader Bitmap unless a filter supplies its own image. These are source findings, with navigation and object lifecycle separately covered by Windows candidate characterization. No claim is made that the candidate fixture set is approved.

## Dependency matrix

| Area | Current target and dependencies | Host consequence |
|---|---|---|
| `RootKernel` / reader registration | Legacy Windows WinForms executable, net48; explicitly registers Bitmap, FFmpeg, GIF and SVG reader classes (`Kinovea/Kernel.cs:136-145`). | Cannot be the net10 host entry point without moving its app dependency graph. |
| `VideoTypeManager` | net48 `Kinovea.Video`; extension registry constructs legacy `VideoReader` subtypes; default discovery uses `System.Windows.Forms.Application.ExecutablePath` (`VideoTypeManager.cs:34-131`). | The extension map algorithm is simple, but its type and discovery boundary are not a portable contract. Reusing it directly would require moving/retargeting legacy reader APIs. |
| `VideoReader` / `VideoFrame` / `IFrameGenerator` | net48; `VideoFrame.Image` and generator output are `System.Drawing.Bitmap`; `Kinovea.Video` references Services, Drawing and Windows.Forms. | Cannot be referenced from `net10.0` as-is. Preserve and test the old route separately. |
| `VideoReaderBitmap` / `FrameGeneratorImageFile` | net48 with GDI+ decode, EXIF/rotation and `Graphics` use. | Windows oracle only; do not alter or multi-target in this slice. |
| `FrameServerPlayer` and surface paint | ScreenManager net48, WinForms, GDI+, metadata/filters/export, and C++/CLI PlayerServer reference. `CurrentImage` exposes Bitmap. | Full controller and UI are not a minimal host dependency. Do not describe the CLI host as actual FrameServerPlayer integration. |
| Portable image path | .NET 10 `PortableImageReader` -> `PortableImageFrameGenerator` -> `PortableReaderShell` -> `PixelBuffer`. | Reuse unchanged. Shell provides already-characterized navigation and owns decoder/frame lifetime. |

Exact references and alternatives are formalized in [ADR 0005](../adr/0005-net10-still-image-host.md).

## Architecture selection

Proposed: one isolated in-process net10 command-line test host in `tests/PortableStillImageHost.Tests`. Its default mode tests `PortableReaderShell`; `--host <path> [timestamp]` performs the same open/current/navigation/pixel/close workflow as a CLI. The host delegates selection to `PortableImageReader`; it adds no duplicate selector or navigation API. No IPC is needed to exercise this portable workflow; the existing net48 app remains unchanged. A separate Windows net48 harness tests actual `VideoTypeManager` dispatch and `VideoReaderBitmap` open/current behavior. The actual `FrameServerPlayer` and WinForms consumer remain outside this test because referencing ScreenManager would pull in the wider legacy UI and C++/CLI graph.

## Test/evidence status

| Property | Status before implementation | Evidence required |
|---|---|---|
| Existing reader/shell builds and behaviors | Previously `PORTABLE_TESTED` on Linux/Windows in PR #5–#7 workflows | Re-run the host integration workflow on both OSes. |
| New .NET 10 host | `PASS` on Ubuntu 24.04 and Windows Server 2022 | Run `38254889523`; 43 assertions per OS plus successful BMP/PNG CLI flows. |
| Actual Windows reader selection/open/current | `PASS` for scoped direct API path | Same run; actual `VideoTypeManager` -> `VideoReaderBitmap` harness passed 47 assertions. Its separate comparison passed six properties for BMP and PNG. |
| FrameServerPlayer `CurrentImage` / WinForms drawing | `NOT_RUN` | Explicitly out of scope; do not infer from direct VideoReader tests. |
| Cross-platform production application integration | `NOT_ESTABLISHED` | Requires application host/runtime/UI strategy and a later real-consumer integration stage. |

## Local verification

- Host project: `tests/PortableStillImageHost.Tests/Kinovea.PortableStillImageHost.Tests.csproj`; target `net10.0`; references only the existing portable reader shell project.
- Environment: Gentoo Linux 2.18, `linux-x64`; SDK `10.0.111`, runtime `10.0.11`, MSBuild `18.0.11`.
- Build: `dotnet build tests/PortableStillImageHost.Tests/Kinovea.PortableStillImageHost.Tests.csproj --configuration Release -v:minimal -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -p:UseSharedCompilation=false -m:1` => exit 0, 0 warnings, 0 errors.
- Assertion run: `dotnet tests/PortableStillImageHost.Tests/bin/Release/net10.0/Kinovea.PortableStillImageHost.Tests.dll` => exit 0, `PASS (43 assertions)`.
- CLI smoke: `dotnet <host.dll> --host <fixture> 0` for BMP and PNG => both exit 0; timestamps `0/-1`; BMP 7x5 `Rgb24/None`, probe `(17,29,43,255)`, logical pixel SHA-256 `93d943244620afae6fb7dbe59b1f0eb2f1419d3361dd9bfdb70e5937311f5cc9`; PNG 13x9 `Rgba32/Straight`, probe `(54,46,134,64)`, logical pixel SHA-256 `3a4ceaa3b8027dbac77f815a02812145b57e41304d6a7efc962d2cbfd29cf9fe`. Both report repeat wrapper identity, closed state, and rejection of new pixel-memory access after close.
- Fixture input SHA-256: `bmp-rgb-7x5.bmp` `85e0feff83d9920703cbf8be2e6ccc76741da22b4ffe791d5d4c415724de1244`; `png-alpha-13x9.png` `e0759d696bfef305e0a2c1c8fc7b7673e9e46390ef15beb541679c6766041dcd`. They are existing deterministic, license-safe candidate fixtures; no golden was regenerated.
- Legacy harness attempt: `dotnet msbuild tests/StillImageHost.LegacyCharacterization/Kinovea.StillImageHost.LegacyCharacterization.csproj /t:Build /m:1 /nologo /verbosity:minimal /p:Configuration=Release /p:Platform=x64` => exit 1, `MSB3644`; this Gentoo host does not have .NET Framework 4.8 reference assemblies. Windows result remains pending.
- Source SHA for the implementation plus documentation commits and CI run, workflow IDs, Windows trace hash, comparison report hash and portable artifacts will be appended after CI. The legacy harness records `KINOVEA_SOURCE_SHA` supplied from the PR head rather than the synthetic pull-request merge SHA.

## Scope limits

The net48 characterization executable calls actual `VideoTypeManager` and `VideoReaderBitmap` APIs and uses the actual legacy project references; it is not a proxy reader. The portable executable reuses `PortableReaderShell`. The CI comparison does not instantiate `FrameServerPlayer`, paint a WinForms surface, or open the full desktop application. It establishes only the listed BMP/PNG still-image properties. Candidate fixture observations remain unapproved, and this task does not establish full reader parity or Linux UI integration.

## Windows/Linux CI evidence

- Workflow: [run 38254889523](https://github.com/jvteodoro/Kinovea/actions/runs/38254889523), workflow `Portable frame generator integration boundary`, run number 21, attempt 1. All three jobs passed. Pull request source SHA: `f4ce321accb78789f714d8f72d3a2ea75a6a5223` (the legacy trace embeds this PR head SHA; it is distinct from the synthetic PR merge SHA).
- Ubuntu runner: `ubuntu-24.04`, image `20261004.327.1`, runner `2.337.0`; .NET SDK `10.0.401`, runtime `10.0.12`, MSBuild `18.9.11`.
- Windows portable runner: Windows Server 2022, OS build `10.0.20348`, image `20261004.326.1`, runner `2.337.0`; .NET SDK `10.0.401`, runtime `10.0.12`, MSBuild `18.9.11`.
- Legacy runner: Windows Server 2022, OS build `10.0.20348`, image `20261004.326.1`; .NET Framework process CLR `4.0.30319.42000`, 64-bit. Runtime metadata reports GDI+ `C:\Windows\system32\gdiplus.dll`, version `10.0.20348.5622 (WinBuild.160101.0800)`; culture `en-US`.
- The legacy trace's `Environment.OSVersion` field reports `Microsoft Windows NT 6.2.9200.0`; this is the value observed inside that process. The runner log and environment capture identify the actual hosted image as Windows Server 2022, build `10.0.20348`.
- Suite results: portable host 43 assertions on each OS; portable frame generator 617 assertions on each OS; reader shell 65 and portable SingleFrame 73 on each OS; image reader 1,322 assertions on each OS; Pixel Core 10/10 tests on each OS. Windows legacy characterization passed 24 cases/739 assertions; new actual selection harness passed 47 assertions; legacy-to-generator pixel comparison passed 1,233 assertions; reader shell replay passed 411 assertions; SingleFrame trace comparison passed 14 assertions.
- Direct host/legacy comparison: both `bmp-rgb-7x5` and `png-alpha-13x9` passed open status, dimensions, first timestamp pair, authored pixel probe, repeat identity within each process, and close invalidation. Legacy selected `Kinovea.Video.Bitmap.VideoReaderBitmap`; corrupt PNG returned `NotSupported` and `Loaded=false`. Legacy close made `Bitmap.GetPixel` throw `System.ArgumentException`; portable close rejected new pixel-memory access with `ObjectDisposedException` (safe boundary behavior, not identical exception type).
- Input fixture SHA-256 values are in the Local verification section. Portable logical pixel SHA-256 values were identical in local Linux output and both CI OS logs: BMP `93d943244620afae6fb7dbe59b1f0eb2f1419d3361dd9bfdb70e5937311f5cc9`; PNG `3a4ceaa3b8027dbac77f815a02812145b57e41304d6a7efc962d2cbfd29cf9fe`.
- GitHub-reported artifact archive digests: [Ubuntu portable artifact](https://github.com/jvteodoro/Kinovea/actions/runs/38254889523/artifacts/11750050759), SHA-256 `4613e75d7de562d47aab85f2efb5a6952955f7e0947ba1c38d26caaf8a19123a`; [Windows portable artifact](https://github.com/jvteodoro/Kinovea/actions/runs/38254889523/artifacts/11749508492), SHA-256 `5030786022786131703bfc84cdea18fe29fc1293a2c7c29290dd5be29da7dcaf`; [Windows legacy artifact](https://github.com/jvteodoro/Kinovea/actions/runs/38254889523/artifacts/11749831210), SHA-256 `edf53c7e1b8481aaaf04658c61b13df698fcc7b1d839ff34aa01cf4c31169e2b`. Recomputing the downloaded Windows legacy ZIP locally matched its reported digest.
- Downloaded Windows legacy ZIP recomputation matched GitHub's SHA-256 digest. Its `legacy-selection/trace.v1.json` hash is `6c0c0ed6ce08b675522043235d09db07070d6bb2b4140eea6a696d4c7c50b2c8`; `still-image-host-comparison.v1.json` hash is `c50fc2f5692972499112ea608c98a4518692e50904b9fe5e73b1f80d09e70bde`; portable host BMP JSON hash is `d4177d7735410ff5c7ae00f51a41d13331e35814ae86b14743c9f2786d62a392`; PNG JSON hash is `f1b455a1789b4bf6d9c68a39df46f29e262610d31774aa9bb8aea424b5b8ade4`.
- Run `38254608554` was the first PR run at source SHA `f48bca0ec943e36b02847ac7a3befddffb9fa56c`. Its two portable jobs and 47-assertion net48 characterization passed; the comparison failed before performing comparisons because `$Host` is a PowerShell read-only automatic variable. The source fix was committed as `f4ce321a`; the corrected run above passed. The initial attempt is retained here as evidence rather than hidden.
