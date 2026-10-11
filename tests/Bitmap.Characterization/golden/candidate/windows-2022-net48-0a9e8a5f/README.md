# Candidate Windows Bitmap characterization snapshot

Status: `CANDIDATE_NOT_APPROVED`.

This immutable candidate was downloaded from workflow run `38175088092` (job `114575682028`, artifact `11715422599`) at source SHA `0a9e8a5f847b77e030355dd2f38a7de97a4a2572`. Its artifact ZIP SHA-256 was `27bc62de97ef0ce075954eddebbc8f904a56d175a1be562125ec728d520a08ef`.

A clean repeat at the same source SHA, run `38175219336` (job `114576081958`, artifact `11715184920`), also passed 19 cases and 537 assertions. The 72 files under `fixtures/` and `pixels/` matched byte-for-byte. The repeat ZIP SHA-256 differed because the environment metadata records per-run timestamps and machine names.

The directory contains 13 generated BMP, PNG and JPEG inputs, 59 binary pixel evidence files, JSON manifests and environment metadata, and text/JSON test results. `test-results.json` records the assertions and case outcomes. `SHA256SUMS.txt` records the SHA-256 of every file in this snapshot except itself.

These are actual observations from the legacy `Kinovea.Video.Bitmap.VideoReaderBitmap` on Windows/.NET Framework 4.8. They are not an approved expected-output baseline. Do not regenerate or replace this directory during tests. Follow the review process in `../README.md` before adding a separately reviewed immutable copy under `approved/<baseline-id>/`.
