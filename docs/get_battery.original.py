#!/usr/bin/env python3
# -*- coding: utf-8 -*-
"""
读取 Windows 蓝牙 LE 与 2.4G 接收器下的键盘/鼠标电量
"""

import sys
import json
import time
import subprocess

try:
    import hid
except ImportError:
    hid = None


def classify_device(name: str, category: str = "", hid_type_code: int = -1) -> str:
    """区分键盘与鼠标"""
    if hid_type_code in [0, 2]:
        return "键盘"
    elif hid_type_code in [3, 4, 5]:
        return "鼠标"

    cat = (category or "").lower()
    if "input.mouse" in cat:
        return "鼠标"
    elif "input.keyboard" in cat:
        return "键盘"

    n = (name or "").lower()
    if any(k in n for k in ["mouse", "mice", "a9", "atk", "vgn", "g502", "g304", "g pro", "superlight", "viper", "鼠标"]):
        return "鼠标"
    if any(k in n for k in ["keyboard", "keys", "k98", "k855", "k380", "mx keys", "hhkb", "键盘"]):
        return "键盘"

    return "外设"


def get_bluetooth_devices():
    """读取 Windows 蓝牙 BLE 外设电量与类别"""
    ps_cmd = (
        "$res = @(); "
        "$devs = Get-PnpDevice -InstanceId 'BTHLE\\*' -ErrorAction SilentlyContinue; "
        "foreach ($d in $devs) { "
        "  $conn = (Get-PnpDeviceProperty -InstanceId $d.InstanceId -KeyName '{83DA6326-97A6-4088-9453-A1923F573B29} 15' -ErrorAction SilentlyContinue).Data; "
        "  $batt = (Get-PnpDeviceProperty -InstanceId $d.InstanceId -KeyName '{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2' -ErrorAction SilentlyContinue).Data; "
        "  $cat = (Get-PnpDeviceProperty -InstanceId $d.InstanceId -KeyName 'DEVPKEY_DeviceContainer_Category' -ErrorAction SilentlyContinue).Data; "
        "  if ($null -ne $batt) { "
        "    $cat_str = if ($cat) { $cat -join ',' } else { '' }; "
        "    $res += [PSCustomObject]@{ "
        "      Name = $d.FriendlyName; "
        "      IsConnected = [bool]$conn; "
        "      Battery = [int]$batt; "
        "      Category = $cat_str "
        "    } "
        "  } "
        "}; "
        "$res | ConvertTo-Json -Compress"
    )

    devices = []
    try:
        res = subprocess.run(
            ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", ps_cmd],
            capture_output=True, text=True, timeout=15
        )
        if not res.stdout.strip():
            return []
        data = json.loads(res.stdout.strip())
        raw_list = [data] if isinstance(data, dict) else (data if isinstance(data, list) else [])

        for item in raw_list:
            name = item.get("Name", "")
            cat = item.get("Category", "")
            # 过滤非键鼠（如 iPhone 手机等）
            if any(x in name.lower() for x in ["iphone", "phone", "ipad"]):
                continue

            dev_type = classify_device(name, category=cat)
            devices.append({
                "name": name,
                "type": dev_type,
                "battery": int(item.get("Battery", 0)),
                "connected": item.get("IsConnected", False),
                "mode": "蓝牙"
            })
    except Exception as e:
        print(f"蓝牙检测异常: {e}")

    return devices


def get_logitech_24g_devices():
    """读取罗技 2.4G 接收器 (Bolt / Unifying / Lightspeed) 配对的键鼠电量"""
    if hid is None:
        return []

    devices = []
    try:
        candidates = [
            d for d in hid.enumerate(0x046D, 0)
            if d.get('usage_page') == 0xff00 and d.get('usage') == 0x02
        ]
    except Exception:
        return []

    for dev_info in candidates:
        h = None
        try:
            h = hid.device()
            h.open_path(dev_info['path'])

            while h.read(64, 5):
                pass

            def send_and_wait(report, expected_dev, timeout=0.6):
                h.write(report)
                t_end = time.time() + timeout
                while time.time() < t_end:
                    resp = h.read(64, 30)
                    if resp and len(resp) >= 4 and resp[1] == expected_dev:
                        return resp
                return None

            for dev_idx in range(1, 7):
                r_root = send_and_wait([0x11, dev_idx, 0x00, 0x01, 0x00, 0x05] + [0x00] * 14, expected_dev=dev_idx, timeout=0.6 if dev_idx == 1 else 0.15)
                if not r_root or len(r_root) < 5 or r_root[2] == 0x8F:
                    continue

                name_feat_idx = r_root[4]
                name = f"Logitech Device {dev_idx}"
                hid_type_code = -1
                if name_feat_idx > 0:
                    r_cnt = send_and_wait([0x11, dev_idx, name_feat_idx, 0x01] + [0x00] * 16, expected_dev=dev_idx, timeout=0.3)
                    if r_cnt and len(r_cnt) >= 5:
                        name_len = r_cnt[4]
                        if 0 < name_len <= 64:
                            dev_name = ""
                            for offset in range(0, name_len, 16):
                                r_chunk = send_and_wait([0x11, dev_idx, name_feat_idx, 0x11, offset] + [0x00] * 15, expected_dev=dev_idx, timeout=0.2)
                                if r_chunk and len(r_chunk) >= 5:
                                    chunk_len = min(16, name_len - offset)
                                    dev_name += bytes(r_chunk[4:4 + chunk_len]).decode('utf-8', errors='ignore')
                            if dev_name.strip():
                                name = dev_name.strip().replace('\x00', '')

                    r_type = send_and_wait([0x11, dev_idx, name_feat_idx, 0x20] + [0x00] * 16, expected_dev=dev_idx, timeout=0.3)
                    if r_type and len(r_type) >= 5 and r_type[2] != 0x8F:
                        hid_type_code = r_type[4]

                # 读取电量 (Feature 0x1004 / 0x1000)
                battery_pct = None
                r_ub = send_and_wait([0x11, dev_idx, 0x00, 0x01, 0x10, 0x04] + [0x00] * 14, expected_dev=dev_idx, timeout=0.4)
                if r_ub and len(r_ub) >= 5 and r_ub[4] > 0 and r_ub[2] != 0x8F:
                    ub_feat_idx = r_ub[4]
                    r_stat = send_and_wait([0x11, dev_idx, ub_feat_idx, 0x11] + [0x00] * 16, expected_dev=dev_idx, timeout=0.4)
                    if r_stat and len(r_stat) >= 7 and r_stat[2] != 0x8F:
                        battery_pct = r_stat[4]

                if battery_pct is not None and 0 <= battery_pct <= 100:
                    dev_type = classify_device(name, hid_type_code=hid_type_code)
                    devices.append({
                        "name": name,
                        "type": dev_type,
                        "battery": int(battery_pct),
                        "connected": True,
                        "mode": "2.4G"
                    })
        except Exception:
            pass
        finally:
            if h:
                try:
                    h.close()
                except Exception:
                    pass

    return devices


def get_all_devices():
    all_devs = []
    # 1. 2.4G 接收器
    all_devs.extend(get_logitech_24g_devices())
    # 2. 蓝牙设备
    for bt in get_bluetooth_devices():
        if not any(x["name"].lower() == bt["name"].lower() and x["connected"] for x in all_devs):
            all_devs.append(bt)
    return all_devs


if __name__ == "__main__":
    print("正在扫描外设电量...\n")
    devices = get_all_devices()
    if not devices:
        print("未发现无线键鼠设备。")
    else:
        for d in devices:
            status = "已连接" if d["connected"] else "休眠/离线"
            emoji = "⌨️" if d["type"] == "键盘" else ("🖱️" if d["type"] == "鼠标" else "🔌")
            print(f"{emoji} [{d['type']}] {d['name']} -> 电量: {d['battery']}% | 模式: {d['mode']} | 状态: {status}")
