# Legacy analysis formats and persistence inventory

**Source basis:** `Kinovea.ScreenManager/Metadata/Serialization/MetadataSerializer.cs`, `MetadataConverter.cs`, the drawing/keyframe serializers, `Tools/XML/Schema/`, and the three checked-in sample documents. This is an inventory of source behavior; it is not a claim that the portable application semantically imports every field.

## Legacy input and output routes

`MetadataSerializer.Load(metadata, source, isFile)` dispatches by suffix:

| Input | Legacy handling | Portable status |
|---|---|---|
| `.kva` | `MetadataConverter.Convert`, then `MetadataSerializer.Load(XmlReader)` | XML document content can be loaded and saved without removing unknown elements/attributes; legacy context adaptation and rendering are not implemented. |
| `.xml` | Same KVA parser route | Same document preservation limits as `.kva`. |
| `.srt` | `MetadataImporterSRT.Import` | Not implemented here. |
| `*_keypoints.json` | `MetadataImporterOpenPose.Import` | Not implemented here. |
| `.trc` | `MetadataImporterSports2D.Import` | Not implemented here. |

Legacy KVA conversion accepts versions `1.3` through `<2.0` via the `kva-1.5to2.0.xsl` transform, rejects versions `<=1.2`, and leaves `2.0+` content unchanged before parsing. The current writer emits `2.1`. The portable document layer accepts the inventoried numeric range `1.3` through `2.1` for preservation; it deliberately does **not** apply the XSL conversion or legacy import transformations.

`SaveToFile` writes root `KinoveaVideoAnalysis` and version `2.1`. `KVAExportFlags` conditionally emits calibration, drawings/keyframes, and video-specific data. The normal `Full` path includes:

| KVA content | Legacy source path | Current portable behavior |
|---|---|---|
| Version/producer, media name/path/length, title, image size, aspect, rotation, mirror, demosaicing, deinterlacing, stabilization track, background color | `WriteGeneralInformation`; parsed in `Load` | Preserved as XML; basic version/media/image-size fields are indexed. |
| Average timestamp interval, capture/video frame rates, first timestamp, selection range, time origin, context | `WriteGeneralInformation`; consumed before coordinate/time-sensitive sections | Preserved; selected integer/double fields are indexed. No remapping to the FFmpeg stream timebase occurs. |
| Calibration, lens intrinsics/distortion, world/image calibration geometry, unit, origin/offset, calibration driver | `WriteCalibration`; `CalibrationHelper.ReadXml/WriteXml` | Preserved as XML only; not applied to the portable measurement engine. |
| Keyframes, timestamps, title/comment, attached drawings and measured data | `WriteKeyframes`; `KeyframeSerializer` and `DrawingSerializer` | Preserved as XML only; keyframe counts and recognized drawing-name counts are indexed. Unknown drawing names are retained but not included in the recognized count. No interpolation, initialization, coordinate scaling, or display is applied. |
| Detached chronos and tracks | `WriteChronos`, `WriteTracks`; `DrawingSerializer` | Preserved as XML only; top-level counts are indexed. |
| Spotlights, number sequence, coordinate system, test grid | `WriteSpotlights`, `WriteNumberSequence`, `WriteCoordinateSystem`, `WriteTestGrid` | Preserved as XML only. |
| Trackability points, video filters, camera motion | `WriteTrackablePoints`, `WriteVideoFilters`, `WriteCameraMotion` | Preserved as XML only. Not executed by the portable player. |
| Unknown/future extension elements and attributes | Legacy importer skips unknown root children; the writer reconstructs known data | Portable KVA document retains the complete XML tree, including extension nodes, attributes, comments, and unknown nested content. |

The legacy importer calls `BeforeKVAImport`/`AfterKVAImport`, determines same-video context, may scale image coordinates and remap timestamps, fixes relative trajectories after old-format conversion, merges keyframes, initializes drawings, and applies stabilization-track references after tracks are read. On parsing exceptions it logs and returns; callers must not mistake that legacy behavior for a strong success result. The portable preservation API avoids silently pretending these transformations occurred.

## Checked-in sample fixtures

All three samples are KVA `2.0`; they are small, repository-authored legacy fixtures, not newly generated approved goldens.

| File | SHA-256 | Observed content |
|---|---|---|
| `Tools/XML/Schema/samples/minimal.kva` | `5ea713f0aee2275db431460a9fdfbedf347a04e57025a034caebc5ecd76c02ce` | Header, media name, dimensions, time interval, first timestamp, selection start. |
| `Tools/XML/Schema/samples/simple.kva` | `370abf0c8da07f30e669052c240edd4ecd09d6057fbf3f3004270965d21de770` | Calibration help and a keyframed angle drawing. |
| `Tools/XML/Schema/samples/complete.kva` | `b4378a92f550f26b008842423f4831d0852f38be34379dcd05234d223592941b` | Multiple keyframes, comments, and a broad set of drawing records. This is not proof that every 2.0 XSD or 2.1 writer feature is represented. |

## Portable KVA preservation contract

`PortableKvaDocument` uses the .NET XML reader with DTD/entity resolution prohibited and a bounded character count. It requires the Kinovea root and one supported `FormatVersion`. It exposes basic metadata, section counts, keyframe/track/chrono counts, and a count of drawing element names recognized by the inventory while keeping the full XML document for save. Unknown drawing element names remain preserved even though they are not included in that recognized count. `SaveAsync` writes to a sibling temporary file then atomically replaces the destination. `RelinkMedia` returns a cloned document with path/name/byte length changed; all other XML content remains intact. A document's serialized whitespace/encoding can be normalized; the guarantee is structural XML content preservation, not byte-for-byte identity.

The native application can open KVA plus its referenced media (or ask the user to relink missing media) and save the retained KVA XML through the native storage provider. It reports keyframe/track/chrono/drawing counts. It does not render the imported legacy drawings or apply legacy timestamp, calibration, filter, transform, or tracking semantics. New portable distance analyses remain in the separate `.kinovea.json` format; merging those edits into a KVA document is explicitly rejected rather than silently discarding them.

## Remaining semantic work

To complete semantic portability, implement and separately characterize adapters for calibration and image transforms; keyframe/drawing types and styles; track/chrono/trackability records; time origin and selection remapping; video filters and camera motion; plus SRT, OpenPose JSON, and TRC import. Unsupported KVA sections currently survive roundtrip but remain inert. No cross-format equivalence is established by XML preservation alone.
