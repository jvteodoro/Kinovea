# Analysis persistence PR merge-resolution evidence v1

- Source commit: `f58b02a718195dd59f01d8e08be9c9f40851edd1`.
- PR: [#76](https://github.com/jvteodoro/Kinovea/pull/76), branch `port/analysis-roundtrip`.
- Merge base before resolution: `5d0f59f9b1b6f8297af77739d96b2cac3f9211b0`.
- Integrated target head: `cf0df8eb` (`origin/port/net10-linux`).
- Host: Gentoo Linux x86_64; .NET SDK 10.0.111, runtime 10.0.11.

## Conflict decisions

- `MainWindow.cs`: retained calibrated analysis and KVA controls and added the incoming browse, comparison, playback transport, preferences, and capture surfaces. Both toolbars are composed. The picker includes the KVA extension alongside image/video formats. Image and video smoke reports retain their respective properties.
- `Program.cs`: retained analysis-workspace and KVA-format tests, incoming capture-contract and dual-timeline targeted modes, and the existing presentation/player suite. Added `--analysis-persistence-only` so the persistence tests can run without Avalonia platform startup.
- `MIGRATION_LOG.md`: retained both the analysis persistence record and incoming Epic 03 history; corrected the stale statement that the persistence work was uncommitted.

## Executed checks

| Command/check | Result |
|---|---|
| `dotnet restore src/Kinovea.PortableDesktop/Kinovea.PortableDesktop.csproj -v:minimal` | PASS |
| `dotnet build src/Kinovea.PortableDesktop/Kinovea.PortableDesktop.csproj -c Release --no-restore` | PASS; zero warnings/errors |
| `dotnet run --project tests/Kinovea.PortableDesktop.Tests/Kinovea.PortableDesktop.Tests.csproj -c Release -- --analysis-persistence-only` | PASS; 33 assertions |
| Same project with `--capture-contract-only` | PASS; 52 assertions |
| Same project with `--timeline-only` | PASS; 12 assertions |
| `dotnet run --project tests/PortableVideo.Tests/PortableVideo.Tests.csproj -c Release` | PASS; 149 assertions |
| `git diff --check` | PASS |
| Full `Kinovea.PortableDesktop.Tests` run, limited to 40 seconds | NOT_COMPLETED; timed out before output during Avalonia platform startup |
| `xset q` with `DISPLAY=:0` | FAIL; X server unavailable in this execution context |

The focused tests are useful verification of persistence, capture contracts, timeline, and portable video, but they do not replace the full Avalonia presentation/player suite. No Windows/Wine run or hosted CI run was performed.

## Remaining limitation

The full desktop presentation/player suite and actual GUI smoke scenarios should be rerun in a session with a reachable X server. The application build and the headless suites establish that the merged code compiles and the listed targeted contracts pass; they do not establish GUI runtime behavior after this merge.
