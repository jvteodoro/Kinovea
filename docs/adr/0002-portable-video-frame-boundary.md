# ADR 0002: Establish a portable frame boundary before image-reader migration

Status: PROPOSED
Date: 2026-10-11
Baseline: `3fb66f01` plus the Bitmap boundary inspection recorded in `docs/evidence/2026-10-11-bitmap-boundary.md`.

## Context

`Kinovea.Video.Bitmap` is a small reader project, but its surrounding API is not isolated. `IFrameGenerator.Generate`, `VideoFrame.Image`, `VideoSummary.Thumbs`, the working-zone representative image, and export callbacks expose `System.Drawing.Bitmap`. Thirteen C# source files in `Kinovea.Video` reference bitmap/System.Drawing APIs. ScreenManager also consumes frames in filters and analysis, converts them to OpenCV matrices, and creates `Bitmap` staging images for export.

The legacy projects target .NET Framework 4.8. The direct Linux Bitmap project build currently fails during restore without a diagnostic; serial diagnostic build reaches MSB3644 because net48 reference assemblies are missing. Neither build result proves how its image decoder behaves.

Microsoft documents `System.Drawing.Common` as Windows-only starting in .NET 6; its Unix compatibility switch was removed in .NET 7. The .NET 10 target does not make this GDI+ path cross-platform. See [System.Drawing.Common only supported on Windows](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/system-drawing-common-windows-only), [System.Drawing namespace for .NET 10](https://learn.microsoft.com/en-us/dotnet/api/system.drawing?view=net-10.0), and [switch removal in .NET 7](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/system-drawing).

The abstract reader says images should be decoded as `Format32bppPArgb`, but `FrameGeneratorImageFile` does not explicitly convert the loaded bitmap to that format. Pixel format, alpha premultiplication, orientation and decoder behavior remain unverified. Frame ownership, disposal, timestamp, rotation and summary thumbnail behavior are also observable parts of the contract.

## Proposed decision

Do not retarget `Kinovea.Video.Bitmap` alone to `net10.0` and do not select or add a replacement image library yet. First capture legacy Windows outputs for generated fixtures, then define a platform-neutral frame/pixel boundary shared by readers and downstream consumers. Keep the existing GDI+ implementation as a Windows adapter until a replacement has been compared against that oracle.

Treat pixel storage format, channel order, alpha representation, row stride/orientation, dimensions, timestamp and previous-timestamp semantics, ownership/disposal, image rotation, and thumbnail behavior as explicit contract questions. Preserve the current `Format32bppPArgb` expectation only after tests establish how current readers actually deliver pixels.

## Options considered

1. **Retarget only Bitmap and retain `System.Drawing.Bitmap`.** Rejected for cross-platform delivery: the adapter and shared `Video` API remain Windows-specific.
2. **Target `net10.0-windows`.** Viable as an interim Windows build target, but it cannot satisfy the repository's Linux PORTED gate.
3. **Replace GDI+ with an image package now.** Deferred: no Windows legacy oracle, pixel/channel behavior is not recorded, the frame API remains GDI+-shaped, and dependency licensing/packaging has not been reviewed.
4. **Characterize first, then establish a portable frame boundary and retain a Windows adapter.** Recommended: it creates a testable seam across Bitmap/GIF/SVG and eventual media readers without changing the current backend or asserting unmeasured parity.

## Consequences and required evidence

- The next executable characterization requires Windows/.NET Framework 4.8/GDI+ and synthetic or licensed BMP/JPEG/PNG fixtures, including EXIF orientation cases. The detailed plan is in `docs/evidence/2026-10-11-bitmap-boundary.md`.
- The workflow and headless harness are implemented at `.github/workflows/bitmap-characterization.yml` and `tests/Bitmap.Characterization`. They invoke the actual `VideoReaderBitmap` project reference and collect pixel, runtime, source-hash, fixture-manifest and test-report artifacts. Run `38174132063` verified the targeting pack and restore but failed compiling Services under `Release|AnyCPU` (`CS0227`, unsafe code disabled). Run `38174665514` showed the `Release|x64` correction cleared those errors, then failed compiling the harness due to an absent direct Services reference and `Bitmap` namespace/type collisions. Both issues have been corrected in the harness; a fresh Windows build is pending. Neither run executed the characterization or produced measured reader output. See `docs/evidence/2026-10-11-bitmap-characterization-infrastructure.md`.
- Candidate artifacts do not approve a golden baseline. Golden approval requires a reviewed Windows run and a separate commit containing the exact fixture and output hashes plus reviewer and source revision metadata. ADR status remains `PROPOSED`; this infrastructure change does not complete architectural review.
- After those results exist, a separate design task must propose the frame representation and lifecycle contract, including conversion points for ScreenManager/OpenCV/export consumers.
- Any package selection needs a separate license, native/runtime packaging, and pixel-parity review. This ADR does not approve a package or backend replacement.
- Until a Windows oracle and Linux tests of a candidate boundary exist, Bitmap/Video remain unported and regression parity remains `UNVERIFIED`.
