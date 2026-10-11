# ADR 0003: Minimal portable frame boundary

Status: **PROPOSED — review required**
Date: 2026-10-11
Baseline: `port/net10-linux` at `125dbbb907f554162bc31e54f95090876a19773f`; candidate Windows Bitmap evidence is `PENDING_HUMAN_APPROVAL`.

## Decision principle

This is a portability project. Keep Kinovea's algorithms, user-visible behavior, timing, frame selection, cache policy, image composition, errors and resource lifetime behavior as they are unless a specific Windows-only dependency prevents a Linux implementation or safe native interop requires a localized change. A cleaner abstraction alone is not a reason to change behavior.

This ADR proposes a small pixel boundary for a parallel .NET 10 path. It does not replace the current `VideoFrame`, `IFrameGenerator`, caches, readers or UI in this change. Existing .NET Framework 4.8 Windows code stays available while the new path is built and compared. The Windows characterization is candidate evidence only; no baseline approval or Linux parity is claimed.

## Verified legacy contracts and measured limits

Production source inspected: `VideoFrame.cs`, `IFrameGenerator.cs`, `Delegates.cs`, `FrameGeneratorImageFile.cs`, `VideoReaderBitmap.cs`, Bitmap/GIF/SVG reader code, `SingleFrame.cs`, `Cache.cs`, `PreBuffer2.cs`, `FrameServerPlayer.cs`, `VideoExportSettings.cs`, OpenCV call sites and exporter entry points. The dependency map appears below; this is not an exhaustive inventory of unrelated `Bitmap` icons and drawing resources.

- `VideoFrame` is a plain wrapper containing `Bitmap Image`, `long Timestamp`, and `long PreviousTimestamp`; `Empty()` uses null and `-1`. The wrapper is not disposable.
- `IFrameGenerator` says the generator owns generated image memory. `VideoReaderBitmap.UpdateCurrent` calls `DisposePrevious` before replacing its current image. `FrameGeneratorImageFile.Generate` returns the same stored bitmap at every timestamp and `DisposePrevious` is a no-op. Closing the reader closes the generator and disposes the stored image. These observable lifetime rules must be characterized before changing them.
- Caches default to disposing `frame.Image` when clearing/evicting; an optional `VideoFrameDisposer` can substitute the release behavior. `SingleFrame` and `Cache` are synchronous per source comments; `PreBuffer2` decodes asynchronously and evicts frames. This is not enough evidence to assume consumers retain a frame after eviction or that reference-counting is needed.
- `ImageRetriever(VideoFrame, Bitmap)` fills a caller-provided destination; `FrameServerPlayer.EnumerateImages` reuses a staging bitmap and yields cloned images. Exporters consume Bitmap sequences. Preserve this composition, ordering and lifetime until executable tests show an adapter can reproduce them.
- Bitmap reader metadata uses a fixed 25 fps/40 ms interval and existing timestamp arithmetic. Do not introduce a new timestamp unit, rational clock, rounding rule, seek rule or previous-frame meaning into the compatibility path.
- `FrameGeneratorImageFile` loads via `new Bitmap(filename)`, sets 96 DPI, applies its existing EXIF-derived rotation, and maps mirror orientations to no internal rotation. Preserve that behavior, including surprising behavior. A proposed path that applies more complete EXIF mirroring is a separate behavior change.
- Candidate baseline examples: BMP 7x5 Format32bppRgb / stride 28; PNG 13x9 Format32bppArgb / stride 52; JPEG 11x7 Format24bppRgb / row payload 33 / stride 36. Runs 38175088092 and 38175219336 each reported 19 cases and 537 assertions, with 72 fixture/pixel files matching. Raw native byte channel interpretation and premultiplication were not independently asserted. EXIF pixel maps and lifecycle details also have gaps. See [the audit](../evidence/2026-10-11-bitmap-golden-audit.md) and [design evidence](../evidence/2026-10-11-portable-frame-contract-design.md).

## Compatibility-impact matrix

| Classification | Proposed modification | Existing behavior and technical reason | Required for .NET 10/Linux? Smallest change | Compatibility risk and regression required | More legacy-preserving alternative |
|---|---|---|---|---|---|
| `REQUIRED_FOR_PORTABILITY` | Do not expose `System.Drawing.Bitmap` from the *new cross-platform assembly boundary*. | Current shared contracts expose Bitmap; `System.Drawing.Common` is Windows-only in supported modern .NET. Plain metadata and algorithms do not need to change. | Yes, if that assembly must load/run on Linux. First add a parallel pixel-buffer contract; leave legacy `Kinovea.Video` net48 contracts intact. | Adapter may alter pixels or lifetime. Test legacy output independently and compare dimensions and logical pixels through a converter. | Keep all existing Windows projects/API unchanged and build a small new `net10.0` core alongside them.
| `REQUIRED_FOR_SAFE_INTEROP` | Describe buffer layout and ownership at native boundaries. | GDI+ `LockBits`, OpenCV `Mat`, FFmpeg and camera buffers have explicit stride, channel, pointer and lifetime requirements. Unspecified layout risks row/channel corruption or use-after-release. | Yes, only where bytes/pointers cross a boundary. A managed `byte[]` test producer can begin with a simple positive-stride layout; add signed-stride mapping only in an adapter that receives signed stride. | Copies, channel conversion or row flipping may change pixels and throughput. Test raw channels, padded rows, row origin, signed stride, alpha, disposal and pointer validity. | Keep existing GDI+ and native callers unchanged behind localized adapters. Borrow/copy only as the specific API requires; no global lease system by assumption.
| `LEGACY_BEHAVIOR_TO_PRESERVE` | Timestamp unit, `PreviousTimestamp`, seek/index/rounding, cache algorithm, rotation, EXIF mapping, thumbnail sizing/resampling, output composition and errors. | Current source implements observable semantics, including quirks. No portability evidence requires changing them. | No. Preserve existing values and decisions in the new producer/adapter. | Any delta is behavior-breaking. Execute Windows characterization and compare portable producer results; extend tests where current oracle is weak. | Carry legacy `long` values and exact arithmetic unchanged; do not wrap/reinterpret during the first migration.
| `LEGACY_BEHAVIOR_TO_PRESERVE` | Generator and cache disposal timing. | Generator owns the still-image Bitmap and its `DisposePrevious` is a no-op; caches dispose images on eviction by default; readers and exporters have their existing reuse/copy behavior. | No wholesale change. A new implementation must release its own resources correctly, but need not redesign legacy ownership. | Premature release or double disposal can break consumers. Test open/current/replacement/close, cache eviction, export enumeration and OpenCV conversion lifetime. | Reproduce the current ownership transfer and disposal callbacks in a local adapter. Change only if concurrent/native use demonstrates a defect.
| `OPTIONAL_DEFERRED` | Rational timebase, timestamp redesign or metadata rewrite. | Legacy uses `long` timestamps and reader-specific calculations. No evidence says conversion is required to remove Bitmap. | No. | High semantic risk to seek, previous timestamp, duration and export cadence. Would require full video timestamp characterization. | Retain both existing `long` timestamp fields and exact semantics.
| `OPTIONAL_DEFERRED` | Reference-counted leases, copy-on-write, immutable frame publication, redesigned caches. | Current cache ownership uses dispose callbacks; current consumer retention/lifetime has not been comprehensively characterized. | Not established. Apply only if a real cross-thread/native use case cannot be handled with scoped adapter access or explicit owner transfer. | Adds copies, synchronization and changed disposal timing. Requires stress tests and measured cost. | Owner stays with producer/cache as today; adapter copies into independently owned output only where needed.
| `OPTIONAL_DEFERRED` | Canonical RGBA conversion for all frames, mandatory pooling or extra copies. | Existing decoders/consumers use different layouts; measured 24-bit frames have smaller storage than 32-bit RGBA. No benchmark justifies a universal conversion. | No. | Can add 33% pixel storage for 24-bit and potentially duplicate conversion before OpenCV/export. Test exact channels/alpha and benchmark before opting in. | Preserve supported source layout and convert only at consumer boundaries that require it.
| `OPTIONAL_DEFERRED` | Correct historical mirror-orientation behavior, thumbnail geometry/resampling, error mapping or UI composition. | Those behaviors are not portability blockers and some may be legacy quirks. | No. | User-visible difference. Requires an explicit decision and separately named tests/baseline update. | Preserve existing behavior, even when an improvement appears desirable.

## Architectural alternatives

| Option | Description | Coupling / migration effort | Compatibility and performance | Test/maintenance impact |
|---|---|---|---|---|
| **A. Minimal contract substitution** | Replace Bitmap with a byte-buffer image type in existing Video contracts and update every consumer in one coordinated change. Keep metadata, timestamps, navigation and algorithms unchanged. | Lowest conceptual abstraction, but broad immediate source/binary break across readers, caches, ScreenManager, OpenCV, UI, summary and exporters. | Can preserve behavior, but requires all callers to migrate together; direct source layout can avoid needless conversion. High regression risk in one step. | Hard to isolate failures; broad integration tests required before merge.
| **B. Parallel portable seam with adapters (recommended)** | Add a small `net10.0` pixel-buffer contract and synthetic producer. Keep current net48 `Bitmap` path untouched. Add a Windows adapter and later platform-specific consumer adapters one at a time. Move existing metadata and timing semantics as-is when each producer crosses the seam. | Moderate small initial cost; the first milestone is isolated. Consumers migrate only when their behavior and target framework are understood. | Legacy path remains a rollback option. Pixel conversions occur only where needed. | Independent contract tests and legacy compatibility tests; each producer/consumer has a small parity gate. Temporary duplication needs eventual cleanup, but no big-bang break.
| **C. New frame ownership and metadata architecture** | Replace frame metadata, timestamp types, ownership model, caching and consumer APIs with new leases/immutable frames/timebase, then adapt all components. | Highest coupling and implementation effort. | Offers potential stronger safety, but changes lifetime/timing semantics without evidence and may add allocation/synchronization cost. | Requires extensive tests and benchmarks; expands task beyond portability.

Select **Option B**. It is the smallest safe architecture for independently proving a Linux-capable image boundary while preserving the working Windows implementation. Option A may be revisited if the eventual target dependency graph permits a single coordinated API change with manageable compatibility cost. Option C is deferred unless tests demonstrate a specific unsafe lifetime that local adapter ownership cannot solve.

## Proposed minimal portable API

Only the pixel data crossing a new portable boundary needs a new abstraction. Do not replace `VideoFrame` or timestamps in this milestone. Illustrative shape:

```csharp
public enum PixelFormat { Gray8, Rgb24, Bgr24, Rgba32, Bgra32 }
public enum AlphaMode { None, Straight, Premultiplied }

public sealed class PixelBuffer : IDisposable
{
    public int Width { get; }
    public int Height { get; }
    public PixelFormat Format { get; }
    public AlphaMode Alpha { get; }
    public int StrideBytes { get; }       // signed byte step between logical rows
    public int RowZeroOffset { get; }     // storage offset of logical row zero
    public ReadOnlyMemory<byte> Memory { get; }
    public void Dispose();
}
```

This is a design sketch, not an approved API. Channel byte order is defined by the `PixelFormat` names (e.g. `Rgb24` is R,G,B byte order); component depth is 8 bits for this initial proposal. Alpha modes are meaningful only for formats with alpha. Logical row `y` starts at `RowZeroOffset + y * StrideBytes`; the validated address range must lie within storage. This supports padded rows and maps negative native stride without assuming that negative means bottom-up. `RowZeroOffset` and signed stride must be validated together with overflow-safe range checks. No per-row alignment/padding byte is a logical pixel.

`Memory` is a borrowed view valid only while `PixelBuffer` remains undisposed. `ReadOnlyMemory<byte>` cannot revoke a view previously copied by a caller; disposing does not erase or make that copied view inaccessible. Therefore the API must document the validity interval, and callers must not retain `Memory` past owner disposal. Where native interop retains memory asynchronously, the adapter must copy into memory whose owner outlives the operation or use the native API's own buffer-return lifetime. Do not add refcounting or a lease abstraction unless that specific call requires it.

The first implementation may use an owned managed array and deterministic `Dispose` state for API validity checks. Disposal releases any pinned/native resource; for a managed array it does not promise erasure or physical reclamation. Do not pool or pin buffers globally. Mutation and cache mutability remain as close to the current path as practical; the new buffer can expose writable access only to its producer/internal assembly. A separate immutable/copy-on-write policy is not proposed.

### Pixel and orientation rules

- Preserve each producer's decoded format when it is directly representable. Convert explicitly at a consumer that requires a different layout.
- No universal RGBA canonicalization. No claim that the candidate's native bytes reveal channel order until independently tested.
- Alpha is never inferred from “32 bit”. The producer must state none, straight or premultiplied based on measurement or API contract. Until characterized, that source format is `UNVERIFIED` and cannot be silently relabeled.
- The adapter maps logical rows using signed stride plus row-zero offset. Tests cover positive and negative stride, padding, and asymmetric top/bottom rows.
- Preserve the current EXIF output: apply only current rotation behavior and retain current handling for mirror tags. Carry source orientation/applied transform internally only if needed by the new adapter; do not alter the legacy public metadata contract.
- Keep the existing `VideoFrame` `long` timestamps and previous timestamp arithmetic unchanged in parity tests. A future rational transport clock may exist internally only if conversion is exact and is separately reviewed.

## Producer and consumer boundaries

| Boundary | Verified current source | Minimal migration treatment |
|---|---|---|
| Still images | `VideoReaderBitmap.cs`, `FrameGeneratorImageFile.cs`: one stored Bitmap returned for timestamps; EXIF rotation and 96 DPI behavior in GDI+; fixed image timing. | Keep current reader. New `net10.0` synthetic producer first; later image reader produces `PixelBuffer` through a selected backend, with exact behavior comparison. No decoder choice here. |
| GIF/SVG | `VideoReaderGif.cs`, `VideoReaderSVG.cs`, `FrameGeneratorSVG.cs` | Keep existing Windows paths. Migrate independently after per-format timing, animation, palette/alpha and raster behavior is characterized. |
| Frame/cache | `VideoFrame.cs`, `IFrameGenerator.cs`, `Delegates.cs`, `SingleFrame.cs`, `Cache.cs`, `PreBuffer2.cs` | Do not change current contracts/caches initially. New path carries equivalent timestamp values. Adapter owns and releases the new buffer according to the specific producer/cache flow; preserve current eviction policy. |
| Capture | `Kinovea.Pipeline` and `Services/Bitmap/BitmapHelper.cs`; ScreenManager converts RGB24, RGB32, Y800 and JPEG buffers using source pitch/top-down flags. | Separate later stage: wrap/copy camera data only after its callback/pitch lifetime is inspected. Format-specific adapters map actual capture layout. |
| OpenCV | `VideoFilterLensCalibration`, `CameraTracker`, `TrackerTemplateMatching`, `Metadata` call `BitmapConverter.ToMat`. | Add a localized adapter only when these projects target the portable path. Specify `Mat` ownership and actual expected channels; test red/green/blue sentinels. Do not change tracking algorithms. |
| Rendering/UI | `PlayerScreenUserInterface2` uses Bitmap/Graphics; `ImageChanged` events carry Bitmap; drawings and filters use GDI+. | Windows continues to use GDI+. A future Linux presentation backend accepts `PixelBuffer` at its boundary and matches composition tests. UI framework is not selected. |
| Thumbnails | `VideoSummary.Thumbs`; reader `ExtractSummary`; `SummaryLoader` and `ThumbnailFile` consume Bitmap lists. | Preserve current dimensions and resampling. Adapt output at the UI edge; characterize ownership and pixel results before changing who disposes. |
| Export | `ImageRetriever(VideoFrame, Bitmap)` fills caller output; `FrameServerPlayer.EnumerateImages` uses staging and clones; exporters pass `IEnumerable<Bitmap>` to writer/UI paths. | Preserve callback order, composition, keyframe selection and clone semantics. New exporter adapter produces matching pixels/timestamps; do not rewrite export algorithms. |
| Persistence | `XmlHelper.ParseImageFromBase64`/`WriteBitmap`; drawing serialization uses raster images. | Keep existing encoded format; decode/encode only behind platform adapter and test byte/visual compatibility separately. |

GDI+, `System.Drawing.Bitmap`, WinForms and selected native libraries are platform-specific. Frame timestamps, simple containers, navigation algorithms and cache policy are C# logic and are not themselves blocked by Linux, though they currently share projects/API signatures with Windows types. This proposal does not imply the existing broad projects already build on Linux.

## Compatibility guarantees and non-goals

Guarantees for the proposed migration:

1. Existing legacy Windows API and behavior remain usable during the parallel rollout.
2. No timestamp unit, frame indexing, seek, previous-frame calculation, cache ordering/eviction, EXIF mapping, rotation, thumbnail sizing, image composition, or error mapping changes are included implicitly.
3. Pixel conversion is explicit, has a declared output layout/alpha rule, and is tested with independent expected bytes. A conversion may copy if required by lifetime or consumer format; it is not added globally without evidence.
4. When preservation cannot be achieved at a platform seam, the difference is documented and separately approved rather than hidden inside the port.

Non-goals: redesigning timestamps; changing cache algorithms or public metadata; introducing reference counting, copy-on-write, pooling, global immutability or universal canonical RGBA; correcting historical EXIF mirroring; choosing a decoder or UI framework; replacing FFmpeg; porting the UI; changing serialization; claiming Bitmap is ported or the candidate golden is approved.

## Incremental implementation plan

Every stage is a separate reviewable commit/PR slice. Legacy and portable tests are distinct suites. Rollback means leaving the prior Windows path selected and reverting the isolated adapter/assembly commit; do not remove legacy files until all consumers pass their gates.

| Stage / expected commit | Files/components and changes | Behavior retained | Windows tests / oracle | Linux tests / oracle | Acceptance and rollback |
|---|---|---|---|---|---|
| 0. Characterization strengthening (`test(bitmap): strengthen legacy pixel and lifetime oracle`) | Extend `tests/Bitmap.Characterization`; no production files. Independently assert raw channel order, partial-alpha colors/premultiplication, EXIF pixel coordinate maps, open/failed-open/close/reopen and disposal behavior; add timestamp boundaries only where current reader semantics can be independently stated. | No expected values invented from source alone; ambiguous behavior stays observation-only. Existing results remain candidate until human approval. | Run actual net48 reader on windows-2022; existing candidate plus new report. | `NOT_RUN` for legacy runtime. | Pass only if all cases/assertions pass and artifacts/hashes are reviewed; rollback test-only commit if harness invalid, preserving prior evidence. Golden approval remains a distinct human action.
| 1. Portable contract and synthetic producer (`port(video): add minimal portable pixel buffer`) | New small `net10.0` project, `PixelBuffer` and deterministic synthetic producer/tests. No existing reader/cache contract changes. | No user-visible behavior touched; timestamps remain out of scope except test metadata mirrors legacy longs. | Build/test new project on Windows; legacy reader suite unchanged. | Build/test contract on Linux. | Exact test vectors for RGB/BGR/RGBA/BGRA, alpha labels, padding, row-zero offsets, signed stride, bounds and deterministic dispose; rollback by deleting isolated project/commit.
| 2. Windows adapter (`port(video): adapt bitmap to portable pixels`) | Adapter project/source wraps or copies GDI+ LockBits data to `PixelBuffer`; legacy reader stays active and unmodified. | Same dimensions, pixels, alpha, orientation, timestamp and failures. Copy occurs only if required for a safe independent lifetime. | Compare converter output with legacy reader and approved (when approved) Windows baseline. Test `LockBits` unlock/disposal on errors. | Not applicable to GDI+ adapter; compile/run Windows only. | No discrepancy beyond predeclared tolerance; remove adapter commit to rollback. Do not mark Linux portable from this stage.
| 3. First parallel Linux producer (`port(video): add portable synthetic or still-image producer`) | Add a new producer in portable project; do not replace `VideoReaderBitmap` or select backend until separate evidence/license decision. Prefer synthetic producer first; still-image is a later substage. | Timestamp/navigation semantics cloned exactly; EXIF and errors preserved if/when real image reader is attempted. | Existing Windows path and candidate behavior unchanged. New producer tests on Windows. | Synthetic producer output/layout/disposal tests. If real reader later, compare same generated BMP/PNG and defined JPEG metrics. | The new producer passes its exact tests without touching legacy path; rollback by keeping it unused/removing its isolated project.
| 4. One analysis consumer (`port(video): adapt one OpenCV consumer`) | Select one consumer only after project target and OpenCvSharp native runtime support are verified; add converter at call boundary, leave algorithm untouched. | Same OpenCV input values and algorithm results. | Compare old Bitmap-to-Mat result with new buffer-to-Mat on Windows. | Run selected consumer with supported Linux runtime; compare channel probes and algorithm outputs. | Exact input channel equivalence plus existing domain outputs within existing tolerances; rollback adapter only.
| 5. UI/thumbnail/export edges (separate `port(ui)` / `port(video)` commits) | Per consumer, adapt rendering/thumbnail/export output. Keep each callback/algorithm contract until its replacement is validated. | Composition, ordering, dimensions, thumbnail resampling, keyframe selection and export timing unchanged. | Golden image/export comparisons and workflow tests on Windows. | Platform UI/integration test for chosen framework; exact lossless fixture comparisons. | Each edge passes independently; revert only that edge. UI framework remains a separate decision.
| 6. Contract replacement/removal (future ADR/PR) | Only after all consumers are enumerated and migrated, change public frame/generator/cache API or remove legacy implementation. | No unexplained deltas; behavior changes have separate explicit approval. | Full Windows end-to-end workflows. | Full Linux build and end-to-end workflows. | All platform gates pass and migration matrix is updated; revert to parallel path if parity or usability fails.

## Regression traceability and acceptance criteria

**Legacy compatibility tests** execute the actual Windows .NET Framework 4.8 reader and existing consumers. They answer “did the port preserve observed Kinovea behavior?” The current candidate reports 19 cases/537 assertions and matching outputs across two Windows runs, but baseline status is `PENDING_HUMAN_APPROVAL` and several assertions are not independent. Add:

- Raw `LockBits` bytes interpreted against asymmetric RGB sentinels across every pixel, with stride and padding recorded separately.
- Semi-transparent fixture colors whose source RGBA values are known; assert both `GetPixel` and raw bytes to determine straight/premultiplied behavior and rounding.
- EXIF 1–8 expected output coordinate maps authored independently of production mapping; include mirrored orientations and preserve observed legacy result, even if it ignores mirroring.
- Signed-stride mapping in the portable adapter using synthetic buffers; do not attribute signed-stride behavior to GDI+ until measured.
- Timestamp start/end/outside-zone/repeated/step/backward cases against actual reader, with current `long` units and exact expected `PreviousTimestamp` semantics.
- Lifecycle checks around `Generate`, `DisposePrevious`, cache clear/eviction, outstanding consumer use, close/reopen, failed open and exporter staging/clone. Do not demand a new ownership model as a legacy expectation.

**Portable contract tests** execute only the new portable assembly and verify descriptor validation, exact row/channel/alpha interpretation, padded/signed-stride addressing, bounds/overflow rejection, deterministic owner disposal and adapter-scoped memory validity. They do not establish legacy parity.

Acceptance criteria by stage are in the table above and must be executable before each implementation PR is called complete. Windows legacy regression and Linux contract conformance are reported separately. For BMP/PNG, compare logical pixels exactly once baseline/provenance and channel semantics are established. JPEG equality or tolerance is chosen only after decoder/runtime measurement and review. Performance gates require before/after allocation, copy count, throughput and peak memory at the same fixture/resolution; no numeric budget is set without measurements.

## Remaining review questions

1. Approve Option B and the first scope as a standalone `net10.0` contract plus synthetic producer, without changing current `Kinovea.Video` APIs?
2. Confirm the initial supported layouts (Gray8/RGB24/BGR24/RGBA32/BGRA32) based on intended first producer and consumer, or start with only the synthetic producer's actual layout and add formats as needed?
3. Is an owner-scoped `ReadOnlyMemory<byte>` lifetime contract acceptable for synchronous consumers, with copying at retention/asynchronous native boundaries? If not, identify the first real consumer that requires a stronger lease.
4. Which Linux-capable renderer/UI and OpenCV runtime are target dependencies? They are not needed to begin the synthetic contract but block later integration stages.
5. After independent review, may the characterization owner strengthen candidate tests and request baseline approval separately?

No decoder, rendering framework, cache redesign or behavior correction is approved by this ADR. Keep status `PROPOSED` until architectural review is complete.
