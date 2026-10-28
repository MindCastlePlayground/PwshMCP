# 本机安装记录（2026-10-28）

- 版本：0.3.0，当前用户安装到 `%LOCALAPPDATA%\Programs\PwshMCP\0.3.0`。
- Codex：实际 `CODEX_HOME/config.toml` 中添加全局 stdio MCP `pwsh`；命令为安装目录的 `PwshMcp.Host.exe`，参数 `mcp --client codex --display-name Codex`。配置变更前已在原目录备份，不修改其他 MCP。
- 当前用户 Startup 目录创建 `PwshMCP.lnk`，登录时启动托盘。托盘绑定本地 broker；未安装 Windows 服务。
- 原 artifacts/app 的 0.1.0 实例占用单实例入口。确认无活动任务、无有效授权后停止旧实例，安装版托盘和 broker 均已启动。
- 从 Codex 实际配置启动 stdio 进程：initialize 返回 0.3.0，tools/list 返回 11 个工具；run → status → output 返回 succeeded 和 `pwshmcp-installed-ok`，归属 codex、显示名 Codex。
- 验证仅执行普通权限命令，没有触发 UAC 或自动批准管理员权限。当前已打开的 Codex 会话需要重启客户端以重新加载 MCP 配置。

移除 Codex 接入：`codex mcp remove pwsh`。禁用登录启动：移除当前用户 Startup 目录的 `PwshMCP.lnk`；先从托盘退出再处理安装文件。
