# ADR 0005: Isolated .NET 10 host for the portable still-image path

Status: **PROPOSED — review required**
Date: 2026-10-12
Baseline: `port/net10-linux` after PR #9 merge, `8b73d5440908d5df954fda0e255fa3d79ed5694b`.

## Context

Kinovea's existing desktop still-image route is selected in a .NET Framework 4.8 process and returns `System.Drawing.Bitmap` through legacy video contracts. The portable BMP/PNG reader, frame generator, and reader shell are separate `net10.0` assemblies. A .NET Framework 4.8 process cannot load a `net10.0` assembly as an ordinary in-process project reference. The legacy route also runs through WinForms and GDI+ at its application boundary.

PR #9 identified the actual still-image production route as `RootKernel` reader registration -> `VideoTypeManager` extension dispatch -> `FrameServerPlayer.Load` -> `VideoReaderBitmap` -> `FrameGeneratorImageFile` -> `VideoFrame` -> `FrameServerPlayer.CurrentImage` -> WinForms paint. The image reader is uncached. The portable BMP/PNG path already has `PortableImageReader`, `PortableImageFrameGenerator`, and `PortableReaderShell`; these are `net10.0` and have Linux/Windows contract tests.

## Proposed decision

Use one small, standalone, in-process **.NET 10 command-line test host** in `tests/PortableStillImageHost.Tests`. Its default mode runs assertions against the existing `PortableReaderShell`; its `--host <path> [timestamp]` mode opens BMP/PNG files, reads the current frame through `PortableImageFrame`/`PixelBuffer`, reports a logical-pixel SHA-256, and closes the owner. Both modes use the same portable shell directly. The extension dispatch remains in `PortableImageReader`; this slice adds no second selector and no forwarding production API. Run the test mode and both CLI fixtures on Linux and Windows.

This host is a test/diagnostic executable, not a new production library, the existing Kinovea desktop application, or a compatibility shim for `FrameServerPlayer`. It must not copy navigation logic or duplicate decoder logic. Keep `PortableReaderShell`, frame generation, image reader, pixel representation, and every legacy production component unchanged. The portable decoder remains the authority for content validation and BMP/PNG decode.

On Windows, a separate .NET Framework 4.8 characterization executable will exercise `VideoTypeManager` selecting the actual `VideoReaderBitmap`, then call its actual `Open`, first-frame navigation, `Current` and `Close` APIs. It will compare the selected type, status, timestamps, dimensions and authored pixel probes with the portable host. It will not instantiate `FrameServerPlayer`, render through WinForms, or claim UI integration. Those dependencies are not included merely to make the isolated test look end-to-end.

## Dependency and reuse assessment

| Component | Target / boundary | Reuse decision | Reason and source |
|---|---|---|---|
| `RootKernel` | .NET Framework 4.8 WinForms executable | Do not reuse in host | Registers legacy `VideoReader` types inside app initialization; `Kinovea/Kernel.cs:136-145`. |
| `VideoTypeManager` | .NET Framework 4.8 `Kinovea.Video`; references WinForms and Services | Do not reference from .NET 10 | Although extension map logic is plain C#, it stores/constructs types derived from legacy `VideoReader`; default discovery reads `Application.ExecutablePath`. `Kinovea.Video/VideoReaders/VideoTypeManager.cs:34-131`. The portable host has only the `.bmp`/`.png` portable reader and does not need plugin reflection. |
| `FrameServerPlayer` | .NET Framework 4.8 ScreenManager / WinForms | Characterize relevant reader semantics; do not reuse | It returns GDI+ `Bitmap`, manages legacy metadata/filters/watchers, and references C++/CLI PlayerServer through ScreenManager. `Kinovea.ScreenManager/PlayerScreen/FrameServerPlayer.cs:39-203`; `Kinovea.ScreenManager/Kinovea.ScreenManager.csproj:14,257-294,1465-1476`. |
| `VideoReaderBitmap` and `FrameGeneratorImageFile` | .NET Framework 4.8; GDI+ | Execute as Windows oracle only | Decoder and rotation use `System.Drawing.Bitmap`, `Image`, `Graphics`, and EXIF APIs. Reader uses existing long timestamp/navigation behavior. `Kinovea.Video.Bitmap/VideoReaderBitmap.cs:42-286`; `FrameGeneratorImageFile.cs:33-166`. |
| `VideoReader`, `VideoFrame`, `IFrameGenerator` | .NET Framework 4.8; `VideoFrame.Image`/generator expose Bitmap | Do not multi-target in this task | `Kinovea.Video` references `System.Drawing`, `System.Windows.Forms`, and `Kinovea.Services`; the shared public frame contract is not portable. `Kinovea.Video/Kinovea.Video.csproj:10,174-175,239-243`; `VideoFrame.cs:30-58`; `IFrameGenerator.cs:33-69`. |
| `PortableImageReader`, `PortableImageFrameGenerator`, `PortableReaderShell`, `PixelBuffer` | .NET 10 portable assemblies | Reuse unchanged | These already implement BMP/PNG dispatch and tested frame navigation, data access, disposal and ownership. `src/Kinovea.PortableImageReader/PortableImageReader.cs`; `src/Kinovea.PortableFrameGenerator/PortableImageFrameGenerator.cs`; `src/Kinovea.PortableReaderShell/PortableReaderShell.cs`; `src/Kinovea.PortablePixels/PixelBuffer.cs`. |

## Alternatives

| Option | Assessment |
|---|---|
| **A. Minimal in-process .NET 10 test host over portable contracts — proposed** | One isolated executable; reuses already tested reader and shell without decoder or navigation duplication. No IPC serialization copy or extra library surface. Runs on Linux and Windows. It is a proving host, not the legacy GUI. Straightforward rollback by removing the isolated test host. |
| **B. Incrementally multi-target existing selector/reader components** | Not justified yet. `VideoTypeManager` returns the net48 `VideoReader` class family, and that family includes Bitmap and WinForms dependencies. Multi-targeting would need an API split before it could share code, expanding the change beyond the host question. Reconsider only when a consumer-neutral reader-selection contract is required by a real integration. |
| **C. net48 app plus separate net10 process / IPC** | Rejected for this slice. It can bridge runtimes without retargeting, but requires a protocol, frame serialization/copies, process supervision, failure semantics and latency measurements. No requirement currently makes a separate process necessary. |
| **D. Broader application-host migration** | Deferred. Migrating ScreenManager, metadata, renderer, plugin discovery and UI together would be much larger and would not isolate the still-image decoder result. A Linux UI framework remains an architectural decision. |

## Compatibility boundaries

- Reuse the existing portable shell's `[0,249]` navigation window, 25 fps / 40 ms / 250 timestamp metadata, first-frame selection, timestamps, and current-frame identity. Do not implement another clock or navigator.
- Keep the host synchronous and externally serialized. It owns and closes the shell; frame/pixel views are borrowed only until close.
- Compare against the actual Windows reader's `.bmp`/`.png` extension dispatch, open result, first `MoveNext`, timestamps, dimensions, exact logical fixture pixels, repeated-current identity, and close behavior.
- The Windows test invokes `VideoTypeManager` and `VideoReaderBitmap` directly, but it does not invoke `FrameServerPlayer` or WinForms. The portable host is behaviorally comparable for the tested properties, not the production Windows application.
- Preserve the candidate baseline as unapproved. Any mismatch is reported by property and fixture; no auto-regeneration.

## Acceptance criteria

1. The command-line test host targets `net10.0` and introduces no GDI+, WinForms, legacy project, new native decoder, cache, IPC, or UI dependency.
2. The host runs on Linux and Windows, selects `.bmp`/`.png`, opens, obtains the first current frame, navigates via the shell, returns dimensions/timestamps/pixel digest, and closes the owned shell.
3. Tests prove initial empty state, first frame at timestamp 0 with previous timestamp -1, repeat wrapper/storage identity, exact fixture pixels and metadata, corrupt/unsupported open status, and disposed pixel access after close.
4. A separate Windows net48 trace executes the real `VideoTypeManager` -> `VideoReaderBitmap` selection/open/current path and records its outputs. The report explicitly marks `FrameServerPlayer.CurrentImage` and actual UI rendering `NOT_RUN`.
5. Linux and Windows portable host suites pass. Host results are reported separately from legacy characterization and candidate fixture parity.
6. The application and all legacy reader/cache/UI/export/FFmpeg paths remain unchanged.

## Non-goals and open questions

No production registration, legacy project retargeting, FrameServerPlayer adaptation, WinForms migration, Linux GUI, IPC, GIF/SVG/video support, cache integration, algorithm/timestamp changes, image backend change, or claim of full playback parity.

This ADR remains `PROPOSED`. Review questions: Is an isolated CLI sufficient as the first .NET 10 host boundary? Should the actual `FrameServerPlayer` be brought into the characterization harness in a later, separately scoped test? Which Linux UI framework and consumer contract should be selected before any GUI integration?
