# TaskHub 守护接入状态

2026-10-29：实现已提交并推送 `ccc21c4`；尚未执行安装。

- `scripts/install-taskhub.ps1`：管理员安装入口，配置和启动项备份、普通权限 daemon 任务、失败回退。
- `scripts/check-taskhub.py`：真实 Hub 状态和可选托盘异常恢复检查。
- PowerShell 解析、空/非空 daemon 清单转换、Python 编译检查通过。
- 真实 Hub 接入前状态成功：CC Switch run `92dd7d78-a38b-4bc9-a712-286322e5380e`，音量控制台 run `b7e19fb3-cde1-4f2b-8102-3d0e8d349b70`。基线位于本地 `artifacts/taskhub-before.json`。
- 当前管理员授权已过期。已发送托盘请求“PwshMCP 接入 TaskHub 守护”；检查时仍为 pending。未修改已安装的 TaskHub 配置、启动项或计划任务。
- 批准后：通过 MCP 执行安装脚本，核对正式配置/计划任务/普通权限和唯一托盘；执行真实 Hub 状态及异常退出恢复验证；检查 MCP 普通命令与其他 daemon PID，补充并推送结果。TaskHub 正式配置文档也须同步新增项。
