# Verification and regression strategy

## Truthful verification labels
- `BUILD_ONLY`: compile succeeded; no functional claim.
- `TESTED`: declared tests passed on recorded platform.
- `PORTABLE_TESTED`: relevant tests passed on Linux and Windows.
- `REGRESSION_VERIFIED`: results demonstrably agree with known legacy oracle within documented tolerances.
- `NOT_RUN`, `BLOCKED`, `UNVERIFIED`: explicit unknowns; cannot be converted to green.

## Test levels
1. **Static/build:** all paths, project references, analyzers, target frameworks, OS-dependent API inventory.
2. **Unit:** geometry, conversions, calibration, timeline timebase, interpolation, data model serialization.
3. **Characterization:** deterministic I/O fixtures and oracles from trusted legacy behavior; use relative/absolute numerical tolerances with rationale.
4. **Integration:** actual FFmpeg native libraries and codec operations; file reader/writer; plugin discovery; camera adapters if device present.
5. **End-to-end:** open/play/seek/annotate/measure/save/reopen/export, on Linux and Windows.
6. **Nonfunctional:** memory, seek latency, decode throughput, synchronization, leaks under repeat; report platform-specific baselines rather than inventing thresholds.

## Golden master and oracle policy
- Prefer legacy test or saved output from a verified Windows baseline. Record legacy version, OS, runtime and command.
- If legacy execution cannot be performed, construct expectations from specs/invariants/independent tools and label `REFERENCE_VALIDATED`, not `REGRESSION_VERIFIED`.
- Keep fixture provenance, license, generation script, codec/format, checksums, expected results and tolerance under version control.
- For decoded pixel checks, exact equality only if pipeline is deterministic. Otherwise document color model, bit depth, decoder and a justified metric/tolerance; do not silently relax assertions.
- Numerical geometry checks need reference coordinates, units, rounding and edge cases; serialization needs round-trip and backwards-compatibility tests.

## Evidence record (one per slice and important run)
Store `docs/evidence/YYYY-MM-DD-<slice>.md` with:

```markdown
# Verification: <slice>
Commit: <sha>
Host: <OS/version/architecture>
SDK/runtime: <versions>
Native libraries: <names/versions>
Fixtures: <paths and SHA-256>
Legacy oracle: <source and version or UNAVAILABLE>
Commands:
- `<exact command>` => exit `<code>`; `<N passed, M failed, K skipped>`
Platform checks: Linux <result>; Windows <result or NOT_RUN>
Functional observations: <measured or explicitly not tested>
Regressions: <differences, tolerance and rationale>
Limitations: <what cannot be claimed>
```

Do not commit generated giant logs by default; link a CI artifact, store summarized failure codes and optionally small diagnostic snippets. Unavailable environment means `NOT_RUN`, not success.

## Per-PR gates
- Tests are linked to the same modified components.
- Negative paths and representative boundary cases covered.
- All executed commands and outputs recorded.
- Comparison oracle status specified.
- Windows and Linux outcomes separated.
- No new skipped tests without rationale and task reference.

## Timecode source characterization
`tests/Timecode.Characterization` is a package-free `net10.0` console assertion suite that compiles the exact `TimeHelper.cs`, `TimeCodeFormat.cs`, `TimeCodeType.cs`, and `TimestampMapper.cs` files from the legacy tree. Run it with:

```sh
dotnet run --project tests/Timecode.Characterization/Timecode.Characterization.csproj --configuration Release
```

The suite records 50 Linux observations/assertions across all format enum branches, type mapping, culture, rounding, negative and large values, normalization, timestamp narrowing and identity mapping. It is source characterization only: the suite does not establish that the legacy .NET Framework executable behaves identically on Windows. Keep parity `UNVERIFIED` until a Windows legacy oracle is captured and compared.

## Geometry source characterization
`tests/Geometry.Characterization` is a package-free `net10.0` assertion suite linking the exact `Vector3.cs`, `Matrix3x3.cs`, and `SVD.cs` sources. `SVD.cs` is included because `Matrix3x3`'s pseudo-inverse and SVD methods call it. Run it with:

```sh
dotnet run --project tests/Geometry.Characterization/Geometry.Characterization.csproj --configuration Release
```

The suite uses exact comparisons where values are exactly representable and a documented `1e-6` absolute tolerance for float vectors/matrices. It covers arithmetic, tie rules, normalization including zero, construction/indexing, rotations, determinant/inverse, singular and near-singular inputs, transpose products, pseudo-inverse, and SVD reconstruction. It is Linux source characterization only; Windows parity remains `UNVERIFIED`.
