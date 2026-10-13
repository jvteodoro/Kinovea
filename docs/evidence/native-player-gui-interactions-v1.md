# Native Player GUI Interaction and Lifecycle Verification v1

## Scope and result

The native Avalonia still-image app was exercised through actual GUI input on Gentoo Linux. The runs covered the real Avalonia file picker, visible presentation, reader navigation, invalid replacement and cancellation, repeated sessions in one process, viewport gestures, resize, and rendered fixture landmarks. This remains an isolated still-image prototype; it does not establish video playback or legacy application parity.

Overall local Linux GUI result: `PASS` for the listed scenarios and fixtures. Windows GUI and Windows native-library resolution: `NOT_RUN`. GitHub Actions: `NOT_RUN` by task instruction. Candidate Windows Bitmap golden data remains `CANDIDATE_NOT_APPROVED`.

The app/presentation change is in commits `b3d43737` (failed replacement retains current session) and `210e25b1` (GUI scenarios and diagnostics). PR #12 base was `61850e7931456d57bba09c69a15d746b2773bf39`.

## Provenance note

The successful lifecycle and viewport interactions were performed on the task worktree before those changes were committed. The scenario process was given the base SHA in `KINOVEA_SOURCE_SHA`, so its raw local JSON recorded `61850e7931456d57bba09c69a15d746b2773bf39` even though the active worktree contained the pending application and scenario changes. Those files were subsequently committed unchanged as `210e25b1b8229fedf34340feee5577fa1d577d42`; the machine manifest names that committed source tree and retains the discrepancy explicitly. A later exact-SHA lifecycle attempt failed at the first Xwayland window capture with `BadDrawable` / `Permission denied`, before the picker or application interactions ran. The earlier full lifecycle run and viewport run passed. This provenance qualification should remain visible when reusing the screenshots.

## Environment and dependency review

- Gentoo Linux 2.18, kernel `6.18.50-gentoo-dist-bin`, x86_64.
- .NET SDK `10.0.111`, runtime `10.0.11`, MSBuild `18.0.11+e2f47b011`.
- KWin `6.7.5`, Wayland `wayland-0`, Xwayland `:1`, desktop resolution `1920×1080`, Avalonia render scaling `1.5`.
- Avalonia `12.1.4`; SkiaSharp and native asset packages `3.119.4`.
- The actual app process mapped `libSkiaSharp.so` and `libHarfBuzzSharp.so` from its `linux-x64` runtime assets. `ldd` on Skia observed glibc plus `fontconfig`, FreeType, libpng, zlib, bzip2 and expat dependencies. This verifies resolution on this host only; other Linux distributions/architectures and Windows are not verified.
- PR #12's version alignment was inspected and not changed. No library or version change was made in this task.

## Fixture provenance

| Fixture | SHA-256 | Expected/observed metadata |
|---|---|---|
| `synthetic-rgb-1280x720.png` | `9ca619d8c6d186cdc83a90deb10279fe6b097c75ce87ffc0e576ed4328d1d34f` | 1280×720, 25 fps, RGB24, timestamp pair 0/-1 |
| `native-player-rgb-640x480.bmp` | `3f7f8b5907ddeef41c6478d79c8b42414f222b0233f99778337e97117670a314` | 640×480, 25 fps, RGB24, timestamp pair 0/-1 |
| `corrupt-image.png` | `9f2b222cc66ffcc47742bb2f7dabd3f5f8be0ded008fb37297cc6b8f2ae4b36a` | Rejected while preserving active PNG session |
| `png-alpha-13x9.png` | `e0759d696bfef305e0a2c1c8fc7b7673e9e46390ef15beb541679c6766041dcd` | 13×9, straight alpha; candidate characterization fixture |

The candidate alpha and corrupt fixtures were consumed from the existing characterization candidate directory and were not rewritten. The Windows Bitmap candidate remains unapproved.

## File picker and same-process lifecycle

The `gui-interactions-v1` scenario used the actual Avalonia `IStorageProvider.OpenFilePickerAsync` and selected paths through the active KDE portal. On the tested desktop, Ctrl+O opened the portal; keyboard input selected the file. The scenario verified fixture hashes and waited for `VISIBLE_FRAME_RENDERED`, not merely a successful process exit.

- Startup: actual app window showed the Empty state.
- PNG: selected the 1280×720 fixture; the app displayed its filename, 25 fps, timestamp 0, previous timestamp -1, and reported one presentation copy.
- Navigation: Next produced 1/0; Previous returned 0/-1. Presentation-copy count did not change.
- Picker cancellation: Escape returned to the active PNG; hash, reader state, timestamp and copy count were unchanged.
- Corrupt replacement: reader rejected the candidate, retained the prior PNG's hash/path/frame/presentation, retained Open state, and reported an error. The existing visible image remained captured.
- Close/reopen: Ctrl+W cleared the session. BMP opened in the same process with 640×480 and 25 fps; Next/Previous produced the expected 1/0 and 0/-1; close cleared it. PNG then reopened with fresh 0/-1 timestamps. Successful opens incremented presentation copies once each (1, 2, 3); frame navigation added no copy.
- Exit: harness-owned app process returned exit code 0.

The actual portal surface belongs to Wayland and is not an X11 client. Capturing it with the desktop screenshot utility changed activation, so its UI screenshot is `NOT_CAPTURED`; window-scoped captures and independent selected-fixture hashes verify the resulting player state.

## Viewport and rendering interactions

The separate viewport scenario used a real process-owned Avalonia window and actual XTest pointer events:

- Wheel event changed zoom from `1.0` to `1.2`. Xwayland requested point and Avalonia-reported pointer point differed in one run; anchor preservation was checked using the actual pointer point delivered to Avalonia.
- Dragging 54×33 Xwayland pixels at 1.5 render scale produced exactly 36×22 logical pan units.
- Fit reset zoom to 1 and pan to 0.
- Resizing the window from logical 1120×646 to 960×646 changed the viewport and retained the BMP's 4:3 image aspect ratio.
- After zoom, pan, Fit and resize, the real file picker opened the 1280×720 PNG. It had timestamp pair 0/-1, zoom 1, and pan 0/0.
- Screenshot samples at authored BMP landmarks matched expected red `(235,40,35)`, diagonal `(250,250,250)`, and blue `(30,55,220)` exactly. Straight-alpha presentation rendered a transparent pixel as viewport background `(17,21,27)` and the partial-alpha sample `(27,28,55)` against expected `(26,27,54)` within tolerance 30.

These are rendered screenshot samples. They do not replace exact decoder-output tests and are not pixel parity with Wine, GDI+, or another rendering backend.

## Ownership, resource and test findings

The desktop session now stages a reader and presentation bitmap before replacing the current session. A failed decode/presentation keeps the current frame wrapper and Avalonia bitmap alive. On successful replacement, the new session is published before the prior reader and bitmap are disposed. Close clears the viewport's bitmap reference before closing the reader and disposing the presentation bitmap.

The desktop suite passed 115 assertions, including object identity and disposed-access checks for failed replacement, successful replacement, and close. All calls remain synchronous and externally serialized; there is no async decoder, cache, reference-counted lease, or native pointer retention.

Three process samples reported 147 open file descriptors and RSS values 182,944, 188,300, and 198,136 KiB across PNG/BMP/reopened-PNG sessions. The RSS increase is descriptive and neither proves a leak nor proves leak freedom. Presentation copy count was one per successful open and unchanged by navigation.

## Commands and reports

The reusable commands and host prerequisites are in [`tools/native-player/README.md`](../../tools/native-player/README.md). Machine-readable scenario definitions are [`gui-interactions-v1.json`](../../tools/native-player/scenarios/gui-interactions-v1.json) and [`viewport-gestures-v1.json`](../../tools/native-player/scenarios/viewport-gestures-v1.json). The full source, fixture, test, resource and screenshot-hash manifest is [`native-player-gui-interactions-v1.json`](native-player-gui-interactions-v1.json).

Six portable suites passed locally:

| Suite | Result |
|---|---:|
| Portable Pixel Core | 10 checks |
| Portable Image Reader | 1,322 assertions |
| Portable Frame Generator | 617 assertions (`legacy comparison NOT_RUN`) |
| Portable Reader Shell | 65 assertions (`legacy trace comparison NOT_RUN`) |
| Portable Still Image Host | 43 assertions |
| Desktop Presentation/Player | 115 assertions |

All six test project builds and the desktop app build passed with zero warnings and zero errors. Temporary paths were set to a writable task-local `.local-build/tmp` because this host's `/tmp` user quota caused a test fixture write failure on the first attempt; rerunning with the task-local temp path passed.

Seventeen app-window screenshots and their hashes are committed under [`native-player-gui-interactions-v1/`](native-player-gui-interactions-v1/). They contain synthetic fixtures and the application window only. Generated logs/reports outside Git remain in the task-local evidence directory.

## Status and limits

- Linux native GUI interaction for the scenarios above: `PASS`.
- Windows app, GUI, and Windows Skia native resolution: `NOT_RUN`.
- Hosted CI: `NOT_RUN`.
- Candidate Bitmap golden approval: remains unset.
- Wine screenshot equivalence, decoder parity beyond existing reader tests, video playback, cache integration, analysis tools and production UI integration: `NOT_ESTABLISHED`.
