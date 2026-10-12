# Portable reader shell v1 state machine and evidence

Status: portable shell tested on Linux and Windows; scoped legacy navigation comparison passed on Windows. This remains an implementation candidate with no production reader replacement or legacy cache connection. The Windows trace is candidate evidence, not an approved golden.

## Baseline and state model

PR #6 is merged into `port/net10-linux` at `48cc834292568455bc5e78d5a56c9d76178719d3`. The new work branch is based on that commit. Relevant behavior was checked against `VideoReaderBitmap.cs`, `VideoReader.cs`, `FrameGeneratorImageFile.cs`, `VideoFrame.cs`, and the PR #6 Windows lifecycle evidence. The characterization harness invokes the actual net48 `VideoReaderBitmap`.

Legacy stored state consists of `generator`, `initialized`, `firstFrame`, `current`, `workingZone`, `videoInfo`, geometry and cache snapshot. A useful external-state projection is `Unopened`, `Open`, `Closed`, `FailedOpen`, and `Disposed` (the last state does not exist in the original reader). `Current` starts at `VideoFrame.Empty()` (`null`, `-1`, `-1`); `workingZone` starts as the zero-initialized `VideoSection [0,0]`; the `VideoInfo` value fields start at zero.

| Current state | Operation and input | Legacy result and next state | Current frame identity / ownership |
|---|---|---|---|
| Unopened | Construct | `Loaded=false`; current is empty; `firstFrame=true`. | Empty `VideoFrame` object exists; no generated image. |
| Unopened | `Close()` | Source calls `generator.Close()` with null generator and throws `NullReferenceException`. | No state transition is completed. This is a legacy defect; the portable shell will make close idempotent. |
| Unopened | `MoveRequest(true, any)` | First-frame override sets target 0 and consumes `firstFrame`; return true; no generator means current stays empty. | Empty frame identity remains. The shell will preserve this observed result. |
| Unopened | `PlayerRequest(action, ref)` | Computes target by action, `UpdateCurrent` returns because generator is null; return true. | Empty frame remains. |
| Unopened | Open supported valid BMP/PNG | Return `Success`, set loaded, metadata and working zone `[0,249]`. `current` and `firstFrame` are not reset. | Current wrapper and its image are retained. |
| Unopened | Open corrupt supported-extension image | For the characterized corrupt PNG, result is `NotSupported`, loaded remains false; a generator with a 4x4 error placeholder remains assigned. Metadata/zone remain default. | Current stays empty until a request; then the legacy generator can return its placeholder. Portable shell will return `NotSupported` but leave current empty and will not synthesize a fake frame. |
| Open | `MoveRequest(true, target)` | Compute rounded `Current.Timestamp + AverageTimeStampsPerFrame`; the first-frame flag overrides with 0. If target is above zone end, return false without update; otherwise update and return true. | A changed timestamp creates a new `VideoFrame`, but the one-image generator returns the same Bitmap. |
| Open | `MoveRequest(false, target)` | Attempt exact target; outside-zone or below-zone values do not update; method still returns true. | Same timestamp early-return retains the exact `VideoFrame`; changed timestamp creates a new wrapper over same Bitmap. |
| Open | `PlayerRequest(Playback/Timestamp, ref)` | Use reference timestamp. Step actions use rounded current timestamp ± average interval. Call update and always return true, including out-of-zone. | Same selection and same shared Bitmap behavior. |
| Open | Working-zone update | Replace zone directly; no validation and no frame update. | Current is unchanged. Successful open resets zone to `[0,249]`. |
| Open | Repeated request for current timestamp | Return true for MoveRequest; `UpdateCurrent` returns before generation. | Same `VideoFrame` and Bitmap references. |
| Open | Close | Generator closes, loaded becomes false; current, metadata and working zone remain. | Current wrapper remains and still references the disposed Bitmap. GDI+ pixel access fails. |
| Closed | Close again | `FrameGeneratorImageFile.Close` disposes the same Bitmap again; call completes on the characterized GDI+ runtime. | Stale current remains. |
| Closed | Open valid | New generator opens; current and `firstFrame` are not reset. | Current may still refer to the prior disposed Bitmap. |
| Closed | Seek same timestamp as current | Timestamp equality returns before generation. | Same stale wrapper and disposed Bitmap remain. Observed GDI+ access failure is `System.ArgumentException`. |
| Closed | Seek a different in-zone timestamp | New generator's `Generate` returns its new image; wrapper changes. | Fresh image replaces stale one. |
| Any live legacy state | Failed open | A new generator replaces the field; result is `NotSupported`, `initialized=false`; existing info, zone and current are not reset. | Old frame may remain usable, while a seek can return the new generator's 4x4 error placeholder. The exact lifetime of the abandoned old generator is not guaranteed. |

Legacy timing after successful open is fixed: first timestamp 0, last timestamp field 0, average 1 timestamp/frame, 25 fps, duration 250 timestamps, 40 ms interval, and zone `[0,249]`. `PreviousTimestamp = timestamp - (long)AverageTimeStampsPerFrame`, which is `t-1` for the configured image reader. There is no reader-level `Dispose`; `Close` is its resource release operation.

## Planned portable behavior and safe deviations

The standalone shell will preserve frame selection, boolean return values, default timing, current wrapper identity on repeated same-timestamp requests, default working-zone bounds, open/close state, and storage reuse. It will expose a terminal `Disposed` state because the portable component implements `IDisposable`.

Safe differences are constrained to legacy defects/resource hazards:

- Closing before open is idempotent instead of throwing `NullReferenceException`.
- A failed decode returns the legacy-compatible `NotSupported` result category but closes any previous portable owner; no 4x4 synthetic error bitmap is returned. Failed-open navigation while closed leaves Current unchanged.
- Close preserves Current timestamps and wrapper identity, but pixel reads on its disposed `PixelBuffer` throw `ObjectDisposedException`; previously borrowed `ReadOnlyMemory<byte>` views cannot be revoked. On successful reopen, a same-timestamp request refreshes a stale frame instead of leaving it unusable.
- New BMP/PNG reader behavior is limited to the established PortableImageReader contract. BMP and PNG fixture parity does not prove all EXIF or decoder edge cases.

These differences do not change production behavior; the new shell remains opt-in and detached from Kinovea's caches, UI, exports and reader registration. Calls are synchronous and externally serialized.

## Versioned trace

The Windows characterization runner writes `reader-state-traces.v1.json` with schema version 1, sequences, operation names, inputs, result/error type, and post-operation state. Each state includes loaded status, timestamps, frame/image identity numbers, working-zone bounds, timing metadata, and a logical pixel probe or access error. Fixture names are used instead of absolute paths in operation inputs. The trace is candidate evidence from a specific Windows run, not an approved golden.

The four trace sequences are `navigation-close-reopen`, `failed-open-recovery`, `failed-open-after-valid`, and `close-unopened`. The portable test suite executes navigation and recovery scenarios on Linux and Windows. The Windows comparison replays the navigation sequence from the fresh legacy trace and checks method results, loaded state, current/previous timestamps, zone, metadata, dimensions, first-pixel values, and frame/image identity through close and reopen. It explicitly allows the fresh frame at same-timestamp reopen as the documented safe deviation. Failed-open traces assert the legacy placeholder/retained-image observations and the shell's safe release/recovery behavior. Trace status remains `CANDIDATE_NOT_APPROVED`.

## Implementation and local verification

The implemented API is `PortableReaderShell` in `src/Kinovea.PortableReaderShell`. It composes the existing `PortableImageFrameGenerator`; that generator owns the sole decoded `PixelBuffer`. `PortableReaderFrame` holds a borrowed generator frame plus the legacy `long` timestamp pair. `Close` preserves current wrapper and metadata while closing the pixel owner. Calls are synchronous and require external serialization. There is no `IFrameGenerator`, cache, UI, export, thumbnail, or production reader registration.

The shell test suite covers a fresh open/first frame, initial empty frame, pre-open first-frame-flag consumption, sequential and repeated timestamp requests, forward/backward seeks, both sides of the default working-zone bounds, PlayerRequest step behavior, BMP/PNG metadata, shared backing-array identity, corrupt-image recovery, close/reopen, stale-frame refresh, and repeated close/dispose. It uses the committed candidate BMP/PNG fixtures and does not write golden data.

Local host: Gentoo Linux x86_64, .NET SDK `10.0.111`, runtime `10.0.11`. Executed:

```sh
dotnet build tests/PortableReaderShell.Tests/PortableReaderShell.Tests.csproj -c Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -m:1
dotnet tests/PortableReaderShell.Tests/bin/Release/net10.0/PortableReaderShell.Tests.dll
```

Result: build `PASS`, 0 warnings/0 errors; portable shell `PASS`, 65 assertions. `Legacy state trace comparison: NOT_RUN` locally because the trace requires Windows .NET Framework 4.8 execution. Initial restore/build commands without the offline/workload flags failed before compilation; no test result is inferred from those attempts.

## Property status before CI

| Property | Status | Evidence / limitation |
|---|---|---|
| Portable shell first/sequential selection, timestamps and PreviousTimestamp | `PASS` Linux and Windows; matched legacy trace | 65 standalone assertions on each OS; Windows trace replay compared all 20 navigation operations. |
| Repeated current frame wrapper identity | `PASS` Linux and Windows; matched legacy trace | Same portable frame wrapper remains current on repeated timestamp; identity checked before close/reopen safe deviation. |
| Decoded storage reuse across timestamps | `PASS` Linux and Windows; matched legacy trace | Backing managed-array identity checked across multiple timestamp wrappers; trace confirms same legacy image identity during open interval. No decode/copy during navigation. |
| Metadata and default working zone | `PASS` Linux and Windows; matched legacy trace | Fixed 25 fps, average 1 timestamp/frame, duration 250, 40 ms and `[0,249]` matched. |
| Arbitrary forward/backward selection and return values | `PASS` Linux and Windows; matched legacy trace | Seeks tested at 37, 12, 249, 250 and -1; bool results, current and previous timestamps compared. |
| Logical pixel probe on selected frames | `PASS` Windows actual-reader comparison | RGB and alpha at `(0,0)` compared while live; full fixture pixels were independently compared by the existing reader/generator test. |
| Close/reopen and old frame access | `PASS` portable contract; intentional legacy deviation | New frame selected for same timestamp after reopen; old disposed PixelBuffer rejects fresh access. Legacy stale wrapper is preserved until re-request and then refreshed safely. |
| Actual net48 trace generation and replay | `PASS` Windows CI | Final run `38242577373`; legacy harness 22 cases/644 assertions, state trace replay 411 assertions. |
| Failed-open full state equivalence | `PASS` for characterized divergence | Windows trace asserts legacy's transparent 4x4 placeholder after seek; portable shell intentionally leaves Current empty and recovers to real decoded pixels. |
| Cache/PreBuffer integration | `NOT_ESTABLISHED` | Unsupported by design; no ownership retention or production call site changed. |
| Concurrent/asynchronous use | `NOT_ESTABLISHED` | Externally serialized synchronous use only. |
| Approved-golden parity | `NOT_ESTABLISHED` | The Windows candidate fixture remains unapproved. |

The workflow source is `.github/workflows/portable-frame-integration.yml`. It adds the shell suite to both OS matrix legs and compares a fresh Windows state trace after running the actual legacy reader.

## CI execution and provenance

Final GitHub Actions run [38242577373](https://github.com/jvteodoro/Kinovea/actions/runs/38242577373) passed on source SHA `5d4e796ac2844412b7c9289327b09a7e38e5398b`. Linux and Windows matrix runners used .NET SDK `10.0.401` / host `10.0.12`. Ubuntu runner reported 24.04; Windows runner image `20261004.326.1` reported Server 2022 (`10.0.20348`). The legacy Windows job verified the .NET Framework 4.8 targeting pack, used Visual Studio MSBuild `17.14.60.43110`, and reported GDI+ `10.0.20348.5622`. Its source manifest contained 48 entries and no missing inputs.

| Execution | Result |
|---|---|
| Portable Pixel Core — Ubuntu 24.04 | `PASS`, 10/10 tests |
| Portable image reader — Ubuntu 24.04 | `PASS`, 1,322 assertions |
| Portable frame generator — Ubuntu 24.04 | `PASS`, 617 assertions |
| Portable reader shell — Ubuntu 24.04 | `PASS`, 65 assertions; standalone scenarios, legacy trace `NOT_RUN` |
| Portable Pixel Core — Windows Server 2022 | `PASS`, 10/10 tests |
| Portable image reader — Windows Server 2022 | `PASS`, 1,322 assertions |
| Portable frame generator — Windows Server 2022 | `PASS`, 617 assertions |
| Portable reader shell — Windows Server 2022 | `PASS`, 65 assertions; standalone scenarios, legacy trace `NOT_RUN` |
| Actual legacy `VideoReaderBitmap` — Windows .NET Framework 4.8 | `PASS`, 22 cases / 636 assertions |
| Portable shell against fresh legacy state trace | `PASS`, 411 assertions over 20 navigation steps, both failed-open paths, and close-before-open observation |

The uploaded `reader-state-traces.v1.json` SHA-256 is `a7372df8e03e916b49a33c08fdbedb52a27ce37ccb3959f7fc9e7cc101259697`. It declares `CANDIDATE_NOT_APPROVED`. An initial run at SHA `3204cfaea26497c56719f0c25efa7c0b9f62e3d6` executed 22 cases / 636 assertions but failed one assertion because the harness compared the short exception name with a serialized full name. Commit `165076f2` corrected that; run `38241880986` passed the then-current 374 shell trace assertions. Run `38242219681` passed the additional fresh-failed-open comparison with 393 trace assertions. Final run `38242577373` covers both failed-open paths and passed 22 cases / 644 legacy assertions plus 411 trace assertions. The failed run is not counted as a pass.

The successful trace replay compares method results, current/previous timestamps, loaded state, working-zone bounds, fps/interval/duration/dimensions, `(0,0)` logical pixel values and frame/image identity through close and reopen. Failed-open traces from both unopened and valid-image states verify the legacy placeholder/retained-image behavior and the portable shell's deliberate release and clean recovery. It separately allows the safe refresh on same-timestamp reopen, where legacy returns the same stale wrapper with a disposed GDI+ image. This is a scoped characterization result, not full reader parity or approved-golden verification.
