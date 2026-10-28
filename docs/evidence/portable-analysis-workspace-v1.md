# Portable analysis workspace v1 — implementation evidence

**Status:** `PORTABLE_TESTED` on local Gentoo Linux. Legacy `.kva` compatibility: `NOT_ESTABLISHED`. Windows/Wine: `NOT_RUN`. No hosted workflow was triggered.

## Source and environment

- Isolated branch: `port/analysis-roundtrip`.
- Base: `origin/port/net10-linux` commit `5d0f59f9b1b6f8297af77739d96b2cac3f9211b0`.
- Implementation/test source-tree digest: `04720f91bbb44915dff25d59957d4f72a2984a7780fad0267c0d56981d0f54a7`. This is SHA-256 over the UTF-8 relative path, NUL, and file bytes in order for `PortableAnalysisWorkspace.cs`, `PortableVideoPlayerSession.cs`, `ImageViewport.cs`, `MainWindow.cs`, desktop test `Program.cs`, and its `.csproj`; it is not a Git commit SHA.
- Host: Gentoo Base System 2.18, Linux x86_64, kernel `6.18.50-gentoo-dist-bin`.
- .NET SDK `10.0.111`; native FFmpeg `8.1.2`, loaded ABI `avformat 62.12.102`, `avcodec 62.28.102`, `avutil 60.26.102`, `swscale 9.5.102`.
- GUI: Avalonia `12.1.4`, active X11 display `:0`, runtime scaling `1.5`.

## Legacy serialization inventory

Source inspection covered `Kinovea.ScreenManager/Metadata/Serialization/MetadataSerializer.cs`, `Measurement/Calibration/CalibrationHelper.cs`, `Measurement/Calibration/CalibratorPlane.cs`, and `Measurement/Geometry/GeometryHelper.cs`.

- The writer emits XML root `KinoveaVideoAnalysis` and current `FormatVersion` `2.1`. General information can include `OriginalFilename`, `FullPath`, `ByteLength`, image size/adjustments, `AverageTimeStampsPerFrame`, frame rates, first/selection timestamps, and `TimeOrigin`.
- Conditional sections include calibration, keyframes/drawings, chronos, tracks, spotlights, number sequence, coordinate system, test grid, trackable points, video filters, and camera motion. This is not a complete imported-field compatibility inventory.
- Checked-in sample files are older format `2.0`: `minimal.kva` SHA-256 `5ea713f0aee2275db431460a9fdfbedf347a04e57025a034caebc5ecd76c02ce`; `simple.kva` `370abf0c8da07f30e669052c240edd4ecd09d6057fbf3f3004270965d21de770`; `complete.kva` `b4378a92f550f26b008842423f4831d0852f38be34379dcd05234d223592941b`.
- Source inspection found `CalibrationHelper.GetLengthText` maps both image points through `GetPoint` before `GeometryHelper.GetDistance`; `CalibratorPlane.Initialize(line)` builds a square from a line. The native v1 model uses only an undistorted uniform line scale. It does not execute the legacy transform, lens distortion, tracking, origin/offset, or other calibration modes. No runtime equivalence claim is made.
- Legacy KVA examples are inspected by automated XML structure tests only. They are not parsed into the native workspace, and no new `.kva` is generated.

## Implemented portable workflow

The native player now offers **Calibrate**, **Measure**, **Save analysis…**, and **Open analysis…** controls. It pauses video while collecting two image-space endpoints for calibration and two for a measured line, paints the calibration green and measured line red, and displays the computed distance with its unit and stream PTS. The overlay is visible only at the frame timestamp to which the measurement is attached.

The `kinovea.portable-analysis` schema v1 sidecar stores a relative media reference, media SHA-256 and byte length, dimensions, signed stream PTS, and the supported line analysis. Loading rejects wrong format/version, malformed or out-of-image coordinates, inconsistent computed values, missing/changed media and dimension mismatch. The saved PTS is opened on a candidate video session; the active session is replaced only after that candidate decodes the exact timestamp. This avoids a second presentation copy and preserves the current session on failed candidate open/seek. Saving uses a temporary sibling file before replacement.

This format is not `.kva`, does not persist all Kinovea metadata, and is limited to one calibrated distance at one video timestamp. Tracking, keyframes, general drawings, history/undo, multiple measurements, perspective calibration, lens distortion and legacy import/export are absent.

## Verification

Build commands (Release, local SDK):

```sh
dotnet build src/Kinovea.PortableDesktop/Kinovea.PortableDesktop.csproj -c Release --no-restore -m:1
dotnet build tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj -c Release --no-restore -m:1
DISPLAY=:0 XDG_RUNTIME_DIR=/run/user/1000 dotnet tests/Kinovea.PortableDesktop.Tests/bin/Release/net10.0/Kinovea.PortableDesktop.Tests.dll
dotnet build tests/PortableVideo.Tests/PortableVideo.Tests.csproj -c Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -m:1
dotnet tests/PortableVideo.Tests/bin/Release/net10.0/PortableVideo.Tests.dll
```

Results:

- Desktop application: build passed, 0 warnings/errors.
- Desktop test executable: build passed, 0 warnings/errors; 133 assertions passed on the local X11 display. This includes workspace save/load, signed PTS, relative media resolution, SHA mismatch rejection, schema rejection, coordinate/value validation, legacy sample structure inspection, and existing desktop presentation/session tests.
- PortableVideo: 144 assertions passed; generated H.264 fixture SHA-256 `af04755f612b5eff43791fe1665ee50d34b8888fdbe3eb3f300a94f8e84ca282` (64x48, eight CFR frames, stream timebase `1/16384`).
- Portable pixel core: 10 checks passed. Portable image reader: 1,637 assertions passed. Frame generator: 617 assertions passed. Reader shell: 96 assertions passed. Portable SingleFrame: 73 assertions passed.
- `python3 -m py_compile tools/native-player/analysis-roundtrip.py`: passed.
- `git diff --check`: passed for tracked changes; new Python script also compiled successfully.

The real desktop scenario `tools/native-player/analysis-roundtrip.py` passed all six actions: open/render, calibrate, measure, save with the native Avalonia storage provider, close, and reopen in the same process. The app reported restored PTS `0`, dimensions `64×48`, and measurement `200.6849315068492 mm`. The difference from nominal `200 mm` is due to pointer input quantization at display scaling `1.5`; the scenario bounds this result and independently recalculates it from the image-space coordinates actually reported by the app. Across the two open intervals the report showed two presentation copies total, one per open, `18,432` total presentation bytes. The analysis frame was restored in a single open-at-PTS operation.

The versioned scenario definition is `tools/native-player/scenarios/analysis-roundtrip-v1.json`. Local generated artifacts are outside Git at `/tmp/kinovea-analysis-roundtrip-evidence-final/`:

| Artifact | SHA-256 |
|---|---|
| media fixture | `af04755f612b5eff43791fe1665ee50d34b8888fdbe3eb3f300a94f8e84ca282` |
| workspace JSON | `ea85855ad8ac8698d4215e37b37c3df6e87ca8458ebcdcd9962f7826f2c6606b` |
| scenario manifest | `bc0e9b329fabf63d2883b21999a4e990fb2c5e21717386343eac80d3a397b49b` |
| `01-video-open.png` | `d66203944892983b58f6aab232ce8e7a0ffc98a88f922bfc4d5e0ecb03f64d3f` |
| `02-measured-overlay.png` | `1c67351df1b492efce9f2eb6886e8117374cc85e0382e88405d1b42af04d47d0` |
| `03-analysis-reopened.png` | `3eefa5dcd7df81412c827c8ac2e95a6b7fadb05b1db137c20180be853ade5e6e` |

The reopened screenshot was visually inspected: both colored measurement lines are visible over the frame and the restored measured value and PTS are displayed. Screenshot evidence establishes GUI rendering only, not legacy decoder pixel equivalence.

## Remaining issues

Windows and Wine execution, actual legacy `.kva` import/export, legacy calibration numerical parity, tracking/keyframes, more than one measurement, corrupt workspace UI recovery, and other media codecs remain `NOT_RUN` or `NOT_ESTABLISHED`. No GitHub Actions were run. The branch changes are currently uncommitted; the source-tree digest above is not an immutable commit reference.
