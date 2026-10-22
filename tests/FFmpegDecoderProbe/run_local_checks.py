#!/usr/bin/env python3
"""Run deterministic decode, repeated lifecycle, pixel, and corrupt-input checks."""

import argparse
import subprocess
import sys
from pathlib import Path


def run(command: list[str], *, expected: int = 0) -> subprocess.CompletedProcess[str]:
    result = subprocess.run(command, capture_output=True, text=True)
    if result.stdout:
        print(result.stdout, end="")
    if result.stderr:
        print(result.stderr, end="", file=sys.stderr)
    if result.returncode != expected:
        raise SystemExit(f"FAIL: command exited {result.returncode}, expected {expected}: {command}")
    return result


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output-dir", type=Path, default=Path("/tmp/kinovea-ffmpeg-fixture"))
    parser.add_argument("--library-dir", type=Path, default=Path("/usr/lib64"))
    args = parser.parse_args()
    repo = Path(__file__).resolve().parents[2]
    project = Path(__file__).resolve().parent
    dll = repo / "tests/FFmpegDecoderProbe/bin/Release/net10.0/FFmpegDecoderProbe.dll"
    if not dll.is_file():
        raise SystemExit(f"Build the probe first; missing {dll}")

    fixture_dir = args.output_dir.resolve()
    run([sys.executable, str(project / "generate_fixture.py"), "--output-dir", str(fixture_dir)])
    report = fixture_dir / "decode-report.v1.json"
    run(["dotnet", str(dll), "--input", str(fixture_dir / "lossless-4x2-5frames.nut"),
         "--library-dir", str(args.library_dir.resolve()), "--repeat-count", "3", "--output", str(report)])
    run([sys.executable, str(project / "verify_probe.py"), "--fixture",
         str(fixture_dir / "fixture-manifest.v1.json"), "--report", str(report)])

    corrupt = run(["dotnet", str(dll), "--input", str(fixture_dir / "corrupt-input.bin"),
                   "--library-dir", str(args.library_dir.resolve())], expected=1)
    if "avformat_open_input failed: Invalid data found when processing input" not in corrupt.stderr:
        raise SystemExit(f"FAIL: unexpected corrupt-input diagnostic: {corrupt.stderr}")
    print("PASS: corrupt input was rejected with a deterministic open failure")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
