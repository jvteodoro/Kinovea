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

The BMP and PNG inputs are the deterministic candidate fixtures already checked in under `tests/Bitmap.Characterization/golden/candidate/`. They remain candidate evidence, not approved golden data. The MP4 fixture is not in the repository. For this machine, point the variable at the verified matching file:

```sh
export KINOVEA_ORACLE_MP4="/path/to/reference.mp4"
```

The harness checks its SHA-256 before starting Kinovea.

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
```

Review `result.json`, each checkpoint image, and `wine.stdout.log`/`wine.stderr.log`. `executionStatus` records whether the scenario actions completed; `status` reports whether any named outcome was established. Screenshots are visual evidence only. They do not prove decoded pixel equality, exact playback timing, frame timestamps, or parity with Windows. Outcomes needing human review remain `MANUAL_REQUIRED` or `NOT_ESTABLISHED`; a submitted file path alone does not establish that Kinovea opened it. Captures and application logs can expose local filenames, paths, or screen content; review before sharing them.

The harness exits nonzero on a failed launch/action, mismatched executable/fixture hash, or unsupported display configuration. It refuses to start if another Kinovea window is visible. It restores the prior active window after the run when KWin exposes it. Cleanup sends Alt+F4 only to the Kinovea window created by the run; it never runs `wineserver -k` or signals a process group. If Kinovea does not close normally, inspect the recorded result and close only that run’s app window manually. The low-level `oracle.py run` command requires the `KINOVEA_ORACLE_OVERLAY=1` marker; use the wrapper rather than starting the original prefix directly.

For additional Wine diagnostics, pass e.g. `--wine-debug '+seh,+tid'`. Wine debug logs can be large and may contain filenames; keep them outside Git and review before sharing.

## Add a scenario

Copy a versioned `*.v1.json` definition. Keep scenario definitions in this directory and outputs outside it. Include the application version and executable SHA, fixture ID/hash/provenance, preconditions, ordered actions, expected observable outcomes, screenshots, outputs, cleanup, confidence, and limitations. Use source-known shortcuts when possible. Do not translate a screenshot into a pixel/numerical pass claim. Mark manual operations `MANUAL_REQUIRED` and unverified behaviors `NOT_ESTABLISHED`.

## Evidence categories

- `WINE_OBSERVED`: observed on this specific local Wine/app binary combination.
- `WINDOWS_VERIFIED`: separately executed on Windows; reference the exact workflow/run evidence.
- `CROSS_ENVIRONMENT_MATCH`: comparable observation agrees across Wine and Windows for the named property only.
- `DIFFERENCE`: comparable observation differs.
- `NOT_RUN` / `NOT_ESTABLISHED`: no relevant execution/evidence.

Wine GUI evidence is not a Windows reference oracle, and neither proves behavior of a future native .NET 10 application. Visual comparisons should record captured window resolution and review method; exact image equality across graphics backends is not a universal acceptance criterion.
