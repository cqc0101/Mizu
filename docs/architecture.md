# 架构

```
Mizu.Widget (WPF, tray, alpha-clipped translucent material)
  ├─ WidgetEngine: background polling / UI dispatcher
  ├─ SettingsWindow: appearance / city / devices / PushPlus
  └─ Mizu.Core
       ├─ BatteryService → IBatteryProvider
       │    ├─ WindowsBatteryProvider
       │    ├─ StandardHidBatteryProvider
       │    ├─ LogitechBatteryProvider
       │    └─ AtkBatteryProvider (Compx / BitMouse)
       ├─ Native.WindowsDevices / HidChannel
       ├─ TelemetryService (GetSystemTimes, GlobalMemoryStatusEx, LHM)
       ├─ WeatherService (Open-Meteo)
       ├─ AlertRules → INotificationChannel → PushPlusChannel
       └─ SettingsStore (atomic file replacement, DPAPI)
```

## 调度

系统采样 2 秒、外设完整扫描 60 秒、天气成功后 20 分钟。已配对 BLE / 经典蓝牙使用 AEP DeviceWatcher 监听连接，HID 插拔与系统恢复也请求刷新；另有约 3 秒间隔的 Windows / ATK 快速路径，6 秒取消预算，事件合并防抖 180ms。完整扫描各通道先到先显示，ATK 快慢路径共享互斥；忙时保留一次待刷新请求。HID 传输超时 450ms，罗技双通道 550ms，扫描整体取消期限 40 秒。Windows 下罗技请求优先使用长报表（K98M/HID++ 4.5 实机必需），短、长报表分开接收，HID 流禁用写缓冲。原生读取与协议轮询在工作线程，UI 线程仅绘图和应用结果。

若设备短期没有响应，保留最多 24 小时（包括重启后的本机缓存）的旧读数并标记离线。通知必须在线且读数新鲜。Windows 上报电量仍受系统缓存约束；扫描时间不是设备真正测量的时间。

## 新增设备厂商

1. 新建 `IBatteryProvider` 实现，按 VID / PID / usage page 匹配已知控制接口。
2. 只发送经过文档/抓包确认的读取命令。不得扫描未知 Feature / EEPROM / 设置命令。
3. 明确报表长度、命令相关性、错误状态、校验码、在线状态、百分比范围与充电语义。
4. 用录制的响应帧补充 `Mizu.Tests`，验证截断、其他命令、离线、异常值与迟到响应。
5. 在 BatteryService 的 provider 列表注册。禁止 UI 直接依赖任何厂商报文字节。
6. 硬件验证后才更新兼容矩阵。当前 catalog 只是识别数据，不代表所有设备经过验证。

ATK catalog 可以添加同一已知协议的型号映射；这不使新协议自动兼容，也不能凭名称认定连接方式。接收器和蓝牙路径若缺少可靠统一身份，不使用名称强行合并。

## 新增推送渠道

实现 `INotificationChannel.SendAsync`，再在 NotificationService 中选择渠道。阈值、持续时间、冷却与失败退避属于 AlertRules，不放入渠道或视图。未来可加入用户自定义 webhook / Bark / ntfy；本版实际提供 PushPlus，不展示未实现渠道。

## 发布与后续验证

三个 RID 分开发布 self-contained 目录，附带完整官方 PawnIO 2.2.0 安装包与原始再分发说明。安装前校验固定 SHA256，并持有只读共享句柄，防止校验后文件被替换。仅用户点击时调用签名默认版安装器 `-install -silent`，由 UAC 授权；区分 0、3010 和失败退出码。安装后的 Registry 检测不缓存，但 LibreHardwareMonitor 的驱动版本会在进程内缓存，所以首次安装后必须重启 Mizu。驱动安装流程不随设置窗口关闭而被强行终止。温度仅支持 x64 Windows 1809+；按操作系统架构判断，以免 ARM 上的模拟 x86 程序错误启用。

WidgetLayout 按可见模块计算区域；WPF 材质、边线、工具栏统一圆角裁剪。工具栏离开时立即关闭命中测试，再淡出。待办由独立交互层呈现，文本输入和复选操作不触发窗口拖动。显示开关独立于采样及已配置提醒。

下一轮硬件验收需涵盖：K98M 蓝牙/接收器切换、A9 Ultra Max 的 VID/PID 和固件、睡眠/唤醒、拔插、同名多设备、厂商驱动并行、无管理员、Windows 10 透明效果关闭、Windows 11 内存完整性开启、多显示器不同 DPI、x86 与 ARM64 实机。编译通过不等于这些验收均完成。

## 0.5.0 多目标网络

最多 8 个目标，最多 4 个并发 Ping，各有独立 60 样本窗口；每轮仅读取一次网卡计数。删除目标清除其窗口，编辑其他目标不重置已有历史；网卡变化整体重置。隐藏 IP 同时作用于绘图与提示。蓝牙地址仅在内存中按地址或有效容器标识匹配 Windows 节点，不按名称匹配，不写入诊断或缓存。
