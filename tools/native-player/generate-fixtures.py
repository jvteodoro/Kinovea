#!/usr/bin/env python3
"""Generate a synthetic, interleaved-BGR 24-bit BMP for the native player."""

from __future__ import annotations

import hashlib
import json
import struct
import sys
from pathlib import Path


def color(x: int, y: int, width: int, height: int) -> tuple[int, int, int]:
    palette = ((235, 40, 35), (40, 205, 65), (245, 220, 25),
               (30, 55, 220), (220, 35, 190), (25, 210, 215))
    band = min(5, (x * len(palette)) // width)
    diagonal = abs(y - (height // 8 + x * 3 // 8)) <= max(2, width // 160)
    if diagonal:
        return 250, 250, 250
    if x > width * 3 // 4 and y > height * 2 // 3:
        cell = max(8, min(width, height) // 24)
        return (245, 245, 245) if (x // cell + y // cell) % 2 else (25, 25, 25)
    return palette[band]


def write_bmp(path: Path, width: int = 640, height: int = 480) -> None:
    row_bytes = width * 3
    padding = (-row_bytes) % 4
    data = bytearray()
    for y in range(height - 1, -1, -1):
        for x in range(width):
            red, green, blue = color(x, y, width, height)
            data.extend((blue, green, red))
        data.extend(b"\0" * padding)
    header = struct.pack("<2sIHHI", b"BM", 54 + len(data), 0, 0, 54)
    dib = struct.pack("<IIIHHIIIIII", 40, width, height, 1, 24, 0, len(data),
                      2835, 2835, 0, 0)
    path.write_bytes(header + dib + data)


def main() -> int:
    if len(sys.argv) != 2:
        print(f"usage: {Path(sys.argv[0]).name} OUTPUT_DIRECTORY", file=sys.stderr)
        return 2
    output = Path(sys.argv[1]).resolve()
    output.mkdir(parents=True, exist_ok=True)
    path = output / "native-player-rgb-640x480.bmp"
    write_bmp(path)
    digest = hashlib.sha256(path.read_bytes()).hexdigest()
    manifest = {"schemaVersion": 1, "fixtures": [{
        "filename": path.name, "format": "BMP 24-bit BGR storage, RGB logical pixels",
        "width": 640, "height": 480, "sha256": digest,
        "provenance": "Generated from an authored six-color palette and asymmetric diagonal; no third-party content."
    }]}
    (output / "native-player-fixtures.v1.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(manifest, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
