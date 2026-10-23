# Epic 04 legacy-to-portable regression comparison v1

Date: 2026-10-22  
Comparison base: `ca8d5413a949fdad49e1837c81a6028601dfcfcc`

Final comparison source: `7cc73baa3e77570bdd161d842028330323a34b75`.
Initial comparison commit: `90a4b8c030699899968396b02c40588394852cf5`.
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
<recalculate after homography extension commit>  ProjectiveMapper.cs
<recalculate after homography extension commit>  Calibration.cs
```

## Executed comparison

```sh
dotnet run --project tests/PortableDrawing.LegacyComparison.Tests/PortableDrawing.LegacyComparison.Tests.csproj \
  --configuration Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false
```

Exit code: `0`  
Result: `PASS: 34 legacy-to-portable drawing arithmetic comparison assertions`

| Property | Legacy source operation | Portable operation | Cases | Result | Evidence class |
|---|---|---|---:|---|---|
| Image-space distance | `GeometryHelper.GetDistance` | `Measurements.Distance` | 3 | `PASS` within `1e-5` | `CROSS_SOURCE_MATCH` |
| Signed angle | `GeometryHelper.GetAngle` | `Measurements.AngleRadians` | 4 | `PASS` within `1e-5` | `CROSS_SOURCE_MATCH` |
| Clockwise flag | Legacy call with legs reversed | `counterClockwise: false` | 1 | `PASS` | `CROSS_SOURCE_MATCH` |
| Calibrated distance | Explicitly map endpoints, then `GetDistance` | `AffineCalibration` then `Distance` | 1 | `PASS` | `CROSS_SOURCE_MATCH` |
| Calibrated angle | Explicitly map all points, then `GetAngle` | `AffineCalibration` then `AngleRadians` | 1 | `PASS` | `CROSS_SOURCE_MATCH` |
| Unsigned wrap | `AngleHelper` source rule: add `2π` to a negative signed value | `signed: false` | 1 | `PASS` | `SOURCE_RULE_MATCH` |
| Rectangular and trapezoid calibration | Exact linked `ProjectiveMapper.Backward` plus the documented `CalibratorPlane` D-origin/Y-up conversion | `PlanarCalibration.ImageToWorld` | 24 coordinate comparisons | `PASS` within `1e-5` | `CROSS_SOURCE_MATCH` |

The test does not load a `DrawingLine`, `DrawingAngle`, `DrawingTimeSegment`,
`CalibratorPlane`, `CalibrationHelper`, or `AngleHelper` object. The calibration
comparison uses the exact linked `ProjectiveMapper` source and applies the legacy plane
conversion (`worldX = gridX - originX`; `worldY = -gridY + originY`) with origin at
image quadrilateral D. It compares authored rectangle and trapezoid fixtures. It does
not exercise calibration initialization, line-axis construction, distortion, tracked
origin, units, or rounding.

## Supporting executable characterization

| Command | Exit | Result | Scope |
|---|---:|---|---|
| `dotnet run --project tests/Geometry.Characterization/Geometry.Characterization.csproj --configuration Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false` | 0 | `PASS: 319 geometry characterization assertions` | Linked legacy `Vector3`/matrix source; three existing compiler warnings. |
| `dotnet run --project tests/ProjectiveMapper.Characterization/ProjectiveMapper.Characterization.csproj --configuration Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false` | 0 | `PASS: 40 ProjectiveMapper characterization assertions` | Linked mapper source plus existing test seams; existing nullable warnings. |
| `dotnet run --project tests/Kinovea.PortableGeometry.Tests/Kinovea.PortableGeometry.Tests.csproj --configuration Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false` | 0 | `PASS: 22 portable geometry assertions` | Portable viewport contract only. |
| `dotnet run --project tests/PortableDrawing.Tests/PortableDrawing.Tests.csproj --configuration Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false` | 0 | `PASS 36 portable drawing assertions` | Portable overlay contract only. |
| `dotnet run --project tests/Kinovea.PortableAnalysis.Tests/Kinovea.PortableAnalysis.Tests.csproj --configuration Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false` | 0 | `Portable analysis contract checks: PASS (34 assertions)` | Portable calibration/history contract only. |
| `dotnet run --project tests/PortableDrawing.LegacyComparison.Tests/PortableDrawing.LegacyComparison.Tests.csproj --configuration Release -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false` | 0 | `PASS: 34 legacy-to-portable drawing arithmetic comparison assertions` | Direct linked-source arithmetic and projective comparison. |

## Wine and Windows scope

The local Wine oracle definition validation passed for its existing still-image and
video scenarios. It establishes that the installed Kinovea `2025.2.0.0` can open a
still image, display the player header, canvas, timeline, and drawing toolbar. A local
run of `large-png-still.v1.json` passed the visible header OCR (`1280x720`, `25.00 fps`);
its captured source SHA is `90a4b8c030699899968396b02c40588394852cf5`;
the window capture was `1920x969`, and the first-visible-image screenshot SHA-256 was
`e0596a00a6a05bdc0a1d9048c701e45c7c999555815d0dfb402636f2c6f1db55`. A new guarded
line-tool probe passed the player-header OCR but failed closed before mouse input because
XTest pointer motion did not reach the requested point (`requested root=(560,846)`,
`observed root=(540,647)`). No annotation was created or verified; the tool selection
and drawing outcome are `NOT_ESTABLISHED`. The captured source SHA was
`90a4b8c030699899968396b02c40588394852cf5`; the untracked-at-run scenario content is
now committed with SHA-256
`70fd6e4b714409dc42a242e6c356b66b4c1dcc4d9742fb3940fff9d7b6fd73b1`. The result JSON
SHA-256 is `6f9ebc9e86d15234aef7ca5e985c2decba7265312ad38d11e95a2258b2d8d278`; the
header screenshot SHA-256 is `5fcfd8852ea0a5dca53f6e70bc166bb55252aee5d349f98e71d30a7cab9c4c66`.
The screenshots and result manifest are local under `/tmp/kinovea-epic04-regression-wine/`
and are not committed. Its source
correspondence with this `2026.1.0.0` checkout remains
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

Status: the listed distance/angle formulas and the six-point-per-fixture calibration
sample are regression compared against exact checked-out legacy source under `net10.0`.
The truncated GUI probe did not reach its click phase. Epic 04 overall remains
`UNVERIFIED` against a running Windows legacy application; no GUI, rendering,
calibration, persistence, or ownership parity is claimed.
