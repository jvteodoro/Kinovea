# Portable image frame generator v1 evidence

Status: implementation candidate; no production playback integration and no global legacy parity claim.

## Boundary and provenance

PR #5 merged into `port/net10-linux` at `5179f2fc91e057a4af280b1e3de6df1b52ed9eda`. The integration branch was created from that merge. The existing Windows Bitmap baseline remains candidate evidence with `PENDING_HUMAN_APPROVAL` status.

The implementation is Option A: a standalone `net10.0` library references the already merged `Kinovea.PortableImageReader` and `Kinovea.PortablePixels`. The .NET Framework 4.8 `VideoReaderBitmap` and `IFrameGenerator` are not referenced by, or bridged to, this library. Windows CI runs the original net48 characterization executable separately, then compares the portable generator against its fresh machine-readable pixel captures. This keeps the framework boundary real and avoids an invalid net48-to-net10 reference.

## Legacy behavior characterized

Source inspection plus a new executable Windows lifecycle case cover:

| Behavior | Result / evidence type |
|---|---|
| Initial current frame | `VideoFrame.Empty()`: image null; timestamp and previous timestamp `-1` (source and runtime assertion). |
| First sequential request | Timestamp `0`, previous `-1`, regardless of requested target (runtime assertion). |
| Further sequential request | Timestamp increments by one; previous equals timestamp minus one (runtime assertion). |
| Arbitrary forward/backward timestamps | In-range request selects the exact timestamp; previous is timestamp minus one (runtime assertion). |
| Same timestamp repeat | The exact current `VideoFrame` wrapper is retained (runtime assertion); the bitmap instance also remains the same. |
| Working-zone boundary | Zone is `[0,249]`; request `250` through timestamp move returns true but leaves frame unchanged; next past 249 returns false (runtime assertion). |
| Reader metadata | 25 fps, 1 timestamp/frame, duration 250, interval 40 ms; reader shell source and pre-existing characterization assertions. |
| Generator bitmap identity | `FrameGeneratorImageFile.Generate` returns its stored image at all timestamps; source and lifecycle assertions. |
| `DisposePrevious` | No-op for the single stored bitmap; source and runtime assertion show image remains usable. |
| Reader/generator close | Disposes the image; runtime assertion tests GDI+ access failure and verifies `VideoReaderBitmap.Current` still holds its wrapper (runtime assertion). |
| Reopen at current timestamp | `VideoReaderBitmap` keeps stale frame because same-timestamp update returns early; a fresh image appears only after another timestamp. Runtime records the post-reopen access exception type; exact GDI+ exception text is observational. |
| Cache disposal hazard | Default `SingleFrame.Clear` disposes `frame.Image`; `FrameGeneratorImageFile` still returns that same disposed instance. Executable Windows assertion and observation. |

The cache observation is a reason to restrict the portable generator to standalone synchronous use. No cache ownership behavior is changed or declared safe.

## Portable API behavior

`PortableImageFrameGenerator.Open(path)` decodes BMP/PNG using `PortableImageReader`. Every `Generate(long)` returns a new lightweight `PortableImageFrame`, with `Timestamp = t` and `PreviousTimestamp = unchecked(t - 1)`, sharing the one open `PixelBuffer`. It accepts any `long`; the legacy reader shell's `[0,249]` window is not reproduced by this generator. The frame exposes dimensions, format, alpha mode, stride and read-only row/memory views. The generator owns storage; frames are non-owning and non-disposable.

`Close` releases the one PixelBuffer and is sequentially idempotent. `Open` after `Close` is supported and produces new storage. `Dispose` closes and permanently ends the generator. New frame-memory access after close throws through PixelBuffer's disposed checks. Previously copied `ReadOnlyMemory<byte>` views cannot be revoked; consumers must not keep or access them outside the generator lifetime. Concurrency and asynchronous retention are unsupported.

Outputs are inherited from the portable reader: BMP opaque images use RGB24/`AlphaMode.None`; alpha-bearing PNG outputs use RGBA32/straight alpha. Decoded row values and dimensions are validated against deterministic expected formulas and the committed candidate fixtures. No Windows type appears in the portable library API.

## Local verification

Host: Gentoo Linux, x64, .NET SDK 10.0.111. NuGet audit could not reach `api.nuget.org`, so local builds used `-p:NuGetAudit=false`; this affects vulnerability metadata retrieval only. The SDK installation also lacks workload resolver locator directories; builds used `-p:MSBuildEnableWorkloadResolver=false` for these BCL/net10 projects. No production legacy source was modified.

Executed before final CI:

```sh
dotnet build tests/PortableFrameGenerator.Tests/PortableFrameGenerator.Tests.csproj -c Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -m:1
dotnet tests/PortableFrameGenerator.Tests/bin/Release/net10.0/PortableFrameGenerator.Tests.dll
dotnet build tests/PortableImageReader.Tests/PortableImageReader.Tests.csproj -c Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -m:1
dotnet tests/PortableImageReader.Tests/bin/Release/net10.0/PortableImageReader.Tests.dll --measure
dotnet run --project tests/PortablePixels.Tests/PortablePixels.Tests.csproj -c Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false
```

Results: generator build passed with zero warnings/errors and 617 assertions passed after the initial implementation (the final count is recorded by CI); reader passed 1,322 assertions; pixel core passed 10/10. Actual Windows reader execution and dynamic comparison are `NOT_RUN` locally and are required from the Windows CI job.

The first normal restore attempt failed with NU1900 because NuGet vulnerability metadata was unreachable. An initial build also exposed a C# namespace/type collision (`PortableImageReader`); it was corrected by an explicit type alias and the subsequent build passed. A generic `dotnet test --no-build` without a project was not applicable and failed in MSBuild worker-pipe startup under the restricted environment; it is not counted as test execution.

## Parity status

| Property | Status |
|---|---|
| Candidate BMP/PNG dimensions, RGB(A) byte values and declared format/alpha | Portable contract `PASS` locally for two fixtures; comparison to fresh actual Windows reader `NOT_RUN` until CI. |
| Timestamp values `t` / `t-1` | Portable contract `PASS` for initial and arbitrary timestamps; legacy characterization expected to `PASS` on Windows CI. The standalone generator has no reader-shell navigation/window behavior. |
| Same underlying image storage reused for multiple timestamps | Portable generator `PASS` via array identity checks; legacy bitmap identity is characterized by the Windows harness. |
| Close/reopen and sequential disposal | Portable tests `PASS`; direct legacy lifecycle comparison requires Windows CI. |
| Legacy cache integration / eviction ownership | Unsupported; characterized legacy default disposal can invalidate the generator's shared bitmap. |
| Concurrent or asynchronous use | Unsupported and untested. |
| Playback, UI, thumbnail/export integration | `NOT_ESTABLISHED`. |
| Approved golden parity | `NOT_ESTABLISHED`; candidate baseline is not approved. |

This task introduces no new full-frame copy: it retains the `PixelBuffer` returned by the image reader and creates frame wrappers over it. The reader's decode copy/allocation behavior remains as recorded in [portable image reader v1](portable-image-reader-v1.md). No frame-generator performance claim is made.

## Windows CI evidence

To be completed from the final commit's workflow run: source SHA, Windows Server 2022 image, SDK/runtime, legacy case/assertion count, portable test counts, comparison result and uploaded artifact hashes. Until that run passes, Windows characterization and cross-process pixel comparisons remain `NOT_RUN`.
