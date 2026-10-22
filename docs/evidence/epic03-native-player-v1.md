# Epic 03 native player evidence v1

## Scope and baseline

This slice adds transport controls to the isolated Avalonia `net10.0` player that depends on the portable video path from Epic 02. The branch is stacked on `origin/port/net10-linux` at `5d0f59f9b1b6f8297af77739d96b2cac3f9211b0`, which includes `port/epic02-video-playback` commit `5b15026d`. Legacy defaults and shortcuts were source-inspected in `HotkeySettingsManager`, `PlayerScreenUserInterface2`, and `PlayerPreferences`; the Windows UI, reader, cache, and production registration were not changed.

## Automated verification

Host: Gentoo Linux x86_64, .NET SDK 10.0.111/runtime 10.0.11, FFmpeg 8.1.2 native ABI 62/62/60/9.

- `dotnet build src/Kinovea.PortableDesktop/Kinovea.PortableDesktop.csproj -c Release --no-restore -m:1 -nodeReuse:false -p:UseSharedCompilation=false -v:minimal` — PASS, 0 warnings and 0 errors. The app source at final HEAD `312e0aaf9539087fd30c7696fcd7741483ad475e` is identical to the app source built at `346f719f`.
- `dotnet build tests/PortableVideo.Tests/PortableVideo.Tests.csproj -c Release -m:1 -nodeReuse:false -p:UseSharedCompilation=false -v:minimal` — PASS, 0 warnings and 0 errors.
- `dotnet tests/PortableVideo.Tests/bin/Release/net10.0/PortableVideo.Tests.dll` — PASS, 149 assertions, including rational playback-rate changes while playing and paused.
- `dotnet build tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj -c Release -r linux-x64 -m:1 -nodeReuse:false -p:UseSharedCompilation=false -v:minimal` — PASS, 0 warnings and 0 errors.
- `DISPLAY=:1 WAYLAND_DISPLAY=wayland-0 XDG_RUNTIME_DIR=/run/user/1000 dotnet tests/Kinovea.PortableDesktop.Tests/bin/Release/net10.0/linux-x64/Kinovea.PortableDesktop.Tests.dll` — PASS, 115 assertions. The test executable needs the live X11 session; it failed to open X from the default sandbox and passed with approved display access.
- Python syntax compilation for the native-player scripts and `git diff --check` — PASS.

## Real GUI observations

The process-owned Xwayland screenshots and reports are under [`epic03-native-player-v1`](epic03-native-player-v1/).

- Lifecycle scenario `gui-interactions.py` — PASS at source SHA `b3e3fdf3b03cd01ff19859565b0c28e3df97553b`. Real storage picker selection, PNG/BMP metadata, cancel, corrupt replacement retention, Next/Previous, close, repeated in-process reopen, and clean exit passed. The picker surface itself is a Wayland portal and was `NOT_CAPTURED`; selected hashes and visible app state were checked.
- Viewport scenario `viewport-gestures.py` — PASS at source SHA `b3e3fdf3b03cd01ff19859565b0c28e3df97553b`. Real wheel zoom 1.0→1.2 with observed-pointer anchor preservation, drag pan, Fit, RGB/orientation samples, window resize, alpha compositing, and open-after-gestures reset passed.
- Video control scenario `video-smoke.py` reached source SHA `3aef5b2604477adf351ec9cbd72cdd0364dc5345` and passed visible H.264 open, Up/Down and Shift+Up/Down speed steps, Loop off/on through Tab/Return, play, pause, resume, frame step, and loop wrap. It then timed out while trying to reach Loop again for the non-loop EOF check; the control focus was still on another toolbar item. That non-loop EOF keyboard traversal is `NOT_ESTABLISHED`. The scenario's final tab-count correction is commit `312e0aaf`; this final runner state was not rerun after the correction. No complete video scenario manifest was emitted.

Loop is process-local and defaults on. Space, Up/Down, Shift+Up/Down, Page Up/Down, and Shift+Page Up/Down are source-informed defaults. The window listens on Avalonia's tunnel route so shortcuts work while a child control has focus. Nominal playback rates do not port the legacy capture-rate-aware `TimeMapper` or persisted preferences.

## Limits

Linux builds and local GUI observations are prototype evidence, not legacy equivalence. Windows GUI/runtime, Wine comparison, hosted Actions, non-loop EOF behavior at the final source SHA, exact playback pacing, capture-rate speed mapping, persisted preferences, and decoder pixel parity are `NOT_RUN` or `NOT_ESTABLISHED`. No hosted workflow was triggered. The FFmpeg install is host-provided and is not a redistribution package.
