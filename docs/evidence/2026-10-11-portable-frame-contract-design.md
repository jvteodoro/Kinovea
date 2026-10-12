# Design investigation: portable frame contract

Status: **Design proposal only; no production API change**
Repository base requested: `port/net10-linux` at `125dbbb907f554162bc31e54f95090876a19773f`.
Workspace HEAD at inspection: `93151c03378a5105f74d8127f99d331ad7242dd6` (`test/bitmap-baseline-review`).
Host: Gentoo Linux, x86_64; .NET SDK 10.0.111 per repository environment record.
Legacy oracle: Windows .NET Framework 4.8 candidate only; baseline is `PENDING_HUMAN_APPROVAL`.

## Investigation commands

- `git status --short --branch` => existing branch is `test/bitmap-baseline-review`; untracked user work includes `.beads/issues.jsonl`, `docs/COMMIT_POLICY.md`, `docs/MIGRATION_PLAN.md`, `docs/adr/0001-incremental-port.md`, and `tasks/`.
- `git rev-parse port/net10-linux` => `125dbbb907f554162bc31e54f95090876a19773f`.
- `git switch -c docs/portable-frame-contract port/net10-linux` => `NOT_RUN`; Git could not create `.git/index.lock` because `.git` is read-only in this workspace.
- `rg` source searches for `Bitmap`, `VideoFrame`, `IFrameGenerator`, `Thumbs`, frame callbacks, `BitmapConverter`, and frame disposal, followed by direct reads of the contracts, caches and characterization audit.
- No builds or behavior tests were run; this is a documentation and source-inspection task.

## Verified source observations

- `Kinovea.Video/Caching/VideoFrame.cs`: `VideoFrame` is not `IDisposable`; it holds a `Bitmap` reference and two `long`s. `Empty()` uses null image and `-1` timestamps.
- `Kinovea.Video/VideoReaders/IFrameGenerator.cs`: comments assign generated image memory to the generator; methods return `Bitmap`; `DisposePrevious(Bitmap)` is a callback hook.
- `Kinovea.Video/Caching/SingleFrame.cs`, `Cache.cs`, and `PreBuffer2.cs`: default eviction/disposal paths call `frame.Image.Dispose()`; optional `VideoFrameDisposer` delegates override that. The cache design has synchronous and asynchronous paths, so borrowed storage lifetime needs explicit rules.
- `Kinovea.Video/Delegates.cs`: `ImageRetriever(VideoFrame, Bitmap)` is the image-output callback. `PlayerScreenUserInterface2.GetFlushedImage` implements it; document/video/image exporters assign this method to settings.
- `Kinovea.Video/VideoSummary.cs`: thumbnails are a mutable `List<Bitmap>` with no documented disposal/ownership contract.
- Readers: Bitmap and SVG generate VideoFrame values; GIF constructs frames directly. Bitmap and SVG call `DisposePrevious` when replacing current generated images.
- ScreenManager consumers include OpenCvSharp `BitmapConverter.ToMat` calls in lens calibration, camera motion, template tracking and metadata; playback rendering, `ImageChanged` events, image sequence/video exporters, and thumbnails remain Bitmap-shaped.
- Other Bitmap uses include UI icons, drawings, capture viewport images, and XML-embedded image serialization. The dependency map in ADR 0003 labels its scope and calls for a wider implementation-time audit.

## Measured candidate observations

Candidate Windows observations and their limitations are in `docs/evidence/2026-10-11-bitmap-golden-audit.md`. Formats/strides: BMP 7x5 `Format32bppRgb`, 28 bytes/row; PNG 13x9 `Format32bppArgb`, stride 52; JPEG 11x7 `Format24bppRgb`, pixel payload 33 bytes/row and stride 36. The audit explicitly does not establish raw channel byte order, alpha premultiplication, independent full-image EXIF outputs or leak behavior. Fixed image reader time interval is 40 ms with observed initial timestamp zero. EXIF transforms and mirror behavior are only partially asserted.

## Commands/results and limitations

- `git diff --check` => exit 0; no whitespace errors.
- Windows baseline comparison => `NOT_RUN` in this environment.
- Linux/.NET 10 parity => `NOT_RUN`; no portable reader exists in the task scope.
- Production build/tests => `NOT_RUN`; production sources are not changed.
- Git branch creation and commit => blocked by read-only `.git` despite workspace files being writable. No attempt was made to alter permissions or metadata.

See [ADR 0003](../adr/0003-portable-frame-representation.md) for proposed API shape, invariants, ownership rules, producer/consumer map, tradeoffs, conversion boundaries, test thresholds, staged migration and decisions requiring approval.
