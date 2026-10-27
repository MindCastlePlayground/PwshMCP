# 后端恢复集成验证

运行方式：先构建 `src/PwshMcp.Host` 的 Debug `net8.0-windows` 目标，再执行：

```powershell
python tests/integration.py
```

该脚本启动 broker、worker 和 MCP 子进程，通过真实命名管道和 PowerShell 完成端到端检查；不会启动托盘或触发 UAC。每轮使用随机 client 标识，所有本轮启动的进程都在 `finally` 中结束。

当前共 9 项检查：

1. Broker 握手及快照。
2. 普通任务执行并读取输出。
3. PowerShell 非终止错误被判为 `failed`，且结果错误文本包含指定内容。
4. Request ID 幂等，以及相同 ID 的不同请求内容被拒绝。
5. 管理员授权待批时普通任务仍可执行；拒绝后普通任务仍可执行。管理员批准在非提升进程中被拒绝。
6. 取消运行中的任务。
7. Worker 被终止后任务成为 `outcomeUnknown`；新 Worker 可执行后续任务。
8. Broker 重启后运行中任务成为 `outcomeUnknown`，待批授权变为 `expired`；重复提交返回旧任务 ID，重启不会自动重跑，新的普通任务仍可执行。
9. 经真实 stdio MCP 完成 initialize、tools/list、tools/call 到 Broker、Worker 和 PowerShell 的链路。

此测试使用当前用户的 PwshMCP Broker 命名管道及本地持久状态文件。测试前应确保没有其他 Broker 实例占用单例；脚本不修改全局配置。验证结果是本机端到端证据，不等于已验证托盘、UAC 提权或其他用户会话。
