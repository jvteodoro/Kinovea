#!/usr/bin/env python3
"""Compare the AutoGen decoder report with authored fixture expectations."""

import argparse
import base64
import hashlib
import json
from fractions import Fraction
from pathlib import Path


def sha256(data: bytes) -> str:
    return hashlib.sha256(data).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--fixture", type=Path, required=True, help="fixture-manifest.v1.json")
    parser.add_argument("--report", type=Path, required=True, help="FFmpegDecoderProbe JSON output")
    args = parser.parse_args()
    fixture = json.loads(args.fixture.read_text(encoding="utf-8"))
    report = json.loads(args.report.read_text(encoding="utf-8"))
    failures: list[str] = []
    checks = 0

    def require(condition: bool, message: str) -> None:
        nonlocal checks
        checks += 1
        if not condition:
            failures.append(message)

    require(report.get("schemaVersion") == 1, "unexpected report schemaVersion")
    require(report.get("status") == "PASS", "decoder report is not PASS")
    require(report.get("repeatedOpenCount", 0) >= 2, "repeated open/close intervals were not exercised")
    require(report.get("repeatedRunsConsistent") is True, "repeated open/close output differed")
    require(report.get("width") == fixture["width"], "video width differs")
    require(report.get("height") == fixture["height"], "video height differs")
    observed = report.get("frames", [])
    expected = fixture["frames"]
    require(report.get("frameCount") == len(observed), "report frameCount does not match frame list")
    require(len(observed) == fixture["frameCount"], f"frame count {len(observed)} != {fixture['frameCount']}")
    require(len(observed) == len(expected), "expected/observed frame list lengths differ")
    require(report.get("inputSha256") == fixture.get("encodedVideoSha256"), "decoder input hash differs from generated fixture manifest")
    require(report.get("outputPixelFormat") == "RGBA32", "output pixel format is not RGBA32")
    require(report.get("outputAlphaMode") == "Opaque", "synthetic output alpha mode is not opaque")
    repeat_count = report.get("repeatedOpenCount", 0)
    require(report.get("fullFrameCopyCount") == len(expected) * repeat_count, "full-frame output count does not match repeated frame conversions")
    require(report.get("outputPixelBytes") == fixture["width"] * fixture["height"] * 4 * len(expected) * repeat_count,
            "output pixel byte count is unexpected")
    numerator = report.get("timeBaseNumerator", 0)
    denominator = report.get("timeBaseDenominator", 0)
    require(numerator > 0 and denominator > 0, "invalid stream time base")
    if numerator <= 0 or denominator <= 0:
        return 1
    time_base = Fraction(numerator, denominator)
    for index, (got, want) in enumerate(zip(observed, expected)):
        rgba = base64.b64decode(got.get("rgbaBase64", ""), validate=True)
        expected_rgba = base64.b64decode(want["rgbaBase64"], validate=True)
        expected_time = Fraction(want["presentationTimeNumerator"], want["presentationTimeDenominator"])
        actual_time = got["timestamp"] * time_base
        require(got.get("index") == index, f"frame {index}: index mismatch")
        require(actual_time == expected_time, f"frame {index}: presentation time {actual_time} != expected {expected_time}")
        require(got.get("sourcePixelFormat") == "bgr0", f"frame {index}: source format is {got.get('sourcePixelFormat')!r}, expected 'bgr0'")
        require(got.get("rgbaSha256") == sha256(rgba), f"frame {index}: report RGBA hash does not match its byte payload")
        require(got.get("rgbaSha256") == want["rgbaSha256"], f"frame {index}: logical RGBA pixels differ")
        require(rgba == expected_rgba, f"frame {index}: full logical RGBA byte payload differs")
        require(got.get("red00") == want["pixel00"][0] and
                got.get("green00") == want["pixel00"][1] and
                got.get("blue00") == want["pixel00"][2] and
                got.get("alpha00") == 255, f"frame {index}: authored pixel (0,0) differs")

    if failures:
        for failure in failures:
            print(f"FAIL: {failure}")
        return 1
    print(f"PASS: {checks} checks; {len(observed)} frames; timestamps, dimensions, input hash, bgr0 source format, RGBA output and authored pixels match.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
