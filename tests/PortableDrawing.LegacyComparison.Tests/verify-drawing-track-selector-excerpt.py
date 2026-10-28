#!/usr/bin/env python3
"""Verify the compiled legacy selector excerpt remains byte-for-byte source-derived."""

from hashlib import sha256
from pathlib import Path
import sys

root = Path(__file__).resolve().parents[2]
legacy_path = root / "Kinovea.ScreenManager/Tracking/Tracking/DrawingTrack.cs"
excerpt_path = Path(__file__).with_name("LegacyDrawingTrackSelectorExcerpt.cs")


def method_body(text: str, signature: str) -> str:
    position = text.find(signature)
    if position < 0:
        raise ValueError(f"method signature not found: {signature}")
    opening = text.find("{", position + len(signature))
    if opening < 0:
        raise ValueError("method body opening brace not found")
    depth = 0
    for index in range(opening, len(text)):
        if text[index] == "{":
            depth += 1
        elif text[index] == "}":
            depth -= 1
            if depth == 0:
                return text[opening + 1:index].strip()
    raise ValueError("method body closing brace not found")


try:
    legacy = legacy_path.read_text(encoding="utf-8-sig")
    excerpt = excerpt_path.read_text(encoding="utf-8-sig")
    legacy_body = method_body(
        legacy,
        "private int FindClosestPoint(long currentTimestamp, List<TimedPoint> positions)",
    )
    excerpt_body = method_body(
        excerpt,
        "public static int FindClosestPoint(long currentTimestamp, List<TimedPoint> positions)",
    )
except (OSError, ValueError) as error:
    print(f"FAIL: {error}", file=sys.stderr)
    raise SystemExit(1)

if legacy_body != excerpt_body:
    print("FAIL: compiled selector excerpt differs from DrawingTrack source", file=sys.stderr)
    raise SystemExit(1)

print(f"PASS: exact DrawingTrack.FindClosestPoint body; SHA-256 {sha256(legacy_body.encode()).hexdigest()}")
