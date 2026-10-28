# TaskHub 桌面守护

使用现有 TaskHub Windows Worker 的 daemon 功能管理已安装的 PwshMCP 托盘，不将 MCP stdio 进程作为常驻任务。stdio 仍由各 Agent 客户端按连接启动。

## 范围

- 任务 `daemon.pwshmcp`；计划任务 `TaskHub Daemon pwshmcp`。
- 程序 `C:\Program Files\PwshMCP\PwshMcp.Tray.exe`，不带 `--show`，在登录用户的普通权限会话启动。
- TaskHub host 在托盘退出后等待 5 秒再启动。host 本身由登录和周期计划任务恢复。
- 托盘负责连接或启动本地 Broker。现有 `PwshMCP-Admin-<SID>` 管理员协调器任务保持独立，不改变 UAC、授权或执行权限。
- 不重新安装 Worker/host，不更换凭证，不部署远端 Hub。

## 安装

先完成 PwshMCP 桌面安装，并确保 TaskHub Worker 已安装、当前无运行任务。在同一登录管理员用户的提升权限 PowerShell 中执行：

```powershell
powershell -NoProfile -File scripts/install-taskhub.ps1
```

默认 TaskHub 源目录 `H:\Documents\Abyss\TaskHub`，运行目录 `C:\ProgramData\TaskHub`，可用 `-TaskHub` / `-Root` 指定。安装器备份实际 Worker 配置、安装源配置和 PwshMCP 的 HKCU Run 值，登记清单与受 ACL 保护的桥接目录，移除重复自启，接管当前托盘并重启空闲 Worker。重复安装须先核对已有状态，脚本不会覆盖已有 daemon。

PwshMCP 桌面升级安装器可能重新写入 HKCU Run 项；升级时先通过 TaskHub stop，完成升级后核对并移除重复的 PwshMCP Run 项，再 start。

## 使用与影响

向 TaskHub Worker 提交 `task_id=daemon.pwshmcp`，`params.action` 为 `status`、`start`、`stop` 或 `restart`。关闭托盘会被恢复；持续停用请使用 TaskHub stop。

TaskHub 会终止托盘所在的进程树。托盘在守护下新建的 Broker 也属于该进程树；停止、重启或托盘崩溃后的清理可能结束 Broker，使授权失效、未完成作业结果不确定。**先结束执行任务，再进行停止或升级。** 接管前已经存在的 Broker 不属于新 host 的进程树，stop 也不承诺结束该 Broker 或管理员协调器。TaskHub stop 不是“撤销所有授权”；撤销仍使用 PwshMCP 授权管理。

没有用户交互登录会话时，不启动桌面程序。锁屏不等于注销。进程 running 只证明进程存在，不证明管道响应、界面或管理员执行器健康。

## 验收

2026-10-29 实测发现旧 TaskHub host 的输出处理会阻塞托盘退出检测：`exec.Cmd` 的 `Stdout/Stderr = io.Discard` 会建立复制管道，后代进程继承管道后，托盘退出仍不能结束 `Wait`。TaskHub 源码修复将这两个字段保留为 nil，直接连接空设备。使用旧 host 时，`running` 可能仍指向已退出的 PID；不要仅凭状态字段判断恢复成功。修复及安装进度见 [恢复验收记录](../tasks/recovery-acceptance.md)。

本机已为 `TaskHub Daemon pwshmcp` 单独安装修复版 host：`C:\Program Files\PwshMCP\taskhub-daemon-host.exe`，保留原 manifest 参数。共享 TaskHub host 和其他守护任务未替换。真实崩溃恢复已通过。后续重建任务时须继续使用修复版 host；仅重新运行旧 TaskHub 安装器可能退回未修复的共享 host。

```powershell
python scripts/check-taskhub.py --output artifacts/taskhub-status.json
# 仅在没有执行任务时，实际终止托盘并验证恢复：
python scripts/check-taskhub.py --recover --output artifacts/taskhub-recovery.json
```

检查通过真实 Hub 派发 status，核对新 PID、host PID 与恢复计数；凭证只在进程内从 TaskHub 已有 DPAPI 配置解密。再检查 MCP 普通命令可执行，并确认托盘仍为普通权限。

## 回退

先通过 TaskHub stop 并等待确认，禁用并停止 `TaskHub Daemon pwshmcp`。恢复安装器输出的备份中的 Worker 配置、TaskHub 安装源配置和原 HKCU Run 值，重启空闲 Worker，然后从普通权限会话启动原托盘。保留桥接状态、备份与凭证供排查；不要删除整个 TaskHub 数据目录。回退配置不恢复已结束的作业或授权。
