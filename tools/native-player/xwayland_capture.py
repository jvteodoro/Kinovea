"""Capture only a Kinovea client window, retrying transient Xwayland drawable errors."""

from __future__ import annotations

import json
import os
import subprocess
import time
from pathlib import Path


def verify_source_sha(repo: Path, source_sha: str) -> str:
    actual = subprocess.run(["git", "-C", str(repo), "rev-parse", "HEAD"],
                            capture_output=True, text=True, check=True).stdout.strip()
    if source_sha != actual:
        raise RuntimeError(f"requested source SHA {source_sha} does not match checked-out HEAD {actual}")
    dirty = subprocess.run(["git", "-C", str(repo), "status", "--porcelain", "--", "src", "tools/native-player", "tools/wine-oracle/oracle.py"],
                           capture_output=True, text=True, check=True).stdout.strip()
    if dirty:
        raise RuntimeError("GUI smoke requires committed app and scenario tooling; source files differ from HEAD")
    return actual


def capture(window: dict, output: Path, attempts: int = 8) -> dict:
    window_id = window["id"]
    display = os.environ.get("DISPLAY")
    if not display:
        raise RuntimeError("DISPLAY is required for the Xwayland window capture")
    output.parent.mkdir(parents=True, exist_ok=True)
    failures: list[str] = []
    for attempt in range(1, attempts + 1):
        probe_window = subprocess.run(["xprop", "-id", window_id, "_NET_WM_NAME"],
                                      capture_output=True, text=True)
        if probe_window.returncode:
            failures.append(f"attempt {attempt}: target window unavailable: {probe_window.stderr.strip()}")
            time.sleep(min(0.15 * attempt, 0.75))
            continue
        output.unlink(missing_ok=True)
        result = subprocess.run(
            ["ffmpeg", "-hide_banner", "-loglevel", "error", "-f", "x11grab",
             "-window_id", window_id, "-i", display, "-frames:v", "1", "-y", str(output)],
            capture_output=True, text=True, timeout=15)
        if result.returncode == 0 and output.is_file():
            probe = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "v:0",
                                    "-show_entries", "stream=width,height", "-of", "json", str(output)],
                                   capture_output=True, text=True)
            if probe.returncode == 0:
                try:
                    stream = json.loads(probe.stdout)["streams"][0]
                    width, height = int(stream["width"]), int(stream["height"])
                except (KeyError, IndexError, TypeError, ValueError, json.JSONDecodeError):
                    width = height = 0
                if width > 0 and height > 0:
                    return {"imageSize": f"{width}x{height}", "captureAttempts": attempt,
                            "captureMethod": "ffmpeg x11grab -window_id (target Kinovea window only)"}
            failures.append(f"attempt {attempt}: capture produced an unreadable image: {probe.stderr.strip()}")
        else:
            failures.append(f"attempt {attempt}: {result.stderr.strip() or result.stdout.strip() or 'ffmpeg capture failed'}")
        time.sleep(min(0.15 * attempt, 0.75))
    raise RuntimeError("Xwayland window capture failed after retries: " + " | ".join(failures))
