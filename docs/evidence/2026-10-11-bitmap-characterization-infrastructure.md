# Verification: Windows Bitmap characterization infrastructure

- Baseline commit: `e443e417` (`port/net10-linux`).
- Scope: characterization infrastructure only; no production reader, behavior, UI, backend, or target framework changed.
- Host available during implementation: Gentoo Linux 2.18, kernel `6.18.50-gentoo-dist-bin`, x86_64.
- Available .NET: SDK `10.0.111`, runtime `10.0.11`.
- Windows/.NET Framework 4.8/GDI+: `NOT_RUN` on this host.
- Fixtures: generator source added; Windows-generated fixture hashes and decoded outputs are `NOT_RUN`.
- Golden baseline: none approved or committed.

## Implemented runner

`.github/workflows/bitmap-characterization.yml` selects `windows-2022`, sets up Visual Studio MSBuild and NuGet, records runner metadata and source SHA-256 values, checks the .NET Framework 4.8 targeting-pack files, restores the dependencies used by the Services reference, builds the characterization executable with the actual `Kinovea.Video.Bitmap` project reference, and runs it. Evidence upload uses `if: always()` so restore, build, and test failures still upload artifacts produced up to that point.

The current [GitHub-hosted Windows 2022 image manifest](https://github.com/actions/runner-images/blob/main/images/windows/Windows2022-Readme.md) lists Visual Studio 2022, the .NET Framework 4.8 SDK and targeting-pack component. The workflow checks the actual targeting-pack files at runtime; image documentation alone is not treated as runner evidence.

The harness creates BMP (7x5), transparent PNG (13x9), JPEG (11x7), JPEG EXIF orientation 1–9, malformed PNG, and unsupported-extension inputs in the artifact directory. It calls the production `VideoReaderBitmap.Open`, `MoveRequest`, `PlayerRequest`, `UpdateVideoGeometry`, and `ExtractSummary` APIs through a project reference. It captures frame dimensions, GDI+ pixel format, native stride/pixel rows, normalized RGBA bytes, alpha counts, DPI, EXIF property where available, timestamps, rotation/geometry, summary thumbnail pixels, same-object behavior, close/reopen state, and input/output hashes. It does not use `new Bitmap(path)` as a substitute for the reader.

Expected artifact files are `environment.json`, `source-hashes.json`, `runtime-metadata.json`, `fixture-manifest.json`, `test-results.json`, `test-results.txt`, the generated fixture inputs, and `pixels/*.native.bin` plus `pixels/*.rgba`. The test report has explicit assertion/case statuses and the process exits nonzero on any failed assertion or case. All output is marked `CANDIDATE_NOT_APPROVED`.

## Validation performed here

| Command | Exit | Result |
|---|---:|---|
| `xmllint --noout tests/Bitmap.Characterization/Kinovea.Bitmap.Characterization.csproj` | 0 | Project XML is well-formed. |
| Python/PyYAML parse of `.github/workflows/bitmap-characterization.yml` plus structural assertions | 0 | YAML parses; job selects `windows-2022`, contains a targeting-pack check step and artifact upload with `if: always()`. |
| Roslyn C# 7.3 syntax parse of the three harness `.cs` files | 0 | Syntax trees reported no parser errors. This is syntax-only and does not type-check or build the harness. |
| `git diff --check` | 0 | No whitespace errors. |
| `dotnet msbuild tests/Bitmap.Characterization/Kinovea.Bitmap.Characterization.csproj -target:Build -property:Configuration=Release -property:Platform=AnyCPU -verbosity:minimal` | 1 | MSB3644: .NET Framework v4.8 reference assemblies are unavailable on this Linux host. This does not validate the harness compile. |
| Workflow run on `windows-2022` | NOT_RUN | No Windows runner was available during this task. |
| Reader characterization execution | NOT_RUN | No behavioral results or pixel outputs were produced here. |

No `pwsh`, Windows PowerShell, Mono, or Visual Studio MSBuild executable was present on the host. The MSBuild failure above is an environment targeting-pack failure, not a test result. XML validation and source inspection do not establish that the harness compiles or passes on Windows.

## Oracle approval process and limits

Candidate artifacts must be reviewed against their workflow source commit, production/harness source hashes, fixture hashes, runner/targeting-pack metadata, test report, and pixel files. Repeat the same source revision on a clean `windows-2022` runner and investigate all differences before approval. A reviewer then adds a versioned, immutable baseline in a separate reviewed commit under `tests/Bitmap.Characterization/golden/approved/<baseline-id>/`, recording run ID, source SHA, runner image, fixture/source/output hashes, and reviewer. The generator and workflow write only to artifacts and never overwrite approved data. Baseline changes require a separate review and commit.

No approved baseline exists yet. All Bitmap decoder behavior—including EXIF handling and flips, pixel format/channel/alpha semantics, stride, timestamps, rotation, disposal and summary output—remains `UNVERIFIED`. The hard-coded assertions in the new harness are characterization checks to be measured on Windows; until then they are expectations derived from the inspected source, not actual results. ADR 0002 remains `PROPOSED` pending evidence and architectural review.
