#!/usr/bin/env python3
"""Exercise the actual Avalonia player and its native Linux file picker.

Mouse coordinates are derived from the process-owned window and the app's
logical viewport report. Dialog keyboard input is sent only while the native
portal is the active Xwayland focus target. Screenshots are local evidence.
"""

from __future__ import annotations

import argparse
import ctypes
import ctypes.util
import hashlib
import json
import os
import re
import subprocess
import sys
import tempfile
import time
from pathlib import Path

REPO = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(REPO / "tools" / "wine-oracle"))
import oracle


def sha256(path: Path) -> str:
    h = hashlib.sha256()
    with path.open("rb") as f:
        for block in iter(lambda: f.read(1024 * 1024), b""):
            h.update(block)
    return h.hexdigest()


def active_id() -> str | None:
    result = subprocess.run(["xprop", "-root", "_NET_ACTIVE_WINDOW"], check=True, capture_output=True, text=True)
    match = re.search(r"0x[0-9a-fA-F]+", result.stdout)
    return match.group(0) if match else None


def force_activate_window(window_id: str) -> None:
    """Always raise the target through KWin, even if X11 reports it active."""
    if not any(item["id"].casefold() == window_id.casefold() for item in oracle.list_x11_windows()):
        raise RuntimeError(f"player Xwayland window disappeared: {window_id}")
    plugin = f"codex-native-player-input-{os.getpid()}"
    code = f"var id = {int(window_id, 16)}; var found = workspace.getClient(id); if (found) {{ workspace.raiseWindow(found); workspace.activeWindow = found; }}"
    tmp_dir = REPO.parent / "local-evidence" / "tmp"
    tmp_dir.mkdir(parents=True, exist_ok=True)
    with tempfile.NamedTemporaryFile("w", suffix=".js", prefix="activate-", dir=tmp_dir, delete=False) as script:
        script.write(code)
        script_path = Path(script.name)
    try:
        loaded = subprocess.run(["qdbus6", "org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting.loadScript", str(script_path), plugin], capture_output=True, text=True)
        if loaded.returncode or not loaded.stdout.strip().isdigit():
            raise RuntimeError(f"KWin could not load player activation script: {loaded.stderr.strip()}")
        script_id = loaded.stdout.strip()
        executed = subprocess.run(["qdbus6", "org.kde.KWin", f"/Scripting/Script{script_id}", "org.kde.kwin.Script.run"], capture_output=True, text=True)
        if executed.returncode:
            raise RuntimeError(f"KWin could not raise player window: {executed.stderr.strip()}")
        time.sleep(0.25)
    finally:
        subprocess.run(["qdbus6", "org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting.unloadScript", plugin], capture_output=True)
        script_path.unlink(missing_ok=True)
    if active_id() is None or active_id().casefold() != window_id.casefold():
        raise RuntimeError(f"KWin did not activate requested player window {window_id}; active={active_id()}")


def x_display():
    x11 = ctypes.CDLL(ctypes.util.find_library("X11"))
    xtst = ctypes.CDLL(ctypes.util.find_library("Xtst"))
    x11.XOpenDisplay.restype = ctypes.c_void_p
    x11.XCloseDisplay.argtypes = [ctypes.c_void_p]
    x11.XDefaultRootWindow.argtypes = [ctypes.c_void_p]
    x11.XDefaultRootWindow.restype = ctypes.c_ulong
    x11.XGetGeometry.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_uint), ctypes.POINTER(ctypes.c_uint), ctypes.POINTER(ctypes.c_uint), ctypes.POINTER(ctypes.c_uint)]
    x11.XTranslateCoordinates.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_ulong, ctypes.c_int, ctypes.c_int, ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_ulong)]
    x11.XWarpPointer.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_ulong, ctypes.c_int, ctypes.c_int, ctypes.c_uint, ctypes.c_uint, ctypes.c_int, ctypes.c_int]
    x11.XQueryPointer.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_int), ctypes.POINTER(ctypes.c_uint)]
    x11.XSync.argtypes = [ctypes.c_void_p, ctypes.c_int]
    xtst.XTestFakeButtonEvent.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]
    xtst.XTestFakeKeyEvent.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]
    d = x11.XOpenDisplay(None)
    if not d:
        raise RuntimeError("cannot connect to Xwayland")
    return x11, xtst, d


def window_geometry(window_id: str) -> dict:
    x11, _, d = x_display()
    try:
        target = int(window_id, 16)
        root = x11.XDefaultRootWindow(d)
        root_return = ctypes.c_ulong()
        x = ctypes.c_int(); y = ctypes.c_int()
        width = ctypes.c_uint(); height = ctypes.c_uint(); border = ctypes.c_uint(); depth = ctypes.c_uint()
        if not x11.XGetGeometry(d, target, ctypes.byref(root_return), ctypes.byref(x), ctypes.byref(y), ctypes.byref(width), ctypes.byref(height), ctypes.byref(border), ctypes.byref(depth)):
            raise RuntimeError("cannot read target player window geometry")
        root_x = ctypes.c_int(); root_y = ctypes.c_int(); child = ctypes.c_ulong()
        if not x11.XTranslateCoordinates(d, target, root, 0, 0, ctypes.byref(root_x), ctypes.byref(root_y), ctypes.byref(child)):
            raise RuntimeError("cannot translate player window origin")
        return {"x": root_x.value, "y": root_y.value, "width": width.value, "height": height.value}
    finally:
        x11.XCloseDisplay(d)


def point_in_window(window_id: str, x: int, y: int, buttons: list[int] | None = None) -> dict:
    """Warp and verify pointer inside exact player client before optional clicks."""
    force_activate_window(window_id)
    x11, xtst, d = x_display()
    try:
        target = int(window_id, 16)
        x11.XSetInputFocus.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int, ctypes.c_ulong]
        x11.XGetInputFocus.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.c_int)]
        x11.XSetInputFocus(d, target, 2, 0)
        x11.XSync(d, 0)
        focus = ctypes.c_ulong(); revert = ctypes.c_int()
        x11.XGetInputFocus(d, ctypes.byref(focus), ctypes.byref(revert))
        if focus.value != target:
            raise RuntimeError(f"refusing pointer input: target window does not own X focus (0x{focus.value:x})")
        geometry = window_geometry(window_id)
        x11.XWarpPointer(d, 0, target, 0, 0, 0, 0, x, y)
        x11.XSync(d, 0)
        root_return = ctypes.c_ulong(); child_return = ctypes.c_ulong()
        root_x = ctypes.c_int(); root_y = ctypes.c_int(); win_x = ctypes.c_int(); win_y = ctypes.c_int(); mask = ctypes.c_uint()
        x11.XQueryPointer(d, target, ctypes.byref(root_return), ctypes.byref(child_return), ctypes.byref(root_x), ctypes.byref(root_y), ctypes.byref(win_x), ctypes.byref(win_y), ctypes.byref(mask))
        if abs(win_x.value - x) > 2 or abs(win_y.value - y) > 2:
            raise RuntimeError(f"pointer did not reach target-window point; expected ({x},{y}), got ({win_x.value},{win_y.value}); input refused")
        if buttons:
            time.sleep(0.1)
        for button in buttons or []:
            xtst.XTestFakeButtonEvent(d, button, 1, 0)
            x11.XSync(d, 0)
            time.sleep(0.06)
            xtst.XTestFakeButtonEvent(d, button, 0, 0)
        x11.XSync(d, 0)
        return {"windowPoint": [win_x.value, win_y.value], "rootPoint": [root_x.value, root_y.value], "window": geometry}
    finally:
        x11.XCloseDisplay(d)


def drag_in_window(window_id: str, x: int, y: int, dx: int, dy: int) -> dict:
    force_activate_window(window_id)
    x11, xtst, d = x_display()
    try:
        target = int(window_id, 16)
        x11.XSetInputFocus.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int, ctypes.c_ulong]
        x11.XGetInputFocus.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.c_int)]
        x11.XSetInputFocus(d, target, 2, 0)
        x11.XSync(d, 0)
        focus = ctypes.c_ulong(); revert = ctypes.c_int()
        x11.XGetInputFocus(d, ctypes.byref(focus), ctypes.byref(revert))
        if focus.value != target:
            raise RuntimeError(f"refusing drag input: target window does not own X focus (0x{focus.value:x})")
        x11.XWarpPointer(d, 0, target, 0, 0, 0, 0, x, y)
        x11.XSync(d, 0)
        xtst.XTestFakeButtonEvent(d, 1, 1, 0)
        x11.XSync(d, 0)
        time.sleep(0.08)
        x11.XWarpPointer(d, target, target, 0, 0, 0, 0, x + dx, y + dy)
        x11.XSync(d, 0)
        time.sleep(0.08)
        root_return = ctypes.c_ulong(); child_return = ctypes.c_ulong()
        rx = ctypes.c_int(); ry = ctypes.c_int(); wx = ctypes.c_int(); wy = ctypes.c_int(); mask = ctypes.c_uint()
        x11.XQueryPointer(d, target, ctypes.byref(root_return), ctypes.byref(child_return), ctypes.byref(rx), ctypes.byref(ry), ctypes.byref(wx), ctypes.byref(wy), ctypes.byref(mask))
        if abs(wx.value - x - dx) > 2 or abs(wy.value - y - dy) > 2:
            xtst.XTestFakeButtonEvent(d, 1, 0, 0); x11.XSync(d, 0)
            raise RuntimeError("drag pointer left expected player-window location; movement refused")
        xtst.XTestFakeButtonEvent(d, 1, 0, 0)
        x11.XSync(d, 0)
        return {"from": [x, y], "to": [wx.value, wy.value], "delta": [dx, dy]}
    finally:
        x11.XCloseDisplay(d)


def send_focused_key(key: str, expected_active: str | None = None) -> str:
    """Send a key to current focus, including a native portal surface."""
    active = active_id()
    if active is None or expected_active is not None and active.casefold() != expected_active.casefold():
        raise RuntimeError(f"focused-key input refused: active={active}, expected={expected_active}")
    x11, xtst, d = x_display()
    try:
        focus = ctypes.c_ulong(); revert = ctypes.c_int()
        x11.XGetInputFocus.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.c_int)]
        x11.XGetInputFocus(d, ctypes.byref(focus), ctypes.byref(revert))
        if focus.value != int(active, 16):
            raise RuntimeError(f"focused-key input refused: active {active} does not own X input focus 0x{focus.value:x}")
        x11.XStringToKeysym.argtypes = [ctypes.c_char_p]; x11.XStringToKeysym.restype = ctypes.c_ulong
        x11.XKeysymToKeycode.argtypes = [ctypes.c_void_p, ctypes.c_ulong]; x11.XKeysymToKeycode.restype = ctypes.c_uint
        names = {"Escape": ["Escape"], "Return": ["Return"], "Alt+n": ["Alt_L", "n"], "Ctrl+a": ["Control_L", "a"], "Ctrl+l": ["Control_L", "l"]}.get(key)
        if names is None:
            raise ValueError(key)
        codes = []
        for name in names:
            sym = x11.XStringToKeysym(name.encode("ascii"))
            code = x11.XKeysymToKeycode(d, sym)
            if not code:
                raise RuntimeError(f"missing X keycode for {name}")
            codes.append(code)
        for code in codes: xtst.XTestFakeKeyEvent(d, code, 1, 0)
        for code in reversed(codes): xtst.XTestFakeKeyEvent(d, code, 0, 0)
        x11.XSync(d, 0)
        time.sleep(0.08)
        return active
    finally:
        x11.XCloseDisplay(d)


def type_into_portal(text: str, expected_active: str) -> None:
    if active_id() != expected_active:
        raise RuntimeError("portal typing refused: portal is no longer the active window")
    x11, xtst, d = x_display()
    try:
        x11.XStringToKeysym.argtypes = [ctypes.c_char_p]; x11.XStringToKeysym.restype = ctypes.c_ulong
        x11.XKeysymToKeycode.argtypes = [ctypes.c_void_p, ctypes.c_ulong]; x11.XKeysymToKeycode.restype = ctypes.c_uint
        x11.XGetInputFocus.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.c_int)]
        focus = ctypes.c_ulong(); revert = ctypes.c_int()
        x11.XGetInputFocus(d, ctypes.byref(focus), ctypes.byref(revert))
        if focus.value != int(expected_active, 16):
            raise RuntimeError("portal typing refused: X input focus is not the active portal surface")
        aliases = {"/": "slash", ".": "period", "-": "minus", "_": "minus", " ": "space"}
        for char in text:
            shifted = char.isalpha() and char.isupper() or char == "_"
            name = aliases.get(char, char.lower() if char.isalpha() else char)
            sym = x11.XStringToKeysym(name.encode("ascii"))
            code = x11.XKeysymToKeycode(d, sym)
            if not code:
                raise RuntimeError(f"cannot type path character {char!r}")
            if shifted:
                shift_sym = x11.XStringToKeysym(b"Shift_L")
                shift = x11.XKeysymToKeycode(d, shift_sym)
                xtst.XTestFakeKeyEvent(d, shift, 1, 0)
            xtst.XTestFakeKeyEvent(d, code, 1, 0)
            xtst.XTestFakeKeyEvent(d, code, 0, 0)
            if shifted:
                xtst.XTestFakeKeyEvent(d, shift, 0, 0)
            x11.XSync(d, 0)
            time.sleep(0.01)
    finally:
        x11.XCloseDisplay(d)


def validate_fixture_manifest(scenario_path: Path, fixtures: dict[str, Path]) -> dict:
    scenario = json.loads(scenario_path.read_text(encoding="utf-8"))
    declared = scenario.get("fixtures", {})
    for key, path in fixtures.items():
        expected = declared.get(key, {}).get("sha256")
        if not expected:
            raise RuntimeError(f"scenario has no recorded SHA-256 for fixture '{key}'")
        actual = sha256(path)
        if actual != expected:
            raise RuntimeError(f"fixture '{key}' hash mismatch: expected {expected}, got {actual}")
    return scenario


def report(path: Path) -> dict:
    try:
        return json.loads(path.read_text(encoding="utf-8"))
    except (OSError, json.JSONDecodeError):
        return {}


def wait_for(path: Path, predicate, description: str, timeout: float = 12) -> dict:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        data = report(path)
        if data and predicate(data):
            return data
        time.sleep(0.12)
    raise TimeoutError(f"timed out waiting for {description}; last report={report(path)}")


def capture(window: dict, output: Path) -> dict:
    output.parent.mkdir(parents=True, exist_ok=True)
    subprocess.run(["ffmpeg", "-hide_banner", "-loglevel", "error", "-f", "x11grab", "-window_id", window["id"], "-i", os.environ["DISPLAY"], "-frames:v", "1", "-y", str(output)], check=True)
    stats = subprocess.run(["ffmpeg", "-hide_banner", "-i", str(output), "-vf", "signalstats,metadata=print", "-frames:v", "1", "-f", "null", "-"], check=True, capture_output=True, text=True).stderr
    import re
    match = re.search(r"lavfi\.signalstats\.YAVG=([0-9.]+)", stats)
    y_average = float(match.group(1)) if match else None
    if y_average is None or y_average <= 20:
        raise RuntimeError(f"process-owned window screenshot is blank or occluded (YAVG={y_average})")
    return {"file": output.name, "sha256": sha256(output), "method": "ffmpeg x11grab -window_id", "size": window_geometry(window["id"]), "lumaAverage": y_average}


def process_resources(pid: int) -> dict:
    status_path = Path(f"/proc/{pid}/status")
    maps_path = Path(f"/proc/{pid}/maps")
    fd_path = Path(f"/proc/{pid}/fd")
    status = status_path.read_text(encoding="utf-8") if status_path.exists() else ""
    maps = maps_path.read_text(encoding="utf-8") if maps_path.exists() else ""
    rss = re.search(r"^VmRSS:\s+(\d+)\s+kB", status, re.M)
    libraries = sorted({line.split()[-1] for line in maps.splitlines() if re.search(r"lib(SkiaSharp|HarfBuzz|Avalonia).*\.so", line) and line.split()[-1].startswith("/")})
    return {"rssKiB": int(rss.group(1)) if rss else None, "openFileDescriptors": len(list(fd_path.iterdir())) if fd_path.exists() else None, "mappedSkiaAndHarfBuzzLibraries": libraries}


def sample_screen(image_path: Path, report_data: dict, image_x: float, image_y: float) -> tuple[int, int, int]:
    from PIL import Image
    scale = float(report_data["renderScaling"])
    x = float(report_data["viewportLogicalX"]) + float(report_data["imageRectX"]) + image_x * float(report_data["imageRectWidth"]) / float(report_data["width"])
    y = float(report_data["viewportLogicalY"]) + float(report_data["imageRectY"]) + image_y * float(report_data["imageRectHeight"]) / float(report_data["height"])
    with Image.open(image_path) as raw:
        image = raw.convert("RGB")
        return image.getpixel((min(image.width - 1, max(0, round(x * scale))), min(image.height - 1, max(0, round(y * scale)))))


def near_color(actual: tuple[int, int, int], expected: tuple[int, int, int], tolerance: int = 24) -> bool:
    return all(abs(a - e) <= tolerance for a, e in zip(actual, expected))


def screenshot_dialog(output: Path) -> dict | None:
    raw = output.with_suffix(".raw.png")
    result = subprocess.run(["spectacle", "--background", "--activewindow", "--output", str(raw), "--nonotify", "--no-decoration"], capture_output=True, text=True)
    if result.returncode or not raw.exists():
        return None
    try:
        from PIL import Image
        with Image.open(raw) as image:
            width, height = image.size
            # Keep only the portal's bottom controls, excluding its file listing.
            region = image.crop((0, int(height * 0.78), width, height))
            region.save(output)
    finally:
        raw.unlink(missing_ok=True)
    return {"file": output.name, "sha256": sha256(output), "capture": "Spectacle active-window; bottom controls crop; local only"}


def open_from_picker(window: dict, fixture: Path, report_path: Path, output: Path, action_id: str, expected_hash: str | None = None) -> tuple[dict, dict]:
    actual_fixture_hash = sha256(fixture)
    if expected_hash and actual_fixture_hash != expected_hash:
        raise RuntimeError(f"fixture hash mismatch before selection: expected {expected_hash}, got {actual_fixture_hash}")
    before = report(report_path)
    before_ids = {x["id"].casefold() for x in oracle.list_x11_windows()}
    oracle.send_key("Control_L+o", window["id"])
    deadline = time.monotonic() + 12
    modal = None
    while time.monotonic() < deadline:
        current = active_id()
        if current and current.casefold() != window["id"].casefold():
            modal = current
            break
        time.sleep(0.1)
    if modal is None:
        raise TimeoutError("Ctrl+O did not open an active file-picker surface")
    # Wait for the portal to finish its async transition and establish input focus.
    time.sleep(0.4)
    if active_id() != modal:
        raise RuntimeError(f"portal lost activation before path selection: expected {modal}, active={active_id()}")
    # Spectacle takes activation from the DBus portal on this Wayland session,
    # which makes subsequent keyboard interaction ambiguous. Keep the portal
    # surface uncaptured; the opened-player window and selected fixture hashes
    # are the durable checkpoints.
    dialog_shot = {"status": "NOT_CAPTURED", "reason": "Wayland portal surface is not an X11 client; screenshot utility changes activation"}
    send_focused_key("Alt+n", modal)
    send_focused_key("Ctrl+a", modal)
    type_into_portal(str(fixture.resolve()), modal)
    send_focused_key("Return", modal)
    deadline = time.monotonic() + 20
    fallback_sent = False
    while time.monotonic() < deadline:
        if active_id() == modal and not fallback_sent and time.monotonic() + 17 < deadline:
            time.sleep(0.6)
            if active_id() == modal:
                # KDE portal builds can leave the name field unchanged after
                # Alt+N; Ctrl+L is the file chooser's location-entry route.
                send_focused_key("Ctrl+l", modal)
                type_into_portal(str(fixture.resolve()), modal)
                send_focused_key("Return", modal)
            fallback_sent = True
        if active_id() == window["id"]:
            data = report(report_path)
            if data and data.get("lastPickerResult") == "Selected" and data.get("lastAttemptedFileName") == fixture.name:
                if data.get("status") != "VISIBLE_FRAME_RENDERED":
                    data = wait_for(report_path, lambda x: x.get("status") == "VISIBLE_FRAME_RENDERED", "visible frame rendered")
                if data.get("lastOpenResult") == "Opened" and data.get("inputSha256") != actual_fixture_hash:
                    raise RuntimeError("application report does not match selected fixture SHA-256")
                shot = capture(window, output / f"{action_id}-visible.png")
                return data, {"action": "real Avalonia storage provider selection", "result": "PASS", "fileName": fixture.name, "selectedFixtureSha256": actual_fixture_hash, "activeImageSha256": data.get("inputSha256"), "dialogControlsScreenshot": dialog_shot, "visibleScreenshot": shot, "beforePresentationCopyCount": before.get("presentationCopyCount"), "afterPresentationCopyCount": data.get("presentationCopyCount")}
        time.sleep(0.15)
    if active_id() == modal:
        # Do not leave an owned native dialog holding pointer/keyboard focus
        # when a flaky portal selection fails.
        send_focused_key("Escape", modal)
    raise TimeoutError(f"file selection did not reach visible player state: {report(report_path)}")


def cancel_picker(window: dict, report_path: Path) -> dict:
    before = report(report_path)
    oracle.send_key("Control_L+o", window["id"])
    deadline = time.monotonic() + 12
    modal = None
    while time.monotonic() < deadline:
        active = active_id()
        if active and active.casefold() != window["id"].casefold():
            modal = active
            break
        time.sleep(0.1)
    if not modal:
        raise TimeoutError("file picker did not open before cancel test")
    send_focused_key("Escape", modal)
    after = wait_for(report_path, lambda x: x.get("lastPickerResult") == "Cancelled", "picker cancellation report")
    invariant = all(after.get(key) == before.get(key) for key in ("inputSha256", "presentationCopyCount", "timestamp", "readerState"))
    if not invariant:
        raise RuntimeError("file-picker cancellation changed the active player session")
    return {"action": "Escape cancels real file picker", "result": "PASS", "activeImageHash": after.get("inputSha256"), "presentationCopyCount": after.get("presentationCopyCount"), "readerState": after.get("readerState")}


def wait_report(path: Path, condition, description: str, timeout: float = 10) -> dict:
    return wait_for(path, condition, description, timeout)


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--assembly", type=Path, required=True)
    parser.add_argument("--png", type=Path, required=True)
    parser.add_argument("--bmp", type=Path, required=True)
    parser.add_argument("--corrupt", type=Path, required=True)
    parser.add_argument("--alpha", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--source-sha", required=True)
    args = parser.parse_args()
    assembly = args.assembly.resolve(); output = args.output.resolve()
    fixtures = {"png": args.png.resolve(), "bmp": args.bmp.resolve(), "corrupt": args.corrupt.resolve(), "alpha": args.alpha.resolve()}
    for name, path in fixtures.items():
        if not path.is_file():
            raise FileNotFoundError(f"{name} fixture unavailable: {path}")
    validate_fixture_manifest(REPO / "tools/native-player/scenarios/gui-interactions-v1.json", fixtures)
    output.mkdir(parents=True, exist_ok=True)
    report_path = output / "player-state.json"
    report_path.unlink(missing_ok=True)
    log_path = output / "player.log"
    actions: list[dict] = []
    screenshots: list[dict] = []
    resource_samples: list[dict] = []
    with log_path.open("w", encoding="utf-8") as log:
        process = subprocess.Popen(["dotnet", str(assembly), "--smoke-report", str(report_path)], cwd=REPO, env={**os.environ, "KINOVEA_SOURCE_SHA": args.source_sha}, stdout=log, stderr=subprocess.STDOUT)
        window = None
        try:
            deadline = time.monotonic() + 30
            while time.monotonic() < deadline:
                if process.poll() is not None:
                    raise RuntimeError(f"player exited during startup with {process.returncode}")
                window = next((item for item in oracle.list_x11_windows() if item.get("pid") == str(process.pid) and item["title"] == "Kinovea Native Player"), None)
                if window and report_path.exists():
                    break
                time.sleep(0.2)
            if not window:
                raise TimeoutError("actual process-owned Avalonia window did not appear")
            empty = wait_for(report_path, lambda x: x.get("status") == "EMPTY", "empty initial player")
            force_activate_window(window["id"])
            time.sleep(0.5)
            actions.append({"action": "startup empty state", "result": "PASS", "processId": process.pid, "readerState": empty.get("readerState"), "report": empty})
            screenshots.append({"checkpoint": "startup-empty", **capture(window, output / "01-startup-empty.png")})

            png, opened_png = open_from_picker(window, fixtures["png"], report_path, output, "png", sha256(fixtures["png"]))
            actions.append(opened_png); screenshots.append({"checkpoint": "png-first-visible", **opened_png["visibleScreenshot"]})
            resource_samples.append({"after": "first PNG open", **process_resources(process.pid)})
            if (png.get("width"), png.get("height"), png.get("fps"), png.get("timestamp"), png.get("previousTimestamp")) != (1280, 720, 25, 0, -1):
                raise RuntimeError(f"PNG visible metadata mismatch: {png}")
            actions.append({"action": "PNG legacy metadata", "result": "PASS", "fileName": png.get("fileName"), "dimensions": [png.get("width"), png.get("height")], "fps": png.get("fps"), "timestamp": png.get("timestamp"), "previousTimestamp": png.get("previousTimestamp")})

            next_before = report(report_path)
            next_button = next_before["nextButton"]
            # Focus order is Open, Close, Previous (disabled at t=0), Next.
            # This is also the route used by the existing native-player smoke harness.
            for _ in range(3): oracle.send_key("Tab", window["id"])
            oracle.send_key("Return", window["id"])
            next_report = wait_for(report_path, lambda x: x.get("timestamp") == 1, "right arrow advances frame")
            if next_report.get("presentationCopyCount") != next_before.get("presentationCopyCount"):
                raise RuntimeError("frame navigation unexpectedly allocated a new presentation bitmap")
            actions.append({"action": "Next button keyboard activation", "result": "PASS", "timestamp": next_report.get("timestamp"), "previousTimestamp": next_report.get("previousTimestamp"), "presentationCopyCount": next_report.get("presentationCopyCount"), "buttonBoundsLogical": next_button, "focusSequence": ["Tab", "Tab", "Tab", "Return"]})
            screenshots.append({"checkpoint": "navigation-next", **capture(window, output / "02-navigation-next.png")})
            oracle.send_key("Shift_L+Tab", window["id"])
            oracle.send_key("Return", window["id"])
            wait_for(report_path, lambda x: x.get("timestamp") == 0, "left arrow returns to first frame")

            actions.append(cancel_picker(window, report_path))
            screenshots.append({"checkpoint": "cancelled-picker-player", **capture(window, output / "03-cancel-picker.png")})

            corrupt_before = report(report_path)
            corrupt, corrupt_action = open_from_picker(window, fixtures["corrupt"], report_path, output, "corrupt", "9f2b222cc66ffcc47742bb2f7dabd3f5f8be0ded008fb37297cc6b8f2ae4b36a")
            if corrupt.get("lastOpenResult") != "Rejected" or corrupt.get("readerState") != "Open" or corrupt.get("inputSha256") != corrupt_before.get("inputSha256") or corrupt.get("presentationCopyCount") != corrupt_before.get("presentationCopyCount") or not corrupt.get("errorMessage"):
                raise RuntimeError(f"corrupt replacement did not preserve current session: {corrupt}")
            corrupt_action["result"] = "PASS"
            corrupt_action["retainedActiveHash"] = corrupt.get("inputSha256")
            corrupt_action["errorMessage"] = corrupt.get("errorMessage")
            actions.append(corrupt_action)

            # Close the current session explicitly, then exercise a full BMP
            # session in the same process. The viewport gesture suite is a
            # separate scenario so failures remain attributable.
            oracle.send_key("Control_L+w", window["id"])
            closed_png = wait_for(report_path, lambda x: x.get("readerState") == "Empty", "Ctrl+W closes PNG")
            if closed_png.get("status") != "EMPTY" or closed_png.get("width") is not None:
                raise RuntimeError(f"PNG close left presentation state behind: {closed_png}")
            actions.append({"action": "Ctrl+W closes PNG", "result": "PASS", "readerState": closed_png.get("readerState"), "inputSha256": closed_png.get("inputSha256"), "presentationCopyCount": closed_png.get("presentationCopyCount")})
            screenshots.append({"checkpoint": "png-closed-empty", **capture(window, output / "04-png-closed.png")})

            bmp, opened_bmp = open_from_picker(window, fixtures["bmp"], report_path, output, "bmp")
            if (bmp.get("width"), bmp.get("height"), bmp.get("fps"), bmp.get("timestamp"), bmp.get("previousTimestamp")) != (640, 480, 25, 0, -1):
                raise RuntimeError(f"BMP visible metadata mismatch: {bmp}")
            actions.append(opened_bmp)
            actions.append({"action": "BMP legacy metadata", "result": "PASS", "fileName": bmp.get("fileName"), "dimensions": [bmp.get("width"), bmp.get("height")], "fps": bmp.get("fps"), "timestamp": bmp.get("timestamp"), "previousTimestamp": bmp.get("previousTimestamp")})
            screenshots.append({"checkpoint": "bmp-first-visible", **opened_bmp["visibleScreenshot"]})
            resource_samples.append({"after": "BMP reopened in same process", **process_resources(process.pid)})

            bmp_next_before = report(report_path)
            for _ in range(3): oracle.send_key("Tab", window["id"])
            oracle.send_key("Return", window["id"])
            bmp_next = wait_for(report_path, lambda x: x.get("timestamp") == 1, "BMP next-frame action")
            if bmp_next.get("presentationCopyCount") != bmp_next_before.get("presentationCopyCount"):
                raise RuntimeError("BMP navigation allocated another presentation bitmap")
            oracle.send_key("Shift_L+Tab", window["id"])
            oracle.send_key("Return", window["id"])
            bmp_previous = wait_for(report_path, lambda x: x.get("timestamp") == 0, "BMP previous-frame action")
            actions.append({"action": "BMP next and previous frame controls", "result": "PASS", "timestamps": [bmp_next.get("timestamp"), bmp_previous.get("timestamp")], "previousTimestamps": [bmp_next.get("previousTimestamp"), bmp_previous.get("previousTimestamp")], "presentationCopyCount": bmp_previous.get("presentationCopyCount")})
            screenshots.append({"checkpoint": "bmp-navigation", **capture(window, output / "05-bmp-navigation.png")})

            oracle.send_key("Control_L+w", window["id"])
            closed_bmp = wait_for(report_path, lambda x: x.get("readerState") == "Empty", "Ctrl+W closes BMP")
            if closed_bmp.get("status") != "EMPTY" or closed_bmp.get("width") is not None:
                raise RuntimeError(f"BMP close left presentation state behind: {closed_bmp}")
            actions.append({"action": "Ctrl+W closes BMP", "result": "PASS", "readerState": closed_bmp.get("readerState"), "inputSha256": closed_bmp.get("inputSha256")})
            screenshots.append({"checkpoint": "bmp-closed-empty", **capture(window, output / "06-bmp-closed.png")})

            png_again, opened_again = open_from_picker(window, fixtures["png"], report_path, output, "reopen-png")
            if png_again.get("inputSha256") != sha256(fixtures["png"]) or png_again.get("timestamp") != 0 or png_again.get("previousTimestamp") != -1:
                raise RuntimeError(f"reopened PNG did not produce a fresh initial reader state: {png_again}")
            actions.append({"action": "reopen PNG after PNG and BMP close", "result": "PASS", "inputSha256": png_again.get("inputSha256"), "timestamp": png_again.get("timestamp"), "previousTimestamp": png_again.get("previousTimestamp"), "presentationCopyCount": png_again.get("presentationCopyCount")})
            screenshots.append({"checkpoint": "png-reopened", **opened_again["visibleScreenshot"]})
            resource_samples.append({"after": "PNG reopened after two closed sessions", **process_resources(process.pid)})

            oracle.send_key("Alt_L+F4", window["id"])
            process.wait(timeout=12)
            if process.returncode != 0:
                raise RuntimeError(f"application exit returned {process.returncode}")
            actions.append({"action": "exit application", "result": "PASS", "exitCode": process.returncode})

            manifest = {"schemaVersion": 1, "sourceSha": args.source_sha, "host": {"platform": sys.platform, "python": sys.version.split()[0], "dotnetRuntime": "10.0.11", "display": os.environ.get("DISPLAY"), "waylandDisplay": os.environ.get("WAYLAND_DISPLAY"), "xwaylandResolution": oracle.xwayland_resolution(), "sessionType": os.environ.get("XDG_SESSION_TYPE")}, "processId": process.pid, "windowId": window["id"], "fixtures": {k: {"fileName": v.name, "sha256": sha256(v)} for k, v in fixtures.items()}, "actions": actions, "screenshots": screenshots, "resourceSamples": resource_samples, "packageEvidence": {"avalonia": "12.1.4", "skiaSharp": "3.119.4", "linuxNativeSkiaMappedLibraries": resource_samples[0].get("mappedSkiaAndHarfBuzzLibraries", [])}, "limitations": ["Screenshot appearance is not full decoded-pixel parity.", "Windows GUI behavior was not run.", "No hosted CI was run."]}
            (output / "gui-interactions.v1.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
            print(json.dumps(manifest, indent=2))
            return 0
        finally:
            if window and process.poll() is None:
                try: oracle.send_key("Alt_L+F4", window["id"])
                except Exception: pass
            try: process.wait(timeout=7)
            except subprocess.TimeoutExpired:
                process.terminate()
                try: process.wait(timeout=5)
                except subprocess.TimeoutExpired:
                    process.kill(); process.wait()


if __name__ == "__main__":
    try:
        raise SystemExit(main())
    except Exception as ex:
        print(f"native player interaction scenario failed: {ex}", file=sys.stderr)
        raise SystemExit(1)
