#!/usr/bin/env python3
"""Audit and compare a reviewed Bitmap characterization baseline, read-only."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import platform
import shutil
import subprocess
import sys
import tempfile
from datetime import datetime, timezone

REPO = Path(__file__).resolve().parents[2]
DEFAULT_MANIFEST = REPO / "tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f/baseline-manifest.v1.json"
HARNESS_PATHS = {
    "tests/Bitmap.Characterization/Program.cs",
    "tests/Bitmap.Characterization/FixtureFactory.cs",
    "tests/Bitmap.Characterization/EvidenceWriter.cs",
    "tests/Bitmap.Characterization/Kinovea.Bitmap.Characterization.csproj",
}
OUTPUT_PREFIXES = ("fixtures/", "pixels/")


def sha256(data):
    return hashlib.sha256(data).hexdigest()


def sha256_file(path):
    h = hashlib.sha256()
    with path.open("rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            h.update(chunk)
    return h.hexdigest()


def windows_checkout_bytes(data):
    return data.replace(b"\r\n", b"\n").replace(b"\n", b"\r\n")


def read_json(path):
    with path.open("r", encoding="utf-8-sig") as stream:
        return json.load(stream)


def audit(manifest_path):
    errors = []
    manifest_path = manifest_path.resolve()
    try:
        manifest = read_json(manifest_path)
    except Exception as exc:
        return None, [f"cannot read manifest {manifest_path}: {exc}"]
    if manifest.get("schemaVersion") != "1.0.0":
        errors.append(f"unsupported schemaVersion: {manifest.get('schemaVersion')!r}")
    if manifest.get("baselineId") != "bitmap-windows-net48-v1":
        errors.append(f"unexpected baselineId: {manifest.get('baselineId')!r}")
    root = manifest_path.parent
    manifest["_manifestPath"] = str(manifest_path)
    checksum_file = root / "SHA256SUMS.txt"
    if not checksum_file.is_file():
        errors.append("candidate SHA256SUMS.txt is missing")
    else:
        try:
            listed_checksums = {}
            for line in checksum_file.read_text(encoding="utf-8").splitlines():
                if not line.strip():
                    continue
                digest, rel = line.split(None, 1)
                rel = rel.lstrip(" *")
                if rel.startswith("./"):
                    rel = rel[2:]
                listed_checksums[rel] = digest
            actual_paths = {p.relative_to(root).as_posix() for p in root.rglob("*") if p.is_file() and p != checksum_file}
            if set(listed_checksums) != actual_paths:
                errors.append("SHA256SUMS.txt path list does not cover exactly every candidate package file")
            for rel, digest in listed_checksums.items():
                target = root / rel
                if not target.is_file() or sha256_file(target) != digest:
                    errors.append(f"candidate package checksum mismatch: {rel}")
        except Exception as exc:
            errors.append(f"cannot validate candidate SHA256SUMS.txt: {exc}")
    runs = manifest.get("runs", [])
    if len(runs) != 2:
        errors.append(f"expected two provenance runs, found {len(runs)}")
    expected = manifest.get("expectedOutputs", {})
    if len(expected) != 72:
        errors.append(f"expected 72 fixture/pixel hashes, found {len(expected)}")
    expected_fixtures = {p: h for p, h in expected.items() if p.startswith("fixtures/")}
    expected_pixels = {p: h for p, h in expected.items() if p.startswith("pixels/")}
    if len(expected_fixtures) != 14:
        errors.append(f"expected 14 fixture hashes, found {len(expected_fixtures)}")
    if len(expected_pixels) != 58:
        errors.append(f"expected 58 pixel hashes, found {len(expected_pixels)}")

    # Confirm both runs independently reported exactly the same fixture and pixel bytes.
    for index, run in enumerate(runs, 1):
        if run.get("report") != {
            "status": "PASS", "caseCount": 19, "failedCaseCount": 0,
            "assertionCount": 537, "passedAssertions": 537, "failedAssertions": 0,
        }:
            errors.append(f"run {run.get('runId')} report summary is not the audited 19/537 PASS result")
        run_outputs = run.get("outputs", {})
        if run_outputs != expected:
            errors.append(f"run {run.get('runId')} fixture/pixel hash set differs from baseline expectedOutputs")
        for prior in runs[:index - 1]:
            if run_outputs != prior.get("outputs", {}):
                errors.append(f"runs {prior.get('runId')} and {run.get('runId')} differ in fixture/pixel hashes")
        preserved = run.get("preservedFiles", {})
        # The repository stores the first run as the immutable source snapshot. The repeat run
        # is independently hash-audited in its recorded map, but its metadata is not duplicated.
        if index == 1:
            for rel, expected_hash in preserved.items():
                target = root / rel
                if not target.is_file():
                    errors.append(f"preserved artifact file is missing: {rel}")
                elif sha256_file(target) != expected_hash:
                    errors.append(f"preserved artifact hash mismatch: {rel}")
        for rel, expected_hash in run_outputs.items():
            target = root / rel
            if not target.is_file():
                errors.append(f"baseline observation is missing: {rel}")
            elif sha256_file(target) != expected_hash:
                errors.append(f"baseline observation hash mismatch: {rel}")
        for rel in run.get("diagnosticFilesOmittedFromRepository", {}):
            if rel not in {"msbuild.binlog", "msbuild.log", "nuget-restore.log"}:
                errors.append(f"unrecognized omitted diagnostic artifact: {rel}")

    fixture_manifest_path = root / "fixture-manifest.json"
    if fixture_manifest_path.is_file():
        try:
            fixture_manifest = read_json(fixture_manifest_path)
            if fixture_manifest.get("approvalStatus") != "CANDIDATE_NOT_APPROVED":
                errors.append("preserved fixture manifest must retain CANDIDATE_NOT_APPROVED")
            entries = fixture_manifest.get("fixtures", [])
            fixture_hashes = {"fixtures/" + entry["fileName"]: entry.get("sha256") for entry in entries}
            if fixture_hashes != expected_fixtures:
                errors.append("preserved fixture manifest provenance/hashes differ from expected fixture files")
            if len(entries) != 14 or any(not entry.get("generator") or not entry.get("id") for entry in entries):
                errors.append("fixture manifest does not contain 14 identified fixtures with generator provenance")
        except Exception as exc:
            errors.append(f"cannot validate preserved fixture-manifest.json: {exc}")
    else:
        errors.append("preserved fixture-manifest.json is missing")

    source = manifest.get("sourceRevision", {})
    recorded_source_list = root / "source-hashes.json"
    if recorded_source_list.is_file():
        try:
            listed = read_json(recorded_source_list)
            manifest_listed = [{"path": e["path"], "sha256": e["recordedWindowsCheckoutSha256"]} for e in source.get("files", [])]
            if listed != manifest_listed:
                errors.append("sourceRevision file list differs from preserved source-hashes.json")
        except Exception as exc:
            errors.append(f"cannot parse source-hashes.json: {exc}")
    else:
        errors.append("preserved source-hashes.json is missing")
    source_sha = source.get("gitSha")
    if not source_sha or not source.get("sourceRevisionVerified"):
        errors.append("source revision is not marked verified")
    required_build_paths = {"tests/Bitmap.Characterization/Kinovea.Bitmap.Characterization.csproj", ".github/workflows/bitmap-characterization.yml"}
    if not required_build_paths.issubset({entry.get("path") for entry in source.get("files", [])}):
        errors.append("source hashes do not pin the characterization project and workflow")
    pinned_text = {}
    for entry in source.get("files", []):
        path = entry.get("path")
        try:
            blob = subprocess.check_output(["git", "-C", str(REPO), "show", f"{source_sha}:{path}"], stderr=subprocess.PIPE)
        except subprocess.CalledProcessError:
            errors.append(f"source path missing at {source_sha}: {path}")
            continue
        pinned_text[path] = blob.decode("utf-8", errors="replace")
        blob_hash = sha256(blob)
        windows_hash = sha256(windows_checkout_bytes(blob))
        if blob_hash != entry.get("gitBlobSha256") or windows_hash != entry.get("windowsCrLfSha256"):
            errors.append(f"source revision hash mismatch: {path}")
        if entry.get("verification") not in ("MATCH_GIT_BLOB", "MATCH_WINDOWS_CRLF_CHECKOUT"):
            errors.append(f"source hash was not verified against commit: {path}")
    project_xml = pinned_text.get("tests/Bitmap.Characterization/Kinovea.Bitmap.Characterization.csproj", "")
    workflow = pinned_text.get(".github/workflows/bitmap-characterization.yml", "")
    for required in ("<TargetFrameworkVersion>v4.8</TargetFrameworkVersion>", "Release|x64", "<PlatformTarget>x64</PlatformTarget>"):
        if required not in project_xml:
            errors.append(f"pinned project configuration does not contain {required!r}")
    for required in ("runs-on: windows-2022", "msbuild tests/Bitmap.Characterization/Kinovea.Bitmap.Characterization.csproj", "/p:Configuration=Release", "/p:Platform=x64", "Kinovea.Bitmap.Characterization.exe", "if: always()"):
        if required not in workflow:
            errors.append(f"pinned workflow does not contain {required!r}")
    return manifest, errors


def ensure_output_is_safe(output):
    resolved = output.resolve()
    golden_root = (REPO / "tests/Bitmap.Characterization/golden").resolve()
    try:
        resolved.relative_to(golden_root)
    except ValueError:
        return resolved
    raise ValueError(f"output directory must be outside immutable golden data: {resolved}")


def compare_run(manifest, output, enforce_legacy_shape=True):
    mismatches = []
    expected = manifest["expectedOutputs"]
    actual = {}
    for prefix in OUTPUT_PREFIXES:
        directory = output / prefix
        if directory.exists():
            for path in sorted(directory.rglob("*")):
                if path.is_file():
                    rel = path.relative_to(output).as_posix()
                    actual[rel] = sha256_file(path)
    for rel in sorted(set(expected) - set(actual)):
        mismatches.append(f"missing output: {rel}")
    for rel in sorted(set(actual) - set(expected)):
        mismatches.append(f"unexpected output: {rel}")
    for rel in sorted(set(actual) & set(expected)):
        if actual[rel] != expected[rel]:
            mismatches.append(f"SHA-256 mismatch: {rel}: expected {expected[rel]}, got {actual[rel]}")

    fixture_manifest = output / "fixture-manifest.json"
    if not fixture_manifest.is_file():
        mismatches.append("missing fixture-manifest.json")
    else:
        try:
            fixtures = read_json(fixture_manifest).get("fixtures", [])
            reported = {"fixtures/" + f["fileName"]: f.get("sha256") for f in fixtures}
            expected_fixtures = {p: h for p, h in expected.items() if p.startswith("fixtures/")}
            if reported != expected_fixtures:
                mismatches.append("fixture-manifest entries or hashes differ from the approved fixture inputs")
        except Exception as exc:
            mismatches.append(f"cannot parse fixture-manifest.json: {exc}")

    report_path = output / "test-results.json"
    if not report_path.is_file():
        mismatches.append("missing test-results.json")
    else:
        try:
            report = read_json(report_path)
            if report.get("status") != "PASS":
                mismatches.append(f"suite status is {report.get('status')!r}, expected 'PASS'")
            if report.get("failedCaseCount") != 0 or report.get("failedAssertions") != 0:
                mismatches.append(f"suite reports failures: cases={report.get('failedCaseCount')}, assertions={report.get('failedAssertions')}")
            if enforce_legacy_shape:
                if report.get("caseCount") != 19 or report.get("passedAssertions") != 537:
                    mismatches.append(f"suite shape changed: cases={report.get('caseCount')}, passed assertions={report.get('passedAssertions')}; expected 19/537")
                expected_cases = {c["name"] for c in runs_report_cases(manifest)}
                actual_cases = {c.get("name") for c in report.get("cases", []) if c.get("status") == "PASS"}
                if expected_cases and actual_cases != expected_cases:
                    mismatches.append(f"passing case names differ: expected {sorted(expected_cases)}, got {sorted(actual_cases)}")
        except Exception as exc:
            mismatches.append(f"cannot parse test-results.json: {exc}")
    return mismatches, actual


def runs_report_cases(manifest):
    # The initial artifact report is preserved. Keeping names out of duplicated manifest
    # fields avoids maintaining a second editable list of assertion cases.
    path = Path(manifest.get("_manifestPath", ""))
    report_path = path.parent / "test-results.json" if path else None
    if not report_path or not report_path.is_file():
        return []
    try:
        return read_json(report_path).get("cases", [])
    except Exception:
        return []


def approval_is_complete(manifest):
    approval = manifest.get("approval", {})
    return bool(
        manifest.get("status") == "APPROVED"
        and approval.get("reviewer")
        and approval.get("approvedAtUtc")
        and approval.get("approvalCommit")
        and approval.get("reviewUrl")
        and approval.get("independentHumanApproval") is True
    )


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--manifest", type=Path, default=DEFAULT_MANIFEST)
    parser.add_argument("--mode", required=True, choices=("audit", "windows-legacy", "linux-net10-parity"),
                        help="audit verifies stored hashes; windows-legacy executes the .NET Framework reader; linux-net10-parity is reserved for a separate net10 harness")
    parser.add_argument("--command", nargs=argparse.REMAINDER, help="Linux parity harness command; include {output} in its arguments")
    parser.add_argument("--output", type=Path, help="fresh output directory, outside golden/")
    parser.add_argument("--report", type=Path, help="write comparison summary JSON (outside golden/)")
    args = parser.parse_args()
    manifest, errors = audit(args.manifest)
    if manifest is None or errors:
        for error in errors:
            print("AUDIT ERROR: " + error, file=sys.stderr)
        return 1
    print(f"Audit PASS: {manifest['baselineId']} v{manifest['schemaVersion']}; source {manifest['sourceRevision']['gitSha']}; 14 fixtures, 58 pixel outputs; two matching 19-case/537-assertion runs.")
    if args.mode == "audit":
        print(f"Baseline state: {manifest['status']} (no execution performed)")
        return 0
    if not approval_is_complete(manifest):
        print(f"EXECUTION REFUSED: baseline status/approval provenance is incomplete ({manifest.get('status')!r}); a human reviewer must approve it and record reviewer, timestamp, review URL and approval commit.", file=sys.stderr)
        return 2
    if args.mode == "linux-net10-parity":
        if sys.platform != "linux":
            print("PLATFORM ERROR: linux-net10-parity mode requires Linux.", file=sys.stderr)
            return 2
        if not args.command or "{output}" not in args.command:
            print("COMMAND ERROR: provide a Linux/.NET 10 characterization command containing a {output} argument placeholder.", file=sys.stderr)
            return 2
        if not any(token in " ".join(args.command).lower() for token in ("net10.0", "net10")):
            print("COMMAND ERROR: Linux parity command must explicitly identify the net10 harness.", file=sys.stderr)
            return 2
        linux_mode = True
    else:
        linux_mode = False
    if not linux_mode and os.name != "nt":
        print("PLATFORM ERROR: windows-legacy mode requires Windows/.NET Framework 4.8; use the Windows runner.", file=sys.stderr)
        return 2

    source_by_path = {f["path"]: f for f in manifest["sourceRevision"]["files"]}
    for rel in (() if linux_mode else HARNESS_PATHS):
        if rel not in source_by_path:
            print(f"MANIFEST ERROR: characterization harness path is not pinned: {rel}", file=sys.stderr)
            return 1
        path = REPO / rel
        if not path.is_file() or sha256(windows_checkout_bytes(path.read_bytes())) != source_by_path[rel]["windowsCrLfSha256"]:
            print(f"HARNESS SOURCE CHANGED: {rel}; review harness change before comparing this baseline.", file=sys.stderr)
            return 2
    try:
        default_out = REPO / "artifacts" / "bitmap-baseline-compare" / datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
        out = ensure_output_is_safe(args.output or default_out)
    except ValueError as exc:
        print(str(exc), file=sys.stderr)
        return 2
    if out.exists() and any(out.iterdir()):
        print(f"OUTPUT DIRECTORY NOT EMPTY: {out}", file=sys.stderr)
        return 2
    out.mkdir(parents=True, exist_ok=True)
    command = [token.replace("{output}", str(out)) for token in args.command] if linux_mode else None
    if linux_mode:
        completed = subprocess.run(command, cwd=REPO)
    else:
        targeting_pack = Path(os.environ.get("ProgramFiles(x86)", r"C:\Program Files (x86)")) / "Reference Assemblies/Microsoft/Framework/.NETFramework/v4.8/mscorlib.dll"
        if not targeting_pack.is_file():
            print(f"TARGETING PACK MISSING: {targeting_pack}", file=sys.stderr)
            return 2
        restore = subprocess.run(["nuget", "restore", "Kinovea.Services/packages.config", "-PackagesDirectory", "packages", "-NonInteractive", "-Verbosity", "normal"], cwd=REPO)
        if restore.returncode != 0:
            print(f"NuGet restore failed with exit code {restore.returncode}", file=sys.stderr)
            return restore.returncode or 1
        build = subprocess.run(["msbuild", "tests/Bitmap.Characterization/Kinovea.Bitmap.Characterization.csproj", "/t:Rebuild", "/m", "/nologo", "/verbosity:minimal", "/p:Configuration=Release", "/p:Platform=x64"], cwd=REPO)
        if build.returncode != 0:
            print(f"MSBuild failed with exit code {build.returncode}", file=sys.stderr)
            return build.returncode or 1
        exe = (REPO / "tests/Bitmap.Characterization/bin/x64/Release/Kinovea.Bitmap.Characterization.exe").resolve()
        if not exe.is_file():
            print(f"EXECUTABLE MISSING AFTER SUCCESSFUL BUILD: {exe}", file=sys.stderr)
            return 2
        command = [str(exe), "--output", str(out)]
        completed = subprocess.run(command, cwd=REPO)
    mismatches, actual = compare_run(manifest, out, enforce_legacy_shape=not linux_mode)
    if linux_mode:
        runtime_path = out / "runtime-metadata.json"
        try:
            runtime = read_json(runtime_path)
            if runtime.get("targetFramework") != "net10.0":
                mismatches.append("Linux parity runtime metadata must declare targetFramework=net10.0")
        except Exception as exc:
            mismatches.append(f"cannot verify Linux parity runtime metadata: {exc}")
    result = {"schemaVersion": 1, "mode": args.mode, "baselineId": manifest["baselineId"],
              "baselineStatus": manifest["status"], "baselineSourceSha": manifest["sourceRevision"]["gitSha"],
              "hostPlatform": platform.platform(), "commandExitCode": completed.returncode,
              "matchedOutputs": len(set(manifest["expectedOutputs"]) & set(actual)) - sum(1 for m in mismatches if m.startswith("SHA-256 mismatch:")),
              "expectedOutputs": len(manifest["expectedOutputs"]), "mismatchCount": len(mismatches), "mismatches": mismatches,
              "result": "PASS" if completed.returncode == 0 and not mismatches else "FAIL"}
    if args.report:
        report_path = ensure_output_is_safe(args.report)
        report_path.parent.mkdir(parents=True, exist_ok=True)
        report_path.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))
    return 0 if result["result"] == "PASS" else 1


if __name__ == "__main__":
    sys.exit(main())
