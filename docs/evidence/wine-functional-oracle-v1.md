# Verification: Wine functional and visual oracle v1

Recorded 2026-10-12; corrected after screenshot review on 2026-10-13. Base source: `port/net10-linux` SHA `8b73d5440908d5df954fda0e255fa3d79ed5694b`. Original scenario runner: `test/wine-functional-oracle` SHA `214137a96d5c14703ecc3d2fb26b1c66e1aa640f`.

## Review correction — still images did open

The first review incorrectly described the BMP and PNG application windows as blank. Reviewing the captured application-window screenshots showed active player screens instead. This corrects that earlier interpretation: the 7x5 BMP and 13x9 PNG both opened through the actual Kinovea UI, and their headers showed their filenames, dimensions, and `25.00 fps`. The tiny raster patterns were visible as very small marks in the player canvas. The active player also showed Kinovea's usual timeline/navigation strip and drawing toolbar. This correction is scoped to the visual evidence; it does not establish pixel equality or alpha semantics.

The exact opening route was not captured in the original screenshots. The still-image v2 scenarios recorded on commit `d4b3574892e133d0a1e322d1d4fd5a6169e8e4e4` reproduced two keyboard routes: `Ctrl+O`, and the File menu sequence `Alt`, `Home`, `Enter`, `Enter`. `Alt+F` selected `Ferramentas` in the Portuguese UI and was not used as the File menu route. See the [v2 machine-readable evidence](wine-still-image-gui-v2.json).

## Environment and executable provenance

- Host: Gentoo Linux, x86_64, kernel `6.18.50`; Python `3.14.7`; .NET SDK `10.0.111`.
- Wine: Staging `11.19`; existing prefix architecture `win64`; registry Release value `528049` (.NET Framework 4.8). Kinovea startup log reports CLR `4.0.30319.42000` and Windows compatibility `NT 6.1.7601 Service Pack 1`.
- Desktop: Wayland/KWin `6.7.5`, Xwayland `DISPLAY=:1`, desktop resolution `1920x1080`; scale factor `NOT_DETECTED`. Kinovea window captures are `1920x969`.
- Automation: KWin DBus scripting activates a specific Xwayland window; `xprop`, Xlib/XTest send keys after checking active focus; FFmpeg `8.1.2` captures using `x11grab -window_id`. `xdotool`, `wmctrl`, `wtype`, and `ydotool` were absent. Capture is limited to the Kinovea window; no full-desktop screenshots were taken.
- Installed executable: `Kinovea.exe`, version `2025.2.0.0`, size 459,776 bytes, SHA-256 `22d95a127257e59bd435eaa2689dd7fda2cae802829e801aa5cd367ee7a12534`.
- Source correspondence: `NOT_ESTABLISHED`. The installed binary is `2025.2.0.0`; this checkout reports source assembly/file version `2026.1.0.0`. No matching source revision was identified.

The original Wine prefix was not used as a writable prefix. Each scenario used it as the read-only lower layer of a disposable OverlayFS. Saved Continue-mode screen descriptors were cleared only in that overlay. The runner closed only the Kinovea window it launched and did not invoke `wineserver -k`. It does not delete or modify the user's prefix.

## Fixtures

| ID | Format / dimensions | SHA-256 | Provenance |
|---|---|---|---|
| `bitmap-candidate-bmp-rgb-7x5` | BMP, 7x5, 194 bytes | `85e0feff83d9920703cbf8be2e6ccc76741da22b4ffe791d5d4c415724de1244` | Tracked synthetic Windows net48 candidate fixture; baseline is not approved. |
| `bitmap-candidate-png-alpha-13x9` | PNG RGBA, 13x9 | `e0759d696bfef305e0a2c1c8fc7b7673e9e46390ef15beb541679c6766041dcd` | Tracked synthetic Windows net48 candidate fixture; baseline is not approved. |
| `local-h264-1280x720-30fps-10s` | MP4/H.264, 1280x720, 30/1 fps, 10 s, 3,901,460 bytes | `04761f49477fe2662f93666f67b728c0434cc067cca7ba02cc4df6568298431d` | Pre-existing local video; not committed. `ffprobe` confirmed codec, dimensions, rate and duration. |

The fixture hashes above were recalculated from the checked-out BMP/PNG files and local MP4 before the final runs. The candidate Windows Bitmap baseline remains unapproved.

## Scenario results

A successful automation run records `executionStatus: COMPLETED`, but the runner deliberately leaves behavioral `resultStatus: NOT_ESTABLISHED`. The `reviewedObservations` below are a separate manual review of the captured window images. They apply only to the named visual observation on this Wine/application combination.

| Scenario | Automation | Manually reviewed outcome | Limits |
|---|---|---|---|
| BMP open/close | `COMPLETED`, exit 0. | `WINE_OBSERVED`: player header showed `bmp-rgb-7x5`, `7x5 px`, and `25.00 fps`; the tiny raster was visible on the player canvas. | The exact opening route is not established by this older screenshot set. Close behavior, pixel values and row orientation remain unverified. Candidate input only. |
| PNG alpha open/close | `COMPLETED`, exit 0. | `WINE_OBSERVED`: player header showed `png-alpha-13x9`, `13x9 px`, and `25.00 fps`; the tiny raster was visible on the player canvas. | The exact opening route is not established by this older screenshot set. Screenshot appearance does not reveal alpha values or premultiplication; close behavior remains unverified. Candidate input only. |
| MP4 playback | `COMPLETED`, exit 0. | `WINE_OBSERVED`: the app showed `reference`, `1280x720 px`, and `30.00 fps`; Space visibly changed the transport state between play and pause, and visible video content advanced. | Exact frame timing/rate, pixel values, reader parity with Windows, and source correspondence are `NOT_ESTABLISHED`. |
| Frame navigation | `COMPLETED`, exit 0. | `WINE_OBSERVED`: two Right requests changed visible content; one Left returned the full-window capture to the first forward-step capture. Forward-one/back-one SHA-256 is `d066f53095a23ee0dc64270713066e997a1a5cf1fe4510e7c28d41ae83ec52fe`; forward-two is `1a7912a20d4dd706855675c917e038c983dd918f9290b0e13be5eaca392ad1a7`. | Exact frame indices/timestamps and Windows parity are `NOT_ESTABLISHED`. The UI time overlay is not treated as a measurement. |

Raw evidence outputs for all runs are in `/tmp/kinovea-wine-results/bmp-commit-final/`, `/tmp/kinovea-wine-results/png-commit-final/`, `/tmp/kinovea-wine-results/mp4-commit-final/`, and `/tmp/kinovea-wine-results/navigation-commit-final/`. Screenshots and logs are local because MP4 images and application logs may contain user data. The machine-readable [manifest](wine-functional-oracle-v1.json) includes individual screenshot and generated artifact hashes, inputs, expected outcomes, execution status and reviewed observations. It excludes image binaries and local installation/prefix paths.

## Reproduction

Run from the logged-in graphical session. Set the actual existing prefix and executable paths for the local machine; these are not committed:

```sh
source ~/.bashrc
export WINEPREFIX="$HOME/projects/kinove-wine/prefix"
export KINOVEA_EXE="$WINEPREFIX/drive_c/Program Files/Kinovea/Kinovea.exe"
export KINOVEA_ORACLE_MP4="$HOME/projects/kinove-wine/test-data/reference.mp4"
python3 -m py_compile tools/wine-oracle/oracle.py
bash -n tools/wine-oracle/run-scenario.sh
python3 tools/wine-oracle/oracle.py validate
tools/wine-oracle/run-scenario.sh tools/wine-oracle/scenarios/bmp-open.v1.json /tmp/kinovea-wine-results/bmp-commit-final
tools/wine-oracle/run-scenario.sh tools/wine-oracle/scenarios/png-open.v1.json /tmp/kinovea-wine-results/png-commit-final
tools/wine-oracle/run-scenario.sh tools/wine-oracle/scenarios/mp4-playback.v1.json /tmp/kinovea-wine-results/mp4-commit-final
tools/wine-oracle/run-scenario.sh tools/wine-oracle/scenarios/frame-navigation.v1.json /tmp/kinovea-wine-results/navigation-commit-final
```

Each scenario starts a separate isolated prefix overlay. The wrapper refuses to start if an unrelated Kinovea window is visible and restores the prior active window when available. Review output screenshots and logs before sharing them because they may contain filenames or image content.

## Verification categories and limitations

- `WINE_OBSERVED`: manual visual observation on this specific local Wine and Kinovea binary only.
- `WINDOWS_VERIFIED`: `NOT_RUN` for this task. This is not a Windows CI result.
- `CROSS_ENVIRONMENT_MATCH`: `NOT_ESTABLISHED`.
- Native .NET 10 GUI comparison: `NOT_RUN`.
- Exact pixels, alpha channel values, exact playback timing, frame indices and timestamps: `NOT_ESTABLISHED`.
- BMP/PNG opening through this installed GUI: `WINE_OBSERVED` after screenshot review; see the correction above and v2 run evidence. A later local run verified active player headers for five image fixtures.
- Installed executable to checked-out source correspondence: `NOT_ESTABLISHED`.
- Native Wayland input/capture and desktops other than KWin/Xwayland: `NOT_RUN`.

No GitHub Actions workflow was added or run. This oracle needs a private local installation/prefix and an interactive desktop; omitting a hosted workflow also avoids spending limited Actions minutes.

## Next vertical slice

Use the deterministic synthetic 1280x720 PNG as the first native .NET 10 viewer reference: its known color bars, diagonal and checker region make visible fit and aspect behavior easier to compare without relying on private media. Its Wine screenshot was opened with the File menu keyboard route and shows a 1280x720, 25 fps player header. Keep numerical metadata, visual appearance and pixel-level comparison separate. The Wine close/reopen and player-navigation interactions remain unverified; do not treat this screenshot as a full viewer behavior specification.
