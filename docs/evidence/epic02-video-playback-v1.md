# Epic 02 video playback v1 — local implementation and characterization

**Status:** Linux prototype executed; Windows and legacy parity `NOT_RUN`.
**Implementation source:** `5860f7f03881f27ae1f4a87d98d86ceac63778cb`.
**Date:** 2026-10-22. No GitHub Actions were triggered.

## Architecture and dependency choice

`Kinovea.PortableVideo` targets `net10.0`, references Portable Pixel Core,
and uses FFmpeg.AutoGen 8.1.0 unsafe C bindings. Native FFmpeg 8.1 shared
libraries are host-provided, not packaged in NuGet or the application. The
runtime checks loaded major ABI versions (avformat 62, avcodec 62, avutil 60,
swscale 9) and accepts an explicit `KINOVEA_FFMPEG_ROOT` before initialization.
The portable desktop opts into this source for video paths; still BMP/PNG
remains on its existing path. Legacy C++/CLI reader, cache and frame contracts
are unchanged.

The local binding license file is MIT. Local native FFmpeg 8.1.2 was built
with `--enable-gpl --enable-libx264`; that local native build is not proposed
for redistribution. Product package composition and licensing remain open.
See [ADR 0007](../adr/0007-portable-video-backend.md) and the project
[deployment notes](../../src/Kinovea.PortableVideo/README.md).

## Reproducible environment

- Host: Gentoo Linux 2.18, x86_64, kernel `6.18.50-gentoo-dist-bin`.
- .NET SDK 10.0.111; runtime 10.0.11; MSBuild 18.0.11.
- FFmpeg/ffprobe 8.1.2; loaded libraries: avformat 62.12.102, avcodec
  62.28.102, avutil 60.26.102, swscale 9.5.102.
- GUI: local KWin Wayland with Xwayland, display `:1`, logical viewport
  1120x627 at render scaling 1.5 during the captured run.
- Base source SHA: `f8cf2ec05b98d4499ee72ae96b95d46c8d25d57b` (completed Epic 01).
- Implementation source SHA: `5860f7f03881f27ae1f4a87d98d86ceac63778cb`; the
  exact local GUI scenario was rerun after this commit and carries the same
  source SHA in its manifest.
- Hosted CI: `NOT_RUN` by task constraint.
- Windows .NET 10 and actual Windows net48 `VideoReaderFFMpeg`:
  `NOT_RUN`.

Fixture/test commands:

```sh
dotnet restore tests/PortableVideo.Tests/PortableVideo.Tests.csproj \
  -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false
dotnet build tests/PortableVideo.Tests/PortableVideo.Tests.csproj \
  --no-restore -m:1 -nodeReuse:false -v:minimal
dotnet tests/PortableVideo.Tests/bin/Debug/net10.0/PortableVideo.Tests.dll
dotnet build src/Kinovea.PortableDesktop/Kinovea.PortableDesktop.csproj \
  --no-restore -m:1 -nodeReuse:false -v:minimal
dotnet restore tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj \
  -r linux-x64 -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -m:1 -nodeReuse:false
dotnet build tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj \
  -r linux-x64 --no-restore -m:1 -nodeReuse:false -v:minimal
DISPLAY=:1 WAYLAND_DISPLAY=wayland-0 XDG_RUNTIME_DIR=/run/user/1000 \
  dotnet tests/Kinovea.PortableDesktop.Tests/bin/Debug/net10.0/linux-x64/Kinovea.PortableDesktop.Tests.dll
```

The test program creates two small synthetic H.264 MP4 files in
`/tmp/kinovea-portable-video-tests/` using FFmpeg `color`/`testsrc2` lavfi
sources and x264 lossless mode (`-crf 0`, `-map_metadata -1`, bitexact flags).
No third-party media or video binaries are committed.

| Fixture | Dimensions / time base | Frame presentation timestamps | SHA-256 |
|---|---|---|---|
| `synthetic-h264.mp4`, CFR 4 fps, red/green/blue/yellow segments, B-frames | 64x48, 1/16384 | 0, 4096, 8192, 12288, 16384, 20480, 24576, 28672 | `af04755f612b5eff43791fe1665ee50d34b8888fdbe3eb3f300a94f8e84ca282` |
| `synthetic-vfr-h264.mp4`, irregular setpts, B-frames | 64x48, 1/16384 | 0, 4096, 12288, 16384, 36864, 40960, 45056, 49152 | `7a589f221e909cdc2a8e47689c09aabc11e87160001eac037103def8b24ab4eb` |

The portable test dynamically compares decoded frame timestamps with
`ffprobe -show_entries frame=best_effort_timestamp`; it checks 8 frames in
each file, presentation ordering, previous timestamp, four authored center
color probes with tolerance 15, seek to exact and gap timestamps, decoded
preroll for VFR seek, invalid open and recovery, close/reopen, and borrowed
PixelBuffer invalidation after advancing. PlaybackClock tests rational
timebase, pause, resume, and reset. Result: **76 assertions passed**; a second
run generated the same fixture hashes and timestamps and also passed 76.

## Video format and observed boundary

The reader selects FFmpeg's best decodable video stream, emits packed top-down
RGB24 with stride `width * 3`, and labels alpha `None`. `sws_scale` converts
decoded native planes directly into a new owned PixelBuffer buffer. The frame
borrows the reader's current PixelBuffer until the next `ReadNext`, `Seek`,
`Close`, or `Dispose`; callers must copy if retaining it longer. The Avalonia
adapter copies each presented frame once into its independently owned
WriteableBitmap, then allows decode to advance.

This design exercises one stream and one codec family only. It does not
establish transfer range, matrix, primaries, HDR, display rotation, audio,
multiple video streams, interlacing, hardware acceleration, alpha, color
equivalence with legacy GDI+ output, or nonzero-start timestamp behavior.
The RGB24 output and center RGB probes are verified on this one local build.

## Playback and GUI observation

Built the actual `Kinovea.PortableDesktop` app and launched it as a GUI
process with the synthetic CFR MP4. The local runner was:

```sh
python3 tools/native-player/video-smoke.py \
  --assembly src/Kinovea.PortableDesktop/bin/Debug/net10.0/Kinovea.PortableDesktop.dll \
  --input /tmp/kinovea-portable-video-tests/synthetic-h264.mp4 \
  --output /tmp/kinovea-epic02-video-gui-evidence \
  --source-sha 5860f7f0
```

The harness requires the live KWin/Xwayland session. It verifies the actual
process window and a visible-frame report whose input hash matches the fixture;
it then sends Play, waits for PTS advance, Pause, Resume, waits for another
advance, Pause, uses the UI Next control, and Ctrl+W close. The final report
showed initial PTS 0; first pause at 4096; resume from 4096 and advance to 8192;
step to 12288; close to `Empty`. The screenshot contains the actual app window
with red first frame and green stepped frame.

Artifacts are generated from synthetic media. The GUI manifest and two small
screenshots are committed under `docs/evidence/epic02-video-playback-gui/`;
the same files remain in the local run directory:

- `manifest.v1.json`: SHA-256 `4c6a317d39e2448b583f1c5aac033164ce535424150be54acc047739de8acd16`
- `video-open.png`: SHA-256 `05cb75f84adefdcbdfdcb7e2c6b42ea1a85260840a9c191a3a03bb604ca3d014`
- `video-stepped.png`: SHA-256 `26857e9b699fa5a6e7eac5860df339765c8b2be76744068b362f0f65f7449cd9`

The manifest records process ID, actual fixture hash, native library versions,
view geometry, timestamps, actions, and presentation copies. Four 64x48 RGB
frames were presented, 36,864 bytes copied total (one 9,216-byte frame copy
per presentation). GUI screenshots are visual evidence only; no claim of
pixel-perfect rendering, legacy parity, or exact real-time scheduling follows
from them.

## Performance sample

The test uses one warmup and five whole-file decode measurements on this small
8-frame synthetic input. The observed median was 0.161–0.162 ms for eight
frames (two repeated test process runs); managed allocation was 317 bytes per
frame after warmup. This is descriptive only, excludes FFmpeg native
allocations, excludes open time, and is not a representative large-file
performance claim. Decoding converts pixels into the owned output buffer;
Avalonia presentation adds one full-frame copy per displayed frame. No other
full-frame copy was identified in these paths by source inspection.

## Results by child task / property

| Issue | Result | Evidence and remaining acceptance gaps |
|---|---|---|
| #28 native backend | `PARTIAL` | Linux FFmpeg.AutoGen + host FFmpeg 8.1 ABI selected and executed. Native runtime was not bundled. Windows native resolution, supported deployment package, and product license review remain `NOT_ESTABLISHED`. |
| #29 timestamp/seek | `PARTIAL` | Linux CFR/VFR frame timestamps matched ffprobe; exact/backward/gap seeks and B-frame reorder tested. Legacy Windows trace, nonzero stream start, seek tolerance across codecs/containers remain `NOT_RUN`. |
| #30 playback clock | `PASS` for prototype scenarios | Rational clock tests and actual GUI Play/Pause/Resume/Next/Ctrl+W passed. This does not establish legacy timing, long-run drift, VFR pacing tolerance, or Windows GUI behavior. |
| #31 cache/buffer boundary | `NOT_ESTABLISHED` for cache migration | No legacy cache or portable SingleFrame cache is integrated. Decoder current-frame memory is synchronously copied before advancing; presentation resources are separately owned. Retaining a decoded frame beyond session advance/close has no lease and is unsupported. No cache stress or legacy `PreBuffer2.ForceAdd` runtime characterization was run. |
| #32 GIF/SVG | `NOT_IMPLEMENTED` | Existing GDI+-based readers remain. No replacement dependency or changed image reader was added. GIF timing/loop/palette/transparency and SVG viewBox/rasterization still need independent Windows characterization and dependency review. |
| #33 media/color matrix | `PARTIAL` | Tested: H.264 in MP4, one 64x48 video stream, CFR+VFR, 8 frames, RGB24 output, local FFmpeg 8.1.2. Untested: other containers/codecs, audio, multistream selection, JPEG, orientation/rotation, color range/matrix, HDR, interlacing, hardware decode, Windows. |

### Source-inspected GIF/SVG behavior still requiring an oracle

The legacy GIF reader ([`VideoReaderGif.cs`](../../Kinovea.Video.GIF/VideoReaderGif.cs#L198)) uses `PropertyTagFrameDelay` (`0x5100`) but applies the
first frame's delay to every frame; a nonpositive delay falls back to 5
centiseconds. It computes duration as frame-count times that interval, eagerly
renders each frame to `Format32bppPArgb` (`:240-261`), and fills a legacy
`Cache`. `Close` clears that cache (`:109-112`). The image source is disposed
after the cache is built (`:80-84`).
Loop metadata, per-frame delays, disposal/compositing, palette and transparent
pixel behavior were not executed or independently characterized here.

The legacy SVG reader ([`VideoReaderSVG.cs`](../../Kinovea.Video.SVG/VideoReaderSVG.cs#L221)) reports fixed still-image timing metadata (25 fps and 250 timestamps; `:225-233`). Its generator ([`FrameGeneratorSVG.cs`](../../Kinovea.Video.SVG/FrameGeneratorSVG.cs#L129)) uses SharpVectors `GdiGraphicsRenderer` and a
`GdiSvgWindow`, computes original dimensions from root width/height or
percentage width plus viewBox, and rasterizes by aspect-fitting to the
requested maximum size (`:171-179`). It has an initial 640x480 output size.
Rendering,
SVG feature coverage, viewBox corner cases and malformed-file categories were
not executed here. These are source-inspected observations only, not a
portable contract. Retain them as characterization targets before choosing a
new renderer.

## Evidence categories and limits

- `PASS`: local portable video unit scenarios and actual native app GUI actions
  listed above.
- `NOT_RUN`: Windows .NET 10, Windows native FFmpeg package loading, real
  Windows legacy C++/CLI reader comparison, Wine comparison, hosted Actions.
- `NOT_ESTABLISHED`: behavioral parity with Kinovea's legacy FFmpeg backend,
  cache ownership compatibility, GIF/SVG parity, product redistribution
  rights, broad media/color support.

The legacy reader remains unchanged. This is an opt-in vertical slice and does
not claim production playback, all-format compatibility, or cross-platform
regression parity.
