# 安装版与便携版

安装版与便携版使用同一份应用程序。常见 Intel / AMD 电脑选择 x64，独立 32 位 Windows 选择 x86，Windows on ARM 选择 ARM64。

安装程序默认只为当前 Windows 用户安装到 `%LOCALAPPDATA%\Programs\Mizu`，创建开始菜单入口，可选桌面快捷方式，并在系统“已安装的应用”中提供卸载入口。安装主程序不要求管理员权限。更新前请从托盘退出正在运行的 Mizu。

两种版本共用 `%LOCALAPPDATA%\Mizu` 设置与待办。卸载保留这些数据；也不会卸载可能被其他软件使用的共享 PawnIO 驱动。温度驱动仍由用户在组件设置里主动安装，不会随安装版静默安装。

当前发布的 Mizu 安装程序未购买代码签名证书，Windows 可能显示未知发布者；官方 PawnIO 安装程序保留原厂签名。可使用 Release 附带的 `SHA256SUMS.txt` 核对下载文件。

## 构建安装程序

安装 [Inno Setup 6.7.3 或更新版本](https://jrsoftware.org/isdl.php)，先发布对应便携目录，再运行：

```powershell
pwsh .\scripts\build-installer.ps1 -Runtime win-x64 -Compiler 'C:\Program Files (x86)\Inno Setup 6\ISCC.exe'
```

可用 `-PackageDirectory` 指定现有便携包目录，`-OutputDirectory` 指定输出目录。三个架构分别编译；ARM64 与独立 32 位 Windows 的安装运行仍需对应实机验证。
