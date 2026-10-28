# Portable calibrated-distance workspace v1

The native .NET 10 desktop player has an opt-in, small analysis workflow for a video frame: calibrate an image-space line against a known distance, draw a second line, show its calculated distance, and save/reopen the result with the media and selected timestamp.

The native player supports two deliberately distinct persistence paths: the editable portable sidecar (`*.kinovea.json`) described below, and a legacy `.kva` XML content-preservation path. The latter can open, relink, and save KVA XML without losing unknown sections, but it does not apply or render legacy analysis semantics.

## User workflow

1. Open a supported video in the native player.
2. Enter a positive calibration distance and its unit (the initial value is `100 mm`).
3. Choose **Calibrate**, then click the two endpoints on the displayed frame.
4. Choose **Measure**, then click the two endpoints of the distance to measure.
5. Save using **Save analysis…**. The calibration line is green and the measured line is red.
6. Close the video and choose **Open analysis…** to reopen the sidecar. The media, saved timestamp and line measurement are restored.

The tool pauses playback before collecting points. Points are stored in image coordinates, not viewport coordinates. The overlay is shown only at the saved frame timestamp. The numeric result is `measurement pixel length × known calibration length / calibration pixel length`. This is a uniform-scale line-calibration subset. Source inspection found that legacy `CalibrationHelper.GetLengthText` transforms both points before measuring their Euclidean distance and that `CalibratorPlane.Initialize(line)` builds a square from one known line. This prototype does not call or port those classes and does not apply their lens-distortion, tracking, origin-offset, or broader calibration state. It is not claimed as a regression-equivalent port.

## v1 data and invariants

The JSON root contains:

- `FormatId`: `kinovea.portable-analysis`.
- `SchemaVersion`: integer `1`; unsupported versions and unknown JSON fields are rejected.
- `MediaReference`: relative path from the sidecar directory.
- `MediaSha256` and `MediaLength`: protect against silently associating the sidecar with modified media.
- `Width`, `Height`: decoded video dimensions checked before replacing the current player session.
- `Timestamp`: the portable video stream timestamp in the decoder's stream timebase. It is not converted to the legacy reader's timestamp units.
- `DistanceAnalysis`: calibration endpoints, known distance/unit, measurement endpoints, and computed value.

Points must be finite and lie within the saved image extent. Calibration span and known distance must be positive. On load, the measured value is recalculated from geometry and calibration and compared with the stored value. Save uses a temporary sibling file followed by replacement to avoid exposing a partially written sidecar.

The `PortableImagePoint` record is independent of Avalonia. The UI converts pointer coordinates through the existing `ViewportTransform` before the analysis layer receives them. The overlay converts those same image points through the current transform when drawing, so zoom, pan, fit, and resize do not rewrite the analysis geometry.

## Boundaries and limitations

- The current vertical supports one static distance analysis attached to one video timestamp. It does not support a collection of drawings, keyframes, tracking points, annotations, undo/redo, or analysis history.
- The v1 calibration is a single uniform scale. Perspective/projective calibration, lens distortion, tracked calibration, custom origins/offsets, and the other Kinovea calibration modes are not implemented.
- `PortableKvaDocument` reads KVA versions in the source-inventoried 1.3–2.1 range with DTD/entity resolution prohibited, retains the full XML tree, supports media relinking, and atomically saves the retained tree. All other legacy data is preserved structurally, including unknown extension nodes. The native application can open a KVA and its referenced media, report section counts, and save the KVA content. It does **not** interpret or render the imported drawings, calibration, tracking, filters, image transforms, or camera motion; it does not perform legacy context adaptation, timestamp remapping, or old-format XSL conversion. The editable portable measurement remains separate and merging it into KVA is rejected. See the [legacy format inventory](LEGACY_ANALYSIS_FORMAT_INVENTORY.md).
- The portable sidecar persists the FFmpeg stream PTS and stream dimensions. It does not claim matching the legacy metadata serializer's time remapping, timestamp semantics, rotation, mirror, aspect, or calibration representation.
- Media SHA-256 is computed on save and open. Large videos incur a full sequential media read for each operation.
- Calls remain synchronous from the UI workflow, with awaited video session operations; no new concurrency/ownership model is introduced.
- Workspace loading preflights the media path, file length/hash, and stream dimensions before replacing the current video. The video session decodes the requested PTS on its candidate reader before replacing the current session, so an unavailable timestamp does not discard the previous session and does not require a second presentation copy. The exact PTS must exist in the saved media.
- Wine, Windows, and legacy `.kva` interoperability are not established by the portable tests.

## Verification

The portable desktop test executable covers JSON save/load, relative media resolution, media hash mismatch rejection, schema version rejection, image-coordinate calculation and validation, plus the checked-in v2.0 KVA sample structure. Its existing UI presentation/session assertions remain in the same executable. The existing portable video suite remains a separate FFmpeg decoder test.

For the actual application workflow, use `tools/native-player/analysis-roundtrip.py` with the deterministic clip generated by `tests/PortableVideo.Tests`. For KVA content preservation, use `tools/native-player/kva-roundtrip.py`. Both use the real Avalonia window and storage provider and write screenshots/manifests to a caller-selected local evidence directory; do not commit local display captures by default.
