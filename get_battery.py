#!/usr/bin/env python3
"""Optional Python bridge to Mizu's native Windows backend.

Build/publish Mizu first or set MIZU_EXE. Python is not used by the widget.
The original demo is preserved in docs/get_battery.original.py.
"""
from __future__ import annotations
import argparse
import json
import os
from pathlib import Path
import platform
import subprocess
import sys
import tempfile


def _executable() -> Path:
    root = Path(__file__).resolve().parent
    arch = "win-arm64" if platform.machine().lower() in ("arm64", "aarch64") else "win-x64"
    configured = os.environ.get("MIZU_EXE")
    paths = [Path(configured)] if configured else []
    paths += sorted(root.glob("artifacts/Mizu-*/app/Mizu.exe"), key=lambda p: p.stat().st_mtime, reverse=True)
    paths += [root / "Mizu.exe", root / "artifacts" / f"Mizu-{arch}" / "Mizu.exe",
              root / "src" / "Mizu.Widget" / "bin" / "Release" / "net10.0-windows" / "Mizu.exe"]
    for path in paths:
        if path.is_file():
            return path
    raise FileNotFoundError("Build/publish Mizu first, or set MIZU_EXE to Mizu.exe.")


def scan_native() -> dict:
    if os.name != "nt":
        raise OSError("This backend requires Windows 10/11.")
    with tempfile.TemporaryDirectory(prefix="mizu-battery-") as directory:
        output = Path(directory) / "battery.json"
        process = subprocess.run([str(_executable()), "--scan", str(output)], timeout=55,
                                 creationflags=subprocess.CREATE_NO_WINDOW, capture_output=True, check=False)
        if process.returncode != 0 or not output.is_file():
            raise RuntimeError("Native scan failed. See %LOCALAPPDATA%\\Mizu\\mizu.log.")
        return json.loads(output.read_text(encoding="utf-8-sig"))


def get_all_devices() -> list[dict]:
    kinds = {"Keyboard": "键盘", "Mouse": "鼠标", "Unknown": "外设"}
    links = {"Bluetooth": "蓝牙", "Receiver": "2.4G", "NearLink": "NearLink", "Wired": "有线", "Unknown": "未知"}
    return [{"id": d["Id"], "name": d["Name"], "type": kinds[d["Kind"]], "battery": d["Percent"],
             "connected": d["State"] == "Online", "state": d["State"], "mode": links[d["Link"]],
             "charging": d["Charging"], "source": d["Source"]} for d in scan_native()["Devices"]]


def get_bluetooth_devices() -> list[dict]:
    return [d for d in get_all_devices() if d["mode"] == "蓝牙"]


def get_logitech_24g_devices() -> list[dict]:
    return [d for d in get_all_devices() if d["mode"] == "2.4G" and d["source"].startswith("Logitech")]


if __name__ == "__main__":
    if hasattr(sys.stdout, "reconfigure"):
        sys.stdout.reconfigure(encoding="utf-8")
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--json", action="store_true")
    args = parser.parse_args()
    devices = get_all_devices()
    if args.json:
        print(json.dumps(devices, ensure_ascii=False, indent=2))
    elif not devices:
        print("未检测到可读取电量的无线键鼠。")
    else:
        for d in devices:
            battery = f"{d['battery']}%" if d["battery"] is not None else "—"
            print(f"[{d['type']}] {d['name']} · {battery} · {d['mode']} · {d['state']}")
