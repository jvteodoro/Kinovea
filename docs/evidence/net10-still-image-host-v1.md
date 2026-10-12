# .NET 10 still-image host v1

Status: **Local Linux prototype verified; Windows CI and legacy reader comparison pending.**

## Base and provenance

- Repository: `jvteodoro/Kinovea`.
- PR #9 merged to `port/net10-linux` at `8b73d5440908d5df954fda0e255fa3d79ed5694b` (merge parent includes audit commit `28ac15a32306637610aa899a725e89657731fd86`).
- Proposed task branch: `design/net10-still-image-host`.
- Existing Windows Bitmap outputs remain candidate evidence, not reviewer-approved golden data.
- Architecture was proposed before implementation in commit `b11fc586a13a98238c63fd0540b9e559d0dd161c`. Runtime results and artifact hashes are recorded below and will be extended with the single PR-triggered workflow run.

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
| New .NET 10 host | Local Linux `PASS`; Windows CI pending | Same assertion command and BMP/PNG CLI fixtures on Linux and Windows; capture PR source SHA, SDK/runtime, totals, logs and JSON hashes. |
| Actual Windows reader selection/open/current | `NOT_RUN` locally; Windows CI pending | Windows net48 harness invokes actual manager and reader; compare status, timestamp, dimensions, authored pixel probes, repeat identity within each process and close. |
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
