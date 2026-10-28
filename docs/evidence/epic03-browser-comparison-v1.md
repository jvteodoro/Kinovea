# Epic 03 browser and dual-player evidence v1

## Scope and baseline

Implements Beads issues `Kinovea-gh36` and `Kinovea-gh37` in an isolated Avalonia desktop prototype. The baseline is merged `port/net10-linux` SHA `5d0f59f9b1b6f8297af77739d96b2cac3f9211b0`, which includes Epic 02's portable desktop and video player (PR #72). The implementation commits are `c97f9ce5` and `3d84f0dd`. Existing WinForms/browser/preferences/capture code was not modified. Epic 02's FFmpeg runtime remains host-installed and platform dependent.

## Implementation

The browser adds lazy folder navigation, supported media/image/video filters, name/date/size ordering, ascending/descending selection, BMP/PNG/JPEG thumbnails, file details, open/double-click behavior, file drag source and drop support in the portable player/comparison panes. The comparison window owns two independent portable still/video sessions and adds shared absolute-time seek, linked stepping, play/pause, swap and side-by-side/stacked layouts. `DualPlayerTimeline` maps each frame timestamp through its rational timebase to shared seconds and clamps per-source seeks.

The browser window-open path initially left the file grid empty because it tested `IsLoaded` inside `Opened`; the condition was removed in `3d84f0dd`. The screenshot below is from the fixed source.

## Verification

Environment: Gentoo Linux x86_64, kernel `6.18.50-gentoo-dist-bin`, .NET SDK `10.0.111`, runtime `10.0.11`, Avalonia `12.1.4`, local KWin/Xwayland display. Commands run at source SHA `3d84f0dd`:

```sh
dotnet build src/Kinovea.PortableDesktop/Kinovea.PortableDesktop.csproj -c Release -r linux-x64 -m:1 --no-restore -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false
dotnet build tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj -c Release -r linux-x64 -m:1 --no-restore -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false
dotnet tests/Kinovea.PortableDesktop.Tests/bin/Release/net10.0/linux-x64/Kinovea.PortableDesktop.Tests.dll --timeline-only
DISPLAY=:1 WAYLAND_DISPLAY=wayland-0 XDG_RUNTIME_DIR=/run/user/1000 dotnet tests/Kinovea.PortableDesktop.Tests/bin/Release/net10.0/linux-x64/Kinovea.PortableDesktop.Tests.dll
python3 tools/native-player/smoke.py --assembly src/Kinovea.PortableDesktop/bin/Release/net10.0/linux-x64/Kinovea.PortableDesktop.dll --input tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f/fixtures/bmp-rgb-7x5.bmp --output /tmp/kinovea-epic03-final-smoke --source-sha 3d84f0dd --exercise-navigation-close
```

Both builds passed with zero warnings/errors. Timeline-only passed 12 assertions; complete desktop presentation/player suite passed 127 assertions. Full suite and GUI command require access to the active Xwayland desktop; running the full suite without `DISPLAY` failed with `XOpenDisplay failed`. The local player GUI smoke opened the 7x5 BMP, advanced Next from timestamp 0 to 1, and closed the current reader with Ctrl+W. Fixture SHA-256: `85e0feff83d9920703cbf8be2e6ccc76741da22b4ffe791d5d4c415724de1244`. Screenshot SHA-256 for the opened player: `7adb8e7bc293253774fd4e4b763e45c77c3feec843c55bfd22db8d76ebf623b2`.

A separate local Avalonia interaction opened the browser from the player; its captured window shows the fixture directory, supported-media filter, BMP/JPEG thumbnails, navigation tree and preview region. Screenshot: [epic03-browser-populated.png](epic03-browser-populated.png), SHA-256 `93522ada1de547acb810894007bd258ff424eb1d5d04921083a951a344fa3c8f`.

## Status and limitations

Status: `BUILD_ONLY` for the Epic 03 UI slice, with portable timeline tests and a browser window screenshot. The dual comparison window interaction was not confirmed: after dismissing the modal browser, the local KWin/Xwayland test helper refused to reactivate the main window, so no comparison-window screenshot or two-pane media exercise is claimed. Tile selection/open, drag gestures, both-pane loading, video sync/playback, Windows and hosted Actions are `NOT_RUN`. Linked playback updates two independent decoders on a shared timer; it does not perform frame-lock correction or reproduce the legacy CommonTimeline behavior. No legacy equivalence was tested. No hosted Actions were triggered.
