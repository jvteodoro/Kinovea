# ADR 0006: Native still-image player boundary

Status: **PROPOSED — implementation prototype for review**
Date: 2026-10-13
Baseline: `port/net10-linux` at `22a77fb8c0ae146dd1e14ee70bdc184374e88de5` (PR #11 merge).

## Context

The portable BMP/PNG reader and `PortableReaderShell` already own decoding, timing and frame selection. The legacy player is a WinForms/.NET Framework consumer and cannot host the `net10.0` implementation in-process. PR #10 remains a diagnostic host proposal and is not a merged production dependency. This slice needs a small native GUI boundary while leaving the existing Windows application and call sites untouched.

## Decision

Use an Avalonia 12.1.4 executable targeting `net10.0`, plus a small `net10.0` presentation library. Keep file decoding and navigation in the existing portable reader and shell. Keep the presentation library responsible only for one synchronous copy into an Avalonia-owned bitmap, a reader-session lifecycle wrapper, and image/viewport coordinate transforms.

Avalonia is MIT licensed and runs with the Linux and Windows desktop backends. It provides ordinary desktop controls, storage provider dialogs, and a standard `WriteableBitmap`; a custom renderer is unnecessary. The executable has no dependency on WinForms, GDI+, or Windows-only APIs.

## Pixel and ownership contract

- Accepted portable layouts are RGB24 with no alpha and straight RGBA32 with unpremultiplied alpha. Premultiplied data is mapped to Avalonia's matching alpha format if supplied by the portable contract.
- PixelBuffer row padding and row orientation are handled through `GetRowSpan(y)`, which returns logical rows. Destination padding is left to Avalonia. Bytes are copied once into a separately owned `WriteableBitmap`.
- The adapter retains neither the frame nor a PixelBuffer memory view after it returns. Avalonia bitmap lifetime is independent of reader lifetime. Session close first clears the displayed bitmap reference, closes/disposes the reader, then disposes the old presentation bitmap.
- UI calls are synchronous and externally serialized. There is no background decoder, retained native pointer, pool or lease mechanism.

## Navigation and viewport

The session calls the shell's characterized first-frame request on open, producing timestamp `0`, previous timestamp `-1`, fixed 25 fps/40 ms/250-timestamp metadata, and working zone `[0,249]`. Repeat/current, seek, and step requests delegate to the existing shell. The current image pixels are unchanged while timestamp navigation advances through the still-image timeline.

The viewport uses double-precision fit scale `min(viewportWidth/imageWidth, viewportHeight/imageHeight)`, centered offsets, and an anchor-preserving zoom transform. Pan is a viewport-pixel translation; conversion to and from image coordinates goes through the same affine transform. The original Kinovea source's aspect-fit behavior informed the initial fit rule; exact UI styling and pan limits are not claimed equivalent.

## Dependency alignment required by the single-process boundary

The existing `PortableImageReader` project pinned SkiaSharp 4.153.1. Avalonia 12.1.4's Skia backend depends on SkiaSharp 3.119.4 and its native library. Loading both managed/native Skia versions into this single process caused a native version exception before Avalonia initialized. The reader project was aligned to 3.119.4, retaining the same SkiaSharp decoder and reader source. The existing Linux image-reader suite passed 1,322 assertions, along with generator, shell and desktop tests. Windows decode behavior under this package version remains `NOT_RUN`; its normal Windows suite must be run before merge.

This dependency pin is the smallest in-process solution found. A separate decoder process or an IPC pixel transport would add process, transport and copy boundaries without a demonstrated need. Avalonia and SkiaSharp licenses/notices remain subject to the existing dependency/license review in [ADR 0004](0004-portable-bmp-png-decoder.md).

## Alternatives

| Alternative | Decision |
|---|---|
| Extend legacy WinForms and System.Drawing | Rejected for this slice: it does not run natively on Linux and cannot reference the net10 reader. |
| Use Avalonia as a thin renderer over the current reader shell | Selected: small native host, reusable portable navigation/decoder, deterministic presentation ownership. |
| Migrate the full application shell | Deferred: it would include unrelated UI, cache, playback, analysis and native FFmpeg dependencies. |
| Add IPC between net48 and net10 | Rejected for now: no need for a Linux still-image vertical slice, and it adds serialization/lifetime complexity. |

## Consequences and limitations

The desktop player remains a separate opt-in application and does not imply that the WinForms UI, Bitmap reader, GIF, FFmpeg, caches, export or analysis algorithms have migrated. The view currently supports BMP/PNG open, file selection, metadata, basic timeline stepping, fit, zoom and pan. Still-image metadata and timeline behavior follow the portable shell. Decoding and presentation run synchronously on the UI thread; measured opens are reported descriptively, without a responsiveness budget.

Linux visible-GUI evidence and fixture-scoped Wine comparisons are in [native still-image player v1 evidence](../evidence/native-still-image-player-v1.md). Windows execution, open-dialog interaction automation, screenshot pixel equivalence and source equivalence with the installed Kinovea 2025.2 binary remain `NOT_RUN` or `NOT_ESTABLISHED`.

## Sources

- [Avalonia 12.1.4 NuGet package](https://www.nuget.org/packages/Avalonia.Desktop/12.1.4)
- [Avalonia GitHub license](https://github.com/AvaloniaUI/Avalonia/blob/master/licence.txt)
- [Avalonia WriteableBitmap API](https://docs.avaloniaui.net/api/avalonia/media/imaging/writeablebitmap)
- [Avalonia framebuffer API](https://docs.avaloniaui.net/api/avalonia/platform/ilockedframebuffer)
