# Epic 04 legacy-to-portable regression comparison v1

Date: 2026-10-22  
Comparison source: `ca8d5413a949fdad49e1837c81a6028601dfcfcc`  
Host: Gentoo Linux 2.18 x86_64, kernel `6.18.50-gentoo-dist-bin`; .NET SDK `10.0.111`, runtime `10.0.11`  
Hosted CI and Windows .NET Framework execution: `NOT_RUN`

## Purpose and oracle boundaries

This evidence starts the Epic 04 regression path without changing legacy production code.
`tests/PortableDrawing.LegacyComparison.Tests` links the unchanged legacy
`GeometryHelper.cs`, `Vector.cs`, and `PointLinePosition.cs` directly into a narrow
`net10.0` executable, and compares its observable arithmetic results to
`Kinovea.PortableDrawing.Measurements` on the same authored inputs. The linked source
is a **source-level legacy oracle**, not the net48 application executable. Test-only
seams supply unrelated compilation dependencies (`log4net`, `MathHelper.Radians`, and
two geometry extensions) and are not called by the distance or angle comparison paths.

Input source hashes:

```text
39ea71d6537936f740428a1b97413f5a9cea310bf9a6dda658c53f08fbf1028c  GeometryHelper.cs
5837c724bfd7f71b35a62d81a2fde561835a839d0465bf6239a3858fc3ea435d  Vector.cs
4961fe3b3b80a7733f8436c924defe189f436cd72bfc683253c405285758493e  Measurements.cs
```

## Executed comparison

```sh
dotnet run --project tests/PortableDrawing.LegacyComparison.Tests/PortableDrawing.LegacyComparison.Tests.csproj \
  --configuration Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false
```

Exit code: `0`  
Result: `PASS: 10 legacy-to-portable drawing arithmetic comparison assertions`

| Property | Legacy source operation | Portable operation | Cases | Result | Evidence class |
|---|---|---|---:|---|---|
| Image-space distance | `GeometryHelper.GetDistance` | `Measurements.Distance` | 3 | `PASS` within `1e-5` | `CROSS_SOURCE_MATCH` |
| Signed angle | `GeometryHelper.GetAngle` | `Measurements.AngleRadians` | 4 | `PASS` within `1e-5` | `CROSS_SOURCE_MATCH` |
| Clockwise flag | Legacy call with legs reversed | `counterClockwise: false` | 1 | `PASS` | `CROSS_SOURCE_MATCH` |
| Calibrated distance | Explicitly map endpoints, then `GetDistance` | `AffineCalibration` then `Distance` | 1 | `PASS` | `CROSS_SOURCE_MATCH` |
| Calibrated angle | Explicitly map all points, then `GetAngle` | `AffineCalibration` then `AngleRadians` | 1 | `PASS` | `CROSS_SOURCE_MATCH` |
| Unsigned wrap | `AngleHelper` source rule: add `2π` to a negative signed value | `signed: false` | 1 | `PASS` | `SOURCE_RULE_MATCH` |

The test does not load a `DrawingLine`, `DrawingAngle`, `DrawingTimeSegment`,
`CalibrationHelper`, or `AngleHelper` object. It confirms only the specific scalar
formula and ordering boundaries above.

## Supporting executable characterization

| Command | Exit | Result | Scope |
|---|---:|---|---|
| `dotnet run --project tests/Geometry.Characterization/Geometry.Characterization.csproj --configuration Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false` | 0 | `PASS: 319 geometry characterization assertions` | Linked legacy `Vector3`/matrix source; three existing compiler warnings. |
| `dotnet run --project tests/ProjectiveMapper.Characterization/ProjectiveMapper.Characterization.csproj --configuration Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false` | 0 | `PASS: 40 ProjectiveMapper characterization assertions` | Linked mapper source plus existing test seams; existing nullable warnings. |
| `dotnet run --project tests/Kinovea.PortableGeometry.Tests/Kinovea.PortableGeometry.Tests.csproj --configuration Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false` | 0 | `PASS: 22 portable geometry assertions` | Portable viewport contract only. |
| `dotnet run --project tests/PortableDrawing.Tests/PortableDrawing.Tests.csproj --configuration Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false` | 0 | `PASS 36 portable drawing assertions` | Portable overlay contract only. |
| `dotnet run --project tests/Kinovea.PortableAnalysis.Tests/Kinovea.PortableAnalysis.Tests.csproj --configuration Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false` | 0 | `Portable analysis contract checks: PASS (34 assertions)` | Portable calibration/history contract only. |

## Wine and Windows scope

The local Wine oracle definition validation passed for its seven existing still-image and
video scenarios. It establishes that the installed Kinovea `2025.2.0.0` can open a
still image, display the player header, canvas, timeline, and drawing toolbar. It has
no executable annotation, measurement, calibration, keyframe, clipboard, or undo/redo
scenario. Its source correspondence with this `2026.1.0.0` checkout remains
`NOT_ESTABLISHED`. Therefore it cannot promote any row in this evidence to
`WINDOWS_VERIFIED` or GUI `CROSS_ENVIRONMENT_MATCH`.

## Coverage gaps and next comparison

| Area | Status | Required next oracle |
|---|---|---|
| GDI+ drawing, rendered angle sweep, viewport-scaled handle hit regions | `NOT_ESTABLISHED` | Windows/net48 or guarded Wine mouse scenario with screenshot review plus source-linked model capture. |
| `DrawingTimeSegment` fraction/time conversion and 10-pixel edit rule | `NOT_ESTABLISHED` | Actual legacy object characterization with a controlled timestamp mapper. |
| `CalibratorPlane`, distortion, units, rounding, tracked calibration | `NOT_ESTABLISHED` | Actual `CalibrationHelper` characterization on Windows; compare a versioned numeric trace. |
| DrawingTrack nearest-keyframe selection, keyframe persistence and KVA | `NOT_ESTABLISHED` | net48 trace fixture and KVA roundtrip. |
| History memento execution, event order/deduplication and system clipboard | `NOT_ESTABLISHED` | Legacy runtime characterization with explicit observers. |
| Running Windows .NET Framework parity | `NOT_RUN` | Manual Windows job or an economical, manually dispatched workflow after fixture review. |

Status: the two arithmetic properties listed as `CROSS_SOURCE_MATCH` are regression
compared against the exact checked-out source under `net10.0`. Epic 04 overall remains
`UNVERIFIED` against a running Windows legacy application; no GUI, rendering,
calibration, persistence, or ownership parity is claimed.
