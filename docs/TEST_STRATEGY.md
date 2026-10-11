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

## Projective mapping source characterization
`tests/ProjectiveMapper.Characterization` links the exact `ProjectiveMapper.cs`, `Vector3.cs`, and `Ellipse.cs` sources under `net10.0`. The harness supplies a narrow `QuadrilateralF` substitute with the production vertex/indexer/constructor contract because production `QuadrilateralF` also depends on GDI+ drawing APIs, plus a no-op log4net surface because the mapper only constructs but does not call its logger during tested paths. Run:

```sh
dotnet run --project tests/ProjectiveMapper.Characterization/ProjectiveMapper.Characterization.csproj --configuration Release
```

The suite covers affine/projective vertex mapping, interior forward/backward round trips, quadrilateral overloads, default construction, invalid update behavior and homogeneous mapping. It deliberately pins the current homogeneous `Backward(Vector3)` Y result, whose implementation uses `mapMatrix[1,2]` where the other inverse terms use `unmapMatrix`; do not treat this characterization as approval to change or preserve that behavior without a legacy oracle. This source-linked harness does not execute production `QuadrilateralF` or calibration callers. Windows parity and application integration remain `UNVERIFIED`.

## Windows .NET Framework Bitmap oracle
`.github/workflows/bitmap-characterization.yml` runs on `windows-2022`, uses Visual Studio MSBuild, restores the packages needed by the actual `Kinovea.Video.Bitmap` project reference, checks for the .NET Framework 4.8 targeting pack, builds the headless harness, and invokes `Kinovea.Video.Bitmap.VideoReaderBitmap` through its public `Open`/navigation/geometry/summary APIs. The harness does not use `new Bitmap(path)` as a reader proxy. Run it locally from PowerShell with the same commands used by CI:

```powershell
nuget restore Kinovea.Services/packages.config -PackagesDirectory packages -NonInteractive -Verbosity normal
msbuild tests/Bitmap.Characterization/Kinovea.Bitmap.Characterization.csproj /t:Rebuild /m /nologo /verbosity:minimal /p:Configuration=Release /p:Platform=x64
tests/Bitmap.Characterization/bin/x64/Release/Kinovea.Bitmap.Characterization.exe --output artifacts/bitmap-characterization
```

The run produces `environment.json`, `source-hashes.json`, `fixture-manifest.json`, `runtime-metadata.json`, `test-results.json`, a text summary, generated fixture inputs, and native plus RGBA pixel files. Workflow artifacts upload under `if: always()`, including after restore, build, or characterization failures. A build pass is not a behavior pass; only `test-results.json` with all assertions/cases passing is behavioral evidence.

Fixture inputs are generated in the artifact directory from a documented pixel formula: 7x5 BMP, transparent 13x9 PNG, 11x7 JPEG and JPEG EXIF orientation tags 1 through 9, plus malformed PNG. JPEG bytes use the Windows GDI+ encoder at a fixed quality and therefore record their fixture SHA-256 and encoder/runtime metadata on each run. The manifest marks every run `CANDIDATE_NOT_APPROVED`; generated inputs and outputs never write into the approved-golden directory.

### Golden review and approval
1. Review a successful artifact from a specific workflow run and source commit. Confirm targeting-pack and runtime metadata, production and harness source hashes, every fixture hash, all test results, and pixel evidence files.
2. Repeat on a clean `windows-2022` run with the same source revision. Compare fixture and pixel hashes; investigate any difference before considering approval. Do not normalize away or change pixels to make runs match.
3. A reviewer approves a dedicated PR that adds an immutable versioned snapshot under `tests/Bitmap.Characterization/golden/approved/<baseline-id>/`, with provenance metadata naming the workflow run, commit, runner image, fixture hashes, source hashes, and reviewer. Keep this golden-data commit separate from harness/CI changes.
4. Future output changes are reviewed as diffs against that approved snapshot. Never auto-copy candidate artifacts over approved data or regenerate expected hashes during a test run. An intentional baseline update needs a separate explicit review and commit preserving the prior baseline or its history.

Windows run `38174132063` verified the targeting pack and restore, but its build failed before the characterization executable ran. The failure was caused by compiling the Services reference as `Release|AnyCPU`, which disables unsafe blocks. The workflow and harness now select `Release|x64`, matching an existing Services configuration with unsafe enabled. The corrected configuration awaits a Windows rerun; no approved golden data exists. Current Bitmap decoding, alpha/channel interpretation, orientation, pixel format/stride, timestamp, rotation and lifecycle outcomes remain `UNVERIFIED` until artifacts are produced and reviewed.
