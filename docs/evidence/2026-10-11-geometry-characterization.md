# Verification: geometry source characterization

Baseline commit: `02b879d5` (`test(timecode): characterize legacy formatting`).
Host: Gentoo Linux 2.18, Linux `6.18.50-gentoo-dist-bin`, x86_64.
SDK/runtime: .NET SDK `10.0.111`, MSBuild `18.0.11`, .NET runtime `10.0.11`.
Native libraries: none used.
Fixtures: deterministic scalar/vector/matrix inputs; no external fixtures.
Legacy oracle: unavailable. Windows .NET Framework run: `NOT_RUN`.

## Harness

`tests/Geometry.Characterization/Geometry.Characterization.csproj` targets `net10.0` without external packages and links `Kinovea.ScreenManager/Measurement/Geometry/Vector3.cs`, `Matrix3x3.cs`, and `SVD.cs`. The latter is a required local implementation dependency for `Matrix3x3.SVD` and `PseudoInverse`. The AForge copyright headers remain in the source files and are not copied into a replacement implementation.

## Command and result

- `dotnet run --project tests/Geometry.Characterization/Geometry.Characterization.csproj --configuration Release` => exit `0`; `PASS: 319 geometry characterization assertions`.
- Compiler warnings from linked source: CS8765 at `Vector3.Equals(Object)` and `Matrix3x3.Equals(Object)` for nullability annotation mismatch; CS8981 for the lowercase type name `svd`. They were not suppressed or changed.
- `git diff --check` => exit `0`.

## Coverage and observations

Assertions cover vector constructors, component/scalar arithmetic helpers and operators, exact equality, hash consistency, norm/square, normalize, inverse, abs/negate, dot/cross, extrema and index tie behavior, array/string output; matrix identity/row/column/diagonal creation, row/column bounds exceptions, arithmetic, multiplication order, vector transform, transpose, self-transpose products, exact equality, determinant, adjugate, inverse, singular exception, near-singular inversion without a threshold rejection, pseudo-inverse and SVD reconstruction; and X/Y/Z and yaw-pitch-roll rotation conventions.

Vector/matrix comparisons use `1e-6` absolute tolerance for float components. The near-singular inverse test uses a diagonal entry of `1e-6` with tolerance `1.0` on the resulting `1e6` component. SVD reconstruction is checked at `1e-6` for the selected well-conditioned matrix.

The zero-vector `Normalize` call returns zero and leaves all three coordinates as NaN on this runtime. Exact singular inversion throws `ArgumentException`; the tested near-singular diagonal matrix is inverted without a singularity threshold. The X rotation matrix maps positive Z toward negative Y for a positive quarter-turn in this source convention.

## Limits

This is a Linux execution of the linked source under .NET 10, not a comparison to the original .NET Framework runtime. Windows behavior and legacy parity remain `NOT_RUN` / `UNVERIFIED`. The harness does not test `ProjectiveMapper`, calibration workflows, or the ScreenManager project integration; it does not establish that the broader component is portable or regression-verified.
