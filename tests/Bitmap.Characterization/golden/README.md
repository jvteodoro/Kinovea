# Bitmap characterization snapshots

No reviewer-approved Windows baseline exists in this checkout. The repeatable Windows run is preserved as a **candidate** at [`candidate/windows-2022-net48-0a9e8a5f/`](candidate/windows-2022-net48-0a9e8a5f/). It contains synthetic BMP, PNG and JPEG fixture inputs, actual legacy-reader pixel evidence, manifests, runtime metadata and the test report.

The candidate comes from source SHA `0a9e8a5f847b77e030355dd2f38a7de97a4a2572`, workflow runs `38175088092` and `38175219336`. Both runs passed 19 cases and 537 assertions; all 72 fixture and pixel files matched byte-for-byte. The committed files are from the first run. See its README and `docs/evidence/2026-10-11-bitmap-characterization-infrastructure.md` for provenance and limits.

The workflow is manual-dispatch only. New runs write to `artifacts/bitmap-characterization/`; they never copy files here or rewrite a snapshot.

## Approval process

To approve a baseline, review the candidate from its specific source commit, verify source and fixture hashes and runner metadata, repeat on a clean `windows-2022` run, and investigate any differences. Add the reviewed, immutable snapshot in a separate reviewed change under `approved/<baseline-id>/`, including an approval manifest with workflow run ID, source SHA, runner image, fixture hashes, production/harness source hashes, output pixel hashes, reviewer identity and review date. Preserve prior baselines. Expected-output changes require a separate reviewed change; test execution must not regenerate or accept them automatically.

The candidate report status is `CANDIDATE_NOT_APPROVED`. Its measured assertions are Windows characterization observations; untested behavior remains `UNVERIFIED`, and it does not establish migration parity or `REGRESSION_VERIFIED` status.
