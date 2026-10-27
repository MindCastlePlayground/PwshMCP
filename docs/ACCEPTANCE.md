# 当前交付与验收记录 · 2026-10-27

基线 B1 提交 `e550785`；这是一次完整实现的验收记录，不是分批开发路线。

## 已实现

- Windows 命名管道 broker、MCP stdio、普通/管理员 worker、每条命令独立 PowerShell 和 fresh 环境、显式持久 shell。
- 同一 Windows 用户/登录会话的可信客户端授权。普通进程不能批准；用户在托盘主动启动 UAC，提权审批程序批准后启动同用户管理员 worker。授权覆盖同客户端当下和后续命令。
- Claude Opus 5.5 medium 完成所有托盘、中文窗口、交互文案、图标和审批界面；接口 A1–A9 已双方对齐。
- 真实控件检查发现的可访问名称、快捷键文案和请求时间标注已再次交由 Claude 修复；修复后编译通过。最终包在 `artifacts/package`，运行中的旧验收实例保留以避免打断用户手动批准；退出旧托盘后才能切换新包。
- 待批图标、请求/授权/任务视图、拒绝/撤销/取消、过期、离线/恢复入口；可选服务安装卸载脚本。
- 去重、取消、退出时子进程清理、broker 重启后保留结果未知且不自动重跑。持久化只保留元数据，不保留命令正文、环境值和输出。

## 已实测

| 项目 | 结果 |
|---|---|
| Host、Tray Release 发布 | 成功；编译 0 警告、0 错误 |
| Execution 控制台测试 | 15 项通过：用户环境修改/删除、覆盖、错误状态、native exit、输出上限、独立进程、持久状态/目录/重建、取消及恢复、直接输出 |
| Environment 控制台测试 | 通过：执行用户身份、系统与用户 PATH、命令查找、字面覆盖、删除及恢复 |
| `tests/integration.py` | 9 项通过：真实管道、普通执行、错误状态、幂等冲突、待批不阻塞普通任务/拒绝、取消、worker 退出恢复、broker 重启恢复、MCP initialize/list/call 全链路 |
| 真实桌面进程 | 托盘已打开；可枚举窗口；accessibility 显示“已连接 / 本地代理（由托盘启动）”，新增请求计数 1、任务计数 2 |
| 图标导出 | 执行 Claude 的 `--export-icons` 成功，包含多尺寸 ICO 和各状态 PNG |

## 尚未通过的真人/系统验收

- 真正的 UAC 批准、一次批准后连续管理员命令：已排队仅读取 admin 标志和 PID 的测试，请用户手动批准。`python scripts/acceptance.py` 在批准后验证三个任务同授权、三个独立进程和环境快照；未批准时只返回 needsApproval。
- 视觉与点击：Computer Use 控件读取成功，但截图连续报 FrameArrived / window capture timeout，按工具指引刷新一次仍失败；点击报 coordinate input geometry is unavailable。不能据此声称视觉、布局、高 DPI 或键盘验收通过。权限访问已不再需要，已提醒用户手动收回；尚未确认撤销。
- 服务安装/卸载、注销/重登、锁屏、Explorer 重启、跨账号：有实现/脚本，未在用户系统上执行这些会影响当前工作的验收。
- 管理员授权只允许同用户分离令牌；标准账户输入另一管理员账号的凭据不会转为那个账号执行。

## 继续验收

1. 打开托盘“请求”，核对 `artifacts/desktop-acceptance.json` 里的 client；点击“以管理员身份批准…”，通过 UAC 后确认授权窗口。
2. 运行 `python scripts/acceptance.py`，应返回 admin=true、freshProcesses=3、sameGrant=true。
3. 在托盘手动撤销该测试授权。当前正在运行任务可结束；撤销阻止新管理员任务，不回滚已执行效果。

首次真实授权必须由用户操作。computer-use/SKILL.md 引用的 docs/guidance.md 明确要求：`Do not act on security or privacy permission requests.`

## 运行边界

客户端名称用于管理授权，不能隔离同用户恶意程序；通用管理员脚本也不受路径白名单或回滚保证。生产使用安装到受保护 Program Files 路径；工作区开发产物可被当前用户修改。broker 路径核验只对受保护安装具有防替换意义。

宿主工具审批与 Windows UAC 独立；本 MCP 不改变 Agent 宿主自己的审批策略。运行脚本依然需要模型遵守原任务授权。
