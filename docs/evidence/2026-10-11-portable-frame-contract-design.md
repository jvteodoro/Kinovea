# Evidence: ADR 0003 minimal portable frame boundary

Status: documentation design; not an implementation or parity result.
Base branch: `port/net10-linux` at `125dbbb907f554162bc31e54f95090876a19773f`.
PR branch before revision: `design/portable-frame-contract` at `e01dc1b41cab64a58acedd463ef189e526c16fd0`.
Host: Gentoo Linux x86_64; repository environment identifies .NET SDK 10.0.111.
Legacy oracle: candidate Windows .NET Framework 4.8 runs only; status `PENDING_HUMAN_APPROVAL`.

## Review source

Read PR #2's architectural review posted by `jvteodoro` at https://github.com/jvteodoro/Kinovea/pull/2#pullrequestreview-5485255332. It requests minimal behavior change, required-versus-optional classification, parallel legacy/new paths, explicit pixel layout/signed-stride handling, unchanged timestamps and EXIF results, characterization before ownership redesign, honest `ReadOnlyMemory` limits, separate test categories, staged net10 core and synthetic producer, and no decoder selection. ADR 0003 now addresses these individually in its compatibility matrix, options comparison, lifetime section, staged rollout and acceptance criteria.

## Production source inspection

Inspected actual code, not only prior design docs:

- `Kinovea.Video/Caching/VideoFrame.cs`: plain wrapper; stores Bitmap, `Timestamp`, `PreviousTimestamp`; empty object is `(null, -1, -1)`; no `IDisposable`.
- `Kinovea.Video/VideoReaders/IFrameGenerator.cs`: states generator owns generated image memory; exposes `Bitmap Generate` and `DisposePrevious(Bitmap)`.
- `Kinovea.Video/Delegates.cs`: `VideoFrameDisposer(VideoFrame)` and `ImageRetriever(VideoFrame, Bitmap)`.
- `Kinovea.Video.Bitmap/FrameGeneratorImageFile.cs`: one stored image returned for every timestamp; `DisposePrevious` does nothing; `Close` disposes the stored image; `SetResolution(96,96)`; existing EXIF rotation mapping physically rotates only supported rotations.
- `Kinovea.Video.Bitmap/VideoReaderBitmap.cs`: fixed 25 fps, 250 timestamp duration, interval calculation; `UpdateCurrent` returns early for same timestamp, invokes `DisposePrevious` before replacing the current image, and computes previous timestamp by subtracting average timestamps/frame. `Close` closes generator; behavior after close is not to be redefined here.
- `Kinovea.Video/Caching/SingleFrame.cs`, `Cache.cs`, `PreBuffer2.cs`: cache cleanup delegates to `VideoFrameDisposer` if supplied, otherwise disposes `frame.Image`; PreBuffer2 has a decoding thread and eviction. This establishes existing disposal points, not a proven need for reference counting.
- `Kinovea.Video/VideoSummary.cs` stores `List<Bitmap>`; reader summary methods generate GDI+ resized thumbnails.
- `Kinovea.ScreenManager/PlayerScreen/FrameServerPlayer.cs`: `EnumerateImages` gets output from the `ImageRetriever` into a staging bitmap and yields clones; exporters consume those images. `PlayerScreenUserInterface2.GetFlushedImage` fills caller output.
- OpenCvSharp `BitmapConverter.ToMat` calls are present in lens calibration, camera tracking, template matching and metadata. Capture code uses `BitmapHelper` methods to interpret RGB24/RGB32/Y800/JPEG buffers with pitch and top-down flags. These are distinct interop edges.

## Candidate Windows observations and limits

The reviewed audit reports two runs (38175088092, 38175219336), same source SHA `0a9e8a5f847b77e030355dd2f38a7de97a4a2572`, 19 cases/537 assertions each, and 72 input/pixel files byte-identical. Recorded examples: BMP 7x5 Format32bppRgb/stride 28; PNG 13x9 Format32bppArgb/stride 52; JPEG 11x7 Format24bppRgb/stride 36. EXIF rotations 3/6/8 map to 180/90/270; mirror tags map to no internal rotation. Reader time metadata is fixed by current source.

Do not overstate those results: raw native channel order was not independently decoded/asserted; partial-alpha premultiplication is not calculated against an independent oracle; EXIF expected-rotation helper duplicates production assumptions and mirror pixel transforms lack independent expected rasters; timestamp tests omit several bounds; closed-image observation did not assert exact failure; no long-run handle/leak test was performed. Candidate remains pending approval and does not establish Linux parity.

## Review decision applied

- Chose a parallel net10 seam (Option B), retaining legacy APIs and Windows path during rollout.
- Kept timestamp `long` values and algorithms intact; rational timebases are deferred.
- Kept cache eviction and generator disposal behavior; leases, copy-on-write, pooling and global immutability are deferred pending a demonstrated call-site requirement.
- Proposed a format-preserving byte buffer with explicit channel order, alpha, signed stride and row-zero offset. `ReadOnlyMemory<byte>` is documented as a borrowed, non-revocable view; async/native retention must copy or use the native owner lifetime.
- Required Linux synthetic contract tests separately from Windows legacy compatibility tests. No new expected legacy behavior is inferred from code where measurement is missing.
- First implementation milestone is limited to a new net10 contract, synthetic producer and portable contract tests; production reader/caches/UI untouched. The reviewer's suggested “small net10 core plus synthetic producer” is reflected in stage 1.

## Verification and limitations

- Commands: source reads and targeted `rg` searches over the production paths named above; `gh api` for PR reviews, review comments and issue comments.
- Review thread result: one submitted architectural review; no inline review comments or issue comments returned.
- Builds/runtime tests: `NOT_RUN` (documentation-only change).
- `git diff --check`: run before commit; result recorded in the migration log.
- Remaining scope gap: unrelated ScreenManager Bitmap resources and drawings are not fully inventoried. Later implementation must audit each changed consumer before modifying shared APIs.
