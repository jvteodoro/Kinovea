# Native video playback boundary assessment v1

- Date: 2026-10-22
- Base: `port/net10-linux` at `61850e7931456d57bba09c69a15d746b2773bf39`
- Provenance: prepared on `investigate/native-video-playback`; initially included as a docs-only follow-up in PR #13, subsequently merged into `port/net10-linux` at `78dad5ad3897b12596cdebdb7821454b009b4fe8`.
- Status: source and environment investigation; no decoder integration or playback behavior verified.

## Scope and result

This assessment identifies the work required for the first video frame to appear in the native .NET 10 desktop. It does not replace Kinovea's Windows C++/CLI reader, choose a decoder for product distribution, or claim video compatibility.

The existing desktop has a synchronous still-image session and an Avalonia presentation bitmap. It has no video source, playback clock, asynchronous frame queue, audio path, or cancellation boundary. The legacy reader is not reusable in-process from `net10.0`: its project is C++/CLI, targets CLR v4.8, and implements the net48 `VideoReader` contract, which in turn exposes GDI+ `Bitmap` frames and WinForms types.

Recommendation for the next implementation experiment: a separate `net10.0` console probe using FFmpeg.AutoGen bindings matching the available FFmpeg 8.1 ABI, with one deterministic short video fixture. Exercise open, sequential decode, best-effort timestamps, explicit BGRA/RGB conversion, and close. Keep that probe outside the Avalonia player. This is a candidate for evaluation, not an approved product backend. Do not begin desktop integration until Linux and Windows native-library packaging and licensing are resolved and the probe is compared against actual Windows Kinovea observations.

## Source-backed legacy path

| Boundary | Source evidence | Consequence |
|---|---|---|
| Project/framework | `Kinovea.Video.FFMpeg/PlayerServer/PlayerServer.vcxproj:20-42` sets `TargetFrameworkVersion` to `v4.8`, imports Visual Studio C++ targets and selects toolset `v143`; `CLRSupport=true`. | This project cannot be referenced by the portable `net10.0` desktop as a managed library. A process bridge or a new decoder implementation would be a distinct architecture, neither currently present. |
| Shared contract | `Kinovea.Video.FFMpeg/PlayerServer/VideoReaderFFMpeg.h:101-190` derives `VideoReaderFFMpeg` from `VideoReader`; its `Current` returns the shared `VideoFrame`. The header also names Bitmap-backed frames in its ownership comments. | The reader's API is coupled to the legacy Video assembly and GDI+ frame representation. |
| Decode and timestamps | `VideoReaderFFMpeg.cpp:313` opens through `avformat_open_input`; `:429` derives timestamp rate from stream `time_base`; `:1805-1832` reads `best_effort_timestamp` while decoding; `:2243` contains seek handling. | The new path must preserve frame timestamp units and distinguish packet, decoded-frame and presentation timestamps. Average FPS cannot replace timestamps for variable-frame-rate sources. |
| Frame conversion | `VideoReaderFFMpeg.h:285` selects `AV_PIX_FMT_BGRA`; `VideoReaderFFMpeg.cpp:2370-2396` allocates and scales/converts an FFmpeg frame; `:2408-2465` wraps converted storage in a Bitmap and publishes a `VideoFrame`. | Channel order, stride, rotation, scaling and independent frame ownership are coupled at one native-to-GDI+ boundary. The existing production target's BGRA output is a useful test vector, not proof the portable output should be BGRA. |
| Decode scheduling | `VideoReaderFFMpeg.cpp:835-851`, `:1154-1293`, `:2902-2975` start/stop prebuffering work; `:1346-1398` changes decoding mode; `:3317-3770` plans and executes decoding jobs. | Legacy playback includes asynchronous prebuffering, work cancellation, frame skipping and mode changes. A synchronous one-frame probe tests decoding only; it does not emulate playback. |
| Cache and resource lifetime | The reader creates `SingleFrame`, `PreBuffer2` and `Cache` containers with `DisposeFrame` (see `VideoReaderFFMpeg.cpp` constructor/data initialization and `:2868`); decoded frame comments in `:2290-2305` describe AVFrame/Bitmap ownership and `Bitmap.Tag` retention. | AVBufferRef/native memory lifetime must be tested through cache eviction and shutdown before moving into the desktop. Existing cache tests did not execute this C++/CLI reader. |
| Desktop boundary | `src/Kinovea.PortableDesktop.Presentation/PortablePlayerSession.cs` owns a synchronous `PortableReaderShell` plus an independent Avalonia `WriteableBitmap`; `PixelBufferBitmapAdapter.cs` copies each row once. `MainWindow.cs` drives still-image open/navigation/close. | There is no video clock, decoder worker, UI-thread publication, or cancellation protocol to reuse. Keep decoder output ownership separate from the Avalonia bitmap until a later lifecycle design. |

The Windows cache characterization in `docs/evidence/production-video-cache-lifetimes-v1.md` executed actual C# `Cache` and `PreBuffer2` contracts but explicitly did not instantiate `VideoReaderFFMpeg` or decode with C++/CLI. Actual legacy video playback remains `NOT_RUN` in this assessment.

## Local environment observations

Commands run in the supplied Gentoo Linux environment:

```sh
dotnet --info
ffmpeg -version
pkg-config --modversion libavformat libavcodec libavutil libswscale
ldconfig -p | rg 'libav(format|codec|util|swscale)\\.so'
```

Observed:

| Item | Result |
|---|---|
| OS / architecture | Gentoo Linux, `linux-x64` |
| .NET | SDK 10.0.111; runtime 10.0.11 |
| FFmpeg CLI | 8.1.2, built with GCC 15.3.0 |
| `libavformat` | 62.12.102 |
| `libavcodec` | 62.28.102 |
| `libavutil` | 60.26.102 |
| `libswscale` | 9.5.102 |
| Dynamic library lookup | `libavformat.so.62`, `libavcodec.so.62`, `libavutil.so.60` resolve under `/usr/lib64`; `libswscale` is reported by pkg-config. |
| Build configuration | Local `ffmpeg -version` configuration includes `--enable-gpl` and `--enable-libx264`. This local system binary is not a redistribution recommendation. |
| Test video in checkout | No MP4/MKV/AVI file was found in the assessed worktree. No decoder or playback test was run. |

Kinovea's checked-in `Kinovea.Video.FFMpeg/PlayerServer/FFmpeg.x64.props:10-14` names `ffmpeg-8.1.2-full_build-shared` and links the FFmpeg shared libraries. This is version alignment in build configuration only; it does not verify the binaries used by the installed Windows application or establish that the absent/ignored native files can be redistributed.

## Decoder options considered

| Option | Advantages | Risks / fit |
|---|---|---|
| Existing C++/CLI `VideoReaderFFMpeg` | Best behavior reference; already implements decode, seek, timebase, scaling, rotation, hardware decode, cache policy and scheduling. | Cannot load in the net10 Linux process; depends on CLR v4.8, Visual Studio C++/CLI, GDI+, the legacy Video contract, and Windows-oriented native packaging. Rehosting would require a separate IPC design, which is not justified for this first path. |
| FFmpeg CLI process | Uses system-installed FFmpeg; no direct C ABI bindings and no NuGet dependency; useful for local diagnostics and fixture generation. | Raw frame pipes lose timestamps unless metadata is carried over a synchronized side channel/container; pause, accurate seek, cancellation, stderr draining and process teardown need design. Encoding target pixel frames can create copies. Does not naturally reproduce Kinovea's cache, skipping or hardware-decoder decisions. Keep as a diagnostic tool, not the selected player architecture. |
| FFMediaToolkit | Managed high-level decoder API; repository/package identifies its own license as MIT and advertises frame access by timestamp. | Its README says supported FFmpeg is 7.x/shared build, with `avcodec` v61/`avformat` v61/`avutil` v59/`swscale` v8, while this machine and Kinovea props use FFmpeg 8.1.2 (ABI majors 62/62/60/9). It does not bundle native libraries. It would require a separate compatible FFmpeg 7 runtime and a detailed parity/lifecycle test. Do not use it against the current FFmpeg 8.1 runtime without confirming ABI support. |
| FFmpeg.AutoGen | Low-level bindings for the same FFmpeg API already used by Kinovea; package version tracks the FFmpeg release used to generate bindings. NuGet lists 8.1.0 and 9.0.1.1; local ABI major versions align with the 8.1 binding family. Binding package is MIT and contains no native runtime. | Unsafe/manual native ownership and FFmpeg API complexity remain; package version must be paired with supported native libraries, and platform loaders and binaries must be supplied separately. No decoding probe, Windows package, deployment test, or legal distribution review was run. This is the recommended experiment only because the current Kinovea source and local native runtime both use FFmpeg 8.1.x. |

License distinction: the C# binding package's MIT license does not change the native FFmpeg build's license. FFmpeg states that its default is LGPL 2.1-or-later and that optional GPL components/optimizations change the applicable license. The locally installed build reports `--enable-gpl`; its licensing cannot be generalized to another Gentoo, Windows, or bundled FFmpeg build. The actual runtime build's configuration and notices must be recorded per supported distribution, with project legal review before shipping binaries.

Sources checked on 2026-10-22:

- [FFmpeg license and legal considerations](https://ffmpeg.org/legal.html)
- [FFmpeg.AutoGen repository and version alignment](https://github.com/Ruslan-B/FFmpeg.AutoGen)
- [FFmpeg.AutoGen 9.0.1.1 NuGet page](https://www.nuget.org/packages/FFmpeg.AutoGen/)
- [FFMediaToolkit repository setup, native requirements, and license](https://github.com/radek-k/FFMediaToolkit)
- [FFMediaToolkit 4.8.1 NuGet page](https://www.nuget.org/packages/FFMediaToolkit/)

## Required parity and ownership test plan

Before adding a decoder to the desktop, create a small synthetic video with known frames and timestamps (lossless FFV1 or another licensed deterministic codec/container combination selected in a separate fixture decision). Test:

1. Open metadata: dimensions, stream timebase, nominal/average frame rate, duration and decode error category.
2. Sequential decode: frame count, `best_effort_timestamp`, previous timestamp behavior, dimensions, logical pixel channels and row orientation.
3. Conversion: known red/green/blue channel sentinels, BGRA-to-portable mapping, stride/padding, alpha handling and rotation/scaling policy.
4. Lifetime: native frame released after conversion, returned PixelBuffer lifetime independent from decoder, exactly-once close, cancellation, failed open, corrupt/truncated input, repeated open/close.
5. GUI later: UI-thread publication, pause/resume, frame pacing from media timestamps, seek, window close during decode, rapid open/replacement and no stale frame after close.
6. Legacy compatibility: run actual Windows `VideoReaderFFMpeg` against the same fixture, recording output timestamps, frame hashes, dimensions, rotations, format, and errors. Compare properties separately. Existing cache characterization does not satisfy this requirement.

No exact decode, frame parity, GUI, performance, or Windows result is claimed here. Before any asynchronous desktop implementation, design a cancelable producer boundary with a single explicit owner for decoder frames and separate owned presentation buffers; do not pass FFmpeg pointers or PixelBuffer borrowed views beyond the producing call without an independently valid owner.

## Proposed next implementation experiment

Create a test-only `net10.0` console probe, not an Avalonia playback feature. Pin the matching `FFmpeg.AutoGen` 8.1.0 binding package, resolve FFmpeg 8.1.x from an explicit runtime path, and decode a generated short fixture one frame at a time. Record native library versions, decoded timestamps, color layout, output hashes, allocations/copies, open/close behavior and failure diagnostics. Run Linux locally first; Windows must use a separately verified FFmpeg 8.1.x shared build. If runtime versions cannot be made reproducible across both platforms or the license configuration cannot be documented, stop before adding the dependency to production projects.

This experiment is not a product backend selection. Its acceptance gate is to decide whether the FFmpeg API boundary can be wrapped in a small, tested portable producer while preserving a credible comparison path to Kinovea's Windows reader.
