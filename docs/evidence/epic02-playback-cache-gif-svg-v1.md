# Epic 02 playback, cache, GIF and SVG evidence v1

**Status:** Local Linux prototype verification; Windows and legacy-reader parity are `NOT_RUN`.
**Test source:** `511e9666e545f681dce25b8a10f211f3957747a2`.

## Reproduction and results

```sh
dotnet build tests/PortableVideo.Tests/PortableVideo.Tests.csproj --no-restore -m:1 -nodeReuse:false -v:minimal
dotnet tests/PortableVideo.Tests/bin/Debug/net10.0/PortableVideo.Tests.dll
DISPLAY=:1 WAYLAND_DISPLAY=wayland-0 XDG_RUNTIME_DIR=/run/user/1000 \
  python3 tools/native-player/video-smoke.py \
  --assembly src/Kinovea.PortableDesktop/bin/Debug/net10.0/Kinovea.PortableDesktop.dll \
  --input /tmp/kinovea-portable-video-tests/synthetic-h264.mp4 \
  --output /tmp/kinovea-epic02-playback-final --source-sha 76cdd00f
```

On Gentoo Linux, .NET SDK 10.0.111/runtime 10.0.11 and host FFmpeg 8.1.2,
the decoder/cache/GIF/SVG program passed **144 assertions**. The graphical
smoke ran the actual Avalonia process and returned `PASS` for open-visible-video,
play, pause at PTS 4096, resume/advance to 8192, step to 12288, and close to
the empty state. The H.264 fixture SHA-256 is
`af04755f612b5eff43791fe1665ee50d34b8888fdbe3eb3f300a94f8e84ca282`.
The local screenshots are deliberately outside Git; their SHA-256 values are
`2f1e513e91fc52894344f2b37d9d1315eb6a15d0c8ef07266afcbf3a8ced524c`
and `8d6b6ae3272e4858cd597057acba7db2a3dc44cdcf8af4d12f7a3f0fcce19796`.
No hosted workflow ran.

## Portable cache boundary

`PortableVideoFrameCache` remains an isolated, externally serialized,
detached-copy cache. The test decodes and replaces eight frames over 25 open/
close intervals with capacity three, confirms the cache remains bounded,
confirms the final cached frame remains readable after decoder close, and
confirms `Clear` disposes an entry and sequential cache `Dispose` is idempotent.
It does **not** implement `PreBuffer2`, `ForceAdd`, callback policy, native
`AVBufferRef` retention, background prebuffering, or concurrent access.

The existing Windows net48 cache evidence exercised the direct C# `Cache` and
`PreBuffer2` contracts (workflow 38251549851, 24 cases/739 assertions), but
did not execute the C++/CLI FFmpeg reader. Therefore it is source/isolated-cache
evidence only, not a claim of production playback cache equivalence.

## GIF and SVG boundary

The selected local FFmpeg build decodes the generated 32x24 animated GIF as
five RGB24 frames with timestamps `0,20,40,60,80` in its centisecond time
base. The suite now verifies exact seek to 40 and backward seek to 0 before
linear trace verification. It decodes the authored 32x24 SVG with local
`librsvg`, verifies its one-frame output and red/green raster probes.

This is a narrow decoder capability result. GIF alpha/transparency,
palette/disposal, loops and per-frame timing behavior remain
`NOT_ESTABLISHED` against `VideoReaderGif`. SVG viewBox corner cases,
percentage dimensions, rotation, malformed-file categories and SharpVectors
feature parity remain `NOT_ESTABLISHED` against `VideoReaderSVG`. The desktop
routes `.gif` and `.svg` to the opt-in FFmpeg session; legacy reader selection,
cache and UI are unchanged.

## Compatibility status

| Property | Status |
|---|---|
| Native Linux MP4 play/pause/resume/step/close | `PASS` for synthetic H.264 GUI smoke |
| Portable cache lifetime for synchronous detached copies | `PASS` for listed 144-assertion scenarios |
| Legacy `PreBuffer2`/`ForceAdd` and AVBufferRef lifetime parity | `NOT_ESTABLISHED` |
| Local GIF frame decoding and timestamp seeks | `PASS` for generated five-frame fixture |
| Local SVG dimensions and raster probes | `PASS` for authored fixture |
| Legacy GIF/SVG semantic parity | `NOT_ESTABLISHED` |
| Windows .NET 10/native FFmpeg loading and Windows GUI | `NOT_RUN` |
