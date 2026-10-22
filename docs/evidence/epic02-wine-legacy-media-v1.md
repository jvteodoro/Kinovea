# Epic 02 Wine legacy-media evidence v1

Status: local Wine GUI observations only; no Windows parity claim.

## Executed runs

| Input | Fixture SHA-256 | Result | Visible header observation | Artifact |
|---|---|---|---|---|
| H.264 MP4, 1280x720/30 fps | `04761f49477fe2662f93666f67b728c0434cc067cca7ba02cc4df6568298431d` | `COMPLETED` | `reference`, 1280x720 px, 30.00 fps; open/play/pause captured. | `/tmp/kinovea-epic02-legacy-mp4/result.json` |
| Synthetic H.264 MP4 VFR, 64x48 | `7a589f221e909cdc2a8e47689c09aabc11e87160001eac037103def8b24ab4eb` | `COMPLETED` | Open/play/pause actions and three player-window captures recorded; human visual review remains required. | `/tmp/kinovea-epic02-legacy-vfr/result.json`, SHA-256 `6ce0f1c15bcb8ed2d4ecbe21a61d103ebe235cf6272205267959ae26a8143b9d` |
| Synthetic animated GIF, 64x48/5 fps | `32aa180b25b8c25dd6d9acccd77ca2e538b1bde7c97ad7e3af3556c42cd6a934` | `COMPLETED` | `legacy-gif`, 64x48 px, 5.00 fps, visible 1.00-second timeline. | `/tmp/kinovea-epic02-legacy-gif-valid/result.json` |
| Synthetic SVG, 64x48 | `00220bbb5858c95e283d764629c1f07b3e96af2963f28e493d967ce430e3cec5` | `COMPLETED` | `legacy-svg`, 64x48 px, 25.00 fps; red/green source geometry visibly rasterized. | `/tmp/kinovea-epic02-legacy-svg/result.json` |

## Environment

Wine Staging 11.19; installed Kinovea 2025.2.0.0; executable SHA-256 `22d95a127257e59bd435eaa2689dd7fda2cae802829e801aa5cd367ee7a12534`; win64 prefix with .NET Framework 4.8. Each run used the oracle's disposable copy-on-write prefix overlay.

## Limits

These screenshots establish only Wine-observed GUI presentation and metadata. They do not establish decoded pixels, frame timestamps, seek tolerance, native FFmpeg ownership, or equivalence with the checked-out 2026.1 source. The Windows C# cache evidence remains separately recorded in `production-video-cache-lifetimes-v1.md`; it directly executed Cache and PreBuffer2 but not the C++/CLI FFmpeg reader.
