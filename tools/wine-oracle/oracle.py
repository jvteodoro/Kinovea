#!/usr/bin/env python3
"""Run scoped Kinovea GUI observations under an existing Wine prefix.

This tool only sends keyboard events after selecting a Kinovea-owned Xwayland
window. It captures that window by X11 window id; it never captures the desktop.
"""

from __future__ import annotations

import argparse
import hashlib
import json
import os
import platform
import re
import shutil
import subprocess
import sys
import tempfile
import time
import xml.etree.ElementTree as ET
from datetime import datetime, timezone
from pathlib import Path


SCHEMA_VERSION = 1


def sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as source:
        for block in iter(lambda: source.read(1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def parse_xprop_value(output: str, key: str) -> str | None:
    match = re.search(rf"^{re.escape(key)}\([^)]*\)\s*=\s*(.*)$", output, re.M)
    if not match:
        return None
    value = match.group(1).strip()
    quoted = re.search(r'"(.*)"', value)
    return quoted.group(1) if quoted else value


def list_x11_windows() -> list[dict]:
    if not os.environ.get("DISPLAY"):
        raise RuntimeError("DISPLAY is unset; this oracle currently requires X11/Xwayland.")
    root = subprocess.run(
        ["xprop", "-root", "_NET_CLIENT_LIST_STACKING"],
        check=True,
        capture_output=True,
        text=True,
    ).stdout
    ids = re.findall(r"0x[0-9a-fA-F]+", root)
    windows = []
    for window_id in ids:
        result = subprocess.run(
            ["xprop", "-id", window_id, "_NET_WM_NAME", "WM_NAME", "WM_CLASS", "_NET_WM_PID"],
            capture_output=True,
            text=True,
        )
        output = result.stdout
        title = parse_xprop_value(output, "_NET_WM_NAME") or parse_xprop_value(output, "WM_NAME")
        pid = parse_xprop_value(output, "_NET_WM_PID")
        klass = parse_xprop_value(output, "WM_CLASS")
        if title:
            windows.append({"id": window_id, "title": title, "pid": pid, "class": klass})
    return windows


def kinovea_windows() -> list[dict]:
    return [window for window in list_x11_windows() if "kinovea" in window["title"].casefold()]


def primary_kinovea_windows() -> list[dict]:
    return [window for window in kinovea_windows() if "splash" not in window["title"].casefold() and "loading" not in window["title"].casefold()]


def activate_window(window_id: str) -> None:
    # xdotool/wmctrl are deliberately not dependencies. On KWin/Wayland, use its
    # scripting API to raise and activate the exact Xwayland client.
    script = r'''import os, tempfile, subprocess, sys, time
target = int(sys.argv[1], 16)
plugin = "codex-kinovea-oracle-" + str(os.getpid())
code = "var id = " + str(target) + "; var found = workspace.getClient(id); if (!found) { print('WINE_ORACLE target XID not managed: ' + id); } else { workspace.raiseWindow(found); workspace.activeWindow = found; }"
with tempfile.NamedTemporaryFile("w", suffix=".js", prefix="kinovea-activate-", delete=False) as f:
    f.write(code)
    path = f.name
try:
    load = subprocess.run(["qdbus6", "org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting.loadScript", path, plugin], capture_output=True, text=True)
    if load.returncode: raise SystemExit(load.stderr.strip() or "KWin could not load the temporary focus script")
    script_id = load.stdout.strip()
    if not script_id.isdigit(): raise SystemExit("KWin returned no script id for the temporary focus script")
    start = subprocess.run(["qdbus6", "org.kde.KWin", f"/Scripting/Script{script_id}", "org.kde.kwin.Script.run"], capture_output=True, text=True)
    if start.returncode: raise SystemExit(start.stderr.strip() or "KWin could not run the temporary focus script")
    time.sleep(0.35)
finally:
    subprocess.run(["qdbus6", "org.kde.KWin", "/Scripting", "org.kde.kwin.Scripting.unloadScript", plugin], capture_output=True)
    os.unlink(path)
'''
    active = subprocess.run(["xprop", "-root", "_NET_ACTIVE_WINDOW"], capture_output=True, text=True).stdout
    current = re.search(r"0x[0-9a-fA-F]+", active)
    if current and current.group(0).casefold() == window_id.casefold():
        return
    result = subprocess.run([sys.executable, "-c", script, window_id], capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError(result.stderr.strip() or "could not activate Kinovea window")
    active = subprocess.run(["xprop", "-root", "_NET_ACTIVE_WINDOW"], capture_output=True, text=True).stdout
    match = re.search(r"0x[0-9a-fA-F]+", active)
    if match and match.group(0).casefold() == window_id.casefold():
        return
    if not match or match.group(0).casefold() != window_id.casefold():
        raise RuntimeError("refusing keyboard input because KWin did not activate the requested window")


def send_key(key: str, target_window_id: str | None = None) -> None:
    script = r'''import ctypes, ctypes.util, sys, time, subprocess, re
display_name = None
x11 = ctypes.CDLL(ctypes.util.find_library("X11"))
xtst = ctypes.CDLL(ctypes.util.find_library("Xtst"))
x11.XOpenDisplay.restype = ctypes.c_void_p
x11.XStringToKeysym.argtypes = [ctypes.c_char_p]
x11.XStringToKeysym.restype = ctypes.c_ulong
x11.XKeysymToKeycode.argtypes = [ctypes.c_void_p, ctypes.c_ulong]
x11.XKeysymToKeycode.restype = ctypes.c_uint
x11.XFlush.argtypes = [ctypes.c_void_p]
x11.XCloseDisplay.argtypes = [ctypes.c_void_p]
x11.XSetInputFocus.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int, ctypes.c_ulong]
x11.XGetInputFocus.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.c_int)]
xtst.XTestFakeKeyEvent.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]
display = x11.XOpenDisplay(None)
if not display:
    raise SystemExit("cannot connect to Xwayland display")
active = subprocess.run(["xprop", "-root", "_NET_ACTIVE_WINDOW"], capture_output=True, text=True, check=True).stdout
match = re.search(r"0x[0-9a-fA-F]+", active)
if not match or match.group(0).casefold() != sys.argv[1].casefold():
    raise SystemExit("refusing keyboard input: requested Kinovea window is not KWin's active client")
target = int(sys.argv[1], 16)
x11.XSetInputFocus(display, target, 2, 0)
x11.XFlush(display)
time.sleep(0.05)
focus = ctypes.c_ulong(); revert = ctypes.c_int()
x11.XGetInputFocus(display, ctypes.byref(focus), ctypes.byref(revert))
if focus.value != target:
    raise SystemExit(f"could not focus active Kinovea window (X focus 0x{focus.value:x})")
names = sys.argv[2:]
keycodes = []
for name in names:
    sym = x11.XStringToKeysym(name.encode("ascii"))
    if not sym:
        raise SystemExit(f"unknown X11 keysym: {name}")
    code = x11.XKeysymToKeycode(display, sym)
    if not code:
        raise SystemExit(f"no X11 keycode for keysym: {name}")
    keycodes.append(code)
for code in keycodes:
    xtst.XTestFakeKeyEvent(display, code, 1, 0)
for code in reversed(keycodes):
    xtst.XTestFakeKeyEvent(display, code, 0, 0)
x11.XFlush(display)
time.sleep(0.1)
x11.XCloseDisplay(display)
'''
    parts = key.split("+")
    names = []
    for part in parts:
        if len(part) == 1 and part.isalpha() and part.isupper():
            names.extend(["Shift_L", part.lower()])
        elif part == "/":
            names.append("slash")
        elif part == "-":
            names.append("minus")
        elif part == "_":
            names.extend(["Shift_L", "minus"])
        else:
            names.append(part)
    current = list_x11_windows()
    if target_window_id:
        selected = next((window for window in current if window["id"].casefold() == target_window_id.casefold()), None)
    else:
        selected = current[0] if len(current) == 1 else None
    if not selected:
        raise RuntimeError(f"expected one Kinovea window before key input, found {len(current)}")
    activate_window(selected["id"])
    run = subprocess.run([sys.executable, "-c", script, selected["id"], *names], capture_output=True, text=True)
    if run.returncode:
        detail = run.stderr.strip() or run.stdout.strip() or f"child exit status {run.returncode}"
        raise RuntimeError(f"XTest failed for {key}: {detail}")


def send_text(text: str, target_window_id: str) -> None:
    script = r'''import ctypes, ctypes.util, sys, time, subprocess, re
x11 = ctypes.CDLL(ctypes.util.find_library("X11")); xtst = ctypes.CDLL(ctypes.util.find_library("Xtst"))
x11.XOpenDisplay.restype = ctypes.c_void_p
x11.XStringToKeysym.argtypes = [ctypes.c_char_p]; x11.XStringToKeysym.restype = ctypes.c_ulong
x11.XKeysymToKeycode.argtypes = [ctypes.c_void_p, ctypes.c_ulong]; x11.XKeysymToKeycode.restype = ctypes.c_uint
x11.XFlush.argtypes = [ctypes.c_void_p]
x11.XCloseDisplay.argtypes = [ctypes.c_void_p]
x11.XSetInputFocus.argtypes = [ctypes.c_void_p, ctypes.c_ulong, ctypes.c_int, ctypes.c_ulong]
x11.XGetInputFocus.argtypes = [ctypes.c_void_p, ctypes.POINTER(ctypes.c_ulong), ctypes.POINTER(ctypes.c_int)]
xtst.XTestFakeKeyEvent.argtypes = [ctypes.c_void_p, ctypes.c_uint, ctypes.c_int, ctypes.c_ulong]
target_text = sys.argv[1].casefold()
active = subprocess.run(["xprop", "-root", "_NET_ACTIVE_WINDOW"], capture_output=True, text=True, check=True).stdout
match = re.search(r"0x[0-9a-fA-F]+", active)
if not match or match.group(0).casefold() != target_text: raise SystemExit("refusing text input: requested Kinovea window is not KWin's active client")
d = x11.XOpenDisplay(None)
if not d: raise SystemExit("cannot connect to Xwayland")
target = int(sys.argv[1], 16)
x11.XSetInputFocus(d, target, 2, 0); x11.XFlush(d); time.sleep(0.05)
focus = ctypes.c_ulong(); revert = ctypes.c_int(); x11.XGetInputFocus(d, ctypes.byref(focus), ctypes.byref(revert))
if focus.value != target: raise SystemExit(f"could not focus active Kinovea window (X focus 0x{focus.value:x})")
def keycode(name):
    sym=x11.XStringToKeysym(name.encode("ascii")); code=x11.XKeysymToKeycode(d,sym)
    if not code: raise RuntimeError(f"no keycode for {name}")
    return code
for char in sys.argv[2]:
    if char.isalpha() and char.isupper(): names=["Shift_L",char.lower()]
    elif char.isalpha() or char.isdigit(): names=[char]
    else:
        names={"\\":["backslash"], ":":["Shift_L","semicolon"], "/":["slash"], "-":["minus"], ".":["period"], "_":["Shift_L","minus"], " ":["space"]}.get(char)
        if not names: raise SystemExit(f"unsupported path character: {char!r}")
    codes=[keycode(name) for name in names]
    for code in codes: xtst.XTestFakeKeyEvent(d,code,1,0)
    for code in reversed(codes): xtst.XTestFakeKeyEvent(d,code,0,0)
    x11.XFlush(d); time.sleep(0.012)
x11.XCloseDisplay(d)
'''
    if not any(window["id"].casefold() == target_window_id.casefold() for window in list_x11_windows()):
        raise RuntimeError("refusing text input because the target X11 window no longer exists")
    activate_window(target_window_id)
    result = subprocess.run([sys.executable, "-c", script, target_window_id, text], capture_output=True, text=True)
    if result.returncode:
        raise RuntimeError(result.stderr.strip() or result.stdout.strip() or "XTest text input failed")


def xwayland_resolution() -> str | None:
    result = subprocess.run(["xrandr", "--current"], capture_output=True, text=True)
    match = re.search(r"current (\d+ x \d+)", result.stdout)
    return match.group(1) if match else None


def command_version(command: list[str], pattern: str = r"\S+", line: int = 0) -> str | None:
    if not shutil.which(command[0]):
        return None
    result = subprocess.run(command, capture_output=True, text=True)
    lines = (result.stdout or result.stderr).splitlines()
    if result.returncode or len(lines) <= line:
        return None
    match = re.search(pattern, lines[line])
    return match.group(0) if match else lines[line].strip()


def capture_window(window: dict, output: Path) -> dict:
    ffmpeg = shutil.which("ffmpeg")
    if not ffmpeg:
        raise RuntimeError("ffmpeg is required for window-scoped screenshots")
    output.parent.mkdir(parents=True, exist_ok=True)
    result = subprocess.run(
        [ffmpeg, "-hide_banner", "-loglevel", "error", "-f", "x11grab", "-window_id", window["id"], "-i", os.environ["DISPLAY"], "-frames:v", "1", "-y", str(output)],
        capture_output=True,
        text=True,
    )
    if result.returncode:
        raise RuntimeError(f"window screenshot failed: {result.stderr.strip()}")
    probe = subprocess.run(
        [ffmpeg, "-hide_banner", "-i", str(output)],
        capture_output=True,
        text=True,
    )
    size = re.search(r"Video:.*?(\d{2,5})x(\d{2,5})", probe.stderr)
    return {
        "path": output.name,
        "sha256": sha256(output),
        "windowId": window["id"],
        "windowTitle": window["title"],
        "imageSize": f"{size.group(1)}x{size.group(2)}" if size else None,
        "captureMethod": "ffmpeg x11grab -window_id (target Kinovea window only)",
    }


def version_info(executable: Path) -> dict:
    # Assembly and PE version attributes are obtained from the binary without
    # loading it or starting the application.
    with tempfile.TemporaryDirectory(prefix="kinovea-version-probe-") as temporary:
        project = Path(temporary)
        probe_source = project / "Program.cs"
        (project / "version_probe.csproj").write_text(
            '<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>\n',
            encoding="utf-8",
        )
        probe_source.write_text(
            'using System; using System.Diagnostics; using System.Reflection; var p=args[0]; var n=AssemblyName.GetAssemblyName(p); var v=FileVersionInfo.GetVersionInfo(p); Console.WriteLine($"assembly={n.Version};file={v.FileVersion};product={v.ProductVersion}");\n',
            encoding="utf-8",
        )
        result = subprocess.run(
            ["dotnet", "run", "--project", str(project), "--", str(executable)],
            capture_output=True,
            text=True,
        )
        if result.returncode:
            raise RuntimeError(f"version probe failed: {result.stderr.strip()} {result.stdout.strip()}")
        values = dict(part.split("=", 1) for part in result.stdout.strip().split(";") if "=" in part)
        return values


def environment_record(executable: Path, prefix: Path) -> dict:
    wine = subprocess.run(["wine", "--version"], capture_output=True, text=True, check=True).stdout.strip()
    arch = None
    registry = prefix / "system.reg"
    if registry.exists():
        header = registry.read_text(errors="replace")[:128]
        match = re.search(r"#arch=(\w+)", header)
        arch = match.group(1) if match else None
    clr_files = [
        path.relative_to(prefix).as_posix()
        for path in (prefix / "drive_c/windows/Microsoft.NET/Framework/v4.0.30319/clr.dll", prefix / "drive_c/windows/Microsoft.NET/Framework64/v4.0.30319/clr.dll")
        if path.exists()
    ]
    release = None
    if registry.exists():
        text = registry.read_text(errors="replace")
        match = re.search(r'\[Software\\\\Microsoft\\\\NET Framework Setup\\\\NDP\\\\v4\\\\Full\][^\n]*\n(?:[^\n]*\n){0,4}?"Release"=dword:([0-9a-fA-F]+)', text)
        if match:
            release = int(match.group(1), 16)
    framework = "4.8.1 or later" if release is not None and release >= 533320 else "4.8" if release is not None and release >= 528040 else "not established"
    window_tools = {name: shutil.which(name) is not None for name in ("xdotool", "wmctrl", "wtype", "ydotool")}
    return {
        "host": {"os": platform.platform(), "architecture": platform.machine(), "python": platform.python_version()},
        "dotnetSdk": command_version(["dotnet", "--version"]),
        "wine": {"version": wine, "prefixArchitecture": arch, "prefixPath": "REDACTED_USER_LOCAL_PATH", "clrRuntimeFiles": clr_files, "dotNetFrameworkRelease": release, "dotNetFrameworkVersion": framework},
        "display": {
            "sessionType": os.environ.get("XDG_SESSION_TYPE"),
            "backend": "Xwayland" if os.environ.get("WAYLAND_DISPLAY") and os.environ.get("DISPLAY") else os.environ.get("XDG_SESSION_TYPE"),
            "windowManager": command_version(["kwin_wayland", "--version"], r"kwin\s+\S+") if os.environ.get("WAYLAND_DISPLAY") else None,
            "resolution": xwayland_resolution(),
            "scaleFactor": "NOT_DETECTED",
            "automationUtilities": window_tools,
            "keyboardAutomation": "KWin script activates exact Xwayland client; XSetInputFocus and XTest are guarded by _NET_ACTIVE_WINDOW.",
            "captureBackend": command_version(["ffmpeg", "-version"], r"ffmpeg\s+version\s+\S+") if shutil.which("ffmpeg") else None,
        },
        "application": {
            "executableName": executable.name,
            "sha256": sha256(executable),
            "sizeBytes": executable.stat().st_size,
            "version": version_info(executable),
            "installationPath": "REDACTED_USER_LOCAL_PATH",
        },
        "runtimeEvidence": "Wine registry Release value and CLR v4.0.30319 files are inspected from the existing prefix.",
    }


def resolve_input(spec: dict, repo_root: Path) -> tuple[Path, dict]:
    source = spec["input"]
    if source.get("environmentVariable"):
        raw_path = os.environ.get(source["environmentVariable"])
        if not raw_path:
            raise RuntimeError(f"set {source['environmentVariable']} to the local input fixture")
        path = Path(raw_path).expanduser().resolve()
    else:
        path = (repo_root / source["path"]).resolve()
    if not path.is_file():
        raise RuntimeError(f"input fixture not found: {source.get('id')}")
    actual = sha256(path)
    expected = source.get("sha256")
    if expected and actual != expected:
        raise RuntimeError(f"fixture SHA-256 mismatch for {source['id']}: expected {expected}, got {actual}")
    details = {"id": source["id"], "sha256": actual, "sizeBytes": path.stat().st_size, "provenance": source.get("provenance")}
    return path, details


def prepare_prefix(scenario: dict, prefix: Path) -> dict:
    preparation = scenario.get("prepare", {})
    if not preparation.get("clearSavedScreens"):
        return {"clearSavedScreens": False}
    if os.environ.get("KINOVEA_ORACLE_OVERLAY") != "1":
        raise RuntimeError("saved-session preparation is allowed only in the disposable overlay")
    user_roots = prefix / "drive_c" / "users"
    modified = []
    for path in user_roots.glob("*/AppData/Roaming/Kinovea/Windows/*.xml"):
        tree = ET.parse(path)
        root = tree.getroot()
        mode = root.findtext("StartupMode")
        screen_list = root.find("ScreenList")
        if mode == "Continue" and screen_list is not None and len(screen_list):
            count = len(screen_list)
            screen_list.clear()
            tree.write(path, encoding="utf-8", xml_declaration=True)
            modified.append({"screensCleared": count})
    return {"clearSavedScreens": True, "windowDescriptorsUpdated": len(modified), "screenCountsCleared": [entry["screensCleared"] for entry in modified]}


def windows_path(path: Path, prefix: Path) -> str:
    result = subprocess.run(["winepath", "-w", str(path)], env={**os.environ, "WINEPREFIX": str(prefix)}, check=True, capture_output=True, text=True)
    return result.stdout.strip()


def wait_window(timeout: float) -> dict:
    deadline = time.monotonic() + timeout
    while time.monotonic() < deadline:
        windows = kinovea_windows()
        primary = [window for window in windows if "splash" not in window["title"].casefold() and "loading" not in window["title"].casefold()]
        if primary:
            return primary[0]
        time.sleep(0.25)
    raise TimeoutError("no Kinovea window appeared before timeout")


def wait_dialog(app_pid: str | None, main_window_id: str, existing_ids: set[str], timeout: float = 10) -> dict:
    deadline = time.monotonic() + timeout
    observed: list[dict] = []
    while time.monotonic() < deadline:
        observed = list_x11_windows()
        candidates = [
            window for window in observed
            if window["id"].casefold() not in existing_ids
            and window["id"].casefold() != main_window_id.casefold()
            and (
                (app_pid and window["pid"] == app_pid)
                or window["title"].casefold() in {"open", "select file", "choose file"}
            )
        ]
        if candidates:
            return candidates[0]
        time.sleep(0.2)
    summary = [{"id": item["id"], "title": item["title"], "pid": item["pid"], "class": item["class"]} for item in observed]
    raise TimeoutError(f"no newly opened Kinovea file dialog appeared; visible X11 windows: {summary}")


def run_scenario(scenario_path: Path, executable: Path, prefix: Path, output: Path, repo_root: Path, wine_debug: str | None, collect_app_logs: bool) -> int:
    scenario = json.loads(scenario_path.read_text(encoding="utf-8"))
    if scenario.get("schemaVersion") != SCHEMA_VERSION:
        raise RuntimeError(f"unsupported scenario schemaVersion: {scenario.get('schemaVersion')}")
    if not executable.is_file() or not prefix.is_dir():
        raise RuntimeError("executable or Wine prefix is unavailable")
    if os.environ.get("KINOVEA_ORACLE_OVERLAY") != "1":
        raise RuntimeError("run via run-scenario.sh to keep Wine writes in a disposable copy-on-write overlay")
    app = scenario["application"]
    actual_sha = sha256(executable)
    actual_version = version_info(executable)
    if app.get("executableSha256") and actual_sha != app["executableSha256"]:
        raise RuntimeError(f"executable SHA-256 mismatch: expected {app['executableSha256']}, got {actual_sha}")
    expected_version = app.get("expectedVersion")
    if expected_version and actual_version.get("product") != expected_version:
        raise RuntimeError(f"executable version mismatch: expected {expected_version}, got {actual_version.get('product')}")
    if kinovea_windows():
        raise RuntimeError("Kinovea already has a visible window; close it before this run so the harness cannot affect another instance")
    fixture, input_record = resolve_input(scenario, repo_root)
    prefix_preparation = prepare_prefix(scenario, prefix)
    output.mkdir(parents=True, exist_ok=False)
    (output / "scenario.json").write_bytes(scenario_path.read_bytes())
    env = {**os.environ, "WINEPREFIX": str(prefix)}
    if wine_debug:
        env["WINEDEBUG"] = wine_debug
    args = ["wine", str(executable), *scenario.get("launchArguments", [])]
    stdout_path, stderr_path = output / "wine.stdout.log", output / "wine.stderr.log"
    start = datetime.now(timezone.utc).isoformat()
    start_epoch = time.time()
    result = {
        "schemaVersion": 1,
        "scenarioId": scenario["id"],
        "scenarioVersion": scenario["version"],
        "status": "IN_PROGRESS",
        "classification": "IN_PROGRESS",
        "startedUtc": start,
        "sourceCommit": subprocess.run(["git", "-C", str(repo_root), "rev-parse", "HEAD"], capture_output=True, text=True).stdout.strip(),
        "input": input_record,
        "prefixPreparation": prefix_preparation,
        "executable": {"sha256": actual_sha, "version": actual_version},
        "environment": environment_record(executable, prefix),
        "actions": [],
        "screenshots": [],
        "limitations": scenario.get("limitations", []),
    }
    process = None
    previous_focus = None
    try:
        active = subprocess.run(["xprop", "-root", "_NET_ACTIVE_WINDOW"], capture_output=True, text=True).stdout
        match = re.search(r"0x[0-9a-fA-F]+", active)
        previous_focus = match.group(0) if match else None
        stdout = stdout_path.open("wb")
        stderr = stderr_path.open("wb")
        process = subprocess.Popen(args, env=env, stdout=stdout, stderr=stderr, start_new_session=True)
        result["launch"] = {"executableSha256": sha256(executable), "launcherPid": process.pid, "exitCode": None, "arguments": scenario.get("launchArguments", [])}
        window = wait_window(float(scenario.get("startupTimeoutSeconds", 30)))
        result["window"] = {"id": window["id"], "title": window["title"], "linuxPid": window["pid"]}
        for action in scenario["actions"]:
            kind = action["type"]
            entry = {"type": kind, "description": action.get("description")}
            if kind == "capture":
                time.sleep(float(action.get("waitSeconds", 0)))
                windows = primary_kinovea_windows()
                if not windows:
                    entry["result"] = "NO_KINOVEA_WINDOW"
                    result["actions"].append(entry)
                    raise RuntimeError("Kinovea window disappeared before screenshot")
                window = windows[0]
                checkpoint = action["checkpoint"]
                screenshot = capture_window(window, output / f"{checkpoint}.png")
                screenshot["checkpoint"] = checkpoint
                screenshot["confidence"] = action.get("confidence", "VISUAL_OBSERVATION_ONLY")
                result["screenshots"].append(screenshot)
                entry["screenshot"] = screenshot
            elif kind == "key":
                for key in action["keys"]:
                    send_key(key, window["id"])
                    time.sleep(float(action.get("betweenKeysSeconds", 0.2)))
                entry["keysSent"] = action["keys"]
            elif kind == "open_fixture":
                # Saved playback screens were removed from the disposable
                # overlay; exercise the real menu/dialog file-open route.
                before_dialog = {item["id"].casefold() for item in list_x11_windows()}
                send_key("Control_L+o", window["id"])
                dialog = wait_dialog(window.get("pid"), window["id"], before_dialog, float(action.get("dialogTimeoutSeconds", 10)))
                dialog_capture = capture_window(dialog, output / "open-dialog.png")
                result.setdefault("screenshots", []).append({**dialog_capture, "checkpoint": "open-dialog", "confidence": "VISUAL_OBSERVATION_ONLY"})
                send_key("Alt_L+n", dialog["id"])
                send_key("Control_L+a", dialog["id"])
                send_text(windows_path(fixture, prefix), dialog["id"])
                typed_capture = capture_window(dialog, output / "open-dialog-path-entered.png")
                result.setdefault("screenshots", []).append({**typed_capture, "checkpoint": "open-dialog-path-entered", "confidence": "VISUAL_OBSERVATION_ONLY"})
                send_key("Return", dialog["id"])
                time.sleep(float(action.get("waitSeconds", 2)))
                entry["dialogTitle"] = dialog["title"]
                entry["dialogScreenshot"] = dialog_capture
                entry["typedPathScreenshot"] = typed_capture
                remaining_dialog = [item for item in list_x11_windows() if item["id"].casefold() == dialog["id"].casefold()]
                if remaining_dialog:
                    raise RuntimeError("Kinovea file dialog remained open after selecting the fixture path")
                entry["inputId"] = input_record["id"]
                entry["typedPath"] = "<fixture-path-redacted>"
            elif kind == "wait":
                time.sleep(float(action["seconds"]))
                entry["seconds"] = action["seconds"]
            elif kind == "close_screen":
                send_key("Control_L+F4", window["id"])
                time.sleep(float(action.get("waitSeconds", 1)))
                entry["kinoveaWindowsAfterClose"] = len(kinovea_windows())
            else:
                raise RuntimeError(f"unsupported action type: {kind}")
            entry["status"] = "WINE_OBSERVED"
            result["actions"].append(entry)
        result["status"] = "WINE_OBSERVED"
        result["executionStatus"] = "COMPLETED"
        result["expectedOutcomes"] = scenario["expectedOutcomes"]
        outcome_statuses = {item.get("status") for item in scenario["expectedOutcomes"]}
        if "WINE_OBSERVED" not in outcome_statuses:
            result["status"] = "NOT_ESTABLISHED"
        result["classification"] = result["status"]
        result["evidenceConfidence"] = scenario.get("evidenceConfidence", "WINE_OBSERVED; visual checks are not pixel equivalence")
    except Exception as exc:
        result["status"] = "FAIL"
        result["classification"] = "FAIL"
        result["executionStatus"] = "FAILED"
        result["failure"] = f"{type(exc).__name__}: {exc}"
        if process and process.poll() is not None:
            result["launch"]["exitCode"] = process.returncode
        result["expectedOutcomes"] = scenario.get("expectedOutcomes", [])
    finally:
        # Close only the window launched by this run. Never call wineserver -k,
        # since it could terminate other applications sharing this prefix.
        if process:
            for _ in range(2):
                windows = primary_kinovea_windows()
                if not windows:
                    break
                try:
                    send_key("Alt_L+F4", window["id"])
                except Exception:
                    break
                time.sleep(0.75)
            try:
                process.wait(timeout=3)
            except subprocess.TimeoutExpired:
                # Stop only the exact launcher process returned by Popen. Do not
                # signal its process group or the shared Wine server.
                process.terminate()
                try:
                    process.wait(timeout=2)
                except subprocess.TimeoutExpired:
                    process.kill()
                    process.wait(timeout=2)
            result.setdefault("launch", {})["exitCode"] = process.poll()
            result["launch"]["cleanedUpByHarness"] = True
            remaining = primary_kinovea_windows()
            if remaining:
                result["launch"]["windowStillOpenAfterCleanup"] = True
                result["launch"]["remainingWindowIds"] = [item["id"] for item in remaining]
                result["status"] = "FAIL"
                result["classification"] = "FAIL"
                result["executionStatus"] = "FAILED"
                result.setdefault("failure", "Harness cleanup did not close the Kinovea window; no shared Wine processes were terminated.")
            else:
                result["launch"]["windowStillOpenAfterCleanup"] = False
        if previous_focus:
            try:
                activate_window(previous_focus)
            except Exception:
                result["focusRestore"] = "FAILED"
        result["finishedUtc"] = datetime.now(timezone.utc).isoformat()
        result["logs"] = [
            {"path": path.name, "sha256": sha256(path)} for path in (stdout_path, stderr_path) if path.exists()
        ]
        if collect_app_logs:
            app_logs = []
            for log_path in prefix.glob("drive_c/users/*/AppData/Roaming/Kinovea/Logs/*.txt"):
                if log_path.is_file() and log_path.stat().st_mtime >= start_epoch - 2:
                    copied = output / "app-logs" / log_path.name
                    copied.parent.mkdir(parents=True, exist_ok=True)
                    shutil.copy2(log_path, copied)
                    app_logs.append({"path": str(copied.relative_to(output)), "sha256": sha256(copied)})
            result["applicationLogs"] = app_logs
        (output / "result.json").write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    return 0 if result.get("executionStatus") == "COMPLETED" and result["status"] in {"WINE_OBSERVED", "NOT_ESTABLISHED"} else 1


def validate_scenarios(scenario_dir: Path, repo_root: Path) -> int:
    errors = []
    files = sorted(scenario_dir.glob("*.json"))
    if not files:
        errors.append(f"no scenario definitions found in {scenario_dir}")
    for path in files:
        try:
            scenario = json.loads(path.read_text(encoding="utf-8"))
            required = {"schemaVersion", "id", "version", "application", "input", "preconditions", "actions", "expectedOutcomes", "cleanup", "evidenceConfidence", "limitations"}
            missing = required - scenario.keys()
            if missing:
                raise ValueError(f"missing fields: {sorted(missing)}")
            if scenario["schemaVersion"] != SCHEMA_VERSION:
                raise ValueError("unsupported schema version")
            if scenario["input"].get("path"):
                fixture = (repo_root / scenario["input"]["path"]).resolve()
                if not fixture.is_file():
                    raise ValueError(f"fixture missing: {scenario['input']['id']}")
                if scenario["input"].get("sha256") and sha256(fixture) != scenario["input"]["sha256"]:
                    raise ValueError(f"fixture checksum mismatch: {scenario['input']['id']}")
            for action in scenario["actions"]:
                if action.get("type") not in {"capture", "key", "wait", "close_screen", "open_fixture"}:
                    raise ValueError(f"unsupported action type: {action.get('type')}")
            print(f"PASS {path.name}: {scenario['id']} v{scenario['version']}")
        except Exception as exc:
            errors.append(f"FAIL {path.name}: {exc}")
    for error in errors:
        print(error, file=sys.stderr)
    return 1 if errors else 0


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    subs = parser.add_subparsers(dest="command", required=True)
    validate = subs.add_parser("validate", help="validate scenario JSON and repo fixture hashes")
    validate.add_argument("--scenario-dir", type=Path, default=Path(__file__).resolve().parent / "scenarios")
    validate.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[2])
    inspect = subs.add_parser("inspect", help="write sanitized host and executable provenance")
    inspect.add_argument("--executable", type=Path, required=True)
    inspect.add_argument("--prefix", type=Path, default=Path(os.environ.get("WINEPREFIX", "")))
    inspect.add_argument("--output", type=Path, required=True)
    run = subs.add_parser("run", help="execute one scenario in a dedicated Kinovea window")
    run.add_argument("--scenario", type=Path, required=True)
    run.add_argument("--executable", type=Path, required=True)
    run.add_argument("--prefix", type=Path, default=Path(os.environ.get("WINEPREFIX", "")))
    run.add_argument("--output", type=Path, required=True)
    run.add_argument("--repo-root", type=Path, default=Path(__file__).resolve().parents[2])
    run.add_argument("--wine-debug", help="optional Wine debug channels, e.g. +seh,+tid")
    run.add_argument("--collect-app-logs", action="store_true", help="copy Kinovea log files updated during this run; review for local paths before sharing")
    args = parser.parse_args()
    if args.command == "validate":
        return validate_scenarios(args.scenario_dir, args.repo_root)
    if args.command == "inspect":
        record = environment_record(args.executable.resolve(), args.prefix.resolve())
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(json.dumps(record, indent=2) + "\n", encoding="utf-8")
        print(f"Wrote sanitized environment metadata: {args.output}")
        return 0
    return run_scenario(args.scenario, args.executable.resolve(), args.prefix.resolve(), args.output.resolve(), args.repo_root.resolve(), args.wine_debug, args.collect_app_logs)


if __name__ == "__main__":
    raise SystemExit(main())
