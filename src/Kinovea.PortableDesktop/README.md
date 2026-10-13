# Kinovea Portable Desktop

This is an opt-in .NET 10 desktop prototype. It opens BMP and PNG through the existing `PortableReaderShell`; it is separate from the .NET Framework WinForms application and is not registered in any legacy reader-selection path.

## Build and launch

```sh
dotnet restore src/Kinovea.PortableDesktop/Kinovea.PortableDesktop.csproj -r linux-x64
dotnet build src/Kinovea.PortableDesktop/Kinovea.PortableDesktop.csproj -c Release -r linux-x64 --no-restore
dotnet src/Kinovea.PortableDesktop/bin/Release/net10.0/linux-x64/Kinovea.PortableDesktop.dll
```

On Windows, use `-r win-x64` and the corresponding `bin/Release/net10.0/win-x64` output. A graphical desktop session is required.

Use **Open image…** or Ctrl+O to select a file. `--open <path>` opens a fixture at startup for repeatable GUI checks. Ctrl+W/Escape closes the current image; Alt+F4 closes the app. The native file picker is provided by Avalonia's storage provider.

## Project boundary

- `Kinovea.PortableDesktop`: small Avalonia executable and window controls.
- `Kinovea.PortableDesktop.Presentation`: synchronous frame-to-bitmap adapter, reader session and viewport coordinate transform.
- `Kinovea.PortableDesktop.Tests`: package-free console assertions plus Avalonia platform test initialization. It tests pixel rows, alpha labeling, ownership, session states and coordinate math.

The view draws an Avalonia `WriteableBitmap` that is independently owned. `PixelBufferBitmapAdapter` copies each logical PixelBuffer row once while the reader is open, then releases all borrowed views before returning. Reader close releases the decoded PixelBuffer; closing the view disposes its separate presentation bitmap. Calls are synchronous and externally serialized.

## Dependencies and limitations

Avalonia 12.1.4 is MIT licensed. Linux uses Avalonia's Skia rendering backend and published native Skia/HarfBuzz runtime assets. The combined app needs one compatible SkiaSharp managed/native version. The portable image reader project therefore targets SkiaSharp 3.119.4, the version required by Avalonia 12.1.4; its existing reader tests were rerun after this dependency alignment. No new decoder was introduced.

Only BMP and PNG are offered by the portable reader; other extensions and corrupt files show an error. The UI is not a port of Kinovea's WinForms player and does not implement drawing tools, analysis, video, GIF, export, tracking, cache integration or annotations. Screenshot checks establish visible presentation, not decoded pixel parity. Linux is locally tested; Windows execution remains `NOT_RUN` for this task.

The local window smoke helper is [tools/native-player/smoke.py](../../tools/native-player/smoke.py). It requires the existing KWin/Xwayland oracle dependencies, a running desktop session, and a built app; it activates the process-owned window, captures only that window and fails on a blank screenshot.
