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
| Workflow run `38174132063`, job `114572777464` | 1 | `windows-2022` image `20261004.326.1`; Visual Studio MSBuild and .NET Framework 4.8 targeting-pack check passed; NuGet restore passed with `NU1902` for log4net 2.0.14. Build failed with nine CS0227 errors in `Kinovea.Services/Bitmap/BitmapHelper.cs` and `Kinovea.Services/Infrastructure/NativeMethods.cs`: workflow selected `Release|AnyCPU`, where unsafe blocks are disabled. The uploaded artifact is [bitmap-characterization-38174132063-1](https://github.com/jvteodoro/Kinovea/actions/runs/38174132063/artifacts/11714599252), 258671 bytes, SHA-256 `dae3cb2c0498f91e5e412d2aa289695a121aa35811dbb725f7350f735eda312e`. This establishes a CI build configuration failure only. |
| Reader characterization execution in run `38174132063` | NOT_RUN | Build failure skipped the harness. No fixtures, test report, or pixel evidence were generated. |
| Workflow run `38174665514`, job `114574398335` | 1 | Targeting-pack check and restore passed; x64 build cleared the CS0227 errors. Harness compilation then failed with CS0234/CS0246 because the old-style project did not directly reference `Kinovea.Services` (including `ImageRotation`), and CS0118 because `Bitmap` resolved to the enclosing `Kinovea.Bitmap` namespace in `Program.cs` and `EvidenceWriter.cs`. The uploaded run artifact contains build diagnostics only; reader execution was skipped. |
| Corrective harness build after run `38174665514` | NOT_RUN | Added a direct `Kinovea.Services` project reference and aliased `System.Drawing.Bitmap` in all harness sources. Needs another Windows workflow run. |
| Workflow run `38174835118`, job `114574930533` | 1 | Targeting-pack check, restore, x64 Services build, direct Services reference and System.Drawing type alias all cleared their prior errors. Harness compilation then failed with CS0012/CS0246 because `Kinovea.Video` public API types (`VideoReader`, `VideoFrame`, `VideoSummary`, `VideoInfo`, `VideoGeometry`, and `OpenVideoResult`) were not directly referenced. The harness did not run; uploaded artifacts contain compile diagnostics only. |
| Corrective project reference after run `38174835118` | NOT_RUN | Added a direct `Kinovea.Video` project reference to the harness project. Must be checked on the next Windows run. |
| Reader characterization after direct Video reference | NOT_RUN | No local Windows runner is available; updated project reference has not yet run on GitHub. |

No `pwsh`, Windows PowerShell, Mono, or Visual Studio MSBuild executable was present on the implementation host. The local MSBuild failure above is an environment targeting-pack failure, not a test result. The GitHub runs confirmed targeting-pack and restore availability. The x64 build fixed the original legacy configuration mismatch; subsequent CI runs surfaced old-style project-reference requirements. Direct Services and Video references and System.Drawing aliases are now explicit. XML validation and source inspection do not establish that the latest harness compiles or passes on Windows.

## Oracle approval process and limits

Candidate artifacts must be reviewed against their workflow source commit, production/harness source hashes, fixture hashes, runner/targeting-pack metadata, test report, and pixel files. Repeat the same source revision on a clean `windows-2022` runner and investigate all differences before approval. A reviewer then adds a versioned, immutable baseline in a separate reviewed commit under `tests/Bitmap.Characterization/golden/approved/<baseline-id>/`, recording run ID, source SHA, runner image, fixture/source/output hashes, and reviewer. The generator and workflow write only to artifacts and never overwrite approved data. Baseline changes require a separate review and commit.

No approved baseline exists yet. All Bitmap decoder behavior—including EXIF handling and flips, pixel format/channel/alpha semantics, stride, timestamps, rotation, disposal and summary output—remains `UNVERIFIED`. The hard-coded assertions in the new harness are characterization checks to be measured on Windows; until then they are expectations derived from the inspected source, not actual results. ADR 0002 remains `PROPOSED` pending evidence and architectural review.
