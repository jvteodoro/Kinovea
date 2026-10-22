# Verification: Epic 03 native state and capture boundary

Implementation commit: `bb997dc03a7bb8fa32eb264fc7b6135097a72794`
Base: `origin/port/epic02-video-playback` (`5b15026dc4bc9454ffabae89ecaa99a5dadf9648`), fast-forwarded to `origin/port/net10-linux` (`5d0f59f9b1b6f8297af77739d96b2cac3f9211b0`) before implementation. This preserves the stacked Epic 02 playback work and current issue #72 base.
Host: Gentoo Linux x86_64, kernel `6.18.50-gentoo-dist-bin`, Intel Core i7-8650U
SDK/runtime: .NET SDK `10.0.111`, `Microsoft.NETCore.App 10.0.11`
Native libraries: Avalonia `12.1.4`; no camera backend or FFmpeg operation used by the state/capture contract run.
Fixture: no external fixture; synthetic RGB24 frames are generated in memory by `SyntheticCaptureSourceProvider`.
Legacy oracle: unavailable. Source inspection only; parity remains `UNVERIFIED`.

## Source hashes

```text
5227b98d23b63afe31ca44e4eb274885d9a9319c7f5dc0f4d3aeea5b92cf8bb6  src/Kinovea.PortableDesktop.Presentation/PortableApplicationState.cs
b19563d3004c223b1680f7756d0b44a0af401345ab259c63b0ed0d385be30876  src/Kinovea.PortableDesktop.Presentation/PortableCapture.cs
d52a111ecbd67f29473d559497e573679a33a4e79d31db6b248cb1e643cee904  tests/Kinovea.PortableDesktop.Tests/Program.cs
```

## Commands and outcomes

- `dotnet build src/Kinovea.PortableDesktop/Kinovea.PortableDesktop.csproj -c Release --no-restore -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -m:1` => exit `0`, 0 warnings, 0 errors, 24.64 seconds.
- `dotnet build tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj -c Release --no-restore -p:NuGetAudit=false -p:MSBuildEnableWorkloadResolver=false -m:1` => exit `0`, 0 warnings, 0 errors, 5.88 seconds.
- `dotnet tests/Kinovea.PortableDesktop.Tests/bin/Release/net10.0/Kinovea.PortableDesktop.Tests.dll --capture-contract-only` => exit `0`, 19 assertions passed. This covers state save/reload through a new store instance, settings/layout validation, both independent synthetic sources, frame distinction, start/stop, and invalid source handling.
- `DISPLAY=:1 WAYLAND_DISPLAY=wayland-0 XDG_RUNTIME_DIR=/run/user/1000 dotnet tests/Kinovea.PortableDesktop.Tests/bin/Release/net10.0/Kinovea.PortableDesktop.Tests.dll` => exit `1` at Avalonia initialization: `XOpenDisplay failed`. The full presentation suite did not run.
- `git diff --check` => exit `0` before implementation commit.

## Platform and behavior status

- Linux state persistence and synthetic source contract: `TESTED` (19 assertions).
- Linux native desktop build: `BUILD_ONLY` for UI surfaces. The host could not initialize Avalonia's X11 display, so menu interaction, preference dialog, capture view rendering, and single/dual view teardown are `NOT_RUN`.
- Windows build/UI: `NOT_RUN`.
- Physical camera discovery, controls, recording, and hardware teardown: `NOT_RUN`.
- Epic 06 acquisition pipeline integration: not in this slice; no real provider is registered.
- Legacy preference/workspace/capture comparison: `UNVERIFIED`; source inspected at `Kinovea.Services/Preferences/PreferencesManager.cs`, `Kinovea.ScreenManager/CaptureScreen/CaptureScreen.cs`, `Kinovea.ScreenManager/DualCapture/DualCaptureController.cs`, and `Kinovea.Camera/Interfaces/ICaptureSource.cs`.
- Hosted GitHub Actions: not triggered.

The JSON store uses the platform's `LocalApplicationData` path and atomically replaces its state file. State is limited to the opt-in Avalonia shell and a single active workspace record; this does not convert legacy workspace files or all legacy preference categories. English and `pt-BR` are implemented for the new menus, preferences, and capture windows; the legacy language resources and all existing player labels are not migrated. The synthetic provider proves the source contract only, not physical acquisition or recording parity. No regression-equivalence claim is made.
