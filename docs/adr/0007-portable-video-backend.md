# ADR 0007: Isolated FFmpeg source for the .NET 10 desktop prototype

- **Status:** PROPOSED
- **Date:** 2026-10-22
- **Decision scope:** Experimental video decoding and playback in the native
  desktop prototype only

## Context

The production `Kinovea.Video.FFMpeg` project is C++/CLI targeting CLR v4.8
and Visual Studio's C++ toolchain. It exposes the legacy `VideoFrame`/GDI+
boundary and its buffering behavior. It cannot be referenced by the portable
`net10.0` Linux application. Replacing or changing it would expand this slice
into a production behavior migration before a Windows comparison exists.

## Decision

Prototype an isolated `net10.0` decoder using `FFmpeg.AutoGen` 8.1 bindings
against separately installed FFmpeg 8.1 shared libraries. Keep decoding
synchronous and externally serialized. Emit a borrowed current RGB24
PixelBuffer with a stream-timebase presentation timestamp. The portable
desktop session converts each displayed frame once into a separately owned
Avalonia bitmap and never retains a borrowed decoder buffer across the next
read.

The runtime verifies required FFmpeg ABI major versions and supports an
explicit native-library directory. Native libraries are not bundled. The
decision is **proposed**, because only Gentoo Linux was executed; Windows
packaging, ABI loading, and product distribution licensing remain open.

## Alternatives considered

1. **Reuse the legacy C++/CLI server directly.** Rejected for this prototype:
   the assembly targets CLR v4.8, requires MSVC C++ targets and exposes GDI+
   frames. No Linux runtime can load it.
2. **P/Invoke a project-owned C ABI shim.** Potentially offers a stable ABI,
   but would require building and shipping a native shim per OS/architecture
   and maintaining its ABI, without reducing the need to package FFmpeg.
3. **FFmpeg.AutoGen plus host FFmpeg shared libraries.** Selected for an
   isolated proof because it directly calls the FFmpeg C API, keeps the
   decoder in managed project source, and successfully loaded the available
   Linux ABI. It creates unsafe managed interop and a strict ABI/native
   deployment requirement.
4. **A command-line `ffmpeg` process.** Rejected as the interactive reader
   boundary: frame pipe control, seek, lifetime, and timestamp transport would
   need a separate protocol and extra copies.

## Consequences

- The new project has no reference to legacy net48 assemblies and does not
  modify the existing FFmpeg backend.
- The decoder currently selects one video stream, returns RGB24, and does not
  handle audio, color metadata policy, rotation, HDR, interlacing policy,
  hardware acceleration, or prebuffer/cache contracts.
- Stream timestamps and a simple monotonic playback clock are exercised with
  synthetic CFR/VFR H.264 files. This is not evidence of legacy timestamp
  parity, seek tolerance, or playback-equivalent buffering.
- Native FFmpeg libraries inherit the license and redistribution conditions
  of their build configuration and enabled codecs. The tested Gentoo build is
  GPL-enabled with libx264 and must not be treated as a product package.
- Windows and deployment approval are required before this backend can be
  considered an application-supported dependency.

## Evidence and review conditions

See [Epic 02 video playback v1 evidence](../evidence/epic02-video-playback-v1.md).
The local decoder and GUI prototype passed the documented Linux tests. The
Windows actual-reader comparison is `NOT_RUN`; cache integration and GIF/SVG
remain `NOT_ESTABLISHED`. This ADR must remain proposed until Windows ABI and
distribution reviews are complete.
