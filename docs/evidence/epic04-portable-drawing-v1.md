# Epic 04 portable drawing domain evidence — v1

Date: 2026-10-22
Base source: `origin/port/epic03-desktop`, `ae4c7d964db3024d704717ee0ebb33ad5e185a57`
Implementation commit: see the commit containing this evidence
Host: Gentoo Linux 2.18, x86_64, .NET SDK `10.0.111`, MSBuild `18.0.11`, .NET runtime `10.0.11`
Fixture: synthetic vectors authored in `tests/PortableDrawing.Tests/Program.cs`; no external media or binary fixture
Hosted GitHub Actions: `NOT_RUN`

## Baseline source inspection

The legacy contracts were read directly from the source at the base SHA:

- `Kinovea.ScreenManager/Metadata/Drawings/AbstractDrawing.cs`: drawing identity, reference timestamp, hit return convention (`-1` miss, `0` body, positive handle), whole-object movement, handle movement and copy point are combined with GDI+ painting, WinForms keys, serialization and host callbacks.
- `DrawingLine.cs`: A/B handles are 1/2, optional label handle is 3, copy point is the segment midpoint, line movement translates both endpoints. Measurement uses the calibrated endpoints. Shift movement constrains endpoints to 45-degree increments.
- `DrawingAngle.cs`: origin/A/B handles are 1/2/3, copy point is the origin, signed/clockwise/supplementary options feed `AngleHelper`, and object hits use the rendered sweep region.
- `DrawingTimeSegment.cs`: endpoint/fraction handles are 1/2/3; fraction is projected onto and clamped to the segment; the label time is interpolated inside a frame interval; edited segments are kept at least 10 image pixels long.
- `HitTester.cs`: point handles use a viewport-scaled square with a 10-viewport-pixel radius; line hits widen a GDI+ path.
- `Metadata.DrawingsHitTest`: attached drawings are inspected in collection order, stopping at the first hit.
- `GeometryHelper.GetDistance` and `GetAngle`: Euclidean distance and `atan2(cross,dot)` signed angle in radians.
- `AngleHelper` and `CalibrationHelper.GetLengthText`: points are mapped into calibrated space before angle/distance calculation; unsigned negative angles wrap by adding one full turn.
- `Metadata.GetFractionTime`: derives a frame index from the average timestamp-per-frame, then interpolates from that frame's start to next-frame boundary using the stored fraction.

## Change and verification

Added `src/Kinovea.PortableDrawing` targeting `net10.0` with no package references, plus `tests/PortableDrawing.Tests`, a deterministic console assertion suite. The domain covers line and angle overlays, time-segment position/fraction and timestamp interpolation, explicit point-calibration mapping, hit records, ordered collection selection, basic handle/body manipulation, and removal. It does not reference or edit legacy projects.

Exact commands:

```sh
dotnet build tests/PortableDrawing.Tests/PortableDrawing.Tests.csproj -c Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -m:1
dotnet tests/PortableDrawing.Tests/bin/Release/net10.0/PortableDrawing.Tests.dll
git diff --check
```

Results: build exited 0 with 0 warnings and 0 errors (9.04 seconds including restore check); suite exited 0 with `PASS 36 portable drawing assertions`; `git diff --check` exited 0. The first draft of the suite had an incorrect expected line length and expected calibrated distance; those expected values were corrected to match the authored 10-unit line and 2× scale. The final suite is the recorded result.

Built artifact SHA-256 values:

```text
22cbe269f2b899b58dc966d633e900be3f99520b5f2e9f6fb488f5deafacea35  Kinovea.PortableDrawing.dll
489867b7d2c187f90d2d6fa74e8a103f3285e14645c37eb69ffd3b44adb696e2  PortableDrawing.Tests.dll
```

## Status and limitations

The standalone domain is `PORTABLE_TESTED` on this Gentoo Linux host. It is not integrated with the portable desktop or legacy application, so no project-level or application-level port status is inferred. The assertions are source-derived contract checks, not a credible output capture from the legacy .NET Framework classes. `REGRESSION_VERIFIED` is therefore `UNVERIFIED`.

Windows .NET 10, Windows .NET Framework 4.8, Wine execution of the legacy desktop, rendering, gesture routing, persistence, calibration UI, and end-to-end annotation are `NOT_RUN`. Euclidean image-space hit tolerance differs from legacy viewport-scaled square handles; line bodies are hit against simple segments; angle body hits cover the two arms rather than the legacy sweep region. The time segment accepts explicit frame timestamps and does not format Kinovea timecodes. Fading, distortion, keyframes, tracking, style presets, KVA compatibility, clipboard and undo/redo are outside this implementation.

Follow-up: capture the same line, angle and time-segment cases through the actual legacy runtime on a supported Windows/Wine setup, and add a rendering/presentation adapter only after those observations are reviewed.
