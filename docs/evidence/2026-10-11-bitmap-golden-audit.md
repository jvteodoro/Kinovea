# Windows Bitmap candidate baseline audit

Status: **PENDING_HUMAN_APPROVAL**. This is an evidence audit and review package; it is not an approval and does not establish cross-platform parity.

## Revision and build audit

Both designated workflow runs resolve to source SHA `0a9e8a5f847b77e030355dd2f38a7de97a4a2572`. GitHub reports successful jobs `114575682028` and `114576081958`; both completed targeting-pack verification, package restore, MSBuild, the characterization execution step and artifact upload. The source commit is present locally and its message/log links the fix for the preceding JPEG fixture generation failure.

At that source revision, the harness project targets .NET Framework `v4.8`, configuration `Release|x64`, and `PlatformTarget=x64`. The workflow invokes Visual Studio `msbuild` with `/p:Configuration=Release /p:Platform=x64`, verifies the targeting-pack files before restore/build, and then executes the built harness. The runner manifest records image `20261004.326.1`, MSBuild `17.14.60.43110`, targeting-pack availability and release key `533325`. Runtime metadata records CLR `4.0.30319.42000`, 64-bit, GDI+ `10.0.20348.5622`, and log4net `2.0.14.0`. `Environment.OSVersion` reports `Microsoft Windows NT 6.2.9200.0`, which does not match the runner metadata's Windows 10.0.20348 build; use the runner image metadata as the OS identity and retain this runtime field as an observed discrepancy.

The report files in both artifacts say `PASS`, 19 cases, 537 assertions passed, zero failed assertions/cases, and no fatal error. Each artifact has 14 fixture inputs, 58 pixel outputs and 72 combined fixture/pixel files. Independent SHA-256 calculation from both downloaded ZIPs confirmed every path and byte under `fixtures/` and `pixels/` is identical. The first ZIP hash is `27bc62de97ef0ce075954eddebbc8f904a56d175a1be562125ec728d520a08ef`; the repeat ZIP hash is `1b0b3ed3a58563d947f6b2bc479b0d89ea83503dd250498f86cb4edff39696e5`. ZIPs differ because per-run environment and report metadata includes timestamps and machine names.

All 14 fixture entries' stored hashes independently match the bytes in both artifacts. Every frame's native and RGBA hash in the test report independently matches the referenced pixel file. The checked-in candidate's preserved fixture/pixel and JSON evidence files match run 38175088092. Three diagnostic files (MSBuild binary log, MSBuild text log and NuGet restore log) were not copied into Git; their original hashes remain in the manifest. The JSON run reports, environment/runtime metadata, manifests, 14 input files and 58 pixel files are preserved.

The workflow's source-hash list contains 23 paths. Hashes for files checked out on Windows match either the exact Git blob bytes or the blob normalized to Windows CRLF bytes at the recorded revision. This is a line-ending representation difference, not a source revision mismatch. The manifest records both forms so the source association is reproducible.

## Actual reader boundary

`Program.cs` instantiates `Kinovea.Video.Bitmap.VideoReaderBitmap` and calls its `Open`, `MoveRequest`, `PlayerRequest`, `UpdateVideoGeometry` and `ExtractSummary` public operations. The project references the production `Kinovea.Video.Bitmap`, `Kinovea.Video`, and `Kinovea.Services` projects. The returned `VideoFrame.Image` is inspected; this is not `new Bitmap(path)` substituted for the reader.

## Assertion quality and coverage gaps

The suite is useful executable characterization, but its 537 assertions are not 537 independent correctness oracles. Some assertions pin implementation-specific constants or behavior, while pixel hashes preserve observed bytes without independently deciding that every byte is semantically correct. This needs explicit reviewer consideration before acceptance.

| Area | Verified observation / assertion | Limitation to carry forward |
|---|---|---|
| Input provenance | Deterministic 7x5 BMP, 13x9 alpha PNG, 11x7 JPEG, EXIF 1–9 JPEGs, malformed PNG and unsupported GIF probe; all hashes are captured. | JPEG is encoded by Windows GDI+ at quality 93; exact fixture bytes are tied to that encoder/runtime. The unsupported GIF bytes test extension dispatch, not GIF decoding. |
| Pixel channels | For BMP `(0,0)`, `GetPixel` R/G/B/A is asserted against the fixture formula. All output pixels are saved as RGBA and native `LockBits` bytes are hashed. | No whole-frame independent expected channel comparison exists. Native byte order is described in metadata but not decoded/asserted; the RGBA conversion uses `GetPixel`, so it is not an independent check of raw channel order. JPEG has no per-pixel expected values. |
| Alpha | PNG alpha at `(0,0)` and `(2,0)` is asserted; captured summaries record min/max and transparent/translucent counts. | No expected color values are asserted for partially transparent pixels. Premultiplied versus straight alpha is not explicitly calculated or tested. `Format32bppArgb` is observed for this fixture only. |
| EXIF | Inputs cover orientation values 1–9; reports capture orientation property when available, dimensions, rotation and pixel outputs. 3/6/8 rotate by 180/90/270; mirrored values map to no internal rotation. | `FixtureFactory.ExpectedRotation` encodes the same mapping as the production source, so its rotation assertions repeat an implementation-derived expectation. No independent orientation transform matrix or expected per-pixel image is asserted for all nine values. Mirroring behavior remains weakly characterized. |
| Timestamps | Fixed image reader reports first/last timestamp, duration, interval and working zone; calls exercise first move, requested timestamp 37, player timestamp 80, steps and out-of-zone request. | Most expected values are hard-coded from current semantics. No negative timestamp, working-zone boundary 249, repeated first-move, overflow, or wider timebase cases. The image has no natural media timestamps. |
| Lifecycle | Open/close/reopen and summary extraction are exercised; `Loaded` transitions are asserted; reopened pixels are captured. | Closed-image access only records whether access returned or threw; it does not assert the expected exception. No repeated-cycle/resource-handle/leak measurement, double-close, failed-open cleanup or disposal ownership test. |
| Rotation/summary | Manual 0/90/180/270 dimensions, metadata and binary output are captured; a 4x2 summary thumbnail is recorded. | Rotation pixel order and thumbnail resampling are not checked against independent expected pixels. Geometry update return false is pinned but not independently explained. |
| Error handling | Malformed PNG returns `NotSupported`; GIF extension throws `NotImplementedException`. | Only one malformed byte sequence and one unsupported extension path are included. No truncated valid image, corrupt JPEG/EXIF, huge dimensions, color profiles or multi-frame/animated inputs. |
| Repeatability | Exact fixture/pixel bytes match on two clean runners with the same Windows image and GDI+ file version. | Two runs establish repeatability only for that runner image/runtime; not all Windows releases or future hosted image revisions. |

The current results therefore establish **TESTED Windows legacy characterization** for these observations. They are not independently approved expectations, `REGRESSION_VERIFIED` migration evidence, or Linux/.NET 10 compatibility evidence. Keep untested behavior `UNVERIFIED`.

## Approval guard

The versioned manifest is stored beside this evidence in the candidate snapshot and has `PENDING_HUMAN_APPROVAL`, with reviewer and approval timestamp unset. The comparison command refuses to execute a golden comparison unless the manifest status is exactly `APPROVED`; it has an audit-only mode for verifying this pending package. Normal harness/workflow output stays in `artifacts/bitmap-characterization/`. Neither tool writes under `golden/`.
