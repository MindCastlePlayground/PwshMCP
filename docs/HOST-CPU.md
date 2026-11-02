# Host CPU 修复验收（2026-11-02）

高负载现场的线程采样定位到 Peer.Read → Process.SessionId → ProcessManager.GetProcessInfo → NtProcessInfoHelper.GetProcessInfos；每个请求的会话查询触发系统进程信息查询。

改用 Windows ProcessIdToSessionId 查询指定客户端的会话；查询失败仍拒绝请求。管道模拟、令牌身份与管理员权限判定保持原逻辑。

- Release 构建成功；Peer 身份/会话/权限及 await 后模拟令牌隔离测试通过；24 项历史存储检查通过。
- 已替换安装版 Host DLL 和 PDB，旧文件已备份。托盘、现有客户端与依赖文件保留。
- 托盘自动恢复 broker，原有客户端继续轮询；短时实测 CPU 消耗速率相较修复前下降约 99%。
- 安装版 snapshot 正常返回；真实 MCP 普通权限命令执行成功，退出码 0，output 返回 `pwshmcp-cpu-fix-ok`。

本次安装包含此前已提交的 Host 历史归档实现，恢复后的内存任务摘要为 100 条，已完成任务保留在加密历史记录中。协调器重启会结束已有管理员授权；后续管理员任务按既有流程重新批准。
