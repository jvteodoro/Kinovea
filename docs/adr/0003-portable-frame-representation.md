# ADR 0003: Portable frame representation

Status: **PROPOSED — architectural review required**
Date: 2026-10-11
Baseline: `port/net10-linux` at `125dbbb907f554162bc31e54f95090876a19773f`; Bitmap observations from the candidate Windows .NET Framework 4.8 baseline `bitmap-windows-net48-v1` (`PENDING_HUMAN_APPROVAL`).

## Context and evidence boundary

`VideoFrame` currently carries a `System.Drawing.Bitmap` and two `long` timestamps. `IFrameGenerator` returns `Bitmap` and accepts a `DisposePrevious(Bitmap)` callback. The reader/cache chain owns or disposes the image through `VideoFrameDisposer`; the `VideoFrame` wrapper itself is not disposable. `VideoSummary.Thumbs` is a mutable `List<Bitmap>`. The frame API therefore combines pixel storage, native image handle, timestamp metadata, and implicit ownership.

The candidate Windows reader observations are summarized in [the audit](../evidence/2026-10-11-bitmap-golden-audit.md) and [the boundary inspection](../evidence/2026-10-11-bitmap-boundary.md). The measured examples were BMP 7x5 (`Format32bppRgb`, stride 28), PNG 13x9 (`Format32bppArgb`, stride 52, containing transparent and partially transparent pixels), and JPEG 11x7 (`Format24bppRgb`, row payload 33, stride 36). Candidate timestamps were 0 and 40 ms for the first interval, with a fixed image-reader interval of 40 ms; EXIF values 3/6/8 selected 180/90/270-degree rotations, while mirror orientations mapped to no internal rotation. These are measurements from a pending candidate, not an approved oracle. Raw channel order, premultiplication, independent EXIF pixel transforms, negative stride and resource leak behavior remain unverified.

The candidate harness calls the production `VideoReaderBitmap`, but its assertion gaps are documented in the audit. No behavior in this ADR should be treated as a validated Linux result or as evidence that Bitmap is portable.

## Proposed direction

Introduce a platform-neutral frame contract in a new .NET 10-compatible assembly after the architecture questions below are approved. Separate immutable frame metadata from an explicitly described pixel buffer. Preserve the source pixel representation at decode boundaries where practical, and require an explicit conversion when a consumer needs another representation. Do not put `Bitmap`, `Mat`, UI image types, decoder handles, or platform types in the portable contract.

This ADR proposes a shape, not production API code or a decoder selection. Illustrative API:

```csharp
public readonly record struct FrameMetadata(
    int Width, int Height,
    long Timestamp, long PreviousTimestamp,
    Timebase Timebase,
    FrameRotation AppliedRotation);

public enum PixelLayout { Gray8, Rgb24, Bgr24, Rgba32, Bgra32 }
public enum AlphaMode { None, Straight, Premultiplied }
public enum RowOrder { TopDown, BottomUp }

public sealed class PixelBufferOwner : IDisposable
{
    public FrameMetadata Metadata { get; }
    public PixelLayout Layout { get; }
    public AlphaMode Alpha { get; }
    public int StrideBytes { get; }
    public RowOrder RowOrder { get; }
    public ReadOnlyMemory<byte> Pixels { get; }
    public WritablePixelBufferLease RentWritableCopy();
}
```

The exact names and whether metadata belongs on the owner or a `VideoFrame` wrapper are open. `ReadOnlyMemory<byte>` is illustrative; the approved implementation must specify how a view is invalidated when its owner is disposed and must not expose a `Span<T>` beyond a synchronous call. A consumer that needs to retain pixels must acquire its own owner/reference or copy. The API must validate positive dimensions, supported layout, checked row-byte arithmetic, `abs(stride) >= packed row bytes`, and enough backing memory for all rows. Stride sign and row order must not be inferred from one another; the descriptor must say how a logical row maps into storage.

### Required invariants for a future implementation

1. Width and height are positive for non-empty frames; an explicit empty-frame value carries no pixel owner and cannot be sampled.
2. Timestamp units are explicit through a rational timebase. A frame timestamp means presentation time in that timebase; `PreviousTimestamp` keeps its legacy meaning until characterization or a separately approved API change resolves it.
3. Pixel channel order, component depth, alpha mode, row stride, and logical row order are explicit. Padding bytes are not pixel data and must not affect logical pixel comparisons.
4. A published frame is read-only. Mutation requires a distinct writable lease, normally backed by a copy; shared cache readers cannot observe consumer mutation.
5. Exactly one disposal path releases each owned buffer. Disposing the owner invalidates all borrowed views. Access after disposal throws `ObjectDisposedException` in managed APIs.
6. No pooling in the initial contract. Pooling is considered only after allocation/throughput measurements and a stress test establish bounded retention, single-return behavior, and no use-after-return.
7. Conversion APIs state their output layout, alpha handling, orientation, and copy/ownership behavior. Conversion never silently changes timestamps or dimensions.
8. Orientation is represented as source metadata until a producer explicitly applies it; after application, metadata records the applied transform and pixel dimensions match the transformed raster. Mirror transforms must be representable if the source has them.

## Representation options

### Canonical packed RGBA8, top-down, straight alpha

Advantages: one simple cross-platform interchange format; easy to hash and inspect; consistent renderer and exporter input; one row convention and alpha rule. It would require converting current RGB/BGR or other decoded formats and can increase storage (for 24-bit input, 4/3 the pixel payload; for grayscale, 4x). Every 4K frame is 33,177,600 bytes (3840×2160×4) before alignment; 24-bit storage is 24,883,200 bytes. Converting each decoded frame adds a full read and write, and an OpenCV consumer commonly wants BGR/BGRA, potentially causing another conversion. Whether that cost is acceptable is unmeasured.

### Format-preserving descriptor plus bytes

Advantages: can retain 24-bit data and avoid conversions where the decoder and consumer agree; describes the candidate legacy formats directly; may reduce memory bandwidth. Costs: every consumer must support several layouts, stride and alpha variants; poorly supported formats can trigger repeated conversions; more branches create correctness risk. “Preserve format” does not mean preserve encoded file bytes or decoder-specific hidden state.

### Proposed compromise for review

Use a format-preserving core descriptor over a deliberately small, explicitly supported set, with a canonical conversion helper for consumers that need it. Initial candidates are Gray8, RGB24/BGR24, and RGBA32/BGRA32 with explicit `AlphaMode`; do not finalize this set until the production source inventory and baseline gaps are addressed. A format not representable by the set must be converted at its producer boundary or rejected with a clear error, never mislabeled. Do not require all consumers to implement every format. No performance claim is made before benchmarks.

## Producer and consumer dependency map

| Boundary | Current source and behavior | Contract migration concern |
|---|---|---|
| Still image reader | `Kinovea.Video.Bitmap/FrameGeneratorImageFile.cs` loads and rotates a GDI+ image; `VideoReaderBitmap.cs` wraps it into `VideoFrame`. | Preserve actual decoded pixel semantics, EXIF transforms, dimensions, fixed image timing, open/close and failure behavior. Keep this reader unchanged until a later migration task. |
| GIF/SVG readers | `Kinovea.Video.GIF/VideoReaderGif.cs`; `Kinovea.Video.SVG/FrameGeneratorSVG.cs`, `VideoReaderSVG.cs`; frames and summary thumbs use Bitmap. | Reader-specific frame timing, palette/alpha and rasterization need their own characterization; do not infer from Bitmap reader. |
| Video reader/cache | `Kinovea.Video/Caching/VideoFrame.cs`, `VideoReaders/IFrameGenerator.cs`, `Delegates.cs`, `Caching/{SingleFrame,Cache,PreBuffer2}.cs`. Cache paths dispose `frame.Image` or call `VideoFrameDisposer`. | Replace implicit bitmap disposal callback with explicit owner transfer or retained lease. Eviction must release only after no consumer can use the frame. Current comments mention thread/UI ownership but do not establish a general concurrent ownership contract. |
| Capture pipeline | `Kinovea.Pipeline` frame byte buffers and ScreenManager `CaptureScreen` converts RGB24/RGB32/Y800/JPEG payloads through `Kinovea.Services/Bitmap/BitmapHelper.cs`. | This is a separate producer boundary with source pitch, top-down flags and capture timing; map it after auditing `Pipeline.Frame` and camera formats. |
| Analysis/OpenCV | `VideoFilterLensCalibration.cs`, `Measurement/CameraMotion/CameraTracker.cs`, `Tracking/TemplateMatching/TrackerTemplateMatching.cs`, and `Metadata.cs` call OpenCvSharp Bitmap-to-Mat conversions. | Define whether conversion yields borrowed or owned `Mat`, BGR/BGRA order, alpha policy and row stride. Existing conversion behavior must be measured before replacement. |
| Playback/UI | `PlayerScreen/Controls/PlayerScreenUserInterface2.cs` renders and exports through Bitmap/Graphics; `PlayerScreen` and view expose `ImageChanged` carrying Bitmap; drawing, filters, overlays and capture viewport are GDI+-based. | Keep platform image conversion in UI adapters. A Linux UI/rendering framework is not selected here. Avoid leaking UI image resources to the portable core. |
| Thumbnails | `VideoSummary.Thumbs`, `VideoReader.ExtractSummary`, `ScreenManager/SummaryLoader.cs`, `Thumbnails/ThumbnailFile.cs` and `ThumbnailCamera.cs` exchange Bitmap objects. | Make thumbnail pixels immutable/owned and document whether summary owns them; thumbnails may be downscaled/rasterized and are not equivalent to full-resolution video frames. |
| Image/export callbacks | `Video/Delegates.cs` `ImageRetriever(VideoFrame, Bitmap)`; `PlayerScreenUserInterface2.GetFlushedImage(VideoFrame, Bitmap)`; exporters attach this callback. `WriterFFMpegCLI.Save` accepts `IEnumerable<Bitmap>`. | Replace output destination with an explicit pixel sink or adapter; specify callback lifetime, whether caller supplies/owns output, and composition of drawings/filters. Preserve export appearance and timing through characterization. |
| XML/image persistence | `Services/XmlHelper.ParseImageFromBase64` / `WriteBitmap`; ScreenManager drawing serialization stores raster image data. | This is an encoded-image boundary, not necessarily a frame buffer. Keep serialization format compatibility and separate encoded bytes from decoded pixels. |

This map is scoped to the `Kinovea.Video`, `Kinovea.Services`, and `Kinovea.ScreenManager` paths read for this design. It does not claim to be a complete repository-wide Bitmap usage inventory; UI resource icons and unrelated drawing bitmaps exist in ScreenManager and need a call-site audit before implementation.

## Conversion boundaries

- **Video readers:** decode to an owned buffer with explicit layout and timing. Keep compressed/encoded source data separate from decoded frame ownership. Source orientation is applied once at a named boundary and recorded.
- **OpenCV:** adapter converts/leases to the exact OpenCV channel and stride requirements. Tests compare channel-labelled synthetic patterns; conversion ownership and lifetime are explicit. The current `BitmapConverter.ToMat` paths are behavior references only.
- **UI rendering:** each frontend owns its platform-specific bitmap/texture conversion and presentation lifetime. Core frames are not `Bitmap` or UI objects.
- **Thumbnails:** generate derived owned frames with explicit dimensions and pixel semantics. The summary owns the thumbnails or exposes disposable owners; list mutation does not imply ownership transfer.
- **Video exporters:** consume a timestamped frame sink with declared layout and ordering; pixel encoding and video container timing stay in a separately selected backend. Do not choose an FFmpeg replacement as part of this ADR.
- **Still/image exporters:** request the composed frame (drawings, filters, rotation) into an explicit destination. Define whether the returned image is a snapshot or borrowed view; the current callback uses caller-provided Bitmap output.
- **Capture:** camera buffers enter via format-specific adapters that honor source pitch, orientation, timestamp clock and buffer-return callback. Do not wrap a camera-owned pointer in a frame without a lease that keeps it valid.

## Ownership, mutation and pooling proposal

The producer creates an owner and transfers it to the frame/cache on successful publication. Once published, all frame consumers receive read-only views. Cache eviction releases its reference; a display/export/analysis task that outlives the cache lookup must hold a lease. A writable operation calls `RentWritableCopy`, modifying that private copy only. The current legacy path sometimes disposes immediately on cache eviction; the new contract must make concurrent use safe without relying on that timing.

The initial implementation should allocate owned arrays or equivalent managed memory without pooling. Pooling adds lifetime hazards and should be a measured optimization. If approved later, pool return occurs only after the last lease closes, exactly once; tests must force cache eviction while a consumer lease is active, then confirm the bytes remain valid and are not reused. Finalizers must not be required for correct release. Disposal is deterministic and idempotent.

## Regression plan and acceptance criteria

First strengthen the Windows candidate suite before treating it as a gold reference: independent expected whole-frame channel checks for BMP; raw storage byte interpretation; semi-transparent known-color pixels checked for straight/premultiplied values; all EXIF 1–8 transforms checked against independently authored coordinate maps; explicit negative/boundary timestamp cases; and lifecycle checks for open failure, repeated close, reopen, cache eviction and outstanding consumer use. A reviewer-approved baseline is still pending.

For a future contract implementation, require the following gates:

1. **Descriptor tests:** all supported layouts and alpha modes; invalid dimensions/stride/length overflow rejected; padded rows and positive/negative strides map expected logical pixels; top-down/bottom-up storage verified with asymmetric corner colors.
2. **Pixel tests:** red/green/blue channel sentinels survive each conversion; alpha values 0, 1, 127, 128, 254, 255 preserve or convert according to the declared alpha rule; premultiply/unpremultiply use a documented rounding rule and zero-alpha RGB policy.
3. **Time tests:** timestamp and previous timestamp round-trip with rational timebase; no implicit millisecond conversion; legacy output comparisons use explicit tolerances only where conversion is involved.
4. **Ownership tests:** disposal idempotent; reads after disposal fail; writable copy does not mutate source; two concurrent read leases see unchanged bytes; cache eviction cannot invalidate an outstanding lease; buffer returned once. Pool tests are required only if pooling is approved.
5. **Baseline parity:** on Windows, for each fixture the approved candidate's dimensions, timestamp, rotation and canonicalized logical pixels match within a declared rule. Raw byte hashes match only if the layouts and deterministic decoder are identical. Any intentional alpha/orientation difference requires a reviewed baseline update, never an automatic regeneration.
6. **Cross-platform parity:** after a Linux reader exists, Linux and Windows use the same fixture hashes and compare dimensions, timestamps, logical RGBA pixels, orientation and alpha. Initial acceptance for lossless BMP/PNG is exact logical pixel equality; JPEG requires exact equality only if the selected decoder/runtime demonstrates it, otherwise a pre-approved per-channel metric with a numerical bound and fixture-specific justification. Until run, mark `NOT_RUN`.
7. **Performance/memory:** record allocations per frame, peak retained frame memory, conversion throughput and end-to-end decode throughput at representative dimensions on each platform. Set thresholds only after a measured legacy/current baseline and product review; no arbitrary threshold is proposed here.

Required test patterns include nonstandard widths with row padding, transparent and semi-transparent colors, distinct channel values, top/bottom asymmetric patterns, all EXIF orientations including mirrors, odd dimensions, repeated timestamps, frame bounds, malformed input, reader reopen, summary thumbnail dimensions/pixels, and exporter/OpenCV conversion round trips.

## Compatibility and staged migration

Replacing `Bitmap` in `VideoFrame.Image`, `IFrameGenerator.Generate` / `DisposePrevious`, `VideoSummary.Thumbs`, `VideoFrameDisposer`, `ImageRetriever`, UI `ImageChanged`, and exporter `IEnumerable<Bitmap>` is source- and binary-breaking. GDI+ drawing APIs, serialization helpers, and Bitmap-taking constructors add further migration sites. A shim that keeps Bitmap in the portable assembly would not achieve a platform-neutral boundary.

Proposed stages:

1. Close characterization gaps and obtain independent human approval for the Windows candidate baseline.
2. Approve the representation, timebase and ownership questions in this ADR; create the core contract assembly as a separate task.
3. Add explicit Windows Bitmap adapters and dual-path characterization without changing the production reader's selected path.
4. Migrate one producer (initially synthetic/test producer or Bitmap reader only after a decoder decision) and compare against the approved baseline.
5. Migrate cache ownership and frame consumers, then OpenCV, thumbnails and exporter/UI adapters in dependency order. Keep compatibility adapters at the desktop edge during transition.
6. Remove legacy Bitmap contracts only after all consumers and Windows behavior are verified. Linux `.NET 10` and Windows checks remain separate gates.

## Questions requiring architectural approval

1. Approve the format-preserving descriptor with a small supported set and explicit conversion helper, or choose one canonical interchange format? If canonical, which layout and alpha rule?
2. What is the timestamp unit/timebase API, and should `PreviousTimestamp` remain in the new contract?
3. Is `ReadOnlyMemory<byte>` acceptable for the public contract, or should the boundary expose scoped callbacks/leases only?
4. Should the producer transfer an owner, return a reference-counted lease, or publish an immutable frame value that owns storage?
5. Is no pooling acceptable for the first implementation, with optimization gated on measurements?
6. Should EXIF orientation be applied by image readers, or carried as metadata to a renderer? How do we preserve existing mirrored-orientation behavior while supporting other sources?
7. Which initial consumers are in scope for the first implementation: video decode only, or capture and image summaries as well?
8. What parity metric and JPEG tolerance will reviewers accept after the decoder/runtime is selected and measured?
9. Which UI framework and OpenCV interop boundary are supported by the target application architecture? This ADR intentionally makes no selection.

## Consequences

This proposal provides a concrete review target and test gates. It does not change production code, select a decoder/image package, approve the candidate baseline, establish cross-platform parity, or mark Bitmap/Video as ported. Any implementation requires approval of the open contract choices and a separate migration task.
