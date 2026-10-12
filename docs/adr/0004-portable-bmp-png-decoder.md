# ADR 0004: First portable BMP/PNG decoder

Status: **PROPOSED — implementation evidence in PR review**  
Date: 2026-10-11  
Baseline: `port/net10-linux` after PR #4 merge, `8ed8666aae85ff8cf16d97cdd1629dc2043638ba`.

## Context

The legacy `VideoReaderBitmap` supports `.jpg`, `.jpeg`, `.png`, and `.bmp`, but this slice adds only BMP and PNG to a separate .NET 10 path. `FrameGeneratorImageFile.OpenInternal` constructs a `System.Drawing.Bitmap`, records the original dimensions, forces 96 DPI, reads EXIF tag `0x0112`, and applies only the quarter-turn values 3, 6, and 8. Mirror and rotate-plus-mirror values 2, 4, 5, and 7 are mapped to `Rotate0` by `GetRotation`; this is legacy behavior to characterize, not an opportunity to correct it. `VideoReaderBitmap` separately has fixed 25 fps metadata and timestamp arithmetic; this reader will not implement that API.

The candidate Windows .NET Framework 4.8 evidence has pixel captures for BMP and PNG, but remains `CANDIDATE_NOT_APPROVED`. It establishes observations for the included fixtures, not universal format parity. Current portable core v0.1 provides RGB24 and RGBA32 with explicit `AlphaMode` and owned memory.

## Decoder alternatives

| Alternative | License/distribution | Platform and deployment | BMP/PNG and pixels | Assessment for this slice |
|---|---|---|---|---|
| ImageSharp | Six Labors Split License. Its license grants Apache-2.0 for qualifying open-source software; this repository is GPL-2.0-only. Apache-2.0 is GPLv3-compatible but not GPLv2-compatible, so adding it needs a project-level license decision. | Fully managed, cross-platform, no native runtime package. | Built-in BMP/PNG decoders and explicit typed pixel buffers; broad metadata and format surface. | Technically convenient, but rejected pending resolution of GPLv2-only compatibility. |
| SkiaSharp | SkiaSharp is MIT; bundled Google Skia native code is BSD-3-Clause. Both are compatible with GPLv2 subject to preserving notices. | Cross-platform managed API plus native assets. Linux `NoDependencies` still requires glibc (`libc.so.6`, loader, libm, libdl and libpthread); Windows needs the Win32 native assets. The selected native package omits Fontconfig. Gentoo Linux here is x86_64/glibc. | Native codec supports BMP/PNG and can decode into explicit `RGBA8888` / unpremultiplied pixel storage. PNG `eXIf` orientation is not reported by Skia's current PNG codec, so orientation must be handled only where independently established. | Selected for the isolated reader because its license is compatible with the repository license and both required OSes have published runtime assets. Pin the version; verify pixel bytes and native loading on both CI platforms. |
| BCL-only BMP/PNG decoder | No third-party package license or native runtime. | Cross-platform managed .NET only. | BMP is manageable; full PNG support must cover filters, palette/transparency, color types, bit depths, interlace, malformed chunks, and size limits. | Not selected: a partial private codec would carry a larger correctness/security surface than this narrow migration slice. Reconsider only if external dependency policy requires it. |

Sources reviewed: [ImageSharp license](https://github.com/SixLabors/ImageSharp/blob/main/LICENSE), [FSF Apache-2.0 compatibility note](https://www.gnu.org/licenses/license-list.html.html#apache2), [SkiaSharp license](https://github.com/mono/SkiaSharp/blob/main/LICENSE.md), [Skia license](https://github.com/google/skia/blob/main/LICENSE), [SkiaSharp Linux runtime assets](https://www.nuget.org/packages/SkiaSharp.NativeAssets.Linux.NoDependencies/4.153.1), [SkiaSharp 4.153.1 target frameworks](https://www.nuget.org/packages/SkiaSharp/4.153.1), and [Skia PNG codec orientation note](https://github.com/google/skia/blob/main/src/codec/SkPngCodec.cpp).

## Decision

Use SkiaSharp 4.153.1 in a new `net10.0` library that references `Kinovea.PortablePixels`. Keep this reader independent from the legacy .NET Framework projects, `Kinovea.Video.Bitmap`, the Windows GDI+ adapter, UI, cache, and FFmpeg.

The initial public boundary is synchronous `Read(path) -> PixelBuffer`. It accepts BMP and PNG file extensions, decodes only BMP or PNG content, and returns an independently owned buffer. RGB-only images produce RGB24/`AlphaMode.None`; alpha-bearing images produce RGBA32/`AlphaMode.Straight`. Decode output is requested in RGBA byte order with unpremultiplied alpha. The decoded native bitmap is copied once into the owned PixelBuffer. If a legacy quarter-turn must be applied, the copy writes transformed pixel coordinates directly into the final buffer rather than allocating another full-frame buffer.

The adapter will inspect `SKCodec.EncodedOrigin`. It will apply only the legacy 180/90/270-degree rotations for origin values 3/6/8; mirror origins remain untransformed, matching `FrameGeneratorImageFile.GetRotation`. This behavior will be changed only if the Windows characterization demonstrates that BMP/PNG input makes the rule different. If the selected codec does not expose an orientation supported by legacy GDI+, the mismatch remains documented and the reader will not claim parity for that fixture.

Invalid encoded BMP/PNG data raises `InvalidDataException`; unsupported extension or encoded format raises `NotSupportedException`; file system errors retain their normal `IOException` family. This is a new standalone API, so it does not imitate `OpenVideoResult` or the legacy reader's logging-and-status behavior. It closes file and codec resources before return; the returned buffer has independent ownership.

## Observable behavior and limits

- Preserve decoded dimensions and logical pixel values for verified BMP/PNG fixtures.
- Preserve straight alpha bytes; no premultiplication is introduced by the reader.
- Preserve legacy orientation decisions only for cases established by the Windows reader.
- The reader does not set DPI because `PixelBuffer` has no DPI metadata and the legacy reader's 96-DPI behavior belongs to the old Bitmap/GDI+ boundary.
- The reader has no timestamp, `PreviousTimestamp`, seek, cache, thumbnail, UI, or export behavior. Those remain outside this slice.
- Decoding creates native codec/bitmap storage and one full-frame owned output buffer. No second full-frame staging array is intended. Managed/native allocations and latency will be measured rather than inferred.
- Native deployment is x64 glibc Linux and supported Windows x64 for this first test matrix. Other architectures, musl/Alpine, and application packaging remain unverified.
- Candidate Windows captures are comparison references only. No approved baseline is claimed.

## Acceptance gates

1. Independent fixtures assert RGB channel order, opaque/transparent/partial alpha, nonstandard dimensions, and BMP row padding.
2. Both OS jobs run the same portable reader suite against the committed candidate BMP/PNG inputs and compare dimensions and every logical pixel with both authored expectations and candidate Windows `GetPixel` captures.
3. Windows CI also executes the actual net48 reader harness and compares its fresh BMP/PNG observations with the new reader. This dynamic comparison remains separate from the Linux candidate-snapshot comparison.
4. Corrupt and unsupported content fail with the documented exception category and release file/native resources.
5. Report parity per fixture and property; do not promote either image-reader component to `REGRESSION_VERIFIED` because the baseline is unapproved.
6. Record warmups, repeated timings, managed allocation counts, native runtime package, OS, SDK, and source SHA in evidence.

## Non-goals

No change to `VideoReaderBitmap`, `FrameGeneratorImageFile`, cache policy, WinForms, timestamps, frame ownership, FFmpeg, GIF/SVG readers, thumbnails, exports, or production call sites. No ImageSharp dependency, general-purpose conversion API, pixel-format expansion, package change in `Kinovea.PortablePixels`, decoder benchmark claim, or full Linux image-reader parity claim.

## Open review items

- Confirm repository maintainers accept SkiaSharp's MIT plus Skia BSD-3-Clause notices and the x64 glibc native dependency for the first distribution target.
- Review the legacy reader's behavior for PNG eXIf orientation on Windows; existing candidate fixtures cover JPEG EXIF, not PNG eXIf.
- Review whether a file-path `Read` API is sufficient before any connection to `IFrameGenerator` or reader lifecycle.
