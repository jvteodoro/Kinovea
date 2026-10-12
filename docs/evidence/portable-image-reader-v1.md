# Portable BMP/PNG reader v1 evidence

Date: 2026-10-12  
Branch: `port/portable-image-reader`  
Base: `port/net10-linux` at merged PR #4, `8ed8666aae85ff8cf16d97cdd1629dc2043638ba`  
Legacy golden status: `CANDIDATE_NOT_APPROVED`

## Architecture and scope

`Kinovea.PortableImageReader` is an isolated `net10.0` library using SkiaSharp 4.153.1. The framework boundary follows Option A: the new reader has no reference to the .NET Framework 4.8 projects; a separate Windows .NET Framework characterization process runs the real `Kinovea.Video.Bitmap.VideoReaderBitmap`, writes versioned JSON and pixel files, and the .NET 10 tests compare against those files. The legacy process does not reference the `net10.0` assembly. No production reader, frame API, cache, timestamp, UI, FFmpeg, or call site changed.

The reader accepts `.bmp` and `.png` paths and validates the encoded format. Opaque images return owned RGB24 with no alpha; alpha images return owned RGBA32 with straight alpha. Skia decodes into RGBA8888/unpremultiplied storage. Conversion performs one full-frame copy from native decode storage into the final PixelBuffer owner, using a one-row managed scratch array and `Marshal.Copy` per row. EXIF coordinate transforms, when reported by Skia, are written directly into the final output. Native codec and bitmap resources are closed before returning.

The package set pins SkiaSharp plus Linux `NoDependencies` and Win32 native assets to 4.153.1. Linux was exercised on x64 glibc (Gentoo locally and Ubuntu 24.04 CI). Inspection of the loaded Linux x64 native library with `ldd` found `libc`, `libm`, `libdl`, `libpthread`, `librt`, `libstdc++`, and `libgcc_s`; this does not establish packaging on musl, other architectures, or Kinovea distribution systems.

ADR 0004 records the library comparison. ImageSharp's Six Labors Split License is not selected because the repository is GPL-2.0-only and its applicable Apache-2.0 grant raises a GPLv2 compatibility issue. SkiaSharp's MIT binding and Skia's BSD-3-Clause native license are compatible with GPLv2 subject to retaining notices. Sources and license links are in [ADR 0004](../adr/0004-portable-bmp-png-decoder.md).

## Source and environment

Final tested code SHA: `8b75d148725762942f24ffe2c7c4624d4ad88294`. The evidence-only follow-up commit does not change executable sources. Linux/Windows reader results are in [run 38192763379](https://github.com/jvteodoro/Kinovea/actions/runs/38192763379); the shared Windows adapter and Portable Pixel Core checks are in [run 38192763320](https://github.com/jvteodoro/Kinovea/actions/runs/38192763320). Both runs used the final tested code SHA.

Windows legacy job metadata from run `38192425142`:

- Runner: `windows-2022`, Windows Server 2022 build `10.0.20348`, image `20261004.326.1`, x64.
- Visual Studio MSBuild `17.14.60.43110`; .NET Framework 4.8 targeting pack present (`Release` registry value `533325`).
- .NET 10 SDK `10.0.401`, runtime `10.0.12`.
- Legacy executable ran under CLR `4.0.30319.42000`, GDI+ `10.0.20348.5622`.
- Actual reader assembly SHA-256: `4b9039c662b7b605f28c3814f67c0bbd6d4135c1da02f5f322b5fdd7444ebd9f`.

Linux/Windows .NET 10 CI artifacts report SDK `10.0.401` and runtime `10.0.12`; local developer environment is Gentoo Linux x86_64, SDK `10.0.111`, runtime `10.0.11`.

## Fixtures and observed comparisons

All pixels are compared exactly in logical top-to-bottom, left-to-right RGBA order. Independent pattern formulas check RGB channel order and expected alpha in addition to the legacy capture comparison.

| Fixture | Input SHA-256 | Actual legacy pixel SHA-256 | Result |
|---|---|---|---|
| Candidate BMP `bmp-rgb-7x5` | `85e0feff83d9920703cbf8be2e6ccc76741da22b4ffe791d5d4c415724de1244` | `9a84e308ce741bf2ff2bf1e85d417fc9812fabddcd49d02e0001ffb9e912dd2d` | PASS: 7x5, opaque RGB values, exact channel order and raster rows match candidate and fresh Windows reader capture. |
| Candidate transparent PNG `png-alpha-13x9` | `e0759d696bfef305e0a2c1c8fc7b7673e9e46390ef15beb541679c6766041dcd` | `3a4ceaa3b8027dbac77f815a02812145b57e41304d6a7efc962d2cbfd29cf9fe` | PASS: 13x9, exact RGB and alpha values including transparent and partially transparent pixels; output is labeled straight alpha. |
| Generated PNG eXIf orientation 6 | `ce1e40b17c68bea8c6294f4820f780b3ae97fa6e82abf1fb5e31df9523fd6808` | `3a4ceaa3b8027dbac77f815a02812145b57e41304d6a7efc962d2cbfd29cf9fe` | PASS for this fixture: GDI+ did not expose an EXIF orientation property; actual reader stayed 13x9 and output bytes matched the untagged PNG. The portable reader does not apply a rotation. Other PNG eXIf variants are NOT_ESTABLISHED. |

The candidate directory and its approval metadata are unchanged. The generated orientation fixture is emitted by the Windows harness at run time, not added to or used to regenerate approved golden data.

## Verification results

Final reader workflow [38192763379](https://github.com/jvteodoro/Kinovea/actions/runs/38192763379), source `8b75d148725762942f24ffe2c7c4624d4ad88294`, passed:

- Linux .NET 10 reader suite: 1,322 assertions.
- Windows .NET 10 reader suite: 1,322 assertions.
- Windows fresh .NET Framework 4.8 characterization: 20 cases, 578 assertions.
- Windows reader-versus-capture suite: 2,419 assertions, including candidate input/pixel hash checks and the fresh BMP, PNG, and PNG eXIf comparison.

The existing Windows Bitmap adapter workflow passed 40 checks, including 31 captures from the actual legacy reader. Portable Pixel Core passed 10/10 on Linux and Windows in the corresponding workflow [38192763320](https://github.com/jvteodoro/Kinovea/actions/runs/38192763320). The adapter test harness now accepts additive characterization cases and fixture entries while preserving its original minimum checks.

Commands executed locally on Gentoo:

```sh
dotnet build tests/PortableImageReader.Tests/PortableImageReader.Tests.csproj --configuration Release --no-restore -m:1 -v:minimal
dotnet tests/PortableImageReader.Tests/bin/Release/net10.0/PortableImageReader.Tests.dll --measure
dotnet tests/PortableImageReader.Tests/bin/Release/net10.0/PortableImageReader.Tests.dll --legacy-capture /tmp/reader-head-artifacts/portable-image-reader-windows-legacy-38192763379-1/legacy-capture
dotnet run --project tests/PortablePixels.Tests/PortablePixels.Tests.csproj --configuration Release
git diff --check
```

Local results: build succeeded with 0 warnings/errors; current reader suite passed 1,322 assertions; comparison against the downloaded actual Windows run capture passed 2,419 assertions; Portable Pixel Core passed 10/10. One attempted local build of the Windows-only adapter tests was `NOT_RUN`: package restore failed with `NU1301` because this sandbox denied NuGet network access. Windows CI ran the adapter tests instead.

## Performance observations

Measurements use three warmups and seven timed samples; values are descriptive, not budgets. Managed allocation counts include the final buffer and managed row scratch but exclude Skia's native decoder and bitmap storage. Each reader conversion makes one full-frame copy into the owned output; no second full-frame managed staging array is used.

| Host / format | Size | Managed bytes / image | Median ms |
|---|---:|---:|---:|
| Gentoo local / BMP | 640x480 | 926,056 | 3.499 |
| Gentoo local / PNG | 640x480 | 1,233,256 | 5.474 |
| Gentoo local / BMP | 1920x1080 | 6,230,384 | 19.901 |
| Gentoo local / PNG | 1920x1080 | 8,304,352 | 24.622 |
| Ubuntu 24.04 CI / BMP | 640x480 | 925,896 | 2.169 |
| Ubuntu 24.04 CI / PNG | 640x480 | 1,233,096 | 3.644 |
| Ubuntu 24.04 CI / BMP | 1920x1080 | 6,230,224 | 10.817 |
| Ubuntu 24.04 CI / PNG | 1920x1080 | 8,304,320 | 14.237 |
| Windows Server 2022 CI / BMP | 640x480 | 925,936 | 2.273 |
| Windows Server 2022 CI / PNG | 640x480 | 1,233,136 | 3.926 |
| Windows Server 2022 CI / BMP | 1920x1080 | 6,230,576 | 12.694 |
| Windows Server 2022 CI / PNG | 1920x1080 | 8,304,352 | 13.607 |

One run per host is not a general performance comparison. `LockBits` does not apply here; Skia allocates decoded native storage. Native allocations and internal codec copies are not measured. The number of explicit full-frame managed copies is one.

## Status by property and remaining gaps

| Property | Status | Evidence / limit |
|---|---|---|
| Build and decode BMP/PNG on Linux and Windows | PASS | Separate `net10.0` suite passed on both CI OSes. |
| Dimensions and exact logical pixel values for listed fixtures | PASS | 2,419 dynamic comparison assertions against fresh actual Windows reader outputs plus independent formulas. |
| RGB channel order | PASS | Independent expected formulas and exact legacy captures. |
| Straight alpha and transparent/partial values | PASS for PNG fixture | Actual PNG is `Format32bppArgb`; no claim for other alpha layouts. |
| BMP 24-bit file row padding and output top-to-bottom rows | PASS | Independently authored 3x2 padded 24-bit BMP plus 7x5 actual reader capture. |
| PNG eXIf orientation 6 | PASS for this fixture, identity transform | GDI+ exposes no orientation property; other PNG metadata encodings/orientations are NOT_ESTABLISHED. |
| JPEG, color profiles, indexed PNG/BMP, uncommon BMP variants | NOT_RUN / NOT_SUPPORTED | Intentionally outside the first BMP/PNG scope. |
| Corrupt/unsupported input error categories | PASS for the new standalone API | Not claimed equivalent to legacy logging/status behavior. |
| Timestamps, `PreviousTimestamp`, seeking, caches, thumbnails, UI/export | NOT_APPLICABLE to this standalone API | No production reader or frame integration was added. |
| Linux image-reader integration with Kinovea | NOT_RUN | This project is not connected to a Kinovea production call path. |
| Global Bitmap reader parity / approved baseline | NOT_ESTABLISHED | Existing Windows candidate baseline remains unapproved; only listed properties and fixtures were compared. |

## Limitations and next step

The new reader does not replace `VideoReaderBitmap`, does not add timestamps or lifecycle/cache behavior, and does not claim JPEG support or full cross-platform Kinovea compatibility. PNG eXIf behavior is established only for the generated orientation-6 input. Linux packaging beyond x64 glibc and application distribution are unresolved.

Next milestone: propose a narrow opt-in `IFrameGenerator`/`VideoFrame` adapter that consumes `PixelBuffer` while leaving the current Windows production path intact. Before implementation, characterize frame timestamps, disposal/ownership and thumbnail/export consumers at that boundary, and decide whether the unapproved candidate baseline is sufficient for those scoped checks.
