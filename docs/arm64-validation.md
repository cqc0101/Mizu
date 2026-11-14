# ARM64 云端验证

没有 ARM 电脑时，可以用 GitHub Actions 的 `windows-11-arm` 托管运行器检查 ARM64 便携版。Mizu 仓库是公开仓库；GitHub 官方说明，公开仓库可免费使用标准托管运行器。[运行器说明](https://docs.github.com/en/actions/reference/runners/github-hosted-runners)

## 手动运行

1. 打开仓库的 [Actions 页面](https://github.com/cqc0101/Mizu/actions/workflows/arm64-validation.yml)。
2. 左侧选 **Windows ARM64 validation**，点 **Run workflow**。
3. Release tag 保留 `v0.5.0`，再点绿色 **Run workflow**。
4. 等工作流结束后打开运行记录。绿色勾表示检查通过；红色叉号旁的步骤会显示失败信息。
5. 在运行记录下方下载 `arm64-validation-v0.5.0`，里面有启动检查摘要和组件渲染图。工件保留 14 天。

以后验证新版本时，先发布对应的 `win-arm64.zip`，然后把输入框中的 `v0.5.0` 换成新版本 tag 再运行。

## 工作流检查内容

- 从指定 Release 下载 ARM64 便携 ZIP 并解压。
- 用附件 `SHA256SUMS.txt` 校验 ZIP。
- 检查 `app/Mizu.exe` 的 Windows PE 架构标记确实是 ARM64。
- 确认进程实际以 ARM64 运行，在 GitHub 托管的 Windows on ARM 环境启动组件的短时检查，确认程序退出正常，并生成组件截图与运行报告。

这能验证发布包中的 ARM64 程序在云端 ARM Windows 环境可启动和渲染。它不连接你的外设，不能验证蓝牙 / USB 电量、CPU 温度驱动或其它实体机器的兼容性，也不是可长期登录的远程桌面。

工作流只需仓库读取权限；验证结果作为 Actions 工件保存，不会修改 Release 或源码。
