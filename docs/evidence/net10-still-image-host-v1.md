# .NET 10 still-image host v1

Status: **Architecture proposal and source audit in progress; no host execution yet.**

## Base and provenance

- Repository: `jvteodoro/Kinovea`.
- PR #9 merged to `port/net10-linux` at `8b73d5440908d5df954fda0e255fa3d79ed5694b` (merge parent includes audit commit `28ac15a32306637610aa899a725e89657731fd86`).
- Proposed task branch: `design/net10-still-image-host`.
- Existing Windows Bitmap outputs remain candidate evidence, not reviewer-approved golden data.
- This initial record precedes the host implementation. Runtime results and artifact hashes will be appended after the selected slice is executed.

## Production-equivalent source path

```text
RootKernel
  -> VideoTypeManager.LoadVideoReaders(registered VideoReader types)
  -> FrameServerPlayer.Load(path)
  -> VideoTypeManager.GetVideoReader(Path.GetExtension(path))
  -> VideoReaderBitmap.Open(path)
  -> FrameGeneratorImageFile.Open / System.Drawing.Bitmap
  -> VideoReaderBitmap.MoveRequest(first=true) / Current VideoFrame
  -> FrameServerPlayer.CurrentImage (filter current image, else reader.Current.Image)
  -> PlayerScreenUserInterface2 surface paint
```

`VideoReaderBitmap.Open` initializes metadata and `[0,249]` working zone but leaves `Current` empty until navigation. First sequential request selects timestamp 0; with one timestamp per frame, `PreviousTimestamp` is -1. `FrameGeneratorImageFile` owns and reuses its GDI+ image; close disposes it. `FrameServerPlayer.CurrentImage` returns the current reader Bitmap unless a filter supplies its own image. These are source findings, with navigation and object lifecycle separately covered by Windows candidate characterization. No claim is made that the candidate fixture set is approved.

## Dependency matrix

| Area | Current target and dependencies | Host consequence |
|---|---|---|
| `RootKernel` / reader registration | Legacy Windows WinForms executable, net48; explicitly registers Bitmap, FFmpeg, GIF and SVG reader classes (`Kinovea/Kernel.cs:136-145`). | Cannot be the net10 host entry point without moving its app dependency graph. |
| `VideoTypeManager` | net48 `Kinovea.Video`; extension registry constructs legacy `VideoReader` subtypes; default discovery uses `System.Windows.Forms.Application.ExecutablePath` (`VideoTypeManager.cs:34-131`). | The extension map algorithm is simple, but its type and discovery boundary are not a portable contract. Reusing it directly would require moving/retargeting legacy reader APIs. |
| `VideoReader` / `VideoFrame` / `IFrameGenerator` | net48; `VideoFrame.Image` and generator output are `System.Drawing.Bitmap`; `Kinovea.Video` references Services, Drawing and Windows.Forms. | Cannot be referenced from `net10.0` as-is. Preserve and test the old route separately. |
| `VideoReaderBitmap` / `FrameGeneratorImageFile` | net48 with GDI+ decode, EXIF/rotation and `Graphics` use. | Windows oracle only; do not alter or multi-target in this slice. |
| `FrameServerPlayer` and surface paint | ScreenManager net48, WinForms, GDI+, metadata/filters/export, and C++/CLI PlayerServer reference. `CurrentImage` exposes Bitmap. | Full controller and UI are not a minimal host dependency. Do not describe the CLI host as actual FrameServerPlayer integration. |
| Portable image path | .NET 10 `PortableImageReader` -> `PortableImageFrameGenerator` -> `PortableReaderShell` -> `PixelBuffer`. | Reuse unchanged. Shell provides already-characterized navigation and owns decoder/frame lifetime. |

Exact references and alternatives are formalized in [ADR 0005](../adr/0005-net10-still-image-host.md).

## Architecture selection

Proposed: an isolated in-process net10 CLI host over `PortableReaderShell`, plus a portable test suite. No IPC is needed to exercise the portable workflow; the existing net48 app remains unchanged. A separate Windows net48 harness will test actual `VideoTypeManager` dispatch and `VideoReaderBitmap` open/current behavior. The actual `FrameServerPlayer` and WinForms consumer remain outside this test because referencing ScreenManager would pull in the wider legacy UI and C++/CLI graph.

## Test/evidence status

| Property | Status before implementation | Evidence required |
|---|---|---|
| Existing reader/shell builds and behaviors | Previously `PORTABLE_TESTED` on Linux/Windows in PR #5–#7 workflows | Re-run the host integration workflow on both OSes. |
| New .NET 10 host | `NOT_RUN` | Same command and fixtures on Linux and Windows; capture source SHA, SDK/runtime, test totals and logs. |
| Actual Windows reader selection/open/current | `NOT_RUN` for `VideoTypeManager` selection; actual reader has separate prior characterization | Windows net48 harness invoking real manager and reader; report type, result, timestamp, dimensions, pixels and close. |
| FrameServerPlayer `CurrentImage` / WinForms drawing | `NOT_RUN` | Explicitly out of scope; do not infer from direct VideoReader tests. |
| Cross-platform production application integration | `NOT_ESTABLISHED` | Requires application host/runtime/UI strategy and a later real-consumer integration stage. |
