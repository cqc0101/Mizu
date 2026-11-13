# 使用与功能说明

一个原生 Windows 桌面状态小组件。WPF + C# / .NET 10，生产程序不依赖 Python、浏览器、Node.js 或 WebView。

## 使用

解压对应架构的便携包，双击 `Mizu.exe`。整个文件夹都需要保留。

0.2.3 新增深蓝、浅粉、浅紫主题。外设滚轮分页不循环，合并高频/细粒度输入，保持设备顺序，减少快速来回跳动。便携包根目录仅保留 `Mizu.exe`、`app/`、`说明与许可/`、`使用说明.txt`。依赖与官方驱动安装包收在 `app/` 中，请完整保留；设置与待办自动沿用。本版根启动器使用 Windows 内置 .NET Framework 4.x，主程序仍自带 .NET 10 运行时。启动器也支持转发诊断参数和路径。

- 常见 Intel / AMD 电脑选择 **win-x64**。
- 32 位 Windows 10 选择 **win-x86**。
- Windows on ARM 选择 **win-arm64**；CPU 温度暂不支持此架构。
- 设计运行目标为 Windows 10 1607+ / Windows 11。实际跨机器、跨固件兼容性仍需实机验证；不承诺所有 Windows 10 构建、驱动与硬件都可读取所有指标。

组件只展示图标、数字、必要单位和外设名称。鼠标悬停出现操作按钮；双击打开设置；拖动移动位置。设备超过两个时，在组件上滚动切换。

`F5` 刷新，`Ctrl+,` 设置，`Esc` 收到托盘。`Ctrl+Alt+M` 显示组件并取消鼠标穿透；若热键被其他程序占用，可从托盘恢复。托盘菜单可退出。关闭设置窗口不退出组件。

首次进入设置：选择城市、检查设备分类、按需启用 CPU 温度与推送。不会自动定位、自动开启推送或自动注册开机启动。

0.2.1 优化悬停提示：与浅色 / 深色主题同步的圆角小提示、细边和轻阴影，停留 800ms 后出现；长名称自动换行，离开即关闭。外设提示保留名称、连接方式与状态，协议诊断仍可从设置导出。

0.2.2 新增组件字号 85–125%、五档字重和可关闭的平滑缩放。组件大小连续调节，不再按 5% 跳变；每个渲染周期合并预览更新，350ms 停止调节后保存配置。缩放仅动画内容变换，150ms 缓出；连续操作从当前大小接续，结束后恢复矢量清晰度与准确窗口边界。动画期间共用一次缓存与预留透明窗口，不逐帧重建待办或调整原生窗口。

## 自定义内容

设置采用侧栏导航，修改即时生效并自动保存。

- **外观**：浅色 / 深色、圆角 R 0–64、玻璃浓度 15–95%、缩放、置顶、鼠标穿透。当前为半透明材质，无实时背景模糊。
- **显示内容**：天气、外设电量、CPU、内存、温度、待办分别开关；支持精简布局、趋势图与连接图标开关。模块关闭后自动收起，内容过高时按屏幕可用高度缩小。
- **天气**：当前温度、今日高低温、小时预报、多日预报可任意组合；可设 1–12 个时段、间隔 1–6 小时、包含当前小时、未来 1–7 天（明天起）。时间使用所选城市当地时间。
- **待办**：组件中勾选完成，悬停后显示输入栏，Enter 添加；设置内可编辑、删除、清理完成项、隐藏完成项与调整 1–8 行高度。超出行数可滚动；最多 200 项，仅本地保存。
- **温度驱动**：完整便携包内附原样官方签名 PawnIO 2.2.0 安装程序。设置 → 设备 → 安装内置温度驱动；Windows 会请求管理员授权。成功后点击“以管理员身份重启”刷新检测。若 Windows 要求重启电脑，请保存工作后手动重启。组件不会启动时自动安装驱动。

温度读取目前支持 **x64 Windows 10 1809+ / Windows 11**；x86 程序在 x64 Windows 上也可使用。32 位 Windows 和 ARM 系统暂不支持温度；其他组件功能仍可使用。已安装的 PawnIO 会被复用，不会为 Mizu 创建第二份驱动，也不会退出时卸载共享驱动。

## 已实现与边界

| 功能 | 实现 | 限制 |
| --- | --- | --- |
| 玻璃外观 | 逐像素透明无边框窗口、统一圆角裁剪、暖灰/墨绿、缩放、悬停按钮 | 0.1.1 移除了可能在圆角外露出方框的系统 Acrylic 层；目前为半透明渐变材质，无实时背景模糊或光学折射 |
| CPU 负载 | `GetSystemTimes` 差分，每 2 秒采样 | 超过 64 逻辑处理器的多处理器组机器需要进一步扩展统计 |
| 内存 | `GlobalMemoryStatusEx` 物理内存占用 | 与任务管理器的某些内存口径可能略有差异 |
| CPU 温度 | 可选 LibreHardwareMonitor 0.9.6，仅 CPU 传感器，内附官方 PawnIO 2.2.0 安装包 | x64 Windows 1809+，设置内手动安装并以管理员运行；不保证所有 CPU 均有传感器，无读数时显示 — |
| 蓝牙键鼠 | 原生 SetupAPI / Windows 电量属性，检查同一物理容器的类型 | 同时涵盖 Windows 上报的 BLE / 经典蓝牙节点；缓存刷新由 Windows/固件决定，并非所有设备都提供电量。未知连接状态不视为在线 |
| 通用 HID | 标准 BatteryStrength Feature usage | 只覆盖主动暴露此标准 Feature 的设备 |
| 罗技 | HID++ 2.0，电量 `0x1004` / `0x1000`，名称/类型 `0x0005`，接收器槽位 1–6 与直连 FF | 面向支持相应协议的 Bolt、Unifying、Lightspeed 设备；旧 HID++ 1.0、电压估算、只上报粗略档位的精确百分比未实现。已在本机 K98M / Bolt（046D:C548）上读到 95%；其他型号仍需验证 |
| ATK / VXE | Compx FF02/FF04 和 BitMouse FF05，硬件 ID 白名单、校验码、在线探测 | 包含 A9 Ultra、A9 Ultra Max 2.0 等官方型号条目；已在本机 A9 Ultra Max 2.0 三模版接收器（373B:11FE）上读到 75%；其他型号与固件仍需验证。型号条目数不是实测兼容设备数 |
| 天气 | Open-Meteo 地理搜索、当前温度、小时与未来 7 天预报 | 网络服务可能不可达；离线保留已取到的数据。重启后需重新联网；免费公共接口按其使用条款使用，商业发行需确认服务方案 |
| 推送 | PushPlus 微信渠道，低电量、高 CPU/内存、高温、持续时间、间隔、失败退避 | 用户填写 Token 并启用后才工作；只在程序运行时监控。成功响应表示服务器接受请求，不代表微信已送达 |

不存在能读取“所有 2.4G 设备”的 Windows 统一 API。厂商私有协议必须按硬件逐步适配；本版采用多个 provider 扩展覆盖面，不以随机 HID 报文探测未知设备，也不伪造读数。设备未返回百分比时显示 —。

## 数据、权限与网络

设置保存在 `%LOCALAPPDATA%\Mizu\settings.json`，日志同目录，限制大小。PushPlus Token 使用 DPAPI / CurrentUser 加密，不能直接迁移到其他 Windows 用户。电量缓存也保存在此目录，离线旧读数最多保留 24 小时并明确置灰。诊断不含 Token、原始 HID 路径和设备序列号；包含产品名称、VID/PID、类型、当前读数和哈希标识。

只有配置天气或推送时才访问对应服务：`geocoding-api.open-meteo.com`、`api.open-meteo.com`、`www.pushplus.plus`。无遥测或广告。仅在已安装 PawnIO 驱动、启用温度、具有管理员权限、x86/x64 平台时由 LibreHardwareMonitor 打开传感器；不修改系统驱动安全策略。

“开机启动”使用当前用户的注册表 Run 项，不需要管理员。“以管理员身份重启”仅由用户在设置中手动点击触发 Windows UAC。

## 编译与验证

需要 .NET 10 SDK。编译不要求安装 Visual Studio：

```powershell
dotnet build .\src\Mizu.Widget\Mizu.Widget.csproj -c Release
dotnet run --project .\tests\Mizu.Tests\Mizu.Tests.csproj -c Release
pwsh .\scripts\publish.ps1 -Runtime win-x64
```

发布为 self-contained 文件夹，终端用户无需另装 .NET；不对 WPF 使用 trimming。

```powershell
.\Mizu.exe --diagnose .\diagnostics.json
.\Mizu.exe --scan .\battery.json
.\Mizu.exe --render-preview .\preview.png
.\Mizu.exe --smoke .\smoke-output
```

`--render-preview` 是**明确使用示例数据**的外观渲染；正常启动和 `--smoke` 使用真实系统采集，不会回退到示例数据。`--smoke` 会短暂创建组件与托盘，完成后退出。命令行模式输出到指定 JSON 文件，不依赖 WinExe 标准输出。

`get_battery.py` 是保留的 Python 兼容入口，仅调用已编译的原生程序；原始 demo 备份在 `docs/get_battery.original.py`。

架构与扩展见 [docs/architecture.md](architecture.md)，demo 审查见 [docs/demo-review.md](demo-review.md)。

## 来源与许可

本项目 MIT。依赖和协议来源见 [THIRD-PARTY-NOTICES.md](../THIRD-PARTY-NOTICES.md)。

- [Windows BatteryLife 属性](https://learn.microsoft.com/windows/win32/properties/props-system-devices-batterylife)
- [罗技公开 HID++ 文档](https://github.com/Logitech/cpg-docs/tree/master/hidpp20)
- [ATK 官方驱动协议数据](https://bpcdn.atkgear.com/hub-v3/production/3.2.29/static/filter-BRWy5Cag.js)
- [atkctl 协议说明及 MIT 实现参考](https://github.com/cobanov/atkctl/blob/main/docs/PROTOCOL.md)
- [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
- [Open-Meteo / 数据署名](https://open-meteo.com/en/docs)
- [PushPlus 消息接口](https://www.pushplus.plus/doc/guide/api.html)
