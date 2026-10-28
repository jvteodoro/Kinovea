# Analysis persistence v2 — local implementation evidence

**Status:** `PORTABLE_TESTED` for native Linux UI and structural KVA content roundtrip. Legacy semantic compatibility is `NOT_ESTABLISHED`. Windows/Wine and hosted CI were not run; compatibility/performance runs are deferred while feature implementation proceeds.

## Revision and environment

- Worktree branch: `port/analysis-roundtrip`; base `origin/port/net10-linux` at `5d0f59f9b1b6f8297af77739d96b2cac3f9211b0`.
- No Git commit exists for these uncommitted changes. The implementation-tree SHA below provides a local content reference, not a commit identifier.
- Source-tree SHA-256: `76b5dbb3f9849840591252273a6d75360bf3cfbe023542cdd4e168488446869f`, calculated over UTF-8 relative path, NUL byte, and file bytes in order for `PortableAnalysisWorkspace.cs`, `PortableKvaDocument.cs`, `PortableVideoPlayerSession.cs`, `ImageViewport.cs`, `MainWindow.cs`, desktop test `Program.cs` and its project file, `analysis-roundtrip.py`, `kva-roundtrip.py`, and both scenario JSON files.
- Gentoo Base System 2.18, Linux x86_64, kernel `6.18.50-gentoo-dist-bin`; .NET SDK `10.0.111` / runtime `10.0.11`; Avalonia `12.1.4`; FFmpeg `8.1.2` (loaded ABI avformat 62.12.102, avcodec 62.28.102, avutil 60.26.102, swscale 9.5.102); X11 display `:0`, scaling `1.5`.

## Delivered behavior

1. The portable JSON v1 path remains available for a newly created uniform-scale distance measurement on a single stream PTS. It hashes media, validates dimensions/points/computed value, saves atomically, and opens the exact frame on a candidate session before replacing the active reader.
2. `PortableKvaDocument` safely parses the inventoried KVA version range 1.3–2.1 with DTD/entity resolution disabled and a configurable XML character bound. It preserves the full XML document tree, reports header fields and section counts, supports copy-on-relink for `FullPath`/`OriginalFilename`/`ByteLength`, and saves via a sibling temporary file and replacement.
3. The desktop opens `.kva` from its real file picker or command line, opens its referenced media, prompts for media when the reference is absent/unavailable, shows the KVA version and record counts, and provides Save legacy KVA. It does not rewrite the KVA from a partial native model. When new native measurements are present, KVA saving is disabled with an explanation; those measurements remain in the separate portable JSON format.
4. The KVA roundtrip retained calibration help, keyframe line drawing, track, chrono, trackability, video filters, camera motion, and an unknown vendor extension. XML canonicalization was equal before and after UI save, and the saved KVA reopened in the same process with the same section counts and recognized drawing-name count.

The serializer inventory and checked-in sample hashes are in [the legacy analysis format inventory](../LEGACY_ANALYSIS_FORMAT_INVENTORY.md). The KVA path is structural preservation, not semantic support: keyframe drawings are not painted, legacy timestamps are not mapped onto stream PTS, and calibration, filters, camera motion, tracking, and transforms are retained but not applied. `.srt`, OpenPose keypoints JSON, and `.trc` import are also not implemented. This evidence does not establish Windows/Kinovea application interoperability.

## Commands and results

```sh
dotnet build src/Kinovea.PortableDesktop/Kinovea.PortableDesktop.csproj -c Release --no-restore -m:1
dotnet build tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj -c Release --no-restore -m:1
DISPLAY=:0 XDG_RUNTIME_DIR=/run/user/1000 dotnet tests/Kinovea.PortableDesktop.Tests/bin/Release/net10.0/Kinovea.PortableDesktop.Tests.dll
python3 -m py_compile tools/native-player/analysis-roundtrip.py tools/native-player/kva-roundtrip.py
git diff --check
```

Both builds passed with zero warnings/errors. The desktop suite passed **148 assertions**, including JSON validation, structural save/reload for all checked-in KVA fixtures, relink cloning, rejection of wrong root/unsupported version/DTD, and existing GUI session/presentation cases. Python syntax and `git diff --check` passed.

The actual GUI analysis scenario passed six actions (open/render, calibrate, measure, storage-provider save, close, same-process reopen); restored PTS was `0`, measured result `200.6849315068492 mm`, two presentation copies across two open intervals. Pointer quantization at 1.5 scaling accounts for the difference from nominal 200 mm; the result matches independent recomputation from app-reported image points.

The actual KVA GUI scenario passed three actions (open KVA with a missing media path, explicitly relink through the native picker, save with canonical XML equality against the expected relink, then close and reopen in the same process). Synthetic document had one keyframe, one track, one chrono, three recognized drawing element names, and preserved unknown vendor XML. The source and saved KVA hashes differ because the chosen media path is added and formatting is normalized; canonical XML equals the expected document after that explicit relink.

## Local artifacts

Artifacts remain outside Git:

- Analysis scenario: `/tmp/kinovea-analysis-persistence-final/`
- KVA scenario: `/tmp/kinovea-kva-persistence-final/`
- Shared deterministic media SHA-256: `af04755f612b5eff43791fe1665ee50d34b8888fdbe3eb3f300a94f8e84ca282`.
- Analysis manifest SHA-256: `d05f4352b146c603fbab9517095028a23f100fdd524e45e2ee95f90faaddb081`; saved portable workspace: `ea85855ad8ac8698d4215e37b37c3df6e87ca8458ebcdcd9962f7826f2c6606b`.
- KVA input SHA-256: `73e63112e2153e5a6111aecb85726182cbe684b3ab825bb2b737721ae9a477ba`; saved KVA: `3766cac747571418485b84e77487a5027c68d2d32272587574da4d79159ca1fb`; canonical XML equality: `PASS`.
- KVA manifest SHA-256: `c2f72db5f78f4d081f540cd849e27948eaa4ddacac779b0e5380989f035e0def`.
- KVA screenshots: load `d13ece47c5443945b001aa45f81ea13fa3881918569a5d991882fa0db8f77f5f`; saved `24eb0e662d991dfcb788a591a3346201063d53b88763c0d738fd4fd81f4f819a`; reopened `731f5e2e41860f88ac81337dfb0ef639f21731cce38d9cbae200bb18a6822d30`.

## Separate feature work not implemented by this persistence roundtrip

- Apply and render known legacy drawing types and time placement; preserve unsupported types unchanged.
- Rehydrate and edit calibration, trackability, filters, and camera metadata rather than only retaining their XML.
- Implement or explicitly scope `.srt`, OpenPose keypoints JSON, and `.trc` import.
- Decide old-version conversion and same/different-media timestamp/coordinate mapping; current implementation deliberately does not apply `MetadataConverter` XSL or `MetadataSerializer` context remapping.
- Add user-facing tests for missing/relinked media, malformed KVA recovery without losing the current session, and saving modified portable annotations in a documented format.
- Then run the requested compatibility/performance phases separately, with Wine observations distinct from Windows verification.
