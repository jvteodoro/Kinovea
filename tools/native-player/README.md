# Native player local GUI oracle

These scripts exercise the actual `Kinovea.PortableDesktop` Avalonia process on a local Linux graphical session. They are local diagnostics; they do not use GitHub Actions or modify the legacy application.

## Requirements

- Gentoo Linux x86_64 with .NET SDK 10, KWin, Wayland plus Xwayland.
- Avalonia's desktop file-picker portal configured and reachable from the session.
- `qdbus6`, `xprop`, FFmpeg with `x11grab`, Python 3, Pillow, and the X11/XTest shared libraries.
- A build of `src/Kinovea.PortableDesktop/Kinovea.PortableDesktop.csproj`.
- The deterministic fixtures referenced in `scenarios/gui-interactions-v1.json`.

The scripts raise only the process-owned Kinovea window through KWin before sending keys or pointer input. The native file chooser is a Wayland portal surface, not an X11 client. Its controls cannot be captured with the same window-scoped Xwayland capture method without stealing activation; the scripts report that picker screenshot as `NOT_CAPTURED`. They still verify the selected fixture hash and the resulting visible player state. A failed or blank process-window capture fails the scenario.

## Build and execute

Use a temporary directory writable by the current user. This host's `/tmp` can be quota limited, so the commands below keep build temporary files under the checkout:

```sh
mkdir -p .local-build/tmp "$KINOVEA_LOCAL_EVIDENCE"
export TMPDIR="$PWD/.local-build/tmp" TMP="$PWD/.local-build/tmp" TEMP="$PWD/.local-build/tmp"
export DOTNET_CLI_USE_MSBUILD_SERVER=0 MSBUILDDISABLENODEREUSE=1

dotnet restore src/Kinovea.PortableDesktop/Kinovea.PortableDesktop.csproj
# Restore each test project before building it when using a clean NuGet cache.
dotnet restore tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj
dotnet build src/Kinovea.PortableDesktop/Kinovea.PortableDesktop.csproj \
  -c Release --no-restore -m:1 -p:UseSharedCompilation=false
dotnet build tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj \
  -c Release --no-restore -m:1 -p:UseSharedCompilation=false
```

Run the storage-picker and same-process lifecycle scenario (fixture hashes are checked by the tool and scenario definition):

```sh
python3 tools/native-player/gui-interactions.py \
  --assembly src/Kinovea.PortableDesktop/bin/Release/net10.0/Kinovea.PortableDesktop.dll \
  --png /path/to/synthetic-rgb-1280x720.png \
  --bmp /path/to/native-player-rgb-640x480.bmp \
  --corrupt tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f/fixtures/corrupt-image.png \
  --alpha tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f/fixtures/png-alpha-13x9.png \
  --output "$KINOVEA_LOCAL_EVIDENCE/lifecycle" \
  --source-sha "$(git rev-parse HEAD)"
```

Run the separate real viewport interaction scenario:

```sh
python3 tools/native-player/viewport-gestures.py \
  --assembly src/Kinovea.PortableDesktop/bin/Release/net10.0/Kinovea.PortableDesktop.dll \
  --bmp /path/to/native-player-rgb-640x480.bmp \
  --alpha tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f/fixtures/png-alpha-13x9.png \
  --png /path/to/synthetic-rgb-1280x720.png \
  --output "$KINOVEA_LOCAL_EVIDENCE/viewport" \
  --source-sha "$(git rev-parse HEAD)"
```

`gui-interactions.py` launches and cleans up only its own application process. `viewport-gestures.py` likewise owns its two test processes. Output directories contain JSON reports, per-window screenshots, and process logs; keep them outside Git if they reveal local paths or desktop state. The versioned scenario inputs live in `tools/native-player/scenarios/` and contain no generated output.

## Scenario coverage and limits

- `gui-interactions-v1.json`: empty startup; real Avalonia `IStorageProvider` selection for PNG/BMP/corrupt PNG; navigation; picker cancel; failed replacement retention; Ctrl+W close; repeated open/close in one process; clean exit.
- `viewport-gestures-v1.json`: actual wheel zoom and reported pointer anchor, pointer drag/pan, Fit, Xwayland window resize, reopen a new PNG after gestures, RGB/orientation landmark samples, and straight-alpha screenshot compositing samples.
- Application reports include timestamps, frame dimensions, fixture hash, viewport geometry, zoom/pan, presentation copy counts, and the process ID. Unit tests separately verify that failed replacement retains the same frame and presentation object and that successful close/replacement disposes prior owners.
- Wheel input crosses Xwayland/Wayland scaling. The scripts therefore compare anchor preservation using Avalonia's observed pointer position; they record the requested and observed points and do not claim requested absolute coordinates reached the same image point.
- Screenshots establish rendered presentation only. They are not decoder-pixel proof or cross-renderer parity. The BMP channel/orientation and straight-alpha assertions sample known fixture landmarks; exact decoded pixels remain covered by reader tests.
- Windows GUI, Windows package/native-library resolution, Wine equivalence, and hosted CI are `NOT_RUN`. No full Kinovea parity claim is made.
