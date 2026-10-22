# ADR 0010: Portable analysis contract prototypes

- **Status:** PROPOSED
- **Date:** 2026-10-22
- **Decision scope:** Isolated .NET 10 analysis contracts for calibration, timestamp samples, state history, and drawing clipboard payloads

## Context

Epic 04 source inspection found these behaviors embedded in `Kinovea.ScreenManager`, which targets .NET Framework 4.8 and uses `System.Drawing`, XML metadata, WinForms, and live `Metadata` callbacks. Calibration maps through `CalibratorPlane`/`ProjectiveMapper`; tracked calibration is recomputed from metadata callbacks. Keyframe timestamp changes update child drawing references. `DrawingTrack` selects the nearest stored sample for display and measurement context. `HistoryStack` executes application-specific mementos and raises history events. The static drawing clipboard stores serialized drawing text, image position, and name.

## Decision

Add `Kinovea.PortableAnalysis` as an opt-in `net10.0` library with small data and state contracts. Planar calibration uses four image corners, positive world dimensions, a world origin and offset; it maps through a solved homography. `TimedSeries<T>` stores values sorted by timestamp, replaces duplicate timestamps, and offers explicit clamped linear interpolation for `Vector2`. `UndoHistory<T>` stores caller-provided state snapshots and clears redo on a new push. `DrawingClipboardPayload` carries serialized drawing text, position, and display name without depending on the OS clipboard.

These are newly specified portable contracts. They do not claim legacy equivalence. Keep legacy source and registration unchanged until Windows or Wine characterization covers calibrated transforms/rounding, tracked calibrator behavior, timestamp interpolation and nearest-sample selection, KVA persistence, memento side effects/events, and clipboard/UI interactions.

## Consequences

- The library builds without WinForms, GDI+, project references, or native packages.
- JSON roundtrips for `Vector2` fields require `PortableAnalysisJson.Options` (`IncludeFields = true`).
- Interpolation clamps outside the sample range; legacy `DrawingTrack.FindClosestPoint` instead selects the nearest registered timestamp. Consumers must choose the intended rule explicitly.
- Snapshot history does not implement legacy `HistoryMemento`, same-state drawing deduplication, exception recovery, or `HistoryChanged` events.
- The four-corner calibration contract has no radial distortion, tracked origin/calibration, units, formatting, or legacy rounding behavior. Its homography solver and corner mapping are not yet compared against `ProjectiveMapper`.
- No component is promoted to `PORTED` or `REGRESSION_VERIFIED` by this decision.
