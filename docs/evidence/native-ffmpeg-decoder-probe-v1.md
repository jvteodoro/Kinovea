# Isolated FFmpeg decoder probe v1

## Scope and architecture

This is a diagnostic, test-only .NET 10 console probe. It invokes FFmpeg through `FFmpeg.AutoGen` 8.1.0, opens an explicit local FFmpeg shared-library directory, decodes one synthetic video sequentially, and converts each decoded frame to an owned RGBA32 managed array through `libswscale`. It is not connected to Avalonia, `PortableReaderShell`, production `VideoReaderFFMpeg`, Kinovea caches, or playback. It makes no product backend selection.

The experiment chooses a direct binding only to test the installed FFmpeg 8.1 ABI. It avoids FFMediaToolkit because the candidate version examined during the prior boundary investigation targets a different FFmpeg major family. This is a probe decision, not a general library recommendation. FFmpeg.AutoGen 8.1.0's package license is MIT (verified from the restored NuGet package's `ffmpeg.autogen.nuspec` and `LICENSE.txt`). Native FFmpeg licensing is independent: the local Gentoo runtime was built with GPL and libx264 enabled. A product distribution configuration and notices still require separate legal review.

## Reproduction

From the repository root, with .NET SDK 10, Python 3, FFmpeg CLI, FFmpeg 8.1 shared libraries and the NuGet package available:

```sh
dotnet restore tests/FFmpegDecoderProbe/FFmpegDecoderProbe.csproj
dotnet build tests/FFmpegDecoderProbe/FFmpegDecoderProbe.csproj -c Release -m:1
python3 -m py_compile tests/FFmpegDecoderProbe/generate_fixture.py tests/FFmpegDecoderProbe/verify_probe.py tests/FFmpegDecoderProbe/run_local_checks.py
python3 tests/FFmpegDecoderProbe/run_local_checks.py --output-dir /tmp/kinovea-ffmpeg-fixture --library-dir /usr/lib64
```

Fixture generation uses synthetic, asymmetric BGR0 pixels authored in Python, FFV1 lossless encoding in a NUT container, and a separately authored JSON manifest containing complete expected RGBA bytes and rational presentation times. It creates a deterministic corrupt input as well. All generated binary files and reports are stored under `/tmp`, not checked in.

## Executed result

Execution was local on Gentoo Linux x86_64. Base revision was `61850e7931456d57bba09c69a15d746b2773bf39`; tested probe source commit was `4e4ce942455ea06d2da97847e4a029d1e8c54091`. SDK 10.0.111, MSBuild 18.0.11 and .NET runtime 10.0.11 were used. Native FFmpeg was 8.1.2 with `libavformat 62.12.102`, `libavcodec 62.28.102`, `libavutil 60.26.102`, and `libswscale 9.5.102`, resolved from `/usr/lib64`.

| Check | Result | Evidence |
|---|---|---|
| .NET Release build | PASS; 0 warnings, 0 errors | `dotnet build tests/FFmpegDecoderProbe/FFmpegDecoderProbe.csproj -c Release --no-restore -m:1` |
| Python script syntax | PASS | `python3 -m py_compile tests/FFmpegDecoderProbe/generate_fixture.py tests/FFmpegDecoderProbe/verify_probe.py tests/FFmpegDecoderProbe/run_local_checks.py` |
| Deterministic video decode | PASS; 50 verifier checks, 5/5 frames, 4x2 | `run_local_checks.py`; generated FFV1/NUT fixture and JSON report |
| Timestamp mapping | PASS; each `best_effort_timestamp × 1/51200` equals authored `frameIndex/25` | `verify_probe.py` exact rational comparison |
| Pixel layout | PASS; source decoder format `bgr0` with reported source `linesize[0]=64`; all 32 logical RGBA bytes/frame match authored values | Full-frame base64 bytes and SHA-256 comparison, plus authored `(0,0)` channel probe; asymmetric rows/pixels expose orientation |
| Repeated open/close | PASS; three open/decode/cleanup cycles in one process; observations identical | `repeatedOpenCount=3`, `repeatedRunsConsistent=true` |
| Corrupt input | PASS; exit code 1 and explicit `avformat_open_input` invalid-data error | `run_local_checks.py` expected-failure assertion |
| Windows execution | NOT_RUN | No Windows local run or hosted Actions workflow was used |
| Actual Kinovea `VideoReaderFFMpeg` comparison | NOT_RUN | Existing reader is C++/CLI/net48; this probe does not execute it |

The decode report records 15 output frame arrays over three runs, 480 output pixel bytes, and `fullFrameCopyCount=15` (one `sws_scale` conversion into each owned output array). This final diagnostic invocation measured 30,656 managed bytes on the calling thread and 49.3344 ms summed elapsed time for all three tiny decodes. These figures include probe-side managed work, exclude native allocations and other threads, and are not a benchmark or product performance estimate. `sws_scale` may use internal native implementation details not measured by this counter.

## Artifact hashes

Generated locally in `/tmp/kinovea-ffmpeg-fixture`:

| Artifact | SHA-256 |
|---|---|
| `lossless-4x2-5frames.nut` | `5e5a18644791aab549f730dec3884a35a082c52fbeae84ba0e75a2060ed1f09f` |
| `source.bgr0` | `67c4084102e5f4bcefc963eac891cbe44ac2dff9727dab324bac62065c7f0da8` |
| `fixture-manifest.v1.json` | `fa292f2e54152ce8b697c4e5336873271ca6ff2800149ff00766f9b95a7683b9` |
| `decode-report.v1.json` | `43f8e9092bf39375b744688dfd7039bac39f470603bca8ced99ab1d8a493e75c` |
| `corrupt-input.bin` | `fc18b0b95f0c51717a6b3c11e4a4897ec3b33dd94b1fb85e55bc073d0fe5910d` |

Generating the fixture in two separate directories produced identical hashes for raw bytes, encoded clip, corrupt input and normalized manifest. The video and raw hashes are deterministic for the documented local FFmpeg encoder build; other FFmpeg builds may produce a different container bitstream while still encoding the same authored pixels, so fixture byte identity outside this environment is not established.

The verified source file hashes in tested commit `4e4ce942455ea06d2da97847e4a029d1e8c54091` are:

| Source | SHA-256 |
|---|---|
| `Program.cs` | `04315e12a54933f9ffca51564a0910cae95562a30e4717626308387ac5c81d84` |
| `FFmpegDecoderProbe.csproj` | `803c2aa12ef8d32c3e2f10a4be7bc6db4d79f2528a25aa9cff077003f43bb723` |
| `generate_fixture.py` | `e4b92d667bea8a62e5dc8fc64f50319a535f927985c4fa4e45656939093d950e` |
| `verify_probe.py` | `835aa4882268850e7d5b90ae0cd04593dbb6f6ffe6fdcd20bfa89a2e0b855630` |
| `run_local_checks.py` | `124ec335719f6c858ab67fda39cc26b6f49c3785d6a9745b41db6e5ff02dc939` |

## What this does and does not establish

Established only for this Linux environment and fixture: FFmpeg.AutoGen 8.1.0 loaded the installed FFmpeg 8.1.2 ABI, decoded sequential lossless FFV1 frames with padded source row stride (`linesize[0]=64` for a 4-pixel-wide BGR0 frame), preserved the authored logical raster through explicit RGBA conversion, reported timestamps consistent with the container time base, cleaned up native contexts sufficiently for three subsequent opens in the same process, and returned a failure for corrupt input.

Not established: Windows/native packaging; ABI compatibility across FFmpeg patch builds; seek behavior, audio, interlacing, variable frame rate, color range/matrix behavior, other pixel formats, hardware decoding, threaded decoding, buffering/cache semantics, timing/performance on representative video, resource leak freedom under long-running playback, or parity with Kinovea's actual C++/CLI FFmpeg reader. The Gentoo FFmpeg binary is GPL-enabled and must not be treated as a deployment recommendation.
