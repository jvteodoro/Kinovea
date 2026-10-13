#!/usr/bin/env python3
"""Launch the native player and verify a real Xwayland window drew a frame."""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import subprocess
import sys
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "wine-oracle"))
import oracle  # Reuse the repository's KWin/Xwayland window discovery helpers.


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def capture_native_window(window: dict, output: Path) -> dict:
    output.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(
        ["ffmpeg", "-hide_banner", "-loglevel", "error", "-f", "x11grab",
         "-window_id", window["id"], "-i", os.environ["DISPLAY"],
         "-frames:v", "1", "-y", str(output)],
        check=True,
    )
    probe = subprocess.run(["ffprobe", "-v", "error", "-select_streams", "v:0",
                            "-show_entries", "stream=width,height", "-of", "json", str(output)],
                           check=True, capture_output=True, text=True)
    stream = json.loads(probe.stdout)["streams"][0]
    stats = subprocess.run(["ffmpeg", "-hide_banner", "-i", str(output), "-vf",
                            "signalstats,metadata=print", "-frames:v", "1", "-f", "null", "-"],
                           check=True, capture_output=True, text=True).stderr
    yavg = next((float(line.split("=")[-1]) for line in stats.splitlines()
                 if "lavfi.signalstats.YAVG=" in line), None)
    if yavg is None or yavg <= 20:
        raise RuntimeError(f"window screenshot appears blank or occluded (YAVG={yavg})")
    return {"path": output.name, "sha256": sha256(output),
            "windowId": window["id"], "windowTitle": window["title"],
            "imageSize": f"{stream['width']}x{stream['height']}",
            "lumaAverage": yavg,
            "captureMethod": "ffmpeg x11grab -window_id (native player window only)"}


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--assembly", required=True, type=Path, help="built Kinovea.PortableDesktop.dll")
    parser.add_argument("--input", required=True, type=Path, help="BMP or PNG input fixture")
    parser.add_argument("--output", required=True, type=Path, help="local evidence output directory")
    parser.add_argument("--source-sha", required=True)
    args = parser.parse_args()
    assembly = args.assembly.resolve()
    image = args.input.resolve()
    output = args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    report_path = output / "gui-smoke.json"
    log_path = output / "gui-smoke.log"
    report_path.unlink(missing_ok=True)

    with log_path.open("w", encoding="utf-8") as log:
        process = subprocess.Popen(
            ["dotnet", str(assembly), "--open", str(image), "--smoke-report", str(report_path)],
            cwd=REPO, env={**os.environ, "KINOVEA_SOURCE_SHA": args.source_sha},
            stdout=log, stderr=subprocess.STDOUT,
        )
        window = None
        try:
            deadline = time.monotonic() + 30
            while time.monotonic() < deadline:
                if process.poll() is not None:
                    raise RuntimeError(f"native player exited early with status {process.returncode}")
                window = next((item for item in oracle.list_x11_windows()
                               if item["title"] == "Kinovea Native Player"
                               and item.get("pid") == str(process.pid)), None)
                if window and report_path.exists():
                    report = json.loads(report_path.read_text(encoding="utf-8"))
                    if report.get("status") == "VISIBLE_FRAME_RENDERED":
                        break
                time.sleep(0.25)
            else:
                raise RuntimeError("no visible rendered-frame report from the process-owned player window")

            report = json.loads(report_path.read_text(encoding="utf-8"))
            if report.get("inputSha256") != sha256(image):
                raise RuntimeError("player report input hash does not match the requested fixture")
            oracle.activate_window(window["id"])
            time.sleep(0.6)
            screenshot = capture_native_window(window, output / "player-open.png")
            manifest = {"schemaVersion": 1, "sourceSha": args.source_sha,
                        "processId": process.pid, "input": str(image),
                        "inputSha256": sha256(image), "playerReport": report,
                        "screenshot": screenshot,
                        "limitations": ["Window rendering is verified; screenshot pixels are not a decoder parity oracle."]}
            (output / "manifest.v1.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
            print(json.dumps(manifest, indent=2))
            return 0
        finally:
            if window and process.poll() is None:
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


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as error:
        print(f"native player smoke failed: {error}", file=sys.stderr)
        raise SystemExit(1)
