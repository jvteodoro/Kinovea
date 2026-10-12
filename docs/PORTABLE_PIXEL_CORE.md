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

## Ownership, concurrency, and integration limits

`PixelBuffer.Create` validates before constructing the wrapper. If validation fails, it does not dispose the supplied owner and the caller remains responsible for it. Once creation returns successfully, ownership responsibilities transfer to `PixelBuffer`; the caller must not independently access, mutate, or dispose that owner. `PixelBuffer.Dispose()` invokes the owner's `Dispose()`. Sequential repeated calls are supported. New requests through `Memory` or `GetRowSpan()` throw after disposal.

The `ReadOnlyMemory<byte>` returned by `Memory` is a borrowed view, not an immutable snapshot. Disposing `PixelBuffer` prevents new views through this object, but cannot revoke a view a caller already copied. `ReadOnlyMemory<byte>` also does not prevent mutation through another alias to the same storage. Correctness therefore depends on callers honoring owner transfer and view lifetime. Native or pooled owners can have stricter release and reuse rules than the managed-array owner exercised here; those behaviors have not been verified.

The implementation has an ordinary unsynchronized `bool disposed`. V0.1 does not support concurrent calls to `Dispose()`, or reads racing with disposal. Sequential repeated disposal is tested. Consumers must externally coordinate access and disposal so the owner remains alive for every read. In particular, asynchronous consumers must not capture a borrowed view and use it after the owner may be released. This contract makes no thread-safety or native-memory-safety claim.

### Review requirements mapped to evidence

| Claim or scenario | Evidence category | Test or source evidence | V0.1 status |
|---|---|---|---|
| Failed `Create` leaves disposal responsibility with caller. | Tested and source inspected | `ownership transfers only after successful construction` exercises a bounds-validation failure and confirms no dispose call; `Create` has no disposal on its failure paths. | Supported for the current implementation. |
| Successful `Create` transfers owner responsibility; `PixelBuffer.Dispose()` releases it once for sequential calls. | Tested and source inspected | `ownership transfers only after successful construction` observes one owner release; `disposal releases owner once and blocks new views` calls `Dispose()` twice and observes one release. | Supported sequentially; caller must stop using the transferred owner. |
| New `Memory` and row access after disposal are rejected. | Tested and source inspected | `disposal releases owner once and blocks new views`; accessors call `ThrowIfDisposed()`. | Supported for sequential access/disposal. |
| A previously obtained `ReadOnlyMemory<byte>` remains readable after disposal. | Tested and source inspected | `previously copied memory view cannot be revoked` intentionally reads the view after disposal. | View revocation is impossible here; use after disposal is prohibited by contract. |
| `ReadOnlyMemory<byte>` prevents mutation through aliases. | Source-inspected limitation | The property returns the owner's memory as a read-only view; it does not control other aliases held by callers or the owner. | Not guaranteed. |
| Concurrent `Dispose()` calls, or read racing with `Dispose()`. | Source-inspected limitation; unsupported | `disposed` is an ordinary `bool`; no synchronization is present. No concurrency test exists. | Unsupported; callers must coordinate lifetime externally. |
| Native or pooled owner lifetime, reuse, and concurrent access. | Unverified | Tests use managed array-backed owners only; there is no native/pool owner test. | Unverified; requires a separate owner and interop review before use. |

### Prerequisites for producer and consumer integration

The core remains standalone until a boundary-specific adapter has a documented owner and lifetime rule, exact layout/channel/alpha mapping tests, and tests for release on success and failure. The existing Kinovea readers, caches, and callback lifecycle are not changed by this document.

| Boundary | Required before connecting `PixelBuffer` |
|---|---|
| Legacy GDI+ reader | Start with a synchronous Windows conversion. Define whether LockBits bytes are copied into an independently owned buffer or remain borrowed only inside the lock scope. Always unlock before the call returns if no owner survives it. Test actual channel order, alpha representation, signed stride/row origin, exceptional cleanup, and disposal against the Windows characterization evidence. Do not change the legacy reader lifecycle. |
| FFmpeg decoding | Document the native frame buffer owner, pixel format, plane/line sizes, row orientation, and the exact point at which FFmpeg may reuse/release storage. Copy into a `PixelBuffer` owner before that point, or retain a separately valid native owner for the complete use interval. Test decode/release ordering and pixel layout. |
| OpenCV consumers | Specify the `Mat` channel order, stride, row origin, and whether the `Mat` wraps or copies the bytes. For a synchronous non-retaining call, keep the owner alive through the call. If a `Mat` or task retains the pointer after return, provide an independently valid owner or a separately reviewed retention mechanism; test disposal and task completion ordering. |
| Asynchronous frame caches | Do not enqueue borrowed `Memory` with an owner that can be disposed on eviction. Define who owns the buffer while queued, during processing, and on eviction/cancellation; externally synchronize lifetime or propose a separate reviewed mechanism. Test concurrent read/eviction, cancellation, clear, and shutdown before integration. Do not redesign the existing Kinovea cache in this task. |
| Camera capture callbacks | Determine whether the SDK reuses the callback buffer after callback return and document the SDK's threading rules. Either synchronously copy to a new owner during the callback or transfer a documented owner whose lifetime outlasts all consumers. Test callback return, stop, failure, and concurrent shutdown against the actual SDK/runtime. |

Any operation that retains the memory after returning must provide an independently valid owner or establish a separately reviewed retention mechanism. V0.1 itself supplies neither thread-safe lifetime coordination nor native retention semantics.

## Allocation sample

`--measure` warms up 256 640x480 RGB24 frames, then measures 100 create/fill/dispose cycles using `GC.GetAllocatedBytesForCurrentThread`. It prints pixel payload bytes, observed managed allocation bytes, and the non-payload allocation delta. That delta includes wrapper/owner objects and any other allocations on the measured thread; it is not a direct object-size calculation. Runtime/JIT/allocator differences make the measured number platform-specific; CI stores Linux and Windows reports as separate artifacts. The producer and `PixelBuffer.Create` contain no pixel copy by source inspection. This is a small allocation sample, not a throughput benchmark or performance guarantee.
