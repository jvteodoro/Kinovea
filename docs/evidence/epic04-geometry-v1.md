# Epic 04 portable geometry and viewport contract

## Baseline and scope

- Base: `origin/port/epic03-desktop`, commit `ae4c7d964db3024d704717ee0ebb33ad5e185a57`.
- Implementation: `cd5fe947147c5c09b1b85b506153fb8677da6543` (`port: extract portable viewport geometry contract`).
- Host: Gentoo Linux x86_64, kernel `6.18.50-gentoo-dist-bin`.
- SDK/runtime: .NET SDK `10.0.111`, .NET runtime `10.0.11`.
- Native libraries and fixtures: none; all transform inputs are deterministic in-memory values.
- Legacy oracle: Windows .NET Framework execution `NOT_RUN`; there is no approved legacy output for this transform.

This slice moves the existing desktop `ViewportTransform` calculations into the BCL-only `Kinovea.PortableGeometry` project and gives the caller explicit geometry values (`Point2D`, `Size2D`, `Vector2D`, `Rect2D`). It preserves the existing fit, offset, image/viewport conversion, and anchored-zoom formulas. Image and viewport coordinates use top-left origins, with X increasing right and Y downward. Image coordinates describe continuous pixel edges, in image pixels; pan uses viewport logical pixels. The library does not define physical measurement units, convert time/measurement units, or port the legacy `ProjectiveMapper`.

Relevant legacy source inspected: `Kinovea.ScreenManager/Measurement/Geometry/Vector3.cs`, `Matrix3x3.cs`, `SVD.cs`, and `ProjectiveMapper.cs`. The existing source-linked Linux harnesses separately characterize Vector3/matrix behavior (319 assertions) and mapper behavior (40 assertions). Mapper observations include invalid quadrilateral update outcomes and the current `Backward(Vector3)` Y expression/result; they remain observations, not approved behavior changes. The `QuadrilateralF` substitute and logger seam limit the mapper harness; calibration consumers and production `QuadrilateralF` are not exercised. Earlier reports are [arithmetic characterization](2026-10-11-geometry-characterization.md) and [ProjectiveMapper characterization](2026-10-11-projective-mapper-characterization.md).

## Commands and results

Commands ran from the repository root. `--no-build` is used for the desktop focused invocation after its project build, so it does not need NuGet restore a second time.

| Command | Exit | Result |
|---|---:|---|
| `dotnet run --project tests/Kinovea.PortableGeometry.Tests/Kinovea.PortableGeometry.Tests.csproj --configuration Release` | 0 | `PASS: 22 portable geometry assertions` |
| `dotnet build tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj -c Release -v:minimal -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -p:RestoreIgnoreFailedSources=true -m:1` | 0 | Presentation and desktop test projects build; 0 warnings, 0 errors. |
| `dotnet run --no-build --project tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj --configuration Release -- --viewport-only` | 0 | `Portable desktop viewport integration tests: PASS (14 assertions)` |
| `dotnet run --project tests/Geometry.Characterization/Geometry.Characterization.csproj --configuration Release --property:NuGetAudit=false --property:MSBuildEnableWorkloadResolver=false` | 0 | `PASS: 319 geometry characterization assertions`; three existing linked-source warnings (two CS8765, one CS8981). |
| `dotnet run --project tests/ProjectiveMapper.Characterization/ProjectiveMapper.Characterization.csproj --configuration Release --property:NuGetAudit=false --property:MSBuildEnableWorkloadResolver=false` | 0 | `PASS: 40 ProjectiveMapper characterization assertions`; existing nullable warnings from linked source and test seam. |
| `dotnet tests/Kinovea.PortableDesktop.Tests/bin/Release/net10.0/Kinovea.PortableDesktop.Tests.dll` | 1 | Full presentation/player executable could not initialize Avalonia: `XOpenDisplay failed`. Full display-backed suite is `NOT_RUN`; use the focused viewport mode above in a headless environment. |
| `git diff --check` | 0 | No whitespace errors. |

## Limits

- This is Linux execution of the new .NET 10 contract, not Windows runtime evidence or legacy parity.
- The source-linked legacy characterization results do not compare outputs against a Windows .NET Framework executable.
- The production legacy `ProjectiveMapper`, `QuadrilateralF`, calibration flow and drawing/measurement callers were not moved or integrated into `Kinovea.PortableGeometry`.
- `Kinovea.PortableDesktop` uses the new contract at the Avalonia drawing/interaction boundary; this does not prove complete GUI behavior because the display-backed suite was unavailable.
- Status: `PORTABLE_TESTED` for the isolated viewport-coordinate contract on this Linux host; ScreenManager geometry and ProjectiveMapper parity remain `UNVERIFIED`.
