# PortableVideo v0.1

`Kinovea.PortableVideo` is an opt-in .NET 10 video source for the portable
desktop prototype. It is isolated from the legacy .NET Framework 4.8/C++/CLI
reader, its caches, and the existing production playback selection.

## Backend and deployment

The project uses the `FFmpeg.AutoGen` 8.1.0 managed binding (MIT, license
included in the NuGet package). The binding dynamically loads native FFmpeg
shared libraries; it does not ship them. The runtime checks the loaded ABI
major versions: libavformat 62, libavcodec 62, libavutil 60, and libswscale 9.
An optional `KINOVEA_FFMPEG_ROOT` environment variable selects a library
directory before initialization. On Gentoo/Linux the current fallback is
`/usr/lib64`, then the platform loader path.

The tested Gentoo FFmpeg 8.1.2 build enables GPL and libx264. This is evidence
for the local decoder only; it is not a redistributable package. Native FFmpeg
distribution, build configuration, codec licensing, Windows DLL names, and
ABI resolution on Windows remain deployment decisions. Do not bundle this
host's native libraries or claim a licensing conclusion from the managed
binding's MIT license.

## Current contract

- Synchronous, externally serialized open/read/seek/close/dispose calls.
- Selects the first best decodable video stream.
- Emits packed RGB24, top-down rows, no alpha, with stream timebase timestamps.
- Uses decoded `best_effort_timestamp`; packet timestamp timebase is set to the
  selected stream for the fallback PTS field.
- Converts to a separately owned PixelBuffer. Each returned frame borrows the
  current reader buffer until the next read, seek, close, or dispose.
- Presentation copies one frame into an independently owned Avalonia bitmap
  before the decoder advances. The video desktop session is opt-in and is not
  registered as a replacement for the legacy reader.
- Playback pacing is a monotonic wall-clock target compared with decoded frame
  timestamps. It is a local prototype and does not reproduce legacy prebuffer
  or cache policies.

The local FFmpeg build also decoded the deterministic animated-GIF and SVG
fixtures used by `PortableVideo.Tests`. GIF/SVG are routed through this
opt-in decoder in the desktop prototype; they do not yet claim legacy GIF
timing/cache or SharpVectors rendering parity.

No audio, frame-rate conversion, hardware decode, rotation/display-matrix
handling, color-management guarantees, HDR, alpha, multiple-stream selection,
or production cache retention is implemented. `PortableVideoFrameCache` is a
bounded, detached-copy cache for the prototype; it is intentionally not a
replacement for legacy `PreBuffer2`. See the versioned evidence record in
`docs/evidence/epic02-video-playback-v1.md`.

## Local verification

```sh
dotnet restore tests/PortableVideo.Tests/PortableVideo.Tests.csproj \
  -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false
dotnet build tests/PortableVideo.Tests/PortableVideo.Tests.csproj \
  --no-restore -m:1 -nodeReuse:false
dotnet tests/PortableVideo.Tests/bin/Debug/net10.0/PortableVideo.Tests.dll
```

The test executable generates small deterministic H.264 fixtures with the
installed FFmpeg CLI, checks ffprobe timestamp traces and exercises decode,
seek, VFR timestamps, invalid input, close/reopen, and the playback clock.
Native libraries must be installed separately. Windows has not been executed.
