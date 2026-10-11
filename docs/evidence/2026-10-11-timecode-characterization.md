# Verification: timecode source characterization

Commit: baseline `815c5579ddc2686914d29546e4bf606e84f7e353`; task changes are uncommitted at evidence capture.
Host: Gentoo Linux 2.18, Linux `6.18.50-gentoo-dist-bin`, x86_64.
SDK/runtime: .NET SDK `10.0.111`, MSBuild `18.0.11`, .NET runtime `10.0.11`.
Native libraries: none used.
Fixtures: none; assertions use deterministic scalar inputs.
Legacy oracle: unavailable. The original `Kinovea.Tests` project targets .NET Framework 4.8 and references the broader UI/media graph. Windows legacy run: `NOT_RUN`.

## Harness

`tests/Timecode.Characterization/Timecode.Characterization.csproj` targets `net10.0` without external packages and links the repository's exact source files: `Kinovea.Services/TimeHelper.cs`, `Kinovea.Services/Types/TimeCodeFormat.cs`, `Kinovea.Services/Types/TimeCodeType.cs`, and `Kinovea.Services/Infrastructure/TimestampMapper.cs`. It does not copy or rewrite formatter logic.

## Commands and results

- `dotnet run --project tests/Timecode.Characterization/Timecode.Characterization.csproj --configuration Release` => exit `0`; `PASS: 50 timecode characterization assertions`.
- `git diff --check` => exit `0`.

Coverage includes each declared `TimecodeFormat` branch and type mapping, symbol variants, frame-rate precision, fr-FR culture behavior, zero/negative/large values, midpoint rounding, centisecond carry, normalized endpoints and zero-input boundaries, timestamp conversion at 32-bit and 64-bit boundaries, and the identity timestamp mapper.

## Findings and limits

- Classic time strings use a literal `.` in the source format strings under fr-FR, while normalized numeric output follows the current culture's decimal separator.
- Precision is `ceil(log10(framerate))`; for rates through 100 fps the precision branch emits centiseconds, at 1000 fps it emits three fractional digits, and at 10000 fps it emits four.
- For precision above two, fractional digits are truncated from the millisecond remainder; unlike the centisecond branch, this does not carry into seconds.
- Timestamp display narrows `long` to `int`, including wraparound at the signed 32-bit boundary on this runtime.
- A normalized value with zero duration formats as `Infinity` on this runtime; zero total frames causes `ArgumentOutOfRangeException` while constructing the format string.

These are observations of the linked source running under .NET 10 on Linux, not a legacy Windows comparison. Linux test status is `TESTED` for this harness. Windows and legacy parity remain `NOT_RUN` / `UNVERIFIED`; no component is marked ported or regression-verified.
