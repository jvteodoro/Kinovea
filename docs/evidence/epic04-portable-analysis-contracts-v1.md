# Epic 04 portable analysis contract evidence v1

## Scope and baseline

Addresses the portable-contract implementation slice requested for Beads `Kinovea-gh43` (calibration), `Kinovea-gh44` (timestamped values), and `Kinovea-gh45` (undo/redo and drawing clipboard data). Isolated clone branch `port/epic04-calibration-history`, based on `origin/port/epic03-desktop` SHA `ae4c7d964db3024d704717ee0ebb33ad5e185a57`. The checkout's original projects remain net48. No legacy production source, UI, project references, serialization path, or clipboard integration was changed.

Source inspection:

- `Kinovea.ScreenManager/Measurement/Calibration/CalibratorPlane.cs`: maps a world rectangle to an image quadrilateral using `ProjectiveMapper`; default origin aligns to quadrilateral D; grid Y is down and world Y is up; supports line calibration by synthesizing a square; XML stores dimensions, corners, origin, and offset.
- `CalibrationHelper.cs`: undistorts image inputs, delegates static and tracked transforms, and formats values using `Math.Round(value, decimalPlaces)`; tracked calibration relies on callbacks into Metadata and the calibration drawing.
- `Metadata/DrawingManager/Keyframe.cs`: timestamp assignment updates each child drawing's reference time; keyframe names can update trajectory labels; thumbnails are GDI+ Bitmaps; KVA XML is custom.
- `Tracking/Tracking/DrawingTrack.cs`: `FindClosestPoint` linearly scans timestamps and returns the nearest stored point (first point wins equal-distance ties); this source path does not establish a general linear interpolation rule.
- `UndoRedo/HistoryStack.cs`: stores executable mementos, suppresses equal consecutive drawing-modify states, clears redo on new commands, toggles an undo/redo flag, and raises events. `DrawingClipboard.cs` stores drawing XML text, position, and name in static process state.

These observations are source characterization only. Windows/Wine execution and legacy output traces are unavailable, so all runtime equivalence is `UNVERIFIED`.

## Change

Added `src/Kinovea.PortableAnalysis` (`net10.0`) containing:

- `PlanarCalibration`: finite four-corner homography mapping, image/world inverse, origin/offset, positive dimensions, and degenerate-input checks.
- `TimedSeries<T>`: sorted timestamp storage, exact lookup, duplicate timestamp replacement, and explicit clamped linear interpolation for `Vector2`.
- `UndoHistory<T>`: reversible immutable/caller-owned state snapshots, action labels, and redo invalidation after a new edit.
- `DrawingClipboardPayload`: serializable payload fields without system clipboard dependencies.
- `PortableAnalysisJson.Options`: JSON configuration enabling `System.Numerics.Vector2` field roundtrip.

Added `tests/Kinovea.PortableAnalysis.Tests`, a dependency-free console contract suite using authored known-answer quadrilateral fixtures, coordinate roundtrip/persistence checks, timestamp ordering/replacement/interpolation, undo/redo branches, and invalid-input cases.

## Verification

Environment: Gentoo Linux x86_64, kernel `6.18.50-gentoo-dist-bin`, .NET SDK `10.0.111`, runtime from SDK `10.0.11`.

Command:

```sh
dotnet run --project tests/Kinovea.PortableAnalysis.Tests/Kinovea.PortableAnalysis.Tests.csproj --configuration Release
```

Result: exit code 0; `Portable analysis contract checks: PASS (34 assertions)`. Project uses `TreatWarningsAsErrors=true`. The suite covers a trapezoid's diagonal intersection as a perspective known-answer point and a rectangle center as a linear known-answer point; it does not establish parity against the legacy mapper. The first run identified that `System.Numerics.Vector2` values require JSON field inclusion; the contract now provides `PortableAnalysisJson.Options`, and the passing suite covers save/reopen through that option. A Release library build passed with 0 warnings/errors and `git diff --check` passed. No Windows, Wine, legacy executable, hosted Actions, GUI, KVA reader/writer, or FFmpeg checks ran.

## Status and limitations

Status: `BUILD_ONLY` for the isolated portable contracts. This does not meet Epic 04 acceptance for legacy parity. The original Linear/planar calibration semantics, midpoint rounding, distortions, tracked calibration/origin, keyframe mutation side effects, trajectory/kinematics definitions, undo/redo execution effects, command deduplication, events, OS clipboard behavior, and KVA save/reopen are not implemented or characterized by executable comparison. `TimedSeries` interpolation is a declared new rule; legacy track rendering instead uses nearest-sample lookup. The broader Epic dependencies #40, #42 and #41 are still open in the inspected issue graph. Do not mark these issues done from this contract work alone.

Windows validation: `NOT_RUN`. Wine oracle: `NOT_RUN`. Legacy baseline comparison: `NOT_RUN`. Hosted Actions: `NOT_RUN`.
