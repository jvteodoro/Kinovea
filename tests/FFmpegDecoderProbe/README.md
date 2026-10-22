# FFmpeg decoder probe

This test-only `.NET 10` console program exercises FFmpeg.AutoGen 8.1.0 against explicitly located FFmpeg 8.1.x shared libraries. It does not connect to the Avalonia desktop or replace `VideoReaderFFMpeg`.

## Generate and verify the fixture

```sh
python3 tests/FFmpegDecoderProbe/generate_fixture.py --output-dir /tmp/kinovea-ffmpeg-fixture
dotnet restore tests/FFmpegDecoderProbe/FFmpegDecoderProbe.csproj
dotnet build tests/FFmpegDecoderProbe/FFmpegDecoderProbe.csproj -c Release -m:1
python3 tests/FFmpegDecoderProbe/run_local_checks.py \
  --output-dir /tmp/kinovea-ffmpeg-fixture --library-dir /usr/lib64
```

The generator authors 5 asymmetric 4x2 BGR0 frames, encodes them losslessly as FFV1 in NUT, and stores independent logical RGBA expected bytes and presentation times in a JSON manifest. The local check performs three complete open/decode/cleanup intervals in one process, verifies all logical output bytes and rational presentation times, then confirms deterministic rejection of a corrupt input. Generated files and reports stay outside Git. Use `--repeat-count` for an individual probe run; the report JSON uses camelCase schema version 1.

The probe converts decoded frame data to owned RGBA arrays using `libswscale`. Each output frame is written once into a new managed array; three repetitions of this five-frame fixture report 15 output buffers and 480 output pixel bytes. `ManagedAllocatedBytes` measures managed allocation on the decode caller thread, including probe/report objects, but excludes native FFmpeg allocations and allocations on internal threads. Elapsed time is summed over requested runs. Tiny synthetic-frame observations are diagnostic only, not a performance benchmark or representative latency claim.

## Runtime requirements and limitations

- `FFMPEG_LIBRARY_DIR` or `--library-dir` must point to the FFmpeg shared library directory; the default `/usr/lib64` is Gentoo-specific.
- The bindings are generated for FFmpeg 8.1.0; native ABI/version values are emitted in the report. This does not prove every patch build is compatible.
- FFmpeg.AutoGen 8.1.0's NuGet package includes an MIT license. The FFmpeg runtime has a separate license: the tested Gentoo build has `--enable-gpl` and `--enable-libx264`, so its distribution obligations differ. This probe does not select or approve a redistributable product build.
- The local test's native FFmpeg build reports GPL components. Do not redistribute it based on this test; resolve the intended FFmpeg build and legal obligations separately.
- No playback clock, seek, buffering/cache behavior, audio, hardware decoder path, UI-thread dispatch, or Windows native packaging is implemented.
- The synthetic fixture validates the decoder binding and pixel conversion only. Actual Windows Kinovea video-reader parity remains `NOT_RUN`.
