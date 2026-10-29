# 授权结束状态

2026-10-29，代码提交 `02d3ab2`。

| 原因 | 中文 | English |
| --- | --- | --- |
| 请求等待批准超过 24 小时 | 已超时 | Timed out |
| 批准后的授权达到所选有效期 | 已结束 | Ended |
| Windows 登录会话结束 | 已过期 | Expired |
| 后台代理重启，使原请求或授权失效 | 已过期 | Expired |

列表中的“已结束”分组标题继续表示全部非活动记录；卡片上的状态表示具体结束方式。撤销、拒绝的状态不变。等待授权超时的命令也显示“已超时”。

协议保留原有 `AccessState.Expired`，通过新增 `AccessInfo.EndReason` 保存原因。旧记录没有原因时继续显示“已过期”，不根据时间猜测历史原因。已保存的结束原因在后台重启后保持不变。

## 验收

- `dotnet run --project tests/PwshMcp.Status.Tests`：通过。覆盖等待超时、授权到期、会话失效、重启失效、原因持久化、旧数据回退和中英文标签。
- `scripts/build.ps1 -Output artifacts/status-package`：通过。
- `dotnet artifacts/status-package/PwshMcp.Tray.dll --self-test artifacts/status-selftest`：815 项通过，0 项失败。
- 本记录确认源码及构建产物验收；本次尚未替换本机 Program Files 中的安装版本。
