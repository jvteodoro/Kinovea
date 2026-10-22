# ADR 0008: Portable desktop state and capture boundary

Status: Accepted for the opt-in Avalonia prototype
Date: 2026-10-22
Base: `origin/port/epic02-video-playback`, fast-forwarded to `origin/port/net10-linux` at `5d0f59f9b1b6f8297af77739d96b2cac3f9211b0` (includes the Epic 02 portable video/player work).

## Context

The existing Kinovea application stores preferences through legacy services that include Registry and Windows integration. Its capture screen directly depends on `ICaptureSource`, `Bitmap`, WinForms, the legacy Pipeline, recorder, camera manager, and native device adapters. Epic 03 needs an Avalonia application boundary for state, menus, preferences, and single/dual capture views while Epic 06's common acquisition pipeline remains a separate dependency.

## Decision

- Store only the opt-in desktop's current workspace name, language, theme, last-open path/directory, window dimensions, open shortcut, and synthetic capture interval in an atomically replaced JSON file under `LocalApplicationData/Kinovea`.
- Keep persistence in the portable presentation assembly with an injectable file path for tests. The native shell does not call `Microsoft.Win32.Registry` or legacy `PreferencesManager`.
- Define `IPortableCaptureSource` and `IPortableCaptureSourceProvider` independently of camera packages, WinForms, GDI+, and `Kinovea.Pipeline`. Frames own their `PixelBuffer` until the presentation adapter copies them into Avalonia-owned storage.
- Provide deterministic synthetic sources for single and dual UI flows. Production camera discovery, camera settings, recording, and integration with Epic 06's acquisition pipeline require separate work.
- Keep Avalonia views in new menu, preferences, and capture classes; retain only small wiring changes in the existing prototype `MainWindow`. Do not change the legacy WinForms application.

## Consequences

State restart persistence and source lifecycle can be tested on Linux without a physical camera. The current workspace metadata does not migrate the complete legacy workspace format, and the synthetic provider does not prove camera acquisition, recording, or hardware teardown behavior. English and `pt-BR` strings cover the new menu, preferences, and capture windows; this does not translate all existing player labels or port the legacy resource catalog.

The status remains a portable prototype boundary. No GUI interaction or hardware capture parity is claimed when the host has no accessible X display or camera device.
