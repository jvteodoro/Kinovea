# Migration plan — hypothesis-driven, incremental

## Outcome and success criteria
Deliver Kinovea capabilities on native Linux and Windows with a modern .NET target, preserving measurable behavior and supporting reuse of the analysis core. Finish is measured by executable cross-platform workflows and regression evidence—not percentage of source files converted.

## Track 0 — build forensics (first)
Inventory each `.sln`, `.csproj`, `.vcxproj`, target framework, project reference, external dependency, P/Invoke and WinForms/GDI+ reference. Record upstream revision and present baseline on Gentoo. Validate paths case-sensitively. Distinguish compile problems from runtime and parity failures. Produce matrix and reproducible commands. Preserve original solution; create a separate solution only after recording an ADR.

## Track 1 — characterization and migration seam
Find the smallest analysis/time/geometry/data contract that can be extracted without platform dependencies. Capture legacy behavior with tests/fixtures, declare observable contracts and numeric tolerances before refactoring. Ensure tests run on Linux under current SDK. Avoid defining `System.Drawing` models as public portable contracts.

## Track 2 — portable core
Port one coherent component at a time to modern .NET; minimize API changes. Use portable representations for time, measurements, coordinates, metadata and frame contracts. Document any serialization or numerical deltas. Maintain Linux tests and Windows CI verification.

## Track 3 — media backends
Design native FFmpeg strategy with clear boundary and ADR. Independently establish file probe -> decode -> seek -> timestamps -> color conversion -> playback timing -> caching. Golden fixtures must document codec, container and expected outputs. Verify corrupted/edge cases and native packaging. The existing VS C++ project does not become portable just by changing its build tool.

## Track 4 — desktop and capture
Replace WinForms presentation with a cross-platform UI only after core contracts and media behavior are testable. Keep platform camera SDKs behind adapters; mark absent hardware as not tested. End-to-end tests include open video, frame navigation, calibration, overlays, measure, export and load/save project.

## Track 5 — release/continuous verification
Linux and Windows build/test matrix, smoke tests, reproducible packaging, license audit, performance regression budget and documented feature parity. Release notes list known differences.

## Milestones
M0: inventory + baseline failure reproducibly explained.
M1: a real portable domain slice builds and passes tests on Linux.
M2: first video fixture decoded and validated end-to-end against a documented oracle.
M3: analysis workflow works headlessly with regression comparisons.
M4: GUI workflow works natively on Linux and Windows with smoke tests.
M5: release quality gate and feature matrix.

## Anti-goals
No big-bang conversion, no "error count down" as sole metric, no false equivalence from compilation, no silent removal of unsupported features, and no replacement of upstream intellectual property/algorithms without parity analysis.
