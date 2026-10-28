#!/usr/bin/env python3
"""Exercise actual Avalonia viewport wheel, pan, fit, resize, and alpha rendering."""

from __future__ import annotations

import argparse
import ctypes
import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import time
from pathlib import Path

from PIL import Image

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "native-player"))
spec = importlib.util.spec_from_file_location("native_gui", Path(__file__).with_name("gui-interactions.py"))
gui = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(gui)


def run_player(assembly: Path, fixture: Path, report_path: Path, log_path: Path, source_sha: str):
    log = log_path.open("w", encoding="utf-8")
    process = subprocess.Popen(["dotnet", str(assembly), "--open", str(fixture), "--smoke-report", str(report_path)], cwd=REPO, env={**os.environ, "KINOVEA_SOURCE_SHA": source_sha}, stdout=log, stderr=subprocess.STDOUT)
    window = None
    try:
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            if process.poll() is not None:
                raise RuntimeError(f"player exited during startup: {process.returncode}")
            windows = [w for w in gui.oracle.list_x11_windows() if w.get("pid") == str(process.pid) and w["title"] == "Kinovea Native Player"]
            if windows and gui.report(report_path).get("status") == "VISIBLE_FRAME_RENDERED":
                window = windows[0]
                gui.force_activate_window(window["id"])
                time.sleep(0.8)
                return process, window, log, gui.report(report_path)
            time.sleep(0.15)
        raise TimeoutError("no process-owned player window rendered the startup fixture")
    except Exception:
        log.close()
        if process.poll() is None:
            process.terminate()
            process.wait(timeout=5)
        raise


def sample(image_path: Path, state: dict, image_x: float, image_y: float) -> tuple[int, int, int]:
    scale = float(state["renderScaling"])
    x = float(state["viewportLogicalX"]) + float(state["imageRectX"]) + image_x * float(state["imageRectWidth"]) / float(state["width"])
    y = float(state["viewportLogicalY"]) + float(state["imageRectY"]) + image_y * float(state["imageRectHeight"]) / float(state["height"])
    with Image.open(image_path) as raw:
        frame = raw.convert("RGB")
        return frame.getpixel((min(frame.width - 1, max(0, round(x * scale))), min(frame.height - 1, max(0, round(y * scale)))))


def capture_record(window: dict, path: Path) -> dict:
    return gui.capture(window, path)


def close_player(process, window, log) -> None:
    if process.poll() is None:
        try:
            gui.oracle.activate_window(window["id"])
            gui.oracle.send_key("Control_L+w", window["id"])
            process.wait(timeout=1)
        except Exception:
            pass
        if process.poll() is None:
            try:
                gui.oracle.activate_window(window["id"])
                gui.oracle.send_key("Alt_L+F4", window["id"])
            except Exception:
                pass
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.terminate()
                process.wait(timeout=5)
    log.close()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--assembly", type=Path, required=True)
    parser.add_argument("--bmp", type=Path, required=True)
    parser.add_argument("--alpha", type=Path, required=True)
    parser.add_argument("--png", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--source-sha", required=True)
    args = parser.parse_args()
    gui.xwayland_capture.verify_source_sha(REPO, args.source_sha)
    assembly = args.assembly.resolve(); bmp = args.bmp.resolve(); alpha = args.alpha.resolve(); png = args.png.resolve(); output = args.output.resolve()
    if not assembly.is_file() or not bmp.is_file() or not alpha.is_file() or not png.is_file():
        raise FileNotFoundError("app assembly or viewport fixtures unavailable")
    gui.validate_fixture_manifest(Path(__file__).with_name("scenarios") / "viewport-gestures-v1.json", {"bmp": bmp, "alphaPng": alpha, "pngAfterGestures": png})
    output.mkdir(parents=True, exist_ok=True)
    actions: list[dict] = []; screenshots: list[dict] = []; resources: list[dict] = []; state_trace: list[dict] = []
    report_path = output / "viewport-bmp-state.json"
    report_path.unlink(missing_ok=True)
    process, window, log, state = run_player(assembly, bmp, report_path, output / "viewport-bmp.log", args.source_sha)
    try:
        state_trace.append({"point": "bmp-open", "state": state})
        (output / "viewport-state-trace.json").write_text(json.dumps(state_trace, indent=2) + "\n", encoding="utf-8")
        if (state.get("inputSha256"), state.get("width"), state.get("height"), state.get("timestamp"), state.get("previousTimestamp")) != (gui.sha256(bmp), 640, 480, 0, -1):
            raise RuntimeError(f"BMP startup metadata mismatch: {state}")
        resources.append({"fixture": bmp.name, **gui.process_resources(process.pid)})
        screenshots.append({"checkpoint": "bmp-initial-fit", **capture_record(window, output / "viewport-bmp-initial.png")})

        viewport_x = float(state["viewportLogicalX"]); viewport_y = float(state["viewportLogicalY"])
        viewport_w = float(state["viewportLogicalWidth"]); viewport_h = float(state["viewportLogicalHeight"])
        render_scale = float(state["renderScaling"])
        anchor = (viewport_x + viewport_w * 0.61, viewport_y + viewport_h * 0.43)
        pointer_x = round(anchor[0] * render_scale); pointer_y = round(anchor[1] * render_scale)
        wheel_input = gui.point_in_window(window["id"], pointer_x, pointer_y, [4])
        zoom = gui.wait_for(report_path, lambda value: value.get("wheelEventCount", 0) > state.get("wheelEventCount", 0) and abs(float(value.get("zoom", 1)) - float(state.get("zoom", 1))) > 0.001, "real viewport wheel event changes zoom")
        state_trace.append({"point": "wheel-zoom", "state": zoom, "anchorViewportLogical": anchor, "windowInput": wheel_input})
        (output / "viewport-state-trace.json").write_text(json.dumps(state_trace, indent=2) + "\n", encoding="utf-8")
        requested_local = (anchor[0] - viewport_x, anchor[1] - viewport_y)
        actual_local = (float(zoom["lastWheelViewportX"]), float(zoom["lastWheelViewportY"]))
        image_scale_before = float(state["imageRectWidth"]) / 640
        image_scale_after = float(zoom["imageRectWidth"]) / 640
        source_anchor = ((actual_local[0] - float(state["imageRectX"])) / image_scale_before,
                         (actual_local[1] - float(state["imageRectY"])) / image_scale_before)
        anchor_after = (float(zoom["imageRectX"]) + source_anchor[0] * image_scale_after,
                        float(zoom["imageRectY"]) + source_anchor[1] * image_scale_after)
        if max(abs(anchor_after[i] - actual_local[i]) for i in (0, 1)) > 1.5:
            raise RuntimeError(f"wheel zoom did not preserve Avalonia's reported pointer anchor: {actual_local} -> {anchor_after}; state={zoom}")
        actions.append({"action": "mouse-wheel zoom", "result": "PASS", "wheelEvents": zoom.get("wheelEventCount"), "zoomBefore": state.get("zoom"), "zoomAfter": zoom.get("zoom"), "requestedViewportLogical": requested_local, "avaloniaPointerLogical": actual_local, "anchorAfterLogical": anchor_after, "windowRelativeInput": wheel_input})
        screenshots.append({"checkpoint": "bmp-wheel-zoom", **capture_record(window, output / "viewport-bmp-zoom.png")})

        start_x = round((viewport_x + viewport_w * 0.5) * render_scale)
        start_y = round((viewport_y + viewport_h * 0.5) * render_scale)
        drag = gui.drag_in_window(window["id"], start_x, start_y, 54, 33)
        panned = gui.wait_for(report_path, lambda value: abs(float(value.get("panX", 0)) - float(zoom.get("panX", 0))) > 1 and abs(float(value.get("panY", 0)) - float(zoom.get("panY", 0))) > 1, "real drag pan")
        expected_delta = (54 / render_scale, 33 / render_scale)
        observed_delta = (float(panned["panX"]) - float(zoom["panX"]), float(panned["panY"]) - float(zoom["panY"]))
        if any(abs(observed_delta[i] - expected_delta[i]) > 2 for i in (0, 1)):
            raise RuntimeError(f"pan delta mismatch: expected {expected_delta}, got {observed_delta}")
        actions.append({"action": "mouse drag pan", "result": "PASS", "expectedLogicalDelta": expected_delta, "observedLogicalDelta": observed_delta, "pointer": drag})
        screenshots.append({"checkpoint": "bmp-wheel-pan", **capture_record(window, output / "viewport-bmp-pan.png")})

        fit = panned["fitButton"]
        fit_point = (round((float(fit["x"]) + float(fit["width"]) / 2) * render_scale), round((float(fit["y"]) + float(fit["height"]) / 2) * render_scale))
        fit_input = gui.point_in_window(window["id"], *fit_point, [1])
        fitted = gui.wait_for(report_path, lambda value: value.get("zoom") == 1 and value.get("panX") == 0 and value.get("panY") == 0, "Fit button resets view")
        bmp_shot = output / "viewport-bmp-fit.png"
        fit_capture = capture_record(window, bmp_shot)
        samples = {"topLeftBand": sample(bmp_shot, fitted, 10.5, 10.5), "asymmetricDiagonal": sample(bmp_shot, fitted, 100.5, 100.5), "middleBlueBand": sample(bmp_shot, fitted, 320.5, 240.5)}
        expected = {"topLeftBand": (235, 40, 35), "asymmetricDiagonal": (250, 250, 250), "middleBlueBand": (30, 55, 220)}
        if not all(gui.near_color(samples[key], expected[key], 24) for key in samples):
            raise RuntimeError(f"rendered BMP RGB/orientation samples differ from authored values: {samples}")
        actions.append({"action": "Fit and rendered color/orientation", "result": "PASS", "fitInput": fit_input, "logicalBounds": fit, "samples": {key: {"expected": expected[key], "rendered": samples[key]} for key in samples}, "tolerance": 24, "capture": fit_capture})
        screenshots.append({"checkpoint": "bmp-fit-color-orientation", **fit_capture})

        before_resize = gui.report(report_path)
        geometry = gui.window_geometry(window["id"])
        x11, _, display = gui.x_display()
        try:
            x11.XResizeWindow.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_uint, ctypes.c_uint]
            x11.XResizeWindow(display, int(window["id"], 16), max(1000, geometry["width"] - 240), max(700, geometry["height"] - 140))
            x11.XSync(display, 0)
        finally:
            x11.XCloseDisplay(display)
        resized = gui.wait_for(report_path, lambda value: value.get("windowLogicalWidth") != before_resize.get("windowLogicalWidth") and value.get("viewportLogicalWidth") != before_resize.get("viewportLogicalWidth"), "window resize changes viewport")
        image_aspect = float(resized["imageRectWidth"]) / float(resized["imageRectHeight"])
        if abs(image_aspect - (640 / 480)) > 0.01:
            raise RuntimeError(f"resize changed image aspect ratio to {image_aspect}")
        actions.append({"action": "actual window resize", "result": "PASS", "windowLogicalBefore": [before_resize.get("windowLogicalWidth"), before_resize.get("windowLogicalHeight")], "windowLogicalAfter": [resized.get("windowLogicalWidth"), resized.get("windowLogicalHeight")], "viewportAfter": [resized.get("viewportLogicalWidth"), resized.get("viewportLogicalHeight")], "imageAspect": image_aspect})
        screenshots.append({"checkpoint": "bmp-window-resized", **capture_record(window, output / "viewport-bmp-resized.png")})

        png_state, png_open = gui.open_from_picker(window, png, report_path, output, "png-after-gestures", gui.sha256(png))
        if (png_state.get("width"), png_state.get("height"), png_state.get("timestamp"), png_state.get("previousTimestamp"), png_state.get("zoom"), png_state.get("panX"), png_state.get("panY")) != (1280, 720, 0, -1, 1, 0, 0):
            raise RuntimeError(f"opening a new image after zoom/pan did not reset to initial fit: {png_state}")
        actions.append({"action": "open PNG after zoom, pan, fit, and resize", "result": "PASS", "inputSha256": png_state.get("inputSha256"), "dimensions": [png_state.get("width"), png_state.get("height")], "timestamp": png_state.get("timestamp"), "previousTimestamp": png_state.get("previousTimestamp"), "zoom": png_state.get("zoom"), "pan": [png_state.get("panX"), png_state.get("panY")], "presentationCopyCount": png_state.get("presentationCopyCount"), "picker": png_open["action"]})
        screenshots.append({"checkpoint": "png-opened-after-viewport-gestures", **png_open["visibleScreenshot"]})
    finally:
        close_player(process, window, log)

    alpha_report = output / "viewport-alpha-state.json"
    alpha_report.unlink(missing_ok=True)
    alpha_process, alpha_window, alpha_log, alpha_state = run_player(assembly, alpha, alpha_report, output / "viewport-alpha.log", args.source_sha)
    try:
        if alpha_state.get("alphaMode") != "Straight" or (alpha_state.get("width"), alpha_state.get("height")) != (13, 9):
            raise RuntimeError(f"alpha fixture metadata mismatch: {alpha_state}")
        alpha_shot = output / "viewport-alpha.png"
        alpha_capture = capture_record(alpha_window, alpha_shot)
        background = (17, 21, 27)
        transparent = sample(alpha_shot, alpha_state, 0.5, 0.5)
        partial = sample(alpha_shot, alpha_state, 1.5, 0.5)
        expected_partial = tuple(round(src * (64 / 255) + base * (191 / 255)) for src, base in zip((54, 46, 134), background))
        if not gui.near_color(transparent, background, 24) or not gui.near_color(partial, expected_partial, 30):
            raise RuntimeError(f"rendered alpha samples exceed tolerance: transparent={transparent}, partial={partial}, expected={expected_partial}")
        actions.append({"action": "straight-alpha render", "result": "PASS", "alphaMode": alpha_state.get("alphaMode"), "transparentExpectedBackground": background, "transparentRendered": transparent, "partialExpectedComposite": expected_partial, "partialRendered": partial, "tolerance": 30, "capture": alpha_capture, "limitation": "screenshot compositing; raw alpha values separately checked by image reader tests"})
        screenshots.append({"checkpoint": "straight-alpha-render", **alpha_capture})
        resources.append({"fixture": alpha.name, **gui.process_resources(alpha_process.pid)})
    finally:
        close_player(alpha_process, alpha_window, alpha_log)

    manifest = {"schemaVersion": 1, "scenarioId": "native-player-viewport-gestures-v1", "sourceSha": args.source_sha, "host": {"os": "Gentoo Linux", "dotnetRuntime": "10.0.11", "display": os.environ.get("DISPLAY"), "waylandDisplay": os.environ.get("WAYLAND_DISPLAY"), "sessionType": os.environ.get("XDG_SESSION_TYPE"), "xwaylandResolution": gui.oracle.xwayland_resolution()}, "fixtures": {"bmp": {"fileName": bmp.name, "sha256": gui.sha256(bmp)}, "alphaPng": {"fileName": alpha.name, "sha256": gui.sha256(alpha)}, "pngOpenedAfterGestures": {"fileName": png.name, "sha256": gui.sha256(png)}}, "actions": actions, "screenshots": screenshots, "resourceSamples": resources, "packages": {"Avalonia": "12.1.4", "SkiaSharp": "3.119.4", "mappedNativeLibraries": resources[0].get("mappedSkiaAndHarfBuzzLibraries", [])}, "limits": ["This scenario starts the BMP using the repeatable --open path so it can isolate pointer behavior from the DBus portal.", "A separate GUI lifecycle scenario tests the real storage provider and repeated in-process sessions.", "Rendered screenshot comparisons are not cross-renderer pixel parity.", "Windows GUI execution is NOT_RUN."]}
    (output / "viewport-gestures.v1.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest, indent=2))
    return 0


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as ex:
        print(f"viewport gesture scenario failed: {ex}", file=sys.stderr)
        raise SystemExit(1)
