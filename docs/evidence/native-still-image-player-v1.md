# Verification: Native .NET 10 still-image player v1

Tested source SHA: `47758d1f6a18e6745efaa2e85db58121002a15b4`
Implementation commit: `aca9b64f529580f3c17e4003b9d5fdabceb1dd42`
Test commit: `845838d9e80967590336859e4f1c590ea09d610f`
GUI smoke commit: `bee3c97d7d86ceaeaf7212baeb5ad96df1219735`
Zoom-indicator fix: `47758d1f6a18e6745efaa2e85db58121002a15b4`
Base: `port/net10-linux` at `22a77fb8c0ae146dd1e14ee70bdc184374e88de5`
Host: Gentoo Linux x86_64, kernel `6.18.50-gentoo-dist-bin`
SDK/runtime: .NET SDK `10.0.111`, MSBuild `18.0.11`, .NET runtime `10.0.11`
Desktop: KWin `6.7.5`, Wayland/Xwayland, display `:1`, desktop `1920x1080`; app-window capture `1680x969`
UI toolkit: Avalonia `12.1.4`; SkiaSharp `3.119.4`; native Linux x64 assets
Hosted CI: `NOT_RUN` by task instruction. Windows execution: `NOT_RUN`.

## Architecture and ownership

`Kinovea.PortableDesktop` is a separate `net10.0` executable. `Kinovea.PortableDesktop.Presentation` composes the existing `PortableReaderShell`, calls the characterized first-frame request, and creates an Avalonia-owned `WriteableBitmap`. It does not alter reader selection or any net48 source/call sites.

The adapter handles RGB24/opaque and RGBA32 with straight or premultiplied alpha labels. It obtains logical rows with `PixelBuffer.GetRowSpan(y)`, writes each row into the Avalonia framebuffer, and releases the framebuffer lock before returning. The source `PixelBuffer` remains owned by the reader shell. The presentation bitmap has independent storage and remains usable until session close. Close clears the view's reference, closes/disposes the reader, and disposes the presentation bitmap. There is one full-frame presentation copy per successful open; no presentation copies occur during repeat/step/seek. Calls remain synchronous and externally serialized.

Avalonia's Skia backend depends on SkiaSharp `3.119.4`, while the existing PortableImageReader project pinned `4.153.1`; loading both versions in the same app failed at native library initialization. The implementation commit aligns the existing reader package references to `3.119.4` without changing decoder source. The Linux image-reader suite passed at that version. Windows behavior at the aligned version remains `NOT_RUN` and requires execution before merge. This is dependency alignment, not a decoder replacement.

## Commands and outcomes

All tests ran locally; no Actions workflow was launched.

| Command / action | Result |
|---|---|
| `dotnet build src/Kinovea.PortableDesktop/Kinovea.PortableDesktop.csproj -c Release -r linux-x64 --no-restore -m:1` | PASS, 0 warnings, 0 errors |
| `dotnet build tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj -c Release -r linux-x64 --no-restore -m:1` | PASS, 0 warnings, 0 errors |
| `DISPLAY=:1 WAYLAND_DISPLAY=wayland-0 XDG_RUNTIME_DIR=/run/user/1000 dotnet tests/Kinovea.PortableDesktop.Tests/bin/Release/net10.0/linux-x64/Kinovea.PortableDesktop.Tests.dll` | PASS, 97 assertions |
| `dotnet run --project tests/PortablePixels.Tests/PortablePixels.Tests.csproj -c Release` | PASS, 10 checks |
| `dotnet restore tests/PortableImageReader.Tests/PortableImageReader.Tests.csproj -r linux-x64 --disable-parallel && dotnet run --project tests/PortableImageReader.Tests/PortableImageReader.Tests.csproj -c Release -r linux-x64 --no-restore` | PASS, 1,322 assertions after Skia version alignment |
| `dotnet restore tests/PortableFrameGenerator.Tests/PortableFrameGenerator.Tests.csproj -r linux-x64 --disable-parallel && dotnet build tests/PortableFrameGenerator.Tests/PortableFrameGenerator.Tests.csproj -c Release -r linux-x64 --no-restore -m:1 && dotnet tests/PortableFrameGenerator.Tests/bin/Release/net10.0/linux-x64/PortableFrameGenerator.Tests.dll` | PASS, 617 assertions |
| `dotnet restore tests/PortableReaderShell.Tests/PortableReaderShell.Tests.csproj -r linux-x64 --disable-parallel && dotnet build tests/PortableReaderShell.Tests/PortableReaderShell.Tests.csproj -c Release -r linux-x64 --no-restore -m:1 && dotnet tests/PortableReaderShell.Tests/bin/Release/net10.0/linux-x64/PortableReaderShell.Tests.dll` | PASS, 65 assertions |
| `python3 tools/native-player/generate-fixtures.py /tmp/kinovea-native-player-results/native-fixtures` | PASS; deterministic 640x480 24-bit BMP generated with interleaved BGR storage |
| `python3 tools/native-player/smoke.py --assembly <app.dll> --input <fixture> --output <local-dir> --source-sha 47758d1f6a18e6745efaa2e85db58121002a15b4 --exercise-navigation-close` with active display variables | PASS; visible image, Next timestamp `0/-1 → 1/0`, Ctrl+W state `Empty` |

The desktop test suite covers RGB/RGBA framebuffer rows, alpha format labeling, independent presentation lifetime after reader close, reader-session failure paths, same-session close/reopen, wrapper/backing storage identity, timing metadata, zone boundaries, viewport fitting, aspect ratio, resize math, coordinate round trips and zoom-anchor preservation. It does not establish Windows behavior.

## Fixtures and GUI results

| Fixture | SHA-256 | Result observed by app |
|---|---|---|
| Synthetic PNG 1280x720 (shared with Wine v2) | `9ca619d8c6d186cdc83a90deb10279fe6b097c75ce87ffc0e576ed4328d1d34f` | Visible, RGB24, 25 fps, frame `0/-1`, zone `[0,249]`; presentation copy 2,764,800 bytes |
| Task-specific synthetic BMP 640x480 | `3f7f8b5907ddeef41c6478d79c8b42414f222b0233f99778337e97117670a314` | Visible, RGB24, 25 fps, frame `0/-1`; presentation copy 921,600 bytes |
| Candidate BMP 7x5 | `85e0feff83d9920703cbf8be2e6ccc76741da22b4ffe791d5d4c415724de1244` | Visible, RGB24, 25 fps; 105 presentation bytes |
| Candidate alpha PNG 13x9 | `e0759d696bfef305e0a2c1c8fc7b7673e9e46390ef15beb541679c6766041dcd` | Visible, straight RGBA32, 25 fps; 468 presentation bytes |

Local screenshots and smoke manifests remain outside Git at `/tmp/kinovea-native-player-results/`. They were captured with FFmpeg `x11grab -window_id` against the process-owned window. Final screenshot SHA-256 values:

| Screenshot | SHA-256 | Notes |
|---|---|---|
| `final-png-navigation/player-open.png` | `fb3c5bbceb0cf9109f65629f4ea415a2faa4181aceb0cf5d20614dabdb924ae5` | Initial 1280x720 PNG display, screenshot luma average 100.821 |
| `final-png-navigation/player-next.png` | `d4298f9eb5deaa311df2051cfedd48a2d774ac8c7f836cca6bfb53a639dd3f3b` | Next control; report timestamp `1`, previous `0` |
| `final-png-navigation/player-empty.png` | `58e5f075928ea132c19554debbc079f30716bd9c3b1705f4bc6bc3c2f3fba9f7` | Ctrl+W cleared current reader; window remains open |
| `final-bmp-640/player-open.png` | `93cfb8b3312b04ff7f50e260fcf2c7c5d8724e611ad2956054a827661ce000f8` | Task-specific 640x480 synthetic BMP |
| `final-candidate-bmp-7x5/player-open.png` | `6e5f91a65530f8023cb07e2cbe3d268257ac445ab8ec43dd3577b5c43b4bbe13` | Shared candidate BMP fixture |
| `final-candidate-png-13x9/player-open.png` | `0b1702981a566630874a16164c5ab533d453d7874132033e4e9c75b0f9aed41b` | Shared candidate straight-alpha PNG fixture |

The 1280x720 PNG fixture hash is also present in `docs/evidence/wine-still-image-gui-v2.json`; Wine v2 reports visible 1280x720 at 25.00 fps. The candidate BMP/PNG hashes are also present in the Windows candidate and Wine v2 evidence; both report the same displayed dimensions and 25 fps. These are fixture-scoped metadata matches (`CROSS_ENVIRONMENT_MATCH`) only. Native screenshot appearance is `WINE_OBSERVED`/locally observed, but no normalized screenshot pixel comparison, alpha comparison, numerical decoded pixel comparison, or source/binary equivalence was performed. The Windows golden candidate remains unapproved.

## Measurements

The measurement mode performs 7 opens per fixture (2 warmups, 5 reported samples). It includes reader open/decode and Avalonia presentation copy. It does not include process startup or the native memory allocated internally by Skia/Avalonia.

| Fixture | Median open+decode+present | Median managed allocations | Presentation copy |
|---|---:|---:|---:|
| PNG 1280x720 | 19.690 ms | 2,781,056 bytes | 1 copy / 2,764,800 bytes |
| BMP 640x480 | 8.682 ms | 927,328 bytes | 1 copy / 921,600 bytes |

These are descriptive measurements from one Gentoo host, not budgets. File loading is synchronous on the UI thread; loading latency under slower machines remains unknown.

## Coverage status and limitations

| Property | Status | Evidence |
|---|---|---|
| Linux app build | `PASS` | .NET 10 build on Gentoo, 0 warnings/errors |
| Visible Linux PNG/BMP viewport | `PASS` | Process-owned real GUI captures, fixture hashes and visible-frame reports |
| Initial timestamp and still-image metadata | `PASS` | Actual app smoke report: `0/-1`, 25 fps, zone `[0,249]` |
| GUI Next and current-reader close | `PASS` | Actual GUI keyboard action smoke and state report |
| RGB/RGBA framebuffer rows, alpha label, separate presentation ownership | `PASS` | 97 presentation/session assertions |
| Fit/aspect/coordinate/zoom-anchor math | `PASS` | `ViewportTransform` tests; visual screenshot manually reviewed |
| File picker opens selected file | `NOT_ESTABLISHED` | Avalonia storage-provider action implemented; route was not successfully automated in this desktop session |
| Zoom/pan UI input and actual resize | `NOT_RUN` | Transform math only; no gesture/resize interaction |
| Same-process GUI close then reopen | `NOT_ESTABLISHED` | Reader session close/reopen is tested; UI smoke closes but does not re-open through picker |
| Windows build, runtime, rendering | `NOT_RUN` | No local Windows host and hosted CI intentionally not run |
| Actual net48 reader parity, pixels, alpha, EXIF, timestamp identity | `NOT_ESTABLISHED` | Wine metadata matches are limited; no Windows runtime comparison in this task |
| Full Kinovea UI or production playback integration | `NOT_ESTABLISHED` | New executable remains disconnected from legacy registration, caches, analysis and export |

The PR #11 Wine executable is Kinovea `2025.2.0.0`; the checkout reports `2026.1.0.0`. Exact source correspondence remains unknown. The existing Wine fixture generator's BMP writer emits channel planes rather than per-pixel BGR; this task deliberately used a separate deterministic BMP generator and did not change prior scenario files or hashes.

No actions were run. The PR HEAD uses `[skip ci]` because hosted workflows match the existing portable image-reader project path and this task explicitly requires local-only execution. Run Windows reader/presentation tests before merge.
