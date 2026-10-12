# Verification: Windows GDI+ to Portable PixelBuffer adapter v1

## Architecture and scope

The adapter targets `net10.0-windows10.0.17763.0`, declaring Windows 10.0.17763 (Windows 10 version 1809) as its minimum OS/API target. The .NET 10 SDK rejected `10.0.14393.0` as an invalid target platform version; its valid list included `10.0.17763.0`. Microsoft's [.NET 10 Windows support table](https://learn.microsoft.com/en-us/dotnet/core/install/windows) lists Windows 10 version 1809 LTSC/Enterprise among supported versions. It accepts an existing `System.Drawing.Bitmap`, reads synchronously through `LockBits`, copies/reorders pixels into an independently owned `PixelBuffer`, and releases the lock before returning. It never disposes or mutates the source Bitmap and does not retain `Scan0` or any GDI+ memory.

The framework boundary is Option A: the existing .NET Framework 4.8 characterization executable runs the production `Kinovea.Video.Bitmap.VideoReaderBitmap`; its versioned JSON and hashed pixel files are then consumed by the separate .NET 10 Windows process. The adapter tests reconstruct a Bitmap from the reader's captured native bytes and compare the adapter output with the same observation's independent logical RGBA (`GetPixel`) output. This is actual-reader-produced data, not a shared Bitmap object. The candidate baseline remains `CANDIDATE_NOT_APPROVED`; no approved golden data was changed.

The adapter supports exactly these GDI+ formats:

| GDI+ source | Observed storage / handling | Portable output |
|---|---|---|
| `Format24bppRgb` | B,G,R bytes; source stride may include padding | tightly packed RGB24, `AlphaMode.None` |
| `Format32bppRgb` | B,G,R,unused byte; fourth byte is discarded | tightly packed RGB24, `AlphaMode.None` |
| `Format32bppArgb` | B,G,R,A bytes; alpha retained as straight | tightly packed RGBA32, `AlphaMode.Straight` |
| `Format32bppPArgb` | B,G,R,A bytes; premultiplied colors retained as-is | tightly packed RGBA32, `AlphaMode.Premultiplied` |

Other formats fail with `NotSupportedException`. No format normalization, alpha conversion, codec, cache, reader, shared-frame API or portable-core modification was introduced.

## Verification source and environment

- Branch: `port/windows-bitmap-adapter`
- Final code and source-hash CI SHA: `e02fc457bbf371a456313e6ad59eb89135f6cfc8`
- Final CI run: [38189832385](https://github.com/jvteodoro/Kinovea/actions/runs/38189832385)
- Runner: `windows-2022`, image `20261004.326.1`, Windows Server 2022 build `10.0.20348`, x64.
- Visual Studio MSBuild: `17.14.60.43110`; .NET Framework 4.8 targeting pack and `System.Drawing.dll` reference assemblies were detected.
- Legacy process: CLR `4.0.30319.42000`; GDI+ `10.0.20348.5622`; .NET Framework release key `533325`.
- .NET 10 SDK: `10.0.401`; host runtime `10.0.12`; adapter reference `System.Drawing.Common` `10.0.0`.
- Portable core also ran on `ubuntu-latest` / Ubuntu 24.04 with .NET SDK `10.0.401` and runtime `10.0.12`.

Commands executed by CI:

```powershell
./scripts/bitmap-characterization/prepare-artifacts.ps1 -ArtifactsPath artifacts/bitmap-adapter
nuget restore Kinovea.Services/packages.config -PackagesDirectory packages -NonInteractive -Verbosity normal
msbuild tests/Bitmap.Characterization/Kinovea.Bitmap.Characterization.csproj /t:Rebuild /m /nologo /verbosity:minimal /p:Configuration=Release /p:Platform=x64
tests/Bitmap.Characterization/bin/x64/Release/Kinovea.Bitmap.Characterization.exe --output artifacts/bitmap-adapter/legacy-capture
dotnet run --project tests/WindowsBitmapAdapter.Tests/WindowsBitmapAdapter.Tests.csproj --configuration Release -- --legacy-capture artifacts/bitmap-adapter/legacy-capture --measure
dotnet run --project tests/PortablePixels.Tests/PortablePixels.Tests.csproj --configuration Release -- --measure
```

## Results

| Boundary/property | Result | Evidence and limit |
|---|---|---|
| Actual legacy reader execution | PASS | Net48 harness executed `Kinovea.Video.Bitmap.VideoReaderBitmap`: 19 cases, 537 assertions, zero failures. These remain characterization results and candidate evidence. |
| Adapter test suite | PASS | 38 checks passed, zero failed; 29 reader-generated pixel captures compared and all 14 input fixture hashes validated. Includes direct authored sentinels and capture conversion. |
| Portable Pixel Core | PASS | 10/10 contract tests on Ubuntu 24.04 and Windows Server 2022. |
| Dimensions | PASS for compared captures | Adapter output dimensions equal all 29 captured observations, including the legacy EXIF cases' post-reader dimensions. Does not prove every EXIF transform. |
| Logical RGB channel values | PASS for compared captures | Every output RGB channel compared with the per-capture RGBA output emitted by the legacy harness; direct fixtures independently identify BGR source bytes. The reader-generated expected values are not an independently approved golden. |
| Straight alpha | PASS for tested cases | Direct transparent/partial `Format32bppArgb` sentinels and actual-reader ARGB captures retain expected alpha. |
| Premultiplied alpha | PASS for direct adapter sentinel | `Format32bppPArgb` authored test verifies premultiplied channel bytes and label are preserved. Actual-reader PArgb behavior is not established. |
| Row padding and orientation | PASS for tested cases | 24bpp padded input and externally backed negative-stride Bitmap have asymmetric sentinels and pass. Reader outputs are compared in their recorded logical top-to-bottom row order. |
| Source ownership/output lifetime | PASS for tested cases | Source remains readable after conversion; output remains readable after source disposal. Output repeated sequential disposal follows core contract. |
| Unlock on success/failure | PASS for tested cases | Bitmap can be locked again after conversion; injected undersized owner forces a conversion failure while locked, followed by successful relock. This tests cleanup path deterministically. |
| Unsupported format | PASS for indexed negative case | 8bpp indexed input fails explicitly and source remains usable. Other unsupported formats are not individually enumerated. |
| EXIF/mirror behavior | NOT_ESTABLISHED as an adapter contract | The adapter receives already-loaded Bitmap data and does not inspect EXIF. Captures include orientation inputs after the legacy reader, but mirror-specific expected rasters are not independently authored. |
| Timestamps/reader timing | NOT_APPLICABLE to this conversion API | Adapter has no timestamp parameter and is not connected to `VideoFrame` or `IFrameGenerator`; reader timing remains in the legacy harness only. |
| Linux Bitmap decoding | NOT_RUN / out of scope | No Linux decoder or reader integration was selected. The portable memory core is separately tested on Linux. |
| Production integration/parity | NOT_ESTABLISHED | Existing readers, frame APIs, caches, UI and FFmpeg are unchanged; no end-to-end app path uses this adapter. |

The reader run recorded 14 deterministic fixture inputs (BMP/PNG/JPEG, EXIF orientations 1–9, malformed PNG, and unsupported GIF) and 59 native/RGBA pixel files. The adapter accepted the 29 actual-reader captures exposed by frame/reopen, rotation and thumbnail observations. Fixture bytes and pixel files are retained as run artifacts rather than copied into the approved baseline directory.

SHA-256 provenance from the downloaded artifact:

- `legacy-capture/fixture-manifest.json`: `77e26be647a780534d020034ba617ef22a0d908667404a992f793b2b874eb1cb`
- `legacy-capture/test-results.json`: `7d915d36160f8cc10439ee0101deba1b354b51862eacdbdb06efb71d06877861`
- `source-hashes.json`: `b4c3982a2a8f0f554e497527c8ce4d41b69d668f092f34b61527ef1c596b3d18` (29 source entries, none missing)
- `environment.json`: `237a9e158a21b23ad82beaef3f178bb14f422b7d05deb2ef6193194c1aa2e352`
- `legacy-capture/runtime-metadata.json`: `220603c88a74a65cb4e5c68dd2423c9c078175eba0752ebc3c7fabd6a5f40b7f`
- The fixture manifest contains each of the 14 input file SHA-256 values. `test-results.json` contains SHA-256 for each native and RGBA pixel output. Source hashes, environment, runtime metadata, MSBuild logs/binlog, adapter output and portable job logs are uploaded under the linked run's artifact `windows-bitmap-adapter-38189832385-1`.

Local Linux commands on Gentoo with .NET SDK 10.0.111: portable core command above passed 10/10. Building `src/Kinovea.PortablePixels.GdiPlus/Kinovea.PortablePixels.GdiPlus.csproj --configuration Release --no-restore` passed with 0 warnings/errors using Windows targeting. Building the whole Windows adapter test project on Linux was attempted, but MSBuild failed during `GetTargetFrameworks` project-reference discovery with `Build FAILED` and 0 warnings/0 errors. No Windows GDI+ test was run locally; CI built and executed the same test project successfully on Windows. This local MSBuild graph result is recorded without treating it as a test failure or a pass.

## Allocation and conversion observations

The adapter creates one destination byte array per frame plus a row scratch array and small owner/PixelBuffer objects. It copies each GDI+ row into the reusable per-conversion scratch array with `Marshal.Copy`, then reorders/writes that row once into the destination. There is one full-frame destination materialization and no second full-frame intermediate buffer. GDI+ internal temporary conversion/locking allocations are not observable here.

Each `windows-2022` run warmed up each case three times and recorded seven timed conversions per representative size. The 640×480 managed allocation measurement was 923,747 bytes/conversion in the recorded runs; the 1920×1080 measurement ranged from 8,302,307 to 8,302,437 bytes/conversion (130-byte observed variation). Medians varied as shown. These are observations, not throughput guarantees:

| Input | Destination payload | Median run 38188229081 | Median run 38189020823 | Median run 38189170445 | Median final run 38189832385 |
|---|---:|---:|---:|---:|---:|
| 640×480 `Format24bppRgb` | 921,600 B | 16.972 ms | 17.380 ms | 17.118 ms | 15.550 ms |
| 1920×1080 `Format32bppArgb` | 8,294,400 B | 116.744 ms | 116.098 ms | 114.280 ms | 105.081 ms |

The raw seven-sample arrays and per-run managed allocation measurements are in each uploaded `adapter-tests.log`. These short hosted-runner series do not isolate GDI+ lock cost or distinguish runtime noise from adapter cost; repeat with a controlled benchmark before any performance decision.

## Limitations and next gate

Windows-only adapter execution passed, but this does not mark the reader `PORTED` or `REGRESSION_VERIFIED`, approve the Bitmap candidate baseline, or show cross-platform compatibility. The workflow's first run failed to resolve its MSBuild action (`38188120691`, `38188133096`); later runs failed on target-platform metadata (`38188701325`, `38188866089`, `38188869090`). After targeting SDK-valid Windows 10.0.17763, the fixture provenance check exposed two incorrect assumptions: the manifest has 14 files and stores fixture basenames under `fixtures/` (`38189533763`, `38189681765`). Correcting count and path resolution produced the passing full run (`38189832385`). Missing evidence includes independent mirror-transform rasters, actual PArgb reader output, additional GDI+ input formats, reader/frame timestamp integration, and production consumer lifetime behavior. Before integration, design a narrow opt-in call site and add tests for its ownership bridge; keep asynchronous retention and cache behavior out of scope pending a separate lifetime review.
