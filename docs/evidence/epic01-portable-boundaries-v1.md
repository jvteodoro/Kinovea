# Evidence: Epic 01 portable contracts and ownership v1

**Beads:** `Kinovea-gh14`, `Kinovea-gh24`–`Kinovea-gh27`
**Branch:** `port/epic01-api-inventory`
**Base source:** `78dad5ad3897b12596cdebdb7821454b009b4fe8`
**Inventory commit:** `8da04385`
**Implementation source commit:** `6dd0dc2be81f5619ee6d58b3460a315840ed969e`
**Host:** Gentoo Linux x86_64, kernel `6.18.50`
**SDK/runtime:** .NET SDK `10.0.111`, MSBuild `18.0.11`, runtime `10.0.11`
**Execution date:** 2026-10-22

## Scope and selected boundaries

The production graph and Windows-specific API inventory are recorded separately in [Epic 01 API dependency inventory v1](epic01-api-inventory-v1.md). This implementation adds a parallel .NET 10 seam only:

- `IPortableFrameSource` provides synchronous open/generate/close and dimensions. It does not declare an FPS, timestamp unit, or CFR requirement. The source returns the actual timestamp pair on `PortableImageFrame`.
- `PortableReaderRegistry` selects a source factory by case-insensitive file extension. Duplicate and missing registrations fail deterministically. Registration is expected to finish before reader use, and a factory must return a fresh source instance.
- `PortableReaderShell` delegates decoded frame generation to its selected source and keeps the existing fixed still-image navigation profile: 25 fps, 40 ms per step, timestamp window `[0,249]`. It is not a general video or VFR navigator.
- `Kinovea.PortableServices` compiles the exact, unchanged `TimeHelper.cs`, `TimeCodeFormat.cs`, `TimeCodeType.cs`, and `TimestampMapper.cs` source files into a separate `net10.0` assembly. It does not reference the legacy `Kinovea.Services` assembly.
- Existing `Kernel`, `VideoTypeManager`, `VideoReader`, `VideoFrame`, caches, `IFrameGenerator`, WinForms, FFmpeg and legacy Services files/project references are unchanged.

The selected boundary is an opt-in registry and source interface, not a bootstrap replacement. The legacy reader selection path is not invoked by this portable registry.

## Requirements-to-tests and inspection

| Claim | Evidence class | Executable check or source inspected | Result |
|---|---|---|---|
| Existing still-image open, first timestamp `0/-1`, seek, repeated request identity, fixed reader metadata, close/reopen and source storage reuse remain working. | Tested portable contract; candidate legacy trace comparison not run | `tests/PortableReaderShell.Tests/Program.cs` existing scenarios | `PASS`, 96 assertions total on Linux |
| Extension matching is case-insensitive; unregistered and duplicate extensions are rejected. | Tested | `RegistrySelectionAndLifetime` and `RegistryFailures` | `PASS` |
| A selected source's actual timestamp and `PreviousTimestamp` reach `PortableReaderFrame` without conversion. | Tested | Authored source maps request `20` to frame timestamps `17/11`; repeat uses the same wrapper and does not regenerate. | `PASS` |
| Successful source replacement disposes the previous owner's buffer, creates independent storage, and refreshes the wrapper for the same requested timestamp. | Tested | `SourceReplacementOwnership` | `PASS` |
| Candidate-open exception disposes both failed candidate and active source; repeated close/dispose do not dispose them again. | Tested | `RegistryFailures` and `SourceReplacementOwnership` | `PASS` |
| Timecode output retains current behavior across declared formats, culture, rounding, signs, normalization, and timestamp narrowing. | Tested exact linked legacy source on Linux | `tests/Timecode.Characterization` now references `Kinovea.PortableServices` | `PASS`, 50 assertions |
| The portable desktop presentation suite builds against the updated reader shell. | Build only | `dotnet build tests/Kinovea.PortableDesktop.Tests/...` and app build | `PASS`, 0 warnings/errors |
| Actual Avalonia presentation test runs in this execution environment. | Attempted, environment blocked | `dotnet run --project tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj --configuration Release --no-build --no-restore` | `NOT_RUN`: exit 1, `XOpenDisplay failed`; this shell had `WAYLAND_DISPLAY=wayland-0` and no `DISPLAY` value |
| Reader shell trace matches actual Windows `VideoReaderBitmap` operation outputs. | Not executed | The expected `reader-lifecycle-trace-v1.json` is absent from the candidate directory; no Windows reader was run locally. | `NOT_RUN` |
| Concurrent read and disposal, concurrent source open/close, or async frame retention are safe. | Explicitly unsupported, not tested | `IPortableFrameSource` and `PortableImageFrameGenerator` document synchronous external serialization. | `NOT_ESTABLISHED`; callers must coordinate access and lifetime |
| Native or pooled memory owner behaves like the test managed array owner. | Unverified | Tests use a managed array-backed `IMemoryOwner<byte>`. | `NOT_ESTABLISHED` |

The pre-existing `PixelBuffer` also uses an ordinary unsynchronized `bool disposed`; it rejects new `Memory` and row access after sequential disposal, but cannot revoke a `ReadOnlyMemory<byte>` view that a caller already copied. Those v0.1 constraints remain. No locks, pooling, leases, reference counting, copy-on-write or cache changes were added.

## Commands and results

Commands were executed from the repository root with the current SDK/runtime listed above. Project builds used `-m:1 -p:UseSharedCompilation=false`; restore used `-p:NuGetAudit=false` where dependencies had to be restored because sandboxed NuGet vulnerability-metadata access failed. Test executables were then run with `--no-build --no-restore` where noted.

| Command | Result |
|---|---|
| `dotnet build tests/PortableReaderShell.Tests/PortableReaderShell.Tests.csproj --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false -v:minimal` | Exit 0; 0 warnings, 0 errors |
| `dotnet run --project tests/PortableReaderShell.Tests/PortableReaderShell.Tests.csproj --configuration Release --no-build --no-restore` | Exit 0; 96 assertions. Existing one-operation sample: repeated-current 0 managed bytes, changed timestamp 80 bytes; no full-frame copies observed by backing-array identity. |
| `dotnet run --project tests/Timecode.Characterization/Timecode.Characterization.csproj --configuration Release --no-restore` | Exit 0; 50 assertions |
| `dotnet run --project tests/PortablePixels.Tests/PortablePixels.Tests.csproj --configuration Release -p:NuGetAudit=false -p:UseSharedCompilation=false` | Exit 0; 10 passed, 0 failed |
| `dotnet build tests/PortableImageReader.Tests/PortableImageReader.Tests.csproj --configuration Release -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false -v:minimal`; then `dotnet run --project tests/PortableImageReader.Tests/PortableImageReader.Tests.csproj --configuration Release --no-build --no-restore` | Build exit 0, 0 warnings/errors; exit 0, 1,322 assertions |
| `dotnet build tests/PortableFrameGenerator.Tests/PortableFrameGenerator.Tests.csproj --configuration Release -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false -v:minimal`; then `dotnet run --project tests/PortableFrameGenerator.Tests/PortableFrameGenerator.Tests.csproj --configuration Release --no-build --no-restore` | Build exit 0, 0 warnings/errors; exit 0, 617 assertions; legacy capture `NOT_RUN` |
| `dotnet build tests/PortableSingleFrame.Tests/Kinovea.PortableSingleFrame.Tests.csproj --configuration Release --no-restore -m:1 -p:UseSharedCompilation=false -v:minimal`; then `dotnet run --project tests/PortableSingleFrame.Tests/Kinovea.PortableSingleFrame.Tests.csproj --configuration Release --no-build --no-restore` | Build exit 0, 0 warnings/errors; exit 0, 73 assertions |
| `dotnet build tests/PortableStillImageHost.Tests/Kinovea.PortableStillImageHost.Tests.csproj --configuration Release -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false -v:minimal`; then `dotnet run --project tests/PortableStillImageHost.Tests/Kinovea.PortableStillImageHost.Tests.csproj --configuration Release --no-build --no-restore` | Build exit 0, 0 warnings/errors; exit 0, 43 assertions |
| `dotnet build tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj --configuration Release -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false -v:minimal`; `dotnet build src/Kinovea.PortableDesktop/Kinovea.PortableDesktop.csproj --configuration Release -m:1 -p:NuGetAudit=false -p:UseSharedCompilation=false -v:minimal` | Both builds exit 0, 0 warnings/errors |
| `dotnet run --project tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj --configuration Release --no-build --no-restore` | Exit 1 before assertions because `DISPLAY` was unset and Avalonia's X11 initialization reported `XOpenDisplay failed`. GUI test `NOT_RUN`. |
| `git diff --check` | Exit 0 after documentation changes |

No hosted GitHub Actions were triggered. Windows execution and compatibility are `NOT_RUN`.

## Fixture provenance

The test shell uses the existing candidate files; they were not changed or regenerated.

| Candidate input | SHA-256 | Approval |
|---|---|---|
| `tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f/fixtures/bmp-rgb-7x5.bmp` | `85e0feff83d9920703cbf8be2e6ccc76741da22b4ffe791d5d4c415724de1244` | Candidate, unapproved |
| `tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f/fixtures/png-alpha-13x9.png` | `e0759d696bfef305e0a2c1c8fc7b7673e9e46390ef15beb541679c6766041dcd` | Candidate, unapproved |

## Remaining limits

- `PortableReaderShell` retains the measured still-image timebase/window. No moving-image navigation contract, VFR seek mapping, frame stepping policy, or video metadata format is implemented.
- The registry is not connected to `Kernel`, `VideoTypeManager`, production playback, or the legacy .NET Framework reader.
- The existing actual-reader Windows trace cannot be replayed because the file is absent in this branch; this work adds no new Windows evidence or golden data.
- Linux tests cover the supported managed source lifecycle. Windows runtime, native-owner behavior, concurrency, asynchronous retention, GUI presentation in this process, and user-visible legacy parity remain unverified.
- `Kinovea.Services` remains net48 and mixed-platform. Only the exact timecode source slice is available through the new net10 assembly. Preferences, log routing, WMI, shell/file helpers, WinForms key bindings and native APIs were not moved.
