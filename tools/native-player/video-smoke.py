#!/usr/bin/env python3
"""Run a local Xwayland smoke scenario for the portable FFmpeg video path."""

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

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "wine-oracle"))
import oracle
import xwayland_capture

gui_spec = importlib.util.spec_from_file_location("native_gui", Path(__file__).with_name("gui-interactions.py"))
gui = importlib.util.module_from_spec(gui_spec)
assert gui_spec.loader is not None
gui_spec.loader.exec_module(gui)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def report(path: Path) -> dict:
    return json.loads(path.read_text(encoding="utf-8"))


def wait_for(path: Path, predicate, description: str, timeout: float = 10) -> dict:
    deadline = time.monotonic() + timeout
    last = {}
    while time.monotonic() < deadline:
        try:
            last = report(path)
        except (OSError, json.JSONDecodeError):
            time.sleep(0.05)
            continue
        if predicate(last):
            return last
        time.sleep(0.05)
    raise RuntimeError(f"timed out waiting for {description}; last report: {last}")


def click_control(window: dict, state: dict, control_name: str) -> dict:
    bounds = state.get(control_name)
    if not bounds:
        raise RuntimeError(f"player did not report {control_name} bounds")
    scale = float(state["renderScaling"])
    x = round((float(bounds["x"]) + float(bounds["width"]) / 2) * scale)
    y = round((float(bounds["y"]) + float(bounds["height"]) / 2) * scale)
    return gui.point_in_window(window["id"], x, y, [1])


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--assembly", required=True, type=Path)
    parser.add_argument("--input", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--source-sha", required=True)
    args = parser.parse_args()
    xwayland_capture.verify_source_sha(REPO, args.source_sha)
    assembly = args.assembly.resolve()
    fixture = args.input.resolve()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    report_path = output / "video-player-report.json"
    report_path.unlink(missing_ok=True)
    log_path = output / "video-player.log"
    actions = []
    process = None
    window = None
    try:
        with log_path.open("w", encoding="utf-8") as log:
            process = subprocess.Popen(
                ["dotnet", str(assembly), "--open-video", str(fixture), "--smoke-report", str(report_path)],
                cwd=REPO,
                env={**os.environ, "KINOVEA_SOURCE_SHA": args.source_sha},
                stdout=log,
                stderr=subprocess.STDOUT,
            )
            deadline = time.monotonic() + 30
            while time.monotonic() < deadline:
                if process.poll() is not None:
                    raise RuntimeError(f"player exited with {process.returncode}")
                window = next((item for item in oracle.list_x11_windows()
                               if item.get("title") == "Kinovea Native Player"
                               and item.get("pid") == str(process.pid)), None)
                if window and report_path.exists():
                    current = report(report_path)
                    if current.get("status") == "VISIBLE_FRAME_RENDERED":
                        break
                time.sleep(0.25)
            else:
                raise RuntimeError("video did not produce a visible rendered-frame report")

            initial = report(report_path)
            if initial.get("mediaType") != "video" or initial.get("codec") != "h264":
                raise RuntimeError(f"unexpected player report: {initial}")
            if initial.get("inputSha256") != sha256(fixture):
                raise RuntimeError("player input hash does not match fixture")
            if initial.get("playbackRate") != 1 or initial.get("loopPlayback") is not True:
                raise RuntimeError(f"legacy-default speed/loop state mismatch: {initial}")
            oracle.activate_window(window["id"])
            time.sleep(0.4)
            opened_image = output / "video-open.png"
            opened_capture = capture(window, opened_image)
            actions.append({"action": "open-visible-video", "result": "PASS", "timestamp": initial.get("timestamp")})

            oracle.send_key("Up", window["id"])
            faster = wait_for(report_path, lambda item: item.get("playbackRate") == 2.0, "legacy large speed increase")
            oracle.send_key("Shift_L+Down", window["id"])
            finer = wait_for(report_path, lambda item: item.get("playbackRate") == 1.9, "legacy fine speed decrease")
            oracle.send_key("Shift_L+Up", window["id"])
            restored = wait_for(report_path, lambda item: item.get("playbackRate") == 2.0, "legacy fine speed increase")
            oracle.send_key("Down", window["id"])
            normal = wait_for(report_path, lambda item: item.get("playbackRate") == 1.0, "legacy large speed decrease")
            actions.extend([
                {"action": "speed-Up", "result": "PASS", "rate": faster.get("playbackRate")},
                {"action": "speed-Shift+Down", "result": "PASS", "rate": finer.get("playbackRate")},
                {"action": "speed-Shift+Up", "result": "PASS", "rate": restored.get("playbackRate")},
                {"action": "speed-Down", "result": "PASS", "rate": normal.get("playbackRate")},
            ])

            click_control(window, initial, "loopButton")
            loop_off = wait_for(report_path, lambda item: item.get("loopPlayback") is False, "loop disabled")
            click_control(window, loop_off, "loopButton")
            loop_on = wait_for(report_path, lambda item: item.get("loopPlayback") is True, "loop enabled")
            actions.extend([
                {"action": "toggle-loop-off", "result": "PASS", "enabled": loop_off.get("loopPlayback")},
                {"action": "toggle-loop-on", "result": "PASS", "enabled": loop_on.get("loopPlayback")},
            ])

            oracle.send_key("space", window["id"])
            playing = wait_for(report_path, lambda item: item.get("isPlaying") is True, "play state")
            actions.append({"action": "play", "result": "PASS", "timestamp": playing.get("timestamp")})
            first = playing.get("timestamp", initial.get("timestamp", 0))
            advanced = wait_for(report_path, lambda item: item.get("timestamp", first) > first, "playback advance")
            oracle.send_key("space", window["id"])
            paused = wait_for(report_path, lambda item: item.get("isPlaying") is False, "pause state")
            actions.append({"action": "pause", "result": "PASS", "timestamp": paused.get("timestamp")})

            oracle.send_key("space", window["id"])
            resumed = wait_for(report_path, lambda item: item.get("isPlaying") is True, "resume state")
            resume_start = resumed.get("timestamp", paused.get("timestamp"))
            resumed_advance = wait_for(report_path, lambda item: item.get("timestamp", resume_start) > resume_start,
                                       "playback advance after resume")
            oracle.send_key("space", window["id"])
            paused_after_resume = wait_for(report_path, lambda item: item.get("isPlaying") is False, "pause after resume")
            actions.append({"action": "resume", "result": "PASS", "timestamp": resumed.get("timestamp")})
            actions.append({"action": "resume-advance", "result": "PASS", "from": resume_start,
                            "to": resumed_advance.get("timestamp")})
            actions.append({"action": "pause-after-resume", "result": "PASS",
                            "timestamp": paused_after_resume.get("timestamp")})

            oracle.send_key("Right", window["id"])
            stepped = wait_for(report_path, lambda item: item.get("timestamp", paused_after_resume.get("timestamp")) > paused_after_resume.get("timestamp"), "frame step")
            actions.append({"action": "step-forward", "result": "PASS", "from": paused_after_resume.get("timestamp"), "to": stepped.get("timestamp")})
            stepped_image = output / "video-stepped.png"
            stepped_capture = capture(window, stepped_image)

            before_loop = stepped.get("timestamp", 0)
            oracle.send_key("space", window["id"])
            wait_for(report_path, lambda item: item.get("isPlaying") is True, "loop playback start")
            wrapped = wait_for(report_path, lambda item: item.get("timestamp", before_loop) < before_loop,
                               "loop playback wraps to timeline start", timeout=10)
            actions.append({"action": "loop-wrap", "result": "PASS", "from": before_loop,
                            "to": wrapped.get("timestamp")})
            oracle.send_key("space", window["id"])
            wait_for(report_path, lambda item: item.get("isPlaying") is False, "pause after loop")
            click_control(window, loop_on, "loopButton")
            loop_off = wait_for(report_path, lambda item: item.get("loopPlayback") is False, "loop disabled for EOF check")
            oracle.send_key("Home", window["id"])
            at_start = wait_for(report_path, lambda item: item.get("timestamp") == item.get("workingZoneStart"), "Home seeks to timeline start")
            oracle.send_key("Page_Down", window["id"])
            large_jump = wait_for(report_path, lambda item: item.get("timestamp", at_start["timestamp"]) > at_start["timestamp"], "Page Down timeline jump")
            oracle.send_key("Shift_L+Page_Up", window["id"])
            small_jump = wait_for(report_path, lambda item: item.get("timestamp") == item.get("workingZoneStart"), "Shift+Page Up small jump")
            oracle.send_key("space", window["id"])
            wait_for(report_path, lambda item: item.get("isPlaying") is True, "nonloop playback start")
            stopped_at_eof = wait_for(report_path, lambda item: item.get("isPlaying") is False, "nonloop playback stops at EOF", timeout=10)
            actions.extend([
                {"action": "loop-off-confirmed", "result": "PASS", "enabled": loop_off.get("loopPlayback")},
                {"action": "Home", "result": "PASS", "timestamp": at_start.get("timestamp")},
                {"action": "PageDown-large-jump", "result": "PASS", "timestamp": large_jump.get("timestamp")},
                {"action": "Shift+PageUp-small-jump", "result": "PASS", "timestamp": small_jump.get("timestamp")},
                {"action": "nonloop-EOF", "result": "PASS", "timestamp": stopped_at_eof.get("timestamp"),
                 "isPlaying": stopped_at_eof.get("isPlaying")},
            ])

            oracle.send_key("Control_L+w", window["id"])
            closed = wait_for(report_path, lambda item: item.get("readerState") == "Empty", "reader close")
            actions.append({"action": "close-current", "result": "PASS", "state": closed.get("readerState")})

            manifest = {
                "schemaVersion": 1,
                "sourceSha": args.source_sha,
                "processId": process.pid,
                "fixture": {"path": fixture.name, "sha256": sha256(fixture)},
                "initialReport": initial,
                "finalReport": closed,
                "actions": actions,
                "screenshots": [
                    {"path": opened_image.name, "sha256": sha256(opened_image), **opened_capture},
                    {"path": stepped_image.name, "sha256": sha256(stepped_image), **stepped_capture},
                ],
                "nativeFfmpegVersions": initial.get("nativeFfmpegVersions"),
                "limitations": ["Wine and Windows parity were not tested by this local scenario.",
                                "The synthetic H.264 fixture does not characterize color range, VFR, rotation, or audio."],
            }
            (output / "manifest.v1.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
            print(json.dumps(manifest, indent=2))
            return 0
    finally:
        if process is not None and process.poll() is None:
            if window is not None:
                try:
                    oracle.send_key("Alt_L+F4", window["id"])
                except Exception:
                    pass
            try:
                process.wait(timeout=5)
            except subprocess.TimeoutExpired:
                process.terminate()
                try:
                    process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait()


def capture(window: dict, output: Path) -> dict:
    return xwayland_capture.capture(window, output)


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"portable video GUI smoke failed: {error}", file=sys.stderr)
        raise SystemExit(1)
