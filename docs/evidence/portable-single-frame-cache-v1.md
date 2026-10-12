# Portable SingleFrame cache ownership v1

Status: implementation candidate. This does not integrate the cache with production playback or approve the Windows Bitmap baseline.

## Provenance and scope

- Base branch: `port/net10-linux`, with PR #7 merged at `3d7a8bc4a184869d278fb4bb05f385cac021e977`.
- Task branch: `port/portable-cache-ownership`.
- Tested branch source commit: `1762851bbeffd556452a46aa0c29b8fcf4184700` (the Windows environment manifest also records the GitHub PR merge-checkout SHA `fd45eabcf994f66c40401f9ea051b1c2963082e0`).
- Legacy implementation: actual `Kinovea.Video.Caching.SingleFrame`, `VideoFrame`, `FrameGeneratorImageFile` and `VideoReaderBitmap` source; the Windows harness invokes the real .NET Framework 4.8 reader and generator classes.
- Windows trace schema: `single-frame-cache-traces.v1.json`, marked `CANDIDATE_NOT_APPROVED` and uploaded with the workflow artifacts. It records operation results, wrapper and Bitmap identities, timestamp fields, cache empty state and current/external pixel-access outcomes.
- Local source under test: `PortableSingleFrameCache`, layered over the merged `PortableReaderShell` without a legacy reference or production call-site change.

The original search recorded here covered C# construction sites and missed the production C++/CLI call in `Kinovea.Video.FFMpeg/PlayerServer/VideoReaderFFMpeg.cpp:45-49`, where all three cache types are constructed with the native `DisposeFrame` callback. `SingleFrame` is production-used by FFmpeg OnDemand. The `FrameGeneratorImageFile` plus `SingleFrame` pairing characterized below is still an isolated misuse probe: the production still-image reader uses no cache. PR #9's [production cache audit](production-video-cache-lifetimes-v1.md) expands the call-site search to C++/CLI and characterizes the actual C# Cache and PreBuffer2 implementations separately. No full FFmpeg playback claim is made.

## Legacy observations from source and characterization

| Operation | Legacy source behavior | Executable Windows assertion/trace |
|---|---|---|
| Insert | `SingleFrame.Add` calls `Clear`, then stores the exact `VideoFrame` wrapper and returns `Added`. | Wrapper identity and `Added` result were recorded in Windows run `38247808240`. |
| Read current repeatedly | Getter returns the same stored wrapper. | Identity relation is recorded and checked against the versioned trace. |
| Replace with distinct image | Default `Clear` disposes the old frame's Bitmap, then `Add` stores the new wrapper. | Old external Bitmap becomes unusable; new Bitmap remains usable. |
| Clear with external reference | Default path calls `current.Image.Dispose()` before setting `current = null`; external references are not protected. | The trace records a disposed-image pixel-access exception after Clear. |
| Custom disposer | If supplied, the callback replaces default disposal and receives the exact cached frame. | No-op callback is called once; the Bitmap remains usable until its generator closes it. |
| Reused generator Bitmap | `FrameGeneratorImageFile.Generate` returns the same stored Bitmap for every timestamp; its `DisposePrevious` is a no-op. `SingleFrame.Add` of the next wrapper first disposes that same object. | Trace records that the new cached wrapper points to an unusable Bitmap. This is a constructed contract probe, not a found production call sequence. |
| Generator close/reopen while cached | Generator close disposes its image while the cache keeps its wrapper. Reopen creates a new Bitmap but does not update the cache. | Trace records stale wrapper identity and failed pixel access, then a fresh wrapper/image after explicit replacement. |
| Dispose | `Dispose()` calls `Clear()`; repeated sequential calls on an empty cache are harmless. Its finalizer invokes `Dispose(false)`, which does not clear. | Populated Dispose and repeated Dispose are recorded and asserted in the Windows harness. |

The Windows trace and all values remain candidate observations. The existing candidate golden directory is unchanged and remains `PENDING_HUMAN_APPROVAL`.

## Ownership options and selection

| Option | Behavior and tradeoffs | Decision |
|---|---|---|
| A. Generator owns storage; cache borrows frame references | No frame copy or new disposal mechanism. Cache clear only drops its reference. A borrower must stop reading before the reader closes. | **Selected** for the isolated synchronous shell boundary. It matches the current portable generator owner and requires the least code. |
| B. Cache-owned independent pixel copy | Would let a frame outlive the generator, but adds a full-frame copy/allocation per insertion and changes identity/storage reuse. No measured consumer requirement currently justifies it. | Deferred. |
| C. Adapter-specific disposer callback | A no-op `SingleFrame` callback could preserve generator-owned Bitmap lifetime, but the legacy delegate is tied to `VideoFrame` and is not available to the portable shell. An equivalent mechanism is unnecessary when the portable cache simply borrows. | Not added. The Windows harness characterizes the legacy callback to make its behavior explicit. |
| D. Retained ownership/reference-counted leases | Could support consumers outliving the generator or concurrent/asynchronous work, but adds synchronization and lifetime semantics not required for the scoped synchronous path. | Not added. Revisit only after a concrete retention use case and failing test. |

## Portable contract and lifecycle

`PortableSingleFrameCache` receives a `PortableReaderShell` owned by the caller. The shell remains the sole `PixelBuffer` owner.

1. `Open` clears the cache slot before asking the shell to open/replace an image.
2. `MoveRequest` and `PlayerRequest` preserve the shell's result, timestamp selection, and current wrapper identity; the cache points to the shell's current frame only while the shell is loaded.
3. `Clear` sets the cache slot to null and does not dispose or mutate the frame or pixel storage. A previously held frame remains readable while the reader remains open.
4. `Dispose` has cache-only semantics: it clears the slot and prevents further navigation through the adapter. It does **not** dispose the caller-owned shell. The caller must still close/dispose that shell.
5. `Close` clears the slot first, then closes the reader. Newly requested pixel access through previously returned frames throws `ObjectDisposedException`. Previously copied `ReadOnlyMemory<byte>` values cannot be revoked; they remain borrowed and must not be used after owner close.
6. Calls are synchronous and externally serialized. Clear cached frames before directly closing the supplied shell. Closing the shell behind a non-empty cache leaves a stale wrapper in that slot; new pixel access throws `ObjectDisposedException`. That misuse is tested and documented, not supported.
7. The cache must not retain frames after shell close/replacement. Asynchronous work, `Cache`, `PreBuffer2`, camera callbacks, native consumers and playback integration remain outside this contract.

The adapter does not implement the legacy `IFrameGenerator`, `VideoFrame`, `SingleFrame`, `VideoFrameDisposer` or any production registration point.

## Verification

Local host: Gentoo Linux x86_64, kernel `6.18.50-gentoo-dist-bin`; .NET SDK `10.0.111`, host/runtime `10.0.11`, MSBuild `18.0.11`.

| Suite / command | Result |
|---|---|
| `dotnet run --project tests/PortablePixels.Tests/PortablePixels.Tests.csproj -c Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -m:1` | `PASS`, 10/10 |
| Build `tests/PortableImageReader.Tests/PortableImageReader.Tests.csproj` with `-c Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -m:1`, then execute `dotnet tests/PortableImageReader.Tests/bin/Release/net10.0/PortableImageReader.Tests.dll` | `PASS`, 1,322 assertions |
| Build and execute `tests/PortableFrameGenerator.Tests/PortableFrameGenerator.Tests.csproj` with the same build flags | `PASS`, 617 assertions |
| Build and execute `tests/PortableReaderShell.Tests/PortableReaderShell.Tests.csproj` with the same build flags | `PASS`, 65 assertions; Windows legacy trace comparison `NOT_RUN` locally |
| Build and execute `tests/PortableSingleFrame.Tests/Kinovea.PortableSingleFrame.Tests.csproj` with the same build flags | `PASS`, 73 assertions |
| `git diff --check`; Python YAML parse of `.github/workflows/portable-frame-integration.yml` | `PASS` |
| Actual .NET Framework 4.8 Windows characterization and Windows .NET 10 run | `PASS` in GitHub Actions run [38247808240](https://github.com/jvteodoro/Kinovea/actions/runs/38247808240); Windows/Linux portable jobs and actual net48 legacy run all passed |

Exact successful single-frame commands:

```sh
dotnet build tests/PortableSingleFrame.Tests/Kinovea.PortableSingleFrame.Tests.csproj -c Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -m:1
dotnet tests/PortableSingleFrame.Tests/bin/Release/net10.0/Kinovea.PortableSingleFrame.Tests.dll
```

The local adapter suite checks open, empty/current slot, first and repeated requests, sequential replacement, timestamp values, storage reuse by backing-array identity, external frame readability after cache replacement and `Clear`, working-zone boundaries, player timestamp/step selection, close/reopen, failed-open cleanup/recovery, cache-only Dispose, repeated disposal, and invalid use after Dispose/owner close.

## Windows/Linux CI evidence and hashes

GitHub Actions run [38247808240](https://github.com/jvteodoro/Kinovea/actions/runs/38247808240) completed successfully at source commit `1762851bbeffd556452a46aa0c29b8fcf4184700`. It ran the updated `.github/workflows/portable-frame-integration.yml` on Ubuntu 24.04 and Windows Server 2022. Both used .NET SDK `10.0.401`, MSBuild `18.9.11`, host/runtime `10.0.12`.

| CI check | Result |
|---|---|
| Portable Pixel Core — Ubuntu / Windows | `PASS`, 10/10 each |
| Portable image reader — Ubuntu / Windows | `PASS`, 1,322 assertions each |
| Portable frame generator — Ubuntu / Windows | `PASS`, 617 assertions each |
| Portable reader shell — Ubuntu / Windows | `PASS`, 65 assertions each; legacy state trace `NOT_RUN` in these matrix jobs |
| Portable SingleFrame cache — Ubuntu / Windows | `PASS`, 73 assertions each |
| Actual .NET Framework 4.8 characterization — Windows | `PASS`, 23 cases / 674 assertions, 0 failures |
| Portable frame generator vs. fresh actual-reader captures — Windows | `PASS`, 1,233 assertions across BMP and PNG |
| Portable reader shell vs. actual legacy state trace — Windows | `PASS`, 411 assertions |
| Portable SingleFrame expected-behavior vs. actual legacy trace — Windows | `PASS`, 14 assertions |

The Windows runner was `windows-2022`, image `20261004.326.1`, OS `10.0.20348`, 64-bit. The .NET Framework 4.8 targeting pack was available (`Release=533325`; `mscorlib.dll`, `System.dll`, and `System.Drawing.dll` all present). Visual Studio MSBuild was `17.14.60.43110`; CLR was `4.0.30319.42000`; GDI+ was `10.0.20348.5622`. The artifact source manifest contained 52 entries and no missing files. Its embedded environment `gitSha` is the workflow merge-checkout SHA above; the workflow API's branch `headSha` is the tested branch source commit.

The successful Windows legacy artifact was `portable-frame-windows-legacy-38247808240-1` (artifact ID `11745979248`). Independently recalculated SHA-256 values:

| Evidence file | SHA-256 |
|---|---|
| `single-frame-cache-traces.v1.json` | `5efacd9bc0ce976d28bdaf2f3ac60e69704cadcdb908efa32b9a6fb1142751df` |
| `test-results.json` | `26d4fe9ca4e3432e66c3394a2b3b0146017a2ddef78e6ea88663f89aab781255` |
| `source-hashes.json` | `527a472c97c8764f048e0b1e14bb4e21ffb53abff60bf31a9b66d85c1a075886` |
| `fixture-manifest.json` | `4853cd206cbdf49f591ce55307b99ff3ec09b0f8deeea14b378f597320e8db2a` |
| `runtime-metadata.json` | `55a2791adbececa59c5a1ff82775b89e129c2529c75ad996755dcd00a0f77a7b` |
| `single-frame-cache-comparison.log` | `6652467b221f88696272d7e4e59c45b78479a9358760026715785efec2c058e1` |
| Ubuntu portable test log | `48b82a7de1174ae022fe1d64b0577a8950ffea670c0164f49f55b60626e5e638` |
| Windows portable test log | `79b1b4bdf1a0d289f4f2e157920b43e04cd44ad0683292757e5055a314022275` |

The first PR run, `38247645398` at source `85beb08086b20cd3691cfbc390e64b91445f6e6f`, failed while compiling the characterization harness because of a misspelled local name; characterization did not run. Commit `1762851b` corrected that typo. The rerun above passed. The additional PR checks also passed: [38247808024](https://github.com/jvteodoro/Kinovea/actions/runs/38247808024) and [38247814014](https://github.com/jvteodoro/Kinovea/actions/runs/38247814014). The failure is retained here and not counted as a successful test.

## Copies, allocations, and release observations

- Cache insertion/navigation does not copy frame pixels. The tests compare the managed backing-array identity before/after frame replacement and timestamp changes.
- One warmed local allocation sample measured `0` managed bytes for a repeated-current request and `80` bytes for a changed-timestamp request. This is descriptive for one Gentoo/.NET 10.0.11 run, not a budget.
- The adapter has no pixel owner and makes no disposal calls. `Clear` and cache `Dispose` are verified by continued pixel readability. The reader's `Close` then makes new frame pixel access fail; the shell/generator source nulls its owner before disposal, so repeated sequential close does not dispose it again. Exact disposal-call counts are source-inspected, not instrumented by this test.
- The portable frame wrapper and backing storage are separate from GDI+ objects. No claim is made about native resource counts or Windows GDI+ disposal timing from Linux tests.

## Remaining evidence limits

- The PR #8 source search missed the production C++/CLI construction/use of legacy `SingleFrame`; it is used by FFmpeg OnDemand (`VideoReaderFFMpeg.cpp:45-49`). The generator/cache hazard above is still a constructed probe and does not characterize the FFmpeg call sequence. PR #9's [production cache audit](production-video-cache-lifetimes-v1.md) corrects the inventory and directly tests the C# `Cache` and `PreBuffer2` classes.
- Approved-golden parity, Windows/Linux reader parity beyond existing scoped reader-shell evidence, cache retention across reader close, concurrent/asynchronous access, application playback and production integration: `NOT_ESTABLISHED`.
