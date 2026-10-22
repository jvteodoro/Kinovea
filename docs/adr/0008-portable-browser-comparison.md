# ADR 0008: Portable media browser and dual-player prototype

- **Status:** PROPOSED
- **Date:** 2026-10-22
- **Decision scope:** Experimental Avalonia desktop UI layered on the Epic 02 portable player

## Context

The legacy file browser and multi-screen comparison UI depend on WinForms and legacy player/document state. Epic 02 added an isolated .NET 10 Avalonia host with portable still/video sessions. Reusing it provides a reversible way to build the browser and comparison screen without altering legacy behavior. The comparison screen needs a common seek coordinate even when two media sources use different frame rates or video stream timebases.

## Decision

Keep the browser and comparison windows in `Kinovea.PortableDesktop`. The browser owns folder enumeration, filtering, sorting and disposable Avalonia thumbnail bitmaps. The comparison window owns two independent existing portable player sessions. A presentation-layer `DualPlayerTimeline` maps source timestamps to absolute seconds and translates common seeks back into clamped source timestamps.

Do not merge this prototype into the legacy browser, preferences, or player state. Treat browser and comparison features as experimental until direct GUI acceptance and Windows behavior are established. The shared timer does not claim frame-locked playback or legacy CommonTimeline equivalence.

## Consequences

- Existing WinForms and C++/CLI production source remains untouched.
- File browsing and common timeline mapping can be built and tested independently of the legacy app.
- Native video availability and decoder behavior remain subject to Epic 02's host FFmpeg constraints.
- End-to-end comparison, drag-and-drop, Windows, and regression-parity tests remain required before promoting status beyond `BUILD_ONLY`.
