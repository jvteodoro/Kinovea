# Verification: portable pixel core v0.1

Implementation commit: `029f1fa4027db0ccd05dec2fc0360bf59a8ca7ff` (includes core commit `55ae91760bf3f8996dd0881852e5ce38410210f1`).  
PR: [#3](https://github.com/jvteodoro/Kinovea/pull/3), base `port/net10-linux`.  
Legacy oracle: not applicable; this suite does not execute the Bitmap reader.

## Commands

Local Gentoo Linux, x86_64, SDK `10.0.111`, MSBuild `18.0.11`, runtime `10.0.11`:

- `dotnet build src/Kinovea.PortablePixels/Kinovea.PortablePixels.csproj --configuration Release --no-restore` => exit 0, 0 warnings, 0 errors.
- `dotnet run --project tests/PortablePixels.Tests/PortablePixels.Tests.csproj --configuration Release --no-restore -- --measure` => exit 0, 10 passed, 0 failed.
- `git diff --check` => exit 0.

GitHub Actions run [38181217214](https://github.com/jvteodoro/Kinovea/actions/runs/38181217214) on implementation SHA `029f1fa4027db0ccd05dec2fc0360bf59a8ca7ff` completed successfully on both OS jobs. Each CI job ran:

```sh
dotnet run --project tests/PortablePixels.Tests/PortablePixels.Tests.csproj --configuration Release -- --measure
```

| Platform | Runner/runtime | Result | 100-frame allocation sample |
|---|---|---|---|
| Linux | `ubuntu-latest`, Ubuntu 24.04 x64; SDK 10.0.401; MSBuild 18.9.11; runtime 10.0.12 | 10 passed, 0 failed | Payload 92,160,000 bytes; allocated 92,170,400 bytes; measured overhead 10,400 bytes |
| Windows | `windows-latest`, Windows 10.0.26100 x64; SDK 10.0.401; MSBuild 18.9.11; runtime 10.0.12 | 10 passed, 0 failed | Payload 92,160,000 bytes; allocated 92,178,520 bytes; measured overhead 18,520 bytes |

Both runners used the same test source and fixture formula. The per-platform full reports are uploaded as artifacts named `portable-pixel-core-Linux` and `portable-pixel-core-Windows` on run 38181217214. SHA-256 of the downloaded report files:

- Linux: `82bdde50427d1f25dd4649a6a46398e33f1f6aff707661eb72ed5788160a3c3b`
- Windows: `0d9e9dc1861ed1b19a6801d8873cc40e43d7c36e35e1792e478403e20efd7d46`

## Test coverage

The package-free test executable contains 10 named tests covering RGB channel byte order, deterministic pixels, leading/inter-row/trailing padding, positive and negative stride, nonzero row-zero offset, RGBA alpha labels without byte conversion, dimension/format/alpha/stride validation, buffer bounds, integer overflow, ownership transfer, idempotent disposal, post-disposal access, and the `ReadOnlyMemory<byte>` non-revocation caveat. The mapping from API invariants to tests is documented in `docs/PORTABLE_PIXEL_CORE.md`.

## Allocation and copy observations

The sample warmed up ten frames, then measured 100 create/fill/dispose operations of 640x480 RGB24 with `GC.GetAllocatedBytesForCurrentThread`. Runtime-specific overhead differs by OS as listed above. The producer allocates one backing byte array and fills it directly; `PixelBuffer.Create` stores the supplied owner and does not copy pixel bytes. The sample is not a throughput benchmark and does not justify pooling or other optimization.

## Limitations

This verifies only the standalone managed pixel-buffer contract and synthetic producer on the recorded SDK/runtime versions. It does not validate System.Drawing channel semantics, alpha-premultiplication compatibility, EXIF behavior, any real decoder, legacy timestamps, `VideoFrame`, caches, OpenCV, FFmpeg, capture, rendering, thumbnails or exports. Windows Bitmap characterization remains separate and candidate-only; no legacy regression or reader-port claim is made.
