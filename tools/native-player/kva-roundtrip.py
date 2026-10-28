#!/usr/bin/env python3
"""Exercise KVA open/save in the real native desktop without legacy dependencies."""

from __future__ import annotations

import argparse
import hashlib
import importlib.util
import json
import os
import subprocess
import sys
import time
import xml.etree.ElementTree as ET
from xml.sax.saxutils import escape
from pathlib import Path

TOOLS = Path(__file__).resolve().parent
REPO = TOOLS.parents[1]
sys.path.insert(0, str(TOOLS))
spec = importlib.util.spec_from_file_location("analysis_roundtrip", TOOLS / "analysis-roundtrip.py")
analysis = importlib.util.module_from_spec(spec)
assert spec.loader is not None
spec.loader.exec_module(analysis)
gui = analysis.gui


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def make_fixture(path: Path, media: Path) -> None:
    media_info = media.stat()
    xml = f'''<?xml version="1.0" encoding="utf-8"?>
<KinoveaVideoAnalysis>
  <FormatVersion>2.0</FormatVersion><Producer>Kinovea.synthetic</Producer>
  <OriginalFilename>{media.stem}</OriginalFilename>
  <ByteLength>{media_info.st_size}</ByteLength><ImageSize>64;48</ImageSize>
  <AverageTimeStampsPerFrame>1</AverageTimeStampsPerFrame><FirstTimeStamp>0</FirstTimeStamp>
  <SelectionStart>0</SelectionStart><SelectionEnd>28672</SelectionEnd><TimeOrigin>0</TimeOrigin>
  <CalibrationHelp><PixelToUnit>2</PixelToUnit><LengthUnit UserUnitLength="mm">Millimeters</LengthUnit><CoordinatesOrigin>0;0</CoordinatesOrigin></CalibrationHelp>
  <Keyframes><Keyframe><Position UserTime="0:00:00:00">0</Position><Title>first</Title><Drawings>
    <Line><PositionA>4;5</PositionA><PositionB>30;5</PositionB><DrawingStyle><Color Key="line color"><Value>255;255;0;0</Value></Color></DrawingStyle></Line>
    <VendorDrawing marker="retain"><Payload>opaque-extension</Payload></VendorDrawing>
  </Drawings></Keyframe></Keyframes>
  <Tracks><Track id="track-1" name="example"><PointList><Point timestamp="0">12;15</Point></PointList></Track></Tracks>
  <Chronos><Chrono id="chrono-1" name="timer"><Position>2;2</Position></Chrono></Chronos>
  <Trackability><Point id="p1">12;15</Point></Trackability>
  <VideoFilters active="none"><Filter name="example" enabled="false"/></VideoFilters>
  <CameraMotion><Transform>identity</Transform></CameraMotion>
  <VendorExtension preserve="yes"><Nested>opaque</Nested></VendorExtension>
</KinoveaVideoAnalysis>
'''
    path.write_text(xml, encoding="utf-8")


def wait_state(path: Path, predicate, message: str, timeout: float = 25) -> dict:
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        state = gui.report(path)
        if state and predicate(state):
            return state
        time.sleep(0.1)
    raise TimeoutError(f"{message}; last report={gui.report(path)}")


def canonical(path: Path) -> str:
    return ET.canonicalize(from_file=str(path), with_comments=True)


def expected_after_relink(source: Path, media: Path, output: Path) -> None:
    root = ET.parse(source).getroot()
    fields = {node.tag: node for node in root}
    fields["OriginalFilename"].text = media.stem
    ET.SubElement(root, "FullPath").text = str(media)
    fields["ByteLength"].text = str(media.stat().st_size)
    ET.ElementTree(root).write(output, encoding="utf-8", xml_declaration=True)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--assembly", required=True, type=Path)
    parser.add_argument("--media", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--source-sha", required=True)
    args = parser.parse_args()
    assembly, media, output = args.assembly.resolve(), args.media.resolve(), args.output.resolve()
    output.mkdir(parents=True, exist_ok=True)
    source = output / "legacy-content.kva"
    saved = output / "legacy-content-saved.kva"
    expected_kva = output / "legacy-content-expected.kva"
    report = output / "player-state.json"
    log_path = output / "player.log"
    source.unlink(missing_ok=True)
    saved.unlink(missing_ok=True)
    expected_kva.unlink(missing_ok=True)
    report.unlink(missing_ok=True)
    make_fixture(source, media)
    scenario = json.loads((TOOLS / "scenarios/kva-roundtrip-v1.json").read_text(encoding="utf-8"))
    process = None
    actions = []
    with log_path.open("w", encoding="utf-8") as log:
        process = subprocess.Popen(["dotnet", str(assembly), "--open", str(source), "--smoke-report", str(report)],
                                   cwd=REPO, env={**os.environ, "KINOVEA_SOURCE_SHA": args.source_sha},
                                   stdout=log, stderr=subprocess.STDOUT)
        window = None
        try:
            end = time.monotonic() + 30
            while time.monotonic() < end:
                if process.poll() is not None:
                    raise RuntimeError(f"desktop process exited {process.returncode}")
                window = next((w for w in gui.oracle.list_x11_windows()
                               if w.get("pid") == str(process.pid) and w["title"] == "Kinovea Native Player"), None)
                if window:
                    break
                time.sleep(0.2)
            else:
                raise TimeoutError(f"native player window did not appear: {gui.report(report)}")
            analysis.choose_path(window, media)
            state = wait_state(report, lambda x: x.get("legacyKva") is not None
                               and x.get("status") == "VISIBLE_FRAME_RENDERED", "KVA relink and media open", timeout=30)
            gui.force_activate_window(window["id"])
            data = state["legacyKva"]
            expected = scenario["expected"]
            for key in ("formatVersion", "keyframeCount", "trackCount", "chronoCount"):
                if data.get(key) != expected[key]:
                    raise RuntimeError(f"legacy KVA {key} mismatch: {data.get(key)}")
            if state.get("readerState") != expected["readerState"]:
                raise RuntimeError(f"media reader is not open: {state}")
            actions.append({"operation": "open KVA in GUI and open referenced media", "result": "PASS", "document": data,
                            "readerState": state.get("readerState"), "frameTimestamp": state.get("timestamp")})
            gui.capture(window, output / "01-kva-loaded.png")
            bounds = state.get("saveLegacyKvaButton")
            if not bounds:
                raise RuntimeError("GUI did not expose Save legacy KVA control bounds")
            scale = float(state["renderScaling"])
            x = round((float(bounds["x"]) + float(bounds["width"]) / 2) * scale)
            y = round((float(bounds["y"]) + float(bounds["height"]) / 2) * scale)
            gui.point_in_window(window["id"], x, y, [1])
            analysis.choose_path(window, saved)
            state = wait_state(report, lambda x: x.get("legacyKvaSavePath") == str(saved), "KVA saved via native picker")
            if not saved.is_file():
                raise RuntimeError("save dialog completed without creating output KVA")
            expected_after_relink(source, media, expected_kva)
            if canonical(expected_kva) != canonical(saved):
                raise RuntimeError("KVA content changed beyond explicit media relink during import/save")
            if "VendorExtension" not in ET.tostring(ET.parse(saved).getroot(), encoding="unicode"):
                raise RuntimeError("unknown XML extension did not survive save")
            actions.append({"operation": "save KVA through native picker and compare XML", "result": "PASS",
                            "sourceSha256": sha256(source), "savedSha256": sha256(saved),
                            "canonicalXmlEqualAfterExplicitRelink": True, "unknownExtensionPreserved": True,
                            "relinkedMedia": str(media)})
            gui.capture(window, output / "02-kva-saved.png")
            gui.oracle.send_key("Control_L+w", window["id"])
            analysis.wait_state(report, lambda x: x.get("readerState") == "Empty", "close KVA media")
            gui.oracle.send_key("Control_L+o", window["id"])
            analysis.choose_path(window, saved)
            state = wait_state(report, lambda x: x.get("legacyKva", {}).get("formatVersion") == "2.0"
                               and x.get("readerState") == "Open", "reopen saved KVA in same process", timeout=30)
            if state["legacyKva"].get("recognizedDrawingCount") != expected["recognizedDrawingCount"]:
                raise RuntimeError(f"reopened KVA drawing inventory changed: {state['legacyKva']}")
            actions.append({"operation": "close and reopen saved KVA in same process", "result": "PASS",
                            "readerState": state.get("readerState"), "document": state.get("legacyKva")})
            gui.capture(window, output / "03-kva-reopened.png")
            manifest = {"schemaVersion": 1, "sourceSha": args.source_sha, "scenario": scenario,
                        "mediaSha256": sha256(media), "sourceKvaSha256": sha256(source),
                        "savedKvaSha256": sha256(saved), "actions": actions,
                        "screenshots": [{"file": p.name, "sha256": sha256(p)} for p in sorted(output.glob("*.png"))],
                        "limitations": scenario["limitations"]}
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
        print(f"native KVA round-trip failed: {error}", file=sys.stderr)
        raise SystemExit(1)
