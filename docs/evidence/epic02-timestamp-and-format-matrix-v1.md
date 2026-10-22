# Epic 02 timestamp and media-format matrix v1

**Status:** Linux local evidence. Windows and legacy decoder parity are `NOT_RUN`.
**Date:** 2026-10-22.
**Test source:** `ca24f3c1fb70f28afd75e576cfa3c13f0487c70e`.

## Reproduction

```sh
dotnet build tests/PortableVideo.Tests/PortableVideo.Tests.csproj --no-restore -m:1 -nodeReuse:false -v:minimal
dotnet tests/PortableVideo.Tests/bin/Debug/net10.0/PortableVideo.Tests.dll
dotnet build tests/PortableImageReader.Tests/PortableImageReader.Tests.csproj --no-restore -m:1 -nodeReuse:false -v:minimal
dotnet tests/PortableImageReader.Tests/bin/Debug/net10.0/PortableImageReader.Tests.dll
```

On Gentoo Linux with .NET SDK 10.0.111/runtime 10.0.11 and host FFmpeg 8.1.2
(avformat 62.12.102, avcodec 62.28.102, avutil 60.26.102, swscale 9.5.102),
the commands passed **111 PortableVideo assertions** and **1,637 PortableImageReader
assertions**. They use `ffprobe` for timestamp traces and preserved candidate data
for JPEG comparison. No hosted workflow was triggered.

## Timestamp/seek fixtures

| Fixture | SHA-256 | ffprobe `best_effort_timestamp` trace | Result |
|---|---|---|---|
| H.264 MP4 CFR, 64x48, B-frames | `af04755f612b5eff43791fe1665ee50d34b8888fdbe3eb3f300a94f8e84ca282` | `0,4096,8192,12288,16384,20480,24576,28672` | `PASS`: presentation order, `PreviousTimestamp`, exact and backward seek |
| H.264 MP4 VFR, 64x48, B-frames | `7a589f221e909cdc2a8e47689c09aabc11e87160001eac037103def8b24ab4eb` | `0,4096,12288,16384,36864,40960,45056,49152` | `PASS`: ffprobe trace, exact seek, gap seek and preroll previous timestamp |
| H.264 MP4 nonzero-start, 64x48, B-frames | `10346f29536d6e7aaffe2b2b8482092398726917a2e5521aa8edf712a456b823` | `32768,36864,40960,45056` | `PASS`: stream `start_pts=32768`, exact/gap/backward seek |

The nonzero-start fixture is authored with `setpts=PTS+2/TB`, `-copyts` and
`-avoid_negative_ts disabled`; its stream time base is `1/16384`. The reader
uses FFmpeg `best_effort_timestamp`; it does not derive timestamps from frame
number. This proves only the listed local FFmpeg path. The same deterministic
VFR fixture was also opened and play/pause actions were sent to installed
Kinovea 2025.2.0.0 under a disposable Wine 11.19 prefix overlay. The run
completed with `NOT_ESTABLISHED` behavioral status; its local result manifest
SHA-256 is `6ce0f1c15bcb8ed2d4ecbe21a61d103ebe235cf6272205267959ae26a8143b9d`
at `/tmp/kinovea-epic02-legacy-vfr/result.json`. Actual C++/CLI
`VideoReaderFFMpeg` timing and seek parity are still `NOT_RUN`; Wine GUI
screenshots are visual observations and do not supply numerical seek traces.

## Media and color matrix

| Area | Status | Executed evidence / rule | Limitation |
|---|---|---|---|
| H.264 in MP4, one video stream | `PASS` | Three deterministic fixtures above, packed top-down RGB24 | One codec/container family only |
| CFR, VFR, B-frame presentation order, nonzero start | `PASS` | ffprobe comparisons and 111 assertions | No legacy numerical comparison |
| JPEG still image | `PASS` | Preserved Windows candidate `jpeg-rgb-11x7.jpg` SHA `3e5eb1af564e615faeb3ce12692efad28ddf71338a59eb8c7639cbe723922c78`; RGB24, 11x7, 33-byte stride; all 77 logical pixels compared to candidate RGBA at ±8/channel | Candidate baseline is unapproved; EXIF orientation parity remains separate |
| BMP/PNG still images | `PASS` | Existing 1,637-reader-assertion suite | Scope is the portable still-image reader |
| Animated GIF | `PASS` for local decode | 32x24, five frame trace `0,20,40,60,80` through selected FFmpeg | Legacy GIF delay/compositing parity `NOT_ESTABLISHED` |
| SVG rasterization | `PASS` for local decode | 32x24 authored red/green SVG decoded by local `librsvg` | Legacy SharpVectors dimensions/features parity `NOT_ESTABLISHED` |
| Multiple video streams / explicit stream policy | `NOT_ESTABLISHED` | Reader calls FFmpeg best-video-stream selection | No multistream fixture or legacy oracle has run |
| Audio streams | `NOT_ESTABLISHED` | Video-only reader | No audio contract exists in this slice |
| Display rotation, sample aspect ratio | `NOT_ESTABLISHED` | No fixture | No transform is applied or claimed |
| Range, matrix, primaries, transfer, HDR | `NOT_ESTABLISHED` | `sws_scale` RGB24 conversion only | Metadata and colourimetry are not surfaced/tested |
| Alpha video, interlace, hardware decode | `NOT_ESTABLISHED` | No fixture/contract | Output is RGB24/`AlphaMode.None` |
| Windows .NET 10 and legacy Windows FFmpeg | `NOT_RUN` | Local-only task | Native DLL deployment and regression parity are unresolved |

The JPEG tolerance is intentional: Windows GDI+ and Skia decoders can differ by
small integer rounding values on lossy JPEG output. Dimensions, layout and
opaque alpha are exact. This does not establish JPEG EXIF rotation behavior.
