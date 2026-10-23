# Wine GUI oracle

This local-only harness launches the installed Kinovea executable through an existing Wine prefix, runs versioned JSON scenarios, records process/Wine output, and captures only the Kinovea X11/Xwayland window. It does not modify Kinovea production source, install dependencies, invoke `winecfg`, change the prefix, or terminate `wineserver`.

## Requirements

- A working Kinovea executable and Wine prefix supplied by the operator.
- Python 3.10 or later, Wine and `winepath` on `PATH`.
- `xprop`, `xrandr`, `ffmpeg` built with `x11grab`, `qdbus6`, Python 3, .NET 10 SDK, an active X11 or Xwayland display, and support for rootless user/mount namespaces plus OverlayFS.
- The verified local H.264 file for MP4 scenarios.

The current environment is Wayland through KWin with an Xwayland display. `xdotool`, `wmctrl`, and Wayland automation tools are not installed. The harness locates windows through `xprop`, activates the exact Xwayland client using a temporary KWin script (`workspace.getClient`, `raiseWindow`, and `activeWindow`), then checks KWin's `_NET_ACTIVE_WINDOW` and sets Xwayland input focus to that same window before emitting XTest events. It restores the previous active client when KWin exposes it. Captures use `ffmpeg -f x11grab -window_id <Kinovea-window-id>` so unrelated desktop windows are not recorded. This requires the harness to run in the logged-in graphical session with both DBus and Xwayland available. See the [KWin scripting tutorial](https://develop.kde.org/docs/plasma/kwin/) and [KWin scripting API](https://develop.kde.org/docs/plasma/kwin/api/).

## Verify the installation

Set the existing prefix path in your shell without changing the prefix:

```sh
export WINEPREFIX="/path/to/the/existing/win64-prefix"
export KINOVEA_EXE="$WINEPREFIX/drive_c/Program Files/Kinovea/Kinovea.exe"
python3 tools/wine-oracle/oracle.py inspect \
  --executable "$KINOVEA_EXE" \
  --prefix "$WINEPREFIX" \
  --output /tmp/kinovea-wine-environment.json
```

The generated metadata redacts the executable and prefix paths but includes executable SHA-256/version, Wine version, prefix architecture, detected .NET Framework Release value, CLR file presence, display backend/resolution, OS and SDK-related runtime context. Do not commit environment output from another machine without reviewing it for local data.

## Validate scenario definitions

```sh
python3 tools/wine-oracle/oracle.py validate
```

The Windows Bitmap candidate baseline is still unapproved. For this Wine setup, make byte-identical copies of the small candidate fixtures in the existing Wine-visible local test-data directory, then set the scenario variables. Verify the hashes after copying:

```sh
export KINOVEA_ORACLE_LOCAL_DATA="$HOME/projects/kinove-wine/test-data"
cp tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f/fixtures/bmp-rgb-7x5.bmp "$KINOVEA_ORACLE_LOCAL_DATA/wine-oracle-bmp-rgb-7x5.bmp"
cp tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f/fixtures/png-alpha-13x9.png "$KINOVEA_ORACLE_LOCAL_DATA/wine-oracle-png-alpha-13x9.png"
sha256sum "$KINOVEA_ORACLE_LOCAL_DATA/wine-oracle-bmp-rgb-7x5.bmp" "$KINOVEA_ORACLE_LOCAL_DATA/wine-oracle-png-alpha-13x9.png"
export KINOVEA_ORACLE_CANDIDATE_BMP="$KINOVEA_ORACLE_LOCAL_DATA/wine-oracle-bmp-rgb-7x5.bmp"
export KINOVEA_ORACLE_CANDIDATE_PNG="$KINOVEA_ORACLE_LOCAL_DATA/wine-oracle-png-alpha-13x9.png"

# Keep generated fixtures outside Git and under the home directory used by Wine.
python3 tools/wine-oracle/generate-fixtures.py "$KINOVEA_ORACLE_LOCAL_DATA/wine-oracle-generated"
export KINOVEA_ORACLE_STANDARD_PNG="$KINOVEA_ORACLE_LOCAL_DATA/wine-oracle-generated/synthetic-rgb-1280x720.png"
export KINOVEA_ORACLE_STANDARD_BMP="$KINOVEA_ORACLE_LOCAL_DATA/wine-oracle-generated/synthetic-rgb-640x480.bmp"

# The fitness image is pre-existing local media and is not committed.
export KINOVEA_ORACLE_FITNESS_PNG="$KINOVEA_ORACLE_LOCAL_DATA/fitness_PNG155.png"
export KINOVEA_ORACLE_MP4="/path/to/reference.mp4"
```

The harness checks each fixture SHA-256 before starting Kinovea. Candidate copies must match `85e0feff83d9920703cbf8be2e6ccc76741da22b4ffe791d5d4c415724de1244` (BMP) and `e0759d696bfef305e0a2c1c8fc7b7673e9e46390ef15beb541679c6766041dcd` (PNG). The generated 1280x720 PNG and 640x480 BMP have deterministic hashes recorded in `docs/evidence/wine-still-image-gui-v2.json`.

## Run scenarios

Each run writes to a new, empty output directory. `run-scenario.sh` mounts the original prefix read-only as the lower layer of a disposable copy-on-write overlay; Wine and Kinovea writes land in a temporary upper layer that is discarded after a successful run. The original prefix is never passed as writable `WINEPREFIX`. Scenarios opt in to clearing restored screen descriptors only inside this disposable overlay, so a saved video does not obscure the test. Use a distinct output directory per scenario:

```sh
tools/wine-oracle/run-scenario.sh \
  tools/wine-oracle/scenarios/bmp-open.v1.json \
  /tmp/kinovea-oracle/bmp-open-v1

tools/wine-oracle/run-scenario.sh \
  tools/wine-oracle/scenarios/png-open.v1.json \
  /tmp/kinovea-oracle/png-open-v1

tools/wine-oracle/run-scenario.sh \
  tools/wine-oracle/scenarios/mp4-playback.v1.json \
  /tmp/kinovea-oracle/mp4-playback-v1

tools/wine-oracle/run-scenario.sh \
  tools/wine-oracle/scenarios/frame-navigation.v1.json \
  /tmp/kinovea-oracle/frame-navigation-v1

tools/wine-oracle/run-scenario.sh \
  tools/wine-oracle/scenarios/fitness-png-still.v1.json \
  /tmp/kinovea-oracle/fitness-png-v1

tools/wine-oracle/run-scenario.sh \
  tools/wine-oracle/scenarios/large-png-still.v1.json \
  /tmp/kinovea-oracle/large-png-v1

tools/wine-oracle/run-scenario.sh \
  tools/wine-oracle/scenarios/standard-bmp-still.v1.json \
  /tmp/kinovea-oracle/standard-bmp-v1
```

Review `result.json`, each checkpoint image, and `wine.stdout.log`/`wine.stderr.log`. `executionStatus` records whether automation completed. Fresh runs leave `status` and each `observedOutcomes` entry at `NOT_ESTABLISHED` until a human reviews the screenshots; the runner never converts a sent key or a successful dialog dismissal into a behavioral pass. Screenshots are visual evidence only. They do not prove decoded pixel equality, exact playback timing, frame timestamps, or parity with Windows. Outcomes needing human review remain `MANUAL_REQUIRED` or `NOT_ESTABLISHED`; a submitted file path alone does not establish that Kinovea opened it. Captures and application logs can expose local filenames, paths, or screen content; review before sharing them.

The harness exits nonzero on a failed launch/action, mismatched executable/fixture hash, or unsupported display configuration. It refuses to start if another Kinovea window is visible. It restores the prior active window after the run when KWin exposes it. Cleanup sends Alt+F4 only to the Kinovea window created by the run; it never runs `wineserver -k` or signals a process group. If Kinovea does not close normally, inspect the recorded result and close only that run’s app window manually. The low-level `oracle.py run` command requires the `KINOVEA_ORACLE_OVERLAY=1` marker; use the wrapper rather than starting the original prefix directly.

## Still-image opening routes and current limits

The installed Wine application was observed opening still images through two keyboard-driven routes:

- `Ctrl+O`, configured by `Kernel.mnuOpenFile.ShortcutKeys` and handled by `mnuOpenFileOnClick`.
- File menu: send `Alt`, `Home`, `Enter`, `Enter` to select the first File command. In this Portuguese UI, `Alt+F` selected **Ferramentas**, so it is not used. The File menu and toolbar button share `mnuOpenFileOnClick`; the handler calls `OpenFromPath`, ensures a player screen, and loads it through `LoaderVideo.LoadVideoInScreen`.

Each still scenario verifies visible header text from the captured Kinovea window. The run fails if OCR cannot find the expected filename, dimensions and `25.00 fps`; a dialog closing or a successful process exit alone does not count as an image opening. The 7x5 BMP, 13x9 alpha PNG, 640x480 BMP, 1280x720 PNG, and the local 276x398 `fitness_PNG155.png` each reached a player in the recorded Wine run. The image headers showed 25.00 fps. The candidate 7x5 and 13x9 dimensions/fps match the Windows reader characterization for those same fixture hashes; the Windows golden remains `CANDIDATE_NOT_APPROVED`.

The file-browser double-click path and drag/drop path are present in checked-out source, but were `NOT_RUN` in the final GUI scenarios. They require mouse input. On committed source `d4b35748`, the final guarded close-button probe requested root `(1904,104)` but observed `(486,520)`, so it refused to send a button event. Its run is recorded as failed because the requested interaction did not occur; this verifies fail-closed behavior only. An earlier unguarded probe observed `(357,979)`. Neither probe clicked the close button; do not count either as a close test.

The player appears idle with the play control available. The portrait image is centered on the dark canvas; the 1280x720 image appears 16:9 with side margins and nearly fills the canvas height. Exact scaling was not measured, and no fit-to-window command was activated (`NOT_RUN`). The timeline/navigation strip and drawing toolbar are visible. After `Ctrl+F4`, the same player header remained visible. The shortcut's effect on the active player is therefore `NOT_ESTABLISHED`; no close-then-reopen cycle was achieved. The captured Right/Left key requests changed the file-browser selection region, not a verified player frame or timestamp. Player navigation, zoom, exact fit scale, alpha rendering, and EXIF transforms remain `NOT_ESTABLISHED`. Screenshots cannot establish decoded pixel values or alpha semantics.

`line-tool-probe.v1.json` is a guarded exploratory scenario for the first Epic 04
annotation observation. It first proves the large PNG player header, then uses the
window-relative XTest click guard to select the observed diagonal line icon and click two
canvas points. The output is deliberately `MANUAL_REQUIRED`: a delivered click is not
evidence of tool selection, a created drawing, geometry, measurement, or persistence.
The scenario is tied to this installed binary's captured 1920×969 window layout and must
be recalibrated after a UI or scaling change.

For this Wine environment, generated fixtures under host `/tmp` did not produce a player, while the same hash-verified bytes under the existing home test-data directory did. The cause was not isolated. Keep Wine inputs under a Wine-visible home path and require the header check on every run.

The installed executable is Kinovea `2025.2.0.0`; the checked-out source reports `2026.1.0.0`. Their source correspondence is `NOT_ESTABLISHED`. Wine evidence is distinct from Windows CI evidence and from any future native .NET 10 viewer. See [still-image Wine evidence v2](../../docs/evidence/wine-still-image-gui-v2.json) and the corrected [v1 evidence record](../../docs/evidence/wine-functional-oracle-v1.md).

For additional Wine diagnostics, pass e.g. `--wine-debug '+seh,+tid'`. Wine debug logs can be large and may contain filenames; keep them outside Git and review before sharing.

## Add a scenario

Copy a versioned `*.v1.json` definition. Keep scenario definitions in this directory and outputs outside it. Include the application version and executable SHA, fixture ID/hash/provenance, preconditions, ordered actions, expected observable outcomes, screenshots, outputs, cleanup, confidence, and limitations. Use source-known shortcuts when possible. Include a `verify_visible_text` action after the open checkpoint so an image that did not reach a player fails or reports `INCONCLUSIVE`. Do not translate a screenshot into a pixel/numerical pass claim. Mark manual operations `MANUAL_REQUIRED` and unverified behaviors `NOT_ESTABLISHED`.

## Evidence categories

- `WINE_OBSERVED`: observed on this specific local Wine/app binary combination.
- `WINDOWS_VERIFIED`: separately executed on Windows; reference the exact workflow/run evidence.
- `CROSS_ENVIRONMENT_MATCH`: comparable observation agrees across Wine and Windows for the named property only.
- `DIFFERENCE`: comparable observation differs.
- `NOT_RUN` / `NOT_ESTABLISHED`: no relevant execution/evidence.

Wine GUI evidence is not a Windows reference oracle, and neither proves behavior of a future native .NET 10 application. Visual comparisons should record captured window resolution and review method; exact image equality across graphics backends is not a universal acceptance criterion.
