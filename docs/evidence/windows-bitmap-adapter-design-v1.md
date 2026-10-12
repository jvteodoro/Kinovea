# Windows Bitmap adapter design, v1

Status: selected test architecture; implementation and parity results pending.
Base: `port/net10-linux` merge commit `a9c1e9c8fe66d71356afebdad50a5854937e9fc1` (Portable Pixel Core PR #3).
Legacy reference: Windows .NET Framework 4.8 characterization source and candidate data; candidate remains `CANDIDATE_NOT_APPROVED`.

## Framework boundary options

**Selected: Option A, two isolated Windows targets with a file interchange.** The new adapter and its tests target `net10.0-windows`; they reference the portable `net10.0` `Kinovea.PortablePixels` assembly and `System.Drawing.Common`. The existing characterization executable stays on .NET Framework 4.8 and continues to reference the actual `Kinovea.Video.Bitmap`, `Kinovea.Video`, and `Kinovea.Services` projects. It exports output artifacts; no `net10.0` assembly is referenced by a legacy project.

The test bridge reuses schema version 1 of the existing characterization output: `test-results.json` records the actual reader's `VideoFrame.Image` dimensions, GDI+ pixel format, native stride and row-byte count, plus paths and SHA-256 values for `.native.bin` and `.rgba` captures. `EvidenceWriter.CaptureBitmap` obtains `.native.bin` by `LockBits` on the reader-returned Bitmap and `.rgba` independently through `GetPixel`. The adapter test verifies both file hashes, reconstructs a temporary GDI+ bitmap from the captured native logical rows, converts it with the .NET 10 adapter, and compares the portable logical pixels against the separately captured `GetPixel` values. This executes the old reader and carries its measured image data across the process boundary. It does not pass the same live Bitmap object between runtimes, and no claim will imply that reconstruction is identical to retaining that live object.

Direct adapter tests also create genuine GDI+ Bitmap instances with independently chosen color sentinels, layouts, partial alpha, padding and asymmetric row values. These tests establish GDI+ `LockBits` channel interpretation and adapter behavior separately from the actual-reader interchange tests.

**Option B, a framework-compatible shared contract, is deferred.** It would require changing or adding references to legacy projects, despite the adapter being a test-only Windows boundary. That adds production project coupling without solving transfer of a GDI+ Bitmap between CLR/runtime processes.

**Option C, a custom IPC/native bridge, is rejected for this slice.** The existing deterministic capture files already transport actual-reader bytes and metadata, with hashes and a versioned report. A live cross-runtime Bitmap bridge would add native-handle lifetime and ABI complexity without improving this conversion test.

## Tradeoffs and boundaries

- The .NET 4.8 reader and .NET 10 adapter remain independently buildable and are exercised sequentially in one `windows-2022` job. Adapter tests cannot call the .NET 4.8 object directly.
- Reader capture data is observed output, not approved golden data. Fixture expectations in direct tests are authored as fixed channel sentinels; actual-reader comparisons are explicitly labelled candidate-reference comparisons.
- `Format24bppRgb` and `Format32bppRgb` source bytes are expected to be B,G,R (and unused byte for 32-bit); output is `Rgb24` with explicit red/blue reordering and unused-byte removal. `Format32bppArgb` maps B,G,R,A to `Rgba32/Straight`; `Format32bppPArgb` maps to `Rgba32/Premultiplied` without unpremultiplication. These mappings must pass Windows raw-byte tests before being marked verified.
- Destination storage is a fresh managed owner independent of the source Bitmap. Conversion is synchronous and holds no GDI+ pointer after `UnlockBits`. No source ownership, cache, reader, time, UI or FFmpeg behavior changes.
- The adapter is Windows-only and does not make image reading portable to Linux. The portable core retains only its current formats; this design requires no core extension.

Implementation will report per-property results and leave any unsupported input format or unmeasured legacy behavior as `NOT_ESTABLISHED`.
