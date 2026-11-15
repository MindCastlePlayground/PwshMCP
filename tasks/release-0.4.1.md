# PwshMCP 0.4.1

从 0.3.0 升级：

- 桌面管理员协调器：安装时授权，日常管理员请求在托盘批准；包含连接恢复入口。
- 双语托盘改进：通知布局、任务详情、命令与输出按需加载、简化状态图标。
- 已完成任务加密归档，减少托盘和 Broker 常驻内存；修复 Host 高频轮询导致的 CPU 占用。
- 提供托盘更新脚本；统一组件版本与安装回执为 0.4.1。

## 安装 / Installation

需要 Windows 和 .NET 8 Desktop Runtime。解压 `PwshMCP-0.4.1-win.zip`，在解压目录按 README 的桌面安装步骤执行，安装源为 `artifacts/package`。升级前结束安装目录中的运行进程；升级会中断现有连接与授权。更新后重新连接 MCP。

Requires Windows and .NET 8 Desktop Runtime. Extract the ZIP and follow the desktop installation steps in README.en.md from the extracted directory, using `artifacts/package` as the source. Stop processes in the installation directory before upgrading. Reconnect MCP after the update; existing connections and grants do not survive the restart.

## 验证范围 / Verification

历史安装和交互验证见 `docs/ACCEPTANCE-0.4.0.md` 与 `docs/UI-POLISH-2026-11-06.md`。本次发布的构建与检查结果见 `docs/RELEASE-0.4.1.md`。本次不替换本机正在运行的安装。

Historical installation and interaction evidence is recorded in the documents above. Current release checks are recorded in `docs/RELEASE-0.4.1.md`. This release operation does not replace the running local installation.
