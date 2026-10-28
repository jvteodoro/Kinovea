# ADR 0010: Isolate a portable drawing and measurement domain

Status: Accepted for the opt-in portable desktop migration
Date: 2026-10-22
Base: `origin/port/epic03-desktop` at `ae4c7d964db3024d704717ee0ebb33ad5e185a57`

## Context

Legacy drawings derive from `AbstractDrawing`, which exposes GDI+ rendering, WinForms `Keys`, XML serialization, calibration, fading, and undo/redo through `Kinovea.ScreenManager`. `DrawingLine`, `DrawingAngle`, and `DrawingTimeSegment` contain reusable point and measurement rules, but the project cannot be referenced from a portable `net10.0` application without importing its platform dependencies.

## Decision

- Add `Kinovea.PortableDrawing` as an opt-in `net10.0` library depending only on the .NET base class library.
- Represent lines, angles, and time segments as image-coordinate overlays. Keep painting, persistence, calibration ownership, and UI event wiring outside this library.
- Use a calibration point-mapping interface so distance and angle calculations operate on mapped endpoints, as legacy `GetLengthText` and `AngleHelper` do.
- Preserve legacy drawing collection hit order (first matching item), handle identifiers for the selected basic shapes, line midpoint copy points, angle origin copy point, and the time-segment fraction handle/minimum-length rule.
- Keep the new domain independent of `ScreenManager`; do not retarget or edit legacy drawing sources in this slice.

## Consequences

The library can be built and exercised on Linux without WinForms or GDI+. Hit tolerances are explicit image-coordinate distances and use Euclidean geometry; legacy handle tests use a viewport-scaled square and line body tests use widened GDI+ paths. Angle body selection covers the two arms rather than the legacy rendered sweep region. Time measurement accepts explicit frame-start and frame-interval timestamps; it does not reproduce metadata timecode formatting, frame-rate inference, fade visibility, KVA serialization, keyframes, distortion, tracking, style presets, or undo/redo. These differences keep this slice small and must be addressed before connecting it to the desktop UI or claiming legacy parity.

See [Epic 04 portable drawing evidence](../evidence/epic04-portable-drawing-v1.md).
