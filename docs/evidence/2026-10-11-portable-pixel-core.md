# Verification: portable pixel core v0.1

Final verified source SHA: `41b0ae8384f9af6117cb241ae83d4989c7c6f20d` (includes core commit `55ae91760bf3f8996dd0881852e5ce38410210f1`).
PR: [#3](https://github.com/jvteodoro/Kinovea/pull/3), base `port/net10-linux`.  
Legacy oracle: not applicable; this suite does not execute the Bitmap reader.

## Commands

Local Gentoo Linux, x86_64, SDK `10.0.111`, MSBuild `18.0.11`, runtime `10.0.11`:

- `dotnet build src/Kinovea.PortablePixels/Kinovea.PortablePixels.csproj --configuration Release` => exit 0, 0 warnings, 0 errors.
- `dotnet run --project tests/PortablePixels.Tests/PortablePixels.Tests.csproj --configuration Release -- --measure` => exit 0, 10 passed, 0 failed.
- `git diff --check` => exit 0.
- The local verification above was repeated at final source SHA `41b0ae8384f9af6117cb241ae83d4989c7c6f20d` on Gentoo Linux x86_64, SDK 10.0.111, MSBuild 18.0.11, runtime 10.0.11. The allocation sample reported 92,170,400 managed bytes for 92,160,000 bytes of pixel payload (10,400 bytes delta).

GitHub Actions run [38181952234](https://github.com/jvteodoro/Kinovea/actions/runs/38181952234) on final source SHA `41b0ae8384f9af6117cb241ae83d4989c7c6f20d` completed successfully on both OS jobs. Each CI job ran:

```sh
dotnet run --project tests/PortablePixels.Tests/PortablePixels.Tests.csproj --configuration Release -- --measure
```

| Platform | Runner/runtime | Result | 100-frame allocation sample |
|---|---|---|---|
| Linux | `ubuntu-latest`, Ubuntu 24.04 x64; SDK 10.0.401; MSBuild 18.9.11; runtime 10.0.12 | 10 passed, 0 failed | Payload 92,160,000 bytes; allocated 92,210,360 bytes; measured overhead 50,360 bytes |
| Windows | `windows-latest`, Windows 10.0.26100 x64; SDK 10.0.401; MSBuild 18.9.11; runtime 10.0.12 | 10 passed, 0 failed | Payload 92,160,000 bytes; allocated 92,170,400 bytes; measured overhead 10,400 bytes |

Both runners used the same test source and fixture formula. The per-platform full reports are uploaded as artifacts named `portable-pixel-core-Linux` and `portable-pixel-core-Windows` on run 38181952234. SHA-256 of the downloaded report files:

- Linux: `adfbfa3733d8fa292eeff2673f9b5bf6d75e5a0bc45b5f87420472a8a706b7da`
- Windows: `9f5515084cb3b9ae8bd42ad2d46d64c5fc5610c1f3e6b553b9b51992a2e30fc2`

## Test coverage

The package-free test executable contains 10 named tests covering RGB channel byte order, deterministic pixels, leading/inter-row/trailing padding, positive and negative stride, nonzero row-zero offset, RGBA alpha labels without byte conversion, dimension/format/alpha/stride validation, buffer bounds, integer overflow, ownership transfer, idempotent disposal, post-disposal access, and the `ReadOnlyMemory<byte>` non-revocation caveat. The mapping from API invariants to tests is documented in `docs/PORTABLE_PIXEL_CORE.md`.

## Allocation and copy observations

The sample warms up 256 frames, then measures 100 create/fill/dispose operations of 640x480 RGB24 with `GC.GetAllocatedBytesForCurrentThread`. On final run 38181952234, the measured delta above the 92,160,000-byte pixel payload was 50,360 bytes on Linux and 10,400 bytes on Windows. Earlier runs also varied (including a 40,672-byte Windows delta after the same warm-up); the cause is unresolved. Treat allocation values as descriptive samples, not a stable performance guarantee or threshold. The producer allocates one backing byte array and fills it directly; `PixelBuffer.Create` stores the supplied owner and does not copy pixel bytes, based on source inspection. The sample is not a throughput benchmark and does not justify pooling or other optimization.


## Code review findings

- Construction computes row bytes with checked `int` multiplication and row-address bounds with checked `long` arithmetic before slicing; signed stride magnitude is widened before `Math.Abs`, including `int.MinValue`.
- Failed validation returns no `PixelBuffer` and does not call `Dispose` on the supplied owner. The executable ownership test confirms a failed construction leaves disposal count at zero and that the caller can then dispose it.
- Successful construction stores the owner; sequential repeated `PixelBuffer.Dispose` calls dispose that owner once. The executable tests cover transfer and idempotent disposal separately.
- Newly requested `Memory`/row views throw after disposal. Previously copied `ReadOnlyMemory<byte>` remains accessible by .NET design; use after disposal is prohibited by contract. This is a borrowed-memory limitation, not enforced revocation.
- Disposal is idempotent for sequential calls; thread-safe concurrent disposal/access is not promised or tested.
- The PR diff against `port/net10-linux` has no legacy production changes. It adds an isolated net10 library and test harness only; no Bitmap reader parity is asserted.

## Limitations

This verifies only the standalone managed pixel-buffer contract and synthetic producer on the recorded SDK/runtime versions. It does not validate System.Drawing channel semantics, alpha-premultiplication compatibility, EXIF behavior, any real decoder, legacy timestamps, `VideoFrame`, caches, OpenCV, FFmpeg, capture, rendering, thumbnails or exports. Windows Bitmap characterization remains separate and candidate-only; no legacy regression or reader-port claim is made.
