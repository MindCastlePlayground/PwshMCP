# PwshMCP

Windows 本地 PowerShell MCP 与授权托盘。一次主动开启可信管理员会话，后续命令无需反复 UAC；默认每条命令重建环境并启动独立 PowerShell。

[GitHub](https://github.com/MindCastlePlayground/PwshMCP) · [下载发布包](https://github.com/MindCastlePlayground/PwshMCP/releases)

基线：[B1](docs/BASELINE.md)。开发计划：[DEVELOPMENT](docs/DEVELOPMENT.md)。设计：[可信会话](discussion/DESIGN-TRUSTED.md)。UI 设计与实现由 Claude 负责，接口对齐见 [UI-ALIGNMENT](docs/UI-ALIGNMENT.md)。

## 构建与运行

需要 Windows、.NET 8 SDK；运行已构建文件需要 .NET 8 Desktop Runtime。优先使用系统安装的 PowerShell 7，找不到则使用系统 Windows PowerShell。

```powershell
powershell -NoProfile -File scripts/build.ps1
artifacts/app/PwshMcp.Tray.exe --show
```

托盘自动绑定已有 broker；没有时启动本地 broker，不弹 UAC。0.4.0 的桌面安装方式在安装时注册管理员协调器，Agent 请求管理员执行时仅改变托盘待办状态；在主窗内选择期限并批准，不再弹 UAC 或第二个批准窗口。协调器未安装/离线时显示修复提示，不自动回退提权。每条未指定 session 的命令仍使用独立进程与新环境。

0.3.0 界面支持中文和 English，可在标题栏或托盘菜单切换，选择会保存。启动参数 `--language en` / `--language zh` 可指定本次语言；批准窗口沿用所选语言。切换语言不重启 Broker，也不改变授权。

列表优先展示任务名、命令描述和客户端显示名，诊断 ID 默认折叠；审批处仍直接显示真实授权范围。命令视图按宽度折行并提供 PowerShell 高亮，“复制命令”保留原始文本。超过 20000 字符的代码以纯文本显示；混合换行的部分选区会统一换行风格，复制全部保持原文。

离线布局与代码控件检查：`dotnet artifacts/package/PwshMcp.Tray.dll --render-check artifacts/ui-0.3.0`。该入口使用合成数据，不显示窗口、不连接 Broker 或触发 UAC；报告与 PNG 输出到指定目录。

## MCP 配置

将下面 exe 路径改成构建/安装后的绝对路径。`--client` 为稳定客户端名，同一客户端重连不会仅因进程变化重新请求授权。不同 Agent 使用不同名字用于授权管理；同用户进程隔离不属于该名字提供的安全保证。

0.3.0 可在启动参数中加入 `--display-name "我的开发助手"`，让托盘显示可读名称，内部 `--client` 授权键保持不变。未配置时使用 MCP 初始化的 `clientInfo.title` 或 `name`。显示名称不提供身份认证；重名不会合并权限。任务详情仍可查看内部键以核对授权范围。

```json
{"mcpServers":{"pwsh":{"command":"H:\\Documents\\AgentAbility\\PwshMCP\\artifacts\\app\\PwshMcp.Host.exe","args":["mcp","--client","codex"]}}}
```

工具：run、status、output、cancel、grants、request_access、session_reset、session_close、context_set、context_clear、env_info。run 默认普通权限，`privilege=elevated` 请求管理员权限；有 session 时保留 shell 状态，无 session 时每次 fresh。任务立即返回 ID，后续查状态与输出。

0.2.0 起，`run`、`session_reset`、`session_close` 必填 `description`，用于审计动作和目的。首次管理员请求必填 `taskName`（`request_access` 或没有有效/待批授权时的 elevated run）。例如 `run(code="Get-Service", description="列出服务状态，定位后台服务未启动原因", privilege="elevated", taskName="排查后台服务")`。后续命令仍需独立描述。更新后请重新连接 MCP 以刷新工具定义；旧客户端缺字段会收到明确错误。描述和任务名会持久化，请勿包含秘密。

模型名识别和指定 Agent 会话预批准的可行性见 [AGENT-IDENTITY](docs/AGENT-IDENTITY.md)；当前授权仍按客户端归属，任务名不是权限边界。

已实际验证从宿主结构化日志取得模型名称的方法，见 [MODEL-EVIDENCE](docs/MODEL-EVIDENCE.md)。不接受调用模型自报名称；尚未自动关联每次 MCP 调用，模型名也不参与授权。

`context_set(session="dev", env={...})` 保存显式环境覆盖；`run(context="dev", ...)` 在保持每次新进程的同时使用这些覆盖。`run(session="dev", ...)` 则明确保留 shell 变量、函数和工作目录；环境或覆盖变更后用 `session_reset` 重建。每次执行结果带环境快照 ID 和实际执行身份，不输出环境秘密值。

仅元数据持久化，命令正文、环境值和输出不写入状态档案。broker 重启使授权失效，未完成任务标记结果未知，不自动重跑；历史输出不恢复。普通授权不允许升级成 SYSTEM。

任务记录最多保留 1000 条；达到上限时清理最旧的已结束记录及对应去重键，仍在运行/等待的任务不会被清理。长期任务应使用独立 `requestId`，去重保证仅限保留期间。

## 桌面安装：安装时一次 UAC（推荐）

在当前管理员用户的提权 PowerShell 中运行 `scripts/install-desktop.ps1 -UserSid <当前用户SID> -Source artifacts/package`。安装到受保护的 `Program Files\PwshMCP`，注册当前用户交互登录、最高权限运行的 `PwshMCP-Admin-<SID>` 计划任务。程序不保存账户密码，不以 SYSTEM 执行命令。普通托盘从同一目录启动，MCP command 也需改为该目录的 Host.exe。

安装或替换特权程序需要管理员授权；日常批准和后续命令不再请求 UAC。用户必须已登录且属于管理员组，锁屏继续工作，注销结束管理员执行能力。当前授权按客户端和本次登录会话生效；客户端名称并非经过认证的模型身份，同用户恶意进程不构成此产品承诺的隔离边界。

协调器只接受安装目录托盘进程的批准/恢复请求，校验真实进程路径、用户 SID 和 Windows 会话；MCP 没有批准接口。协调器启动还校验安装目录所有者、写权限和重解析点。授权撤销后不再派发新命令；已运行命令通过取消按钮单独停止。

卸载桌面任务：管理员运行 `scripts/uninstall-desktop.ps1`；`-RemoveFiles` 同时删除经过路径检查的安装文件。安装器与旧 Windows 服务模式互斥。

## 服务安装（兼容运行方式）

先完成构建、退出本地托盘及其 broker，再由用户主动在管理员 PowerShell 中运行 `scripts/install.ps1`。安装到 Program Files，设置受保护 ACL 并创建开机服务；`-StartAtLogon` 为当前安装账号添加托盘登录启动。不同账号提权时不要使用该开关为原用户配置登录启动。

更新沿用安装脚本，保留前一版文件备份；应先结束运行任务。卸载执行 `scripts/uninstall.ps1`；`-RemoveFiles` 删除经过路径校验的安装目录，保留 ProgramData 元数据。脚本执行过的系统修改不会回滚。

服务模式与桌面绑定模式采用同一授权协议。服务运行不代表可以绕过用户授权；实际管理员脚本仍由用户主动启动的管理员 worker 执行。跨重启的通用 SYSTEM 执行未作为默认入口开放。

## 验证边界

自动化覆盖执行环境、MCP、管道、去重、取消及未提权不能批准。真实 UAC、锁屏/注销、Explorer 重启、不同账号和服务安装单独验收，结果见后续验收记录。代码构建不等于这些路径已经通过。

本轮记录见 [ACCEPTANCE](docs/ACCEPTANCE.md)。最终包位于 `artifacts/package`，当前已打开的验收实例位于 `artifacts/app`；切换到最终包前先从旧托盘退出，避免单实例机制仍显示旧窗口。可用 `scripts/build.ps1 -Output artifacts/package` 重建；安装该包时使用 `scripts/install.ps1 -Source artifacts/package`。
