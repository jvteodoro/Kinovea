import importlib.util
import json
from pathlib import Path
import shutil
import tempfile
import unittest

SCRIPT = Path(__file__).resolve().parents[3] / "scripts/bitmap-characterization/compare-baseline.py"
spec = importlib.util.spec_from_file_location("compare_baseline", SCRIPT)
compare_baseline = importlib.util.module_from_spec(spec)
spec.loader.exec_module(compare_baseline)


class BaselineComparisonTests(unittest.TestCase):
    def setUp(self):
        self.baseline = SCRIPT.parents[2] / "tests/Bitmap.Characterization/golden/candidate/windows-2022-net48-0a9e8a5f"
        self.manifest = compare_baseline.read_json(self.baseline / "baseline-manifest.v1.json")
        self.manifest["_manifestPath"] = str(self.baseline / "baseline-manifest.v1.json")
        self.temp = tempfile.TemporaryDirectory(prefix="bitmap-baseline-test-")
        self.output = Path(self.temp.name) / "actual"
        self.output.mkdir()
        for directory in ("fixtures", "pixels"):
            shutil.copytree(self.baseline / directory, self.output / directory)
        for filename in ("fixture-manifest.json", "test-results.json"):
            shutil.copy2(self.baseline / filename, self.output / filename)

    def tearDown(self):
        self.temp.cleanup()

    def test_audit_validates_pending_baseline_package_and_provenance(self):
        manifest, errors = compare_baseline.audit(self.baseline / "baseline-manifest.v1.json")
        self.assertEqual([], errors)
        self.assertEqual("PENDING_HUMAN_APPROVAL", manifest["status"])

    def test_exact_evidence_matches_all_approved_output_hashes(self):
        mismatches, actual = compare_baseline.compare_run(self.manifest, self.output)
        self.assertEqual([], mismatches)
        self.assertEqual(72, len(actual))

    def test_reports_missing_extra_and_changed_outputs_together(self):
        (self.output / "pixels/bmp-rgb-7x5.rgba").write_bytes(b"changed")
        (self.output / "pixels/png-alpha-13x9.rgba").unlink()
        (self.output / "pixels/unexpected.rgba").write_bytes(b"extra")
        mismatches, _ = compare_baseline.compare_run(self.manifest, self.output)
        self.assertTrue(any("SHA-256 mismatch: pixels/bmp-rgb-7x5.rgba" in item for item in mismatches))
        self.assertIn("missing output: pixels/png-alpha-13x9.rgba", mismatches)
        self.assertIn("unexpected output: pixels/unexpected.rgba", mismatches)

    def test_output_guard_rejects_any_golden_destination(self):
        golden = SCRIPT.parents[2] / "tests/Bitmap.Characterization/golden"
        with self.assertRaises(ValueError):
            compare_baseline.ensure_output_is_safe(golden / "approved/bitmap-windows-net48-v1")

    def test_pending_status_is_not_approval(self):
        self.assertEqual("PENDING_HUMAN_APPROVAL", self.manifest["status"])
        self.assertIsNone(self.manifest["approval"]["reviewer"])
        self.assertIsNone(self.manifest["approval"]["approvedAtUtc"])
        self.assertFalse(compare_baseline.approval_is_complete(self.manifest))

    def test_approved_status_requires_human_review_provenance(self):
        self.manifest["status"] = "APPROVED"
        self.assertFalse(compare_baseline.approval_is_complete(self.manifest))
        self.manifest["approval"] = {
            "reviewer": "reviewer", "approvedAtUtc": "2026-10-11T00:00:00Z",
            "approvalCommit": "abc123", "reviewUrl": "https://github.com/example/pr/1",
            "independentHumanApproval": True,
        }
        self.assertTrue(compare_baseline.approval_is_complete(self.manifest))


if __name__ == "__main__":
    unittest.main()
