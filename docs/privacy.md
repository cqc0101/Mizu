# 数据与权限

- 本地目录：`%LOCALAPPDATA%\Mizu`，包含设置、待办、日志和电量缓存。源码仓库与发布包不包含该目录。
- 推送 Token：通过 Windows DPAPI / CurrentUser 加密保存。加密配置仍属于私人文件，不应上传。
- 天气：设置城市后请求 `geocoding-api.open-meteo.com` 与 `api.open-meteo.com`，无自动定位。
- 推送：用户填写 Token 并启用后，请求 `www.pushplus.plus`；只在组件运行时监控。
- 温度：用户主动安装官方 PawnIO 后，按需以管理员身份读取传感器；不自动安装驱动或修改系统安全策略。
- 开机启动：用户选择后写入当前用户注册表 Run 项。
- 无遥测或广告。诊断包含型号、VID/PID、当前读数及哈希标识，不包含 Token、原始 HID 路径与序列号；公开提交前仍请查看内容。

依赖、服务条款与驱动来源见 [第三方声明](../THIRD-PARTY-NOTICES.md)。
