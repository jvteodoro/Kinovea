#!/usr/bin/env python3
"""Exercise calibration, a measured line, save, close, and reopen in the real UI."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import time
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
REPO = TOOLS.parents[1]
sys.path.insert(0, str(TOOLS))
spec = importlib.util.spec_from_file_location("native_gui", TOOLS / "gui-interactions.py")
gui = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(gui)


def wait_state(path: Path, predicate, message: str, seconds: float = 15) -> dict:
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        state = gui.report(path)
        if state and predicate(state):
            return state
        time.sleep(0.1)
    raise TimeoutError(f"{message}; last report={gui.report(path)}")


def click(window: dict, state: dict, control_name: str) -> dict:
    bounds = state.get(control_name)
    if not bounds:
        raise RuntimeError(f"application did not report {control_name} bounds")
    scale = float(state["renderScaling"])
    x = round((float(bounds["x"]) + float(bounds["width"]) / 2) * scale)
    y = round((float(bounds["y"]) + float(bounds["height"]) / 2) * scale)
    return gui.point_in_window(window["id"], x, y, [1])


def click_image(window: dict, state: dict, image_x: float, image_y: float) -> dict:
    scale = float(state["renderScaling"])
    x = float(state["viewportLogicalX"]) + float(state["imageRectX"]) + image_x * float(state["imageRectWidth"]) / int(state["width"])
    y = float(state["viewportLogicalY"]) + float(state["imageRectY"]) + image_y * float(state["imageRectHeight"]) / int(state["height"])
    return gui.point_in_window(window["id"], round(x * scale), round(y * scale), [1])


def choose_path(window: dict, target: Path) -> str:
    deadline = time.monotonic() + 12
    modal = None
    while time.monotonic() < deadline:
        active = gui.active_id()
        if active and active.casefold() != window["id"].casefold():
            modal = active
            break
        time.sleep(0.1)
    if modal is None:
        raise TimeoutError("Avalonia storage picker did not take focus")
    time.sleep(0.4)
    gui.send_focused_key("Alt+n", modal)
    gui.send_focused_key("Ctrl+a", modal)
    gui.type_into_portal(str(target.resolve()), modal)
    gui.send_focused_key("Return", modal)
    time.sleep(1)
    if gui.active_id() == modal:
        gui.send_focused_key("Ctrl+l", modal)
        gui.type_into_portal(str(target.resolve()), modal)
        gui.send_focused_key("Return", modal)
    return modal


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--assembly", required=True, type=Path)
    parser.add_argument("--media", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--source-sha", required=True)
    args = parser.parse_args()
    assembly = args.assembly.resolve()
    media = args.media.resolve()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    workspace = output / "measured-clip.kinovea.json"
    report_path = output / "player-state.json"
    log_path = output / "player.log"
    workspace.unlink(missing_ok=True)
    report_path.unlink(missing_ok=True)
    scenario = json.loads((TOOLS / "scenarios" / "analysis-roundtrip-v1.json").read_text(encoding="utf-8"))
    if scenario["fixture"]["sha256"] != sha256(media):
        raise RuntimeError(f"synthetic media hash mismatch: expected {scenario['fixture']['sha256']}, got {sha256(media)}")
    calibration = scenario["calibration"]
    measurement = scenario["measurement"]
    actions: list[dict] = []
    process = None
    with log_path.open("w", encoding="utf-8") as log:
        process = subprocess.Popen(["dotnet", str(assembly), "--open-video", str(media), "--smoke-report", str(report_path)],
                                   cwd=REPO, env={**os.environ, "KINOVEA_SOURCE_SHA": args.source_sha}, stdout=log, stderr=subprocess.STDOUT)
        window = None
        try:
            end = time.monotonic() + 30
            while time.monotonic() < end:
                if process.poll() is not None:
                    raise RuntimeError(f"desktop process exited: {process.returncode}")
                window = next((w for w in gui.oracle.list_x11_windows() if w.get("pid") == str(process.pid) and w["title"] == "Kinovea Native Player"), None)
                state = gui.report(report_path)
                if window and state and state.get("status") == "VISIBLE_FRAME_RENDERED":
                    break
                time.sleep(0.2)
            else:
                raise TimeoutError(f"video did not reach visible player state: {gui.report(report_path)}")

            gui.force_activate_window(window["id"])
            state = gui.report(report_path)
            if state.get("inputSha256") != sha256(media) or (state.get("width"), state.get("height")) != tuple(scenario["fixture"]["dimensions"]):
                raise RuntimeError(f"unexpected video metadata: {state}")
            initial_copy_count = int(state.get("presentationCopyCount", 0))
            if state.get("timestamp") != scenario["expected"]["initialTimestamp"]:
                raise RuntimeError(f"unexpected initial timestamp: {state.get('timestamp')}")
            gui.capture(window, output / "01-video-open.png")
            actions.append({"operation": "open media and render", "result": "PASS", "timestamp": state.get("timestamp")})

            click(window, state, "calibrateButton")
            state = wait_state(report_path, lambda x: x.get("collectingCalibration"), "calibration mode")
            click_image(window, state, *calibration["imagePoints"][0])
            click_image(window, state, *calibration["imagePoints"][1])
            state = wait_state(report_path, lambda x: not x.get("collectingCalibration") and x.get("calibrationStart") is not None, "calibration endpoints")
            actions.append({"operation": "set calibration", "result": "PASS", "imagePoints": [state["calibrationStart"], state["calibrationEnd"]],
                            "knownDistance": calibration["knownDistance"], "unit": calibration["unit"]})

            click(window, state, "measureButton")
            state = wait_state(report_path, lambda x: x.get("collectingMeasurement"), "measurement mode")
            click_image(window, state, *measurement["imagePoints"][0])
            click_image(window, state, *measurement["imagePoints"][1])
            state = wait_state(report_path, lambda x: x.get("analysis") is not None, "measured distance")
            measured = float(state["analysis"]["MeasuredDistance"])
            analysis = state["analysis"]
            def length(a: dict, b: dict) -> float:
                return ((float(a["X"]) - float(b["X"])) ** 2 + (float(a["Y"]) - float(b["Y"])) ** 2) ** 0.5
            expected_from_reported_points = length(analysis["MeasureStart"], analysis["MeasureEnd"]) * float(analysis["KnownDistance"]) / length(analysis["CalibrationStart"], analysis["CalibrationEnd"])
            nominal = float(measurement["expectedValue"])
            if abs(measured - expected_from_reported_points) > 1e-8 or abs(measured - nominal) > max(5, nominal * 0.025):
                raise RuntimeError(f"measurement result mismatch: measured={measured}, expected from image points={expected_from_reported_points}")
            gui.capture(window, output / "02-measured-overlay.png")
            actions.append({"operation": "measure line against calibration", "result": "PASS", "value": measured,
                            "unit": state["analysis"]["Unit"], "actualImagePoints": analysis,
                            "independentRecalculation": expected_from_reported_points, "nominalValue": 200})

            click(window, state, "saveAnalysisButton")
            choose_path(window, workspace)
            state = wait_state(report_path, lambda x: x.get("analysisWorkspacePath") == str(workspace), "analysis save report", seconds=20)
            if not workspace.is_file():
                raise RuntimeError("save dialog returned but workspace file was not written")
            saved_hash = sha256(workspace)
            actions.append({"operation": "save workspace through native picker", "result": "PASS", "path": workspace.name, "sha256": saved_hash})

            gui.oracle.send_key("Control_L+w", window["id"])
            state = wait_state(report_path, lambda x: x.get("status") == "EMPTY", "close active reader")
            actions.append({"operation": "close media", "result": "PASS", "readerState": state.get("readerState")})

            click(window, state, "openAnalysisButton")
            choose_path(window, workspace)
            state = wait_state(report_path, lambda x: x.get("analysis") is not None and x.get("readerState") == "Open", "reopened analysis", seconds=30)
            restored = float(state["analysis"]["MeasuredDistance"])
            expected_timestamp = scenario["expected"]["restoredTimestamp"]
            if (abs(restored - measured) > 1e-8 or state.get("analysisTimestamp") != expected_timestamp
                    or state.get("timestamp") != expected_timestamp
                    or state.get("presentationCopyCount") != initial_copy_count + 1):
                raise RuntimeError(f"restored analysis differs: {state}")
            gui.capture(window, output / "03-analysis-reopened.png")
            actions.append({"operation": "open saved workspace through native picker", "result": "PASS", "value": restored,
                            "analysisTimestamp": state.get("analysisTimestamp"), "currentTimestamp": state.get("timestamp"),
                            "presentationCopiesForTwoOpenIntervals": state.get("presentationCopyCount"),
                            "initialCopyCount": initial_copy_count})

            manifest = {"schemaVersion": 1, "sourceSha": args.source_sha, "mediaSha256": sha256(media),
                        "workspaceSha256": sha256(workspace), "scenario": scenario, "actions": actions,
                        "finalState": state, "screenshots": [
                            {"file": p.name, "sha256": sha256(p)} for p in sorted(output.glob("*.png"))],
                        "limitations": ["Wine/Windows not executed by this local scenario", "custom portable workspace v1 is not legacy .kva", "one line measurement only"]}
            (output / "manifest.v1.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
            print(json.dumps(manifest, indent=2))
            return 0
        finally:
            if window and process.poll() is None:
                try:
                    gui.oracle.send_key("Alt_L+F4", window["id"])
                except Exception:
                    pass
            if process.poll() is None:
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.terminate()
                    process.wait(timeout=5)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"native analysis round-trip failed: {error}", file=sys.stderr)
        raise SystemExit(1)
