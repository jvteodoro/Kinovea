# Portable pixel core v0.1

This is a platform-neutral memory container and deterministic test producer. It is not connected to `Kinovea.Video`, the Bitmap reader, caches, UI, FFmpeg, OpenCV or camera capture. It does not decode, render or convert pixels.

Project: `src/Kinovea.PortablePixels/Kinovea.PortablePixels.csproj` (`net10.0`).  
Test command: `dotnet run --project tests/PortablePixels.Tests/PortablePixels.Tests.csproj --configuration Release -- --measure`.

## API contract and executable invariants

| API invariant | Test(s) |
|---|---|
| Supported formats are only `Rgb24` (bytes R,G,B) and `Rgba32` (bytes R,G,B,A); other enum values are rejected. | `Rgb24 exposes red-green-blue byte order`; `dimensions, format, alpha, and stride are validated` |
| Components are 8-bit unsigned bytes. No conversion or normalization occurs. `Straight` means color components are not multiplied by alpha; `Premultiplied` means they already are. `GetRowSpan` returns exactly the pixel bytes, excluding row padding. | `Rgb24 exposes red-green-blue byte order`; `synthetic pattern and padding are deterministic`; `RGBA alpha modes are labels and bytes are preserved` |
| Width and height must be positive. `width * bytesPerPixel`, stride magnitude, row address, and row end must be representable and in bounds. | `dimensions, format, alpha, and stride are validated`; `row and address arithmetic overflow is rejected` |
| `Rgb24` requires `AlphaMode.None`; `Rgba32` requires either `Straight` or `Premultiplied`. Alpha mode labels existing bytes; it does not modify them. | `RGBA alpha modes are labels and bytes are preserved`; `dimensions, format, alpha, and stride are validated` |
| `StrideBytes` is signed distance between logical rows; its magnitude must cover `RowBytes`. `RowZeroOffset + y * StrideBytes` locates logical row y. Positive and negative strides are supported independently of “top-down” naming. | `positive stride addresses top-down logical rows`; `negative stride uses explicit row-zero offset`; `dimensions, format, alpha, and stride are validated` |
| Padding before, between, or after logical rows is permitted if every row fits in the owner memory. Padding is not part of row spans. | `synthetic pattern and padding are deterministic`; `positive stride addresses top-down logical rows`; `negative stride uses explicit row-zero offset`; `dimensions, format, alpha, and stride are validated` |
| `Create` takes ownership responsibilities for `IMemoryOwner<byte>` only after successful validation; on failure, the caller retains disposal responsibility. After success, the caller must not use, dispose or mutate the owner. The owner must provide a stable region for its lifetime. | `ownership transfers only after successful construction` |
| `Dispose` releases the owner once. `Memory` and `GetRowSpan` reject calls after disposal. A previously copied `ReadOnlyMemory<byte>` cannot be revoked and is contractually invalid after owner disposal. | `disposal releases owner once and blocks new views`; `previously copied memory view cannot be revoked` |
| The core does not copy bytes; it wraps the supplied owner. `SyntheticPixelProducer` allocates one byte array and fills it directly, with one owner object and one `PixelBuffer` wrapper per frame. | `synthetic pattern and padding are deterministic`; allocation sample and source inspection |

`ReadOnlyMemory<byte>` is a borrowed view, not a lifetime-enforcing lease. Consumers must not retain it after disposing the `PixelBuffer`. Native APIs that retain data beyond a synchronous call need their own owner lifetime or a copy at that boundary. This v0.1 does not introduce reference counting, pooling, copy-on-write, cache integration or general mutation support.

## Allocation sample

`--measure` warms up ten 640x480 RGB24 frames, then measures 100 create/fill/dispose cycles using `GC.GetAllocatedBytesForCurrentThread`. It prints pixel payload bytes, observed managed allocation bytes and overhead separately. Runtime/JIT/allocator differences make the measured number platform-specific; CI stores Linux and Windows reports as separate artifacts. The producer and `PixelBuffer.Create` contain no pixel copy by source inspection. This is a small allocation sample, not a throughput benchmark or performance guarantee.
