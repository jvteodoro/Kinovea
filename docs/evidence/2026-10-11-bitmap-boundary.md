# Diagnostic evidence: Bitmap reader and shared frame boundary

Baseline commit: `3fb66f01` (`test(geometry): characterize vector and matrix arithmetic`).
Host: Gentoo Linux 2.18, Linux `6.18.50-gentoo-dist-bin`, x86_64.
SDK/runtime: .NET SDK `10.0.111`, MSBuild `18.0.11`, .NET runtime `10.0.11`.
Legacy oracle: unavailable; Windows/.NET Framework execution `NOT_RUN`.
Fixtures: none; this diagnostic performed source inspection and build attempts only.

## Reproduction

- `dotnet build Kinovea.Video.Bitmap/Kinovea.Video.Bitmap.csproj -v:minimal` => exit `1`; restore ended with `Build FAILED`, `0 Warning(s)`, `0 Error(s)`, without a diagnostic.
- `dotnet build Kinovea.Video.Bitmap/Kinovea.Video.Bitmap.csproj -m:1 -v:minimal` => exit `1`; restore completed, then MSB3644 reported that .NET Framework v4.8 reference assemblies were not found. This serial command is diagnostic evidence; it does not make the build pass or change project files.

## Source boundary findings

- `Kinovea.Video.Bitmap` targets net48, references `Kinovea.Services` and `Kinovea.Video`, and directly references log4net from the legacy packages directory.
- `VideoReaderBitmap` derives from `VideoReader`, creates an `IFrameGenerator`, exposes `VideoFrame`/`VideoInfo`/`VideoGeometry`, and creates thumbnail `Bitmap`s for summaries.
- `FrameGeneratorImageFile` uses `System.Drawing.Bitmap`, `Image`, `PropertyItem`, `RotateFlipType`, and GDI+ resolution/rotation operations. It supports `.jpg`, `.jpeg`, `.png`, and `.bmp` by extension.
- `IFrameGenerator.Generate` and `DisposePrevious` pass `System.Drawing.Bitmap`; `VideoFrame.Image` and `VideoSummary.Thumbs` also expose that type. Therefore image pixels cross the adapter boundary as GDI+ objects, rather than a portable pixel contract.
- EXIF orientation values 1–8 are inspected. The four mirrored orientations map to `Rotate0` in the local rotation enum; the source comments say mirroring is handled elsewhere. The image is physically rotated for the four supported rotation values.
- The generator returns the same stored image regardless of timestamp and `DisposePrevious` is a no-op, because it owns one image instance. The reader advertises on-demand decoding but sets fixed metadata of 10 seconds at 25 fps and uses timestamps 0 through 249 for the working zone.
- `OpenInternal` catches exceptions, logs them, and returns `NotSupported`; no format-specific error detail reaches the caller. `Close` disposes the stored image. These behaviors need executable characterization before refactoring.
- The abstract `VideoReader` contract documents decoded frames as `Format32bppPArgb`, but `FrameGeneratorImageFile.OpenInternal` loads with `new Bitmap(filename)` and does not explicitly convert to that pixel format. The actual pixel format and alpha/channel behavior for the supported encoders are therefore unverified and must be measured on the legacy runtime.
- The dependency extends beyond the Bitmap adapter: 13 C# source files under `Kinovea.Video` reference `Bitmap`/`System.Drawing`; caching classes dispose frame images, the export delegate accepts a `Bitmap` destination, and `VideoSummary` returns bitmap thumbnails. ScreenManager callers also consume `VideoFrame.Image` in lens/camera-motion analysis and convert it with OpenCvSharp, while the playback/export boundary creates and tags staging bitmaps.

## Platform support decision evidence

Microsoft documents `System.Drawing.Common` as Windows-specific starting with .NET 6; the temporary Unix support switch was removed in .NET 7. The relevant APIs in this source are `Bitmap`, `Image`, `Graphics`, and `System.Drawing.Imaging` EXIF/rotation types. See [System.Drawing.Common only supported on Windows](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/6.0/system-drawing-common-windows-only), [System.Drawing namespace for .NET 10](https://learn.microsoft.com/en-us/dotnet/api/system.drawing?view=net-10.0), and [switch removal in .NET 7](https://learn.microsoft.com/en-us/dotnet/core/compatibility/core-libraries/7.0/system-drawing).

This evidence supports deferring an in-place `net10.0` retarget of `Kinovea.Video.Bitmap` while its public/shared path still exchanges GDI+ bitmap objects. It does not select a replacement graphics library or establish output equivalence for any alternative.

## Required characterization before extraction

On Windows with the legacy .NET Framework/GDI+ runtime, use tiny generated or explicitly licensed BMP/JPEG/PNG fixtures. Record fixture SHA-256, dimensions, pixel colors/channel interpretation, DPI behavior, EXIF orientations 1–8 and absent/invalid metadata, all supported manual rotations, and corrupt/unsupported files. Exercise generator open/close/reopen/disposal and same-image identity across timestamps. Exercise reader timestamps at the start/end and outside the working zone, frame previous timestamp, first `MoveRequest(next)` behavior, repeated timestamp requests, rotation/reopen and summary thumbnail dimensions. Compare exact pixels only where fixture and GDI+ behavior are deterministic; capture legacy outputs as the oracle.

Linux runtime behavior and Windows legacy results were not tested here. The diagnostics do not show that Bitmap, Video, or Services builds or runs portably, and they do not authorize a pixel backend replacement.
