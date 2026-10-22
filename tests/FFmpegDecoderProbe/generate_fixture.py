#!/usr/bin/env python3
"""Create a tiny lossless FFV1/NUT clip and independent logical RGBA expectations."""

import argparse
import base64
import hashlib
import json
import shutil
import subprocess
from pathlib import Path

WIDTH = 4
HEIGHT = 2
FPS = 25
FRAME_COUNT = 5


def pixel(frame: int, x: int, y: int) -> tuple[int, int, int]:
    return ((frame * 37 + x * 53 + y * 17) & 255,
            (frame * 71 + x * 29 + y * 43 + 1) & 255,
            (frame * 19 + x * 61 + y * 23 + 2) & 255)


def sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--ffmpeg", default="ffmpeg")
    args = parser.parse_args()
    ffmpeg = shutil.which(args.ffmpeg)
    if ffmpeg is None:
        raise SystemExit(f"ffmpeg executable not found: {args.ffmpeg}")
    output = args.output_dir.resolve()
    output.mkdir(parents=True, exist_ok=True)
    raw_path = output / "source.bgr0"
    clip_path = output / "lossless-4x2-5frames.nut"
    corrupt_path = output / "corrupt-input.bin"

    logical_frames: list[dict[str, object]] = []
    raw = bytearray()
    for frame_index in range(FRAME_COUNT):
        rgba = bytearray()
        for y in range(HEIGHT):
            for x in range(WIDTH):
                red, green, blue = pixel(frame_index, x, y)
                raw.extend((blue, green, red, 0))
                rgba.extend((red, green, blue, 255))
        logical_frames.append({
            "index": frame_index,
            "presentationTimeNumerator": frame_index,
            "presentationTimeDenominator": FPS,
            "rgbaSha256": hashlib.sha256(rgba).hexdigest(),
            "rgbaBase64": base64.b64encode(rgba).decode("ascii"),
            "pixel00": list(rgba[0:4]),
        })
    raw_path.write_bytes(raw)
    corrupt_path.write_bytes(b"Kinovea corrupt video fixture v1\n")

    command = [
        ffmpeg, "-hide_banner", "-loglevel", "error", "-y",
        "-f", "rawvideo", "-pixel_format", "bgr0",
        "-video_size", f"{WIDTH}x{HEIGHT}", "-framerate", str(FPS),
        "-i", str(raw_path), "-an", "-frames:v", str(FRAME_COUNT),
        "-map_metadata", "-1", "-c:v", "ffv1", "-level", "3",
        "-pix_fmt", "bgr0", "-fps_mode", "passthrough", "-f", "nut", str(clip_path),
    ]
    completed = subprocess.run(command, check=False, capture_output=True, text=True)
    if completed.returncode:
        raise SystemExit(f"ffmpeg fixture generation failed ({completed.returncode}):\n{completed.stderr}")

    manifest = {
        "schemaVersion": 1,
        "fixtureId": "synthetic-ffv1-bgr0-4x2-5frames-v1",
        "width": WIDTH,
        "height": HEIGHT,
        "frameRateNumerator": FPS,
        "frameRateDenominator": 1,
        "timeBaseExpectedSecondsDenominator": FPS,
        "frameCount": FRAME_COUNT,
        "sourceRawBgr0Sha256": sha256(raw_path),
        "encodedVideoSha256": sha256(clip_path),
        "corruptInputSha256": sha256(corrupt_path),
        "encoderCommandTemplate": [
            "ffmpeg", "-hide_banner", "-loglevel", "error", "-y",
            "-f", "rawvideo", "-pixel_format", "bgr0", "-video_size", "4x2",
            "-framerate", "25", "-i", "source.bgr0", "-an", "-frames:v", "5",
            "-map_metadata", "-1", "-c:v", "ffv1", "-level", "3", "-pix_fmt", "bgr0",
            "-fps_mode", "passthrough", "-f", "nut", "lossless-4x2-5frames.nut",
        ],
        "frames": logical_frames,
    }
    manifest_path = output / "fixture-manifest.v1.json"
    manifest_path.write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({"outputDir": str(output), "manifest": str(manifest_path), "videoSha256": manifest["encodedVideoSha256"]}, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
