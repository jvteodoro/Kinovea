# Verification: ProjectiveMapper source characterization

- Baseline commit: `43c8fae5c0ccdaec7f94a42870661bc1ac70d2c3` (`port/net10-linux`).
- Host: Gentoo Linux 2.18, kernel `6.18.50-gentoo-dist-bin`, x86_64 GNU/Linux.
- SDK/runtime: .NET SDK `10.0.111`, .NET runtime `10.0.11`.
- Native libraries: none used.
- Fixtures: deterministic in-memory quadrilaterals; no files or external media.
- Legacy oracle: unavailable; no Windows .NET Framework output was captured.

## Scope and harness boundary

The `net10.0` harness links the exact production `ProjectiveMapper.cs`, `Vector3.cs`, and `Ellipse.cs` files. Its test-only `QuadrilateralF` type implements the constructor, vertex properties, and indexer consumed by ProjectiveMapper. The production type also includes GDI+ polygon/region operations, so the production `QuadrilateralF` was not linked or executed. The harness supplies a no-op `log4net` interface because the source constructs a logger but does not call it on the exercised paths. These seams limit the result to the mapper's scalar math and API use; they do not establish production integration.

## Command and result

From the repository root:

| Command | Exit | Result |
|---|---:|---|
| `dotnet run --project tests/ProjectiveMapper.Characterization/ProjectiveMapper.Characterization.csproj --configuration Release` | 0 | `PASS: 40 ProjectiveMapper characterization assertions`. Build emits existing-source nullable warnings: Vector3 override nullability; ProjectiveMapper uninitialized matrix fields, possibly null quadrilateral mapping, and nullable logger type. No warning was suppressed. |
| `git diff --check` | 0 | No whitespace errors. |

## Characterized behavior

- A perspective trapezoid maps all four plane vertices to corresponding image vertices; an interior point round-trips through `Forward(PointF)` and `Backward(PointF)` within `1e-5` absolute tolerance.
- Both quadrilateral overloads preserve corner correspondence in forward and backward directions.
- An affine rectangle maps and reverses an interior point as expected.
- The parameterless constructor leaves its matrices unset; calling `Forward(PointF)` throws `NullReferenceException`.
- An invalid output quadrilateral makes `Update` return early and preserves the previous mapping. A non-parallelogram degenerate input causes `Update` to throw `NullReferenceException` while composing matrices.
- `Backward(Vector3)` currently returns Y `3161.6` for the documented fixture. Source inspection confirms the Y expression uses `mapMatrix[1,2] * p.Z`, while the other entries use `unmapMatrix`. The test pins the observed result as a source quirk, not as desired or legacy-approved behavior.
- For the affine fixture that maps the unit square to bounds from `(3,4)` to `(7,10)`, `Ellipse()` reports center `(3,4)`, semi-major axis `3`, semi-minor axis `2`, and rotation `π/2`.

## Limits and status

- Linux source characterization only; Windows legacy execution and output comparison are `NOT_RUN`.
- The production `QuadrilateralF`, GDI+ methods, project-level log4net assembly, calibration workflow, and downstream drawing/measurement callers were not executed.
- No production code or behavior changed. This test is not proof that ScreenManager, calibration, or projective mapping works as an integrated application component.
- Regression status: `UNVERIFIED`; the expected values characterize linked source on .NET 10/Linux and include a suspicious homogeneous-coordinate result without a legacy oracle.
