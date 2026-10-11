# Approved Bitmap characterization baselines

No Windows baseline is approved in this checkout.

Workflow runs generate candidate fixtures and observations only under `artifacts/bitmap-characterization/`. They must never copy files into this directory or rewrite an existing approved snapshot.

To approve a baseline, review a passing artifact from a specific source commit, verify source and fixture hashes and runner metadata, repeat on a clean `windows-2022` run, and investigate any differences. Add the reviewed, immutable snapshot in a separate pull request under `approved/<baseline-id>/`, including an approval manifest with workflow run ID, source SHA, runner image, fixture hashes, production/harness source hashes, output pixel hashes, reviewer identity and review date. Preserve prior baselines. Expected-output changes require a separate reviewed commit; test execution must not regenerate or accept them automatically.

Until that review and commit exists, the test report status is `CANDIDATE_NOT_APPROVED`, and reader behavior remains `UNVERIFIED`.
