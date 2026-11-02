# Grok 接入与故障定位

Grok 使用独立的客户端键和显示名，避免与 Claude 共用管理员授权及任务归属：

```powershell
grok mcp add --scope user pwsh -- 'C:/Program Files/PwshMCP/PwshMcp.Host.exe' mcp --client grok --display-name Grok
grok mcp doctor pwsh
```

配置后重新启动 Grok 会话，使其重新连接 MCP。托盘应显示 `Grok`；首次管理员请求需要用户批准，后续请求按授权有效期复用。

`doctor` 检查服务启动、协议握手和工具发现。执行结果通过 `run → status → output` 获取。`needsApproval` 表示等待托盘批准；Grok 返回 `user_cancel` 时，应先确认原任务状态再决定后续操作。

2026-11-03 排障发现 Grok 全局配置使用了 `--client claude --display-name "Claude Code"`。当次管理员请求进入待批状态，随后 Grok 宿主取消了后续工具调用。修正为上述独立配置后，真实 Grok 调用成功返回 `grok-pwsh-e2e-ok`，任务归属为 `grok / Grok`。

同次排障修复了系统 PowerShell 5.1 在 `Select-Object -First` 提前结束上游时，被执行器误判为失败的问题。执行器只排除该内部管道控制异常，保留实际 PowerShell 错误和外部程序非零退出码。

安装验收（2026-11-03）：修复后的执行库已替换到 `C:\Program Files\PwshMCP`，原文件保留为 `PwshMcp.Execution.dll.bak-20261103-grok`。真实 Grok 管理员任务经过托盘批准后执行 `Get-ChildItem | Select-Object -First 1`，并检查当前管理员令牌，最终状态为 `succeeded`、退出码为 `0`，输出包含 `grok-pwsh-admin-select-ok`。执行器相关测试共 16 项通过。
