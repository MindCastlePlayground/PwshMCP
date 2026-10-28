# 环境故障记录

## 2026-10-27：并行 UI 目录尚未创建

- `rg --files src/PwshMcp.Tray` 在 Claude 尚在设计时返回目录不存在。
- 后续先 `Test-Path` 再枚举；属于并行工作时序，不重试为代码故障。

## 2026-10-27：apply_patch 重复目标拒绝

- 同一补丁中对 DESIGN.md 同时 Delete File 与 Add File 被工具拒绝：multiple operations target。
- 未执行该补丁；改用单独新建修订文档，并在原文添加修订入口。以后同路径改写使用 Update File。

## 2026-10-27：只读 shell 启动失败

- 操作：读取当前目录及检索文件、相关记忆。
- 错误：`CreateProcessAsUserW failed: 5 (拒绝访问。)`。
- 已显式指定系统 Windows PowerShell，但工具错误中实际启动路径仍为 WindowsApps 下的 pwsh.exe。
- 有效降级：保持原只读命令范围，使用 `sandbox_permissions=require_escalated`，成功返回。
- 此降级仅表示越过工具沙箱执行限制，不证明进程取得 Windows 管理员令牌；不要对相同失败反复重试默认沙箱。

## 2026-10-27：读取未确认文件路径

- 读取 Claude 项目的 `App.xaml.cs` 失败；实际文件为 `App.cs`。后续先 `rg --files` 确认，不根据模板推测文件名。
- 一次追加本记录的补丁使用不存在的 `#` 上下文而校验失败；改用已读取的末行定位，未造成文件修改。
- 桌面技能的 API 文档在插件根目录 `docs/api.md`，不在 `skills/computer-use/docs`；已通过 `rg --files` 定位，后续使用真实路径。
- recovery_tests 子代理首条默认沙箱读取再次遇到错误 5；同范围 `require_escalated` 成功。主代理已给出故障背景，但子代理仍先触发默认路径；后续委派明确首条即使用已验证的执行通道。
- 桌面工具可枚举到已打开的 PwshMCP 窗口，但首次 `get_window_state` 返回 `Computer Use app approval timed out`；未获得截图。需要用户完成工具的应用访问许可后重试，不通过其他截图路径绕过许可。
- 后续应用访问已可读取控件，但截图先报 `FrameArrived timed out: timed out waiting on channel`，刷新窗口身份并激活后的单次恢复仍报 `window capture timed out: timed out waiting on channel`。点击请求页报 `coordinate input geometry is unavailable`。停止该输入路径；只将控件读取记为通过，视觉和点击验收待人工/捕获工具恢复。
- 后续审计字段任务首条只读命令仍遇到 `CreateProcessAsUserW failed: 5`；同范围 require_escalated 重跑成功，不重复默认启动路径。
- 2026-10-28：显式系统PowerShell仍被执行器映射到WindowsApps pwsh，默认沙箱CreateProcessAsUserW失败5；原命令require_escalated成功。
- 2026-10-28：读取尚未交付的tasks/readable-api-result.md导致组合检查返回1；后续先Test-Path再读取。发布代理误写UI已真实渲染，主代理回读后按原验收证据更正Release说明。
- 2026-10-28：Claude UI任务最终403额度不足，进程退出1；stream result的subtype=success不足以判断完成，必须检查is_error及result。未重试耗尽渠道或替换模型；半成品保存到wip/bilingual-ui-0.3，待额度恢复继续。
- 2026-10-28：PowerShell 命令行中使用了 Bash 风格的 `{a,b}` 路径展开，触发 ParserError（“参数列表中缺少参数”）；后续使用 PowerShell 数组或逐个明确路径，不再使用该写法。
- 2026-10-28：apply_patch 同一补丁中删除并新增同一路径被拒绝（multiple operations target）；改为单个 Update File，原文件未被更改。
- 2026-10-28：gh release create 的 --target 使用短 SHA 返回 HTTP 422 target_commitish invalid；改为 git rev-parse HEAD 取得完整 SHA，后续发布固定使用完整 SHA。
- 2026-10-28 本机安装：默认沙箱首条读取仍报 CreateProcessAsUserW failed: 5；同范围 require_escalated 成功。新托盘启动被旧 artifacts/app 实例的单实例机制拦截；先核对旧 broker 无活动任务和有效授权，再关闭旧实例、启动安装版，并回读进程路径验证。
- 2026-10-28 0.4.0：安装器/验收脚本子代理的默认沙箱读取再次失败 CreateProcessAsUserW 5，require_escalated 成功；后续委派需明确首次使用已验证通道。Claude UI 调用遇到上游 503，保留原进程等待其有界重试，不并发重启或替换模型。
- 2026-10-28 0.4.0：137a363 提交后的 git push 遇到 GitHub SSL_ERROR_SYSCALL（443），本地提交成功、远端未更新；保留提交后单次重试推送。
- 2026-10-28 0.4.0 UI：读取尚未产出的 UI-0.4.0.md 返回退出码 1；改为 Test-Path 后读取。Claude 的复合 Bash 渲染命令因包含 head/echo 超出允许命令而被拒绝，改为已允许的 dotnet 直接构建与运行检查；未扩大工具权限。首次渲染检查 4 项失败为离线视觉树的 IsVisible 判断问题，Claude 改为 Visibility 判断后 190 项全部通过。
- 2026-10-28 0.4.0 安装前检查：对尚不存在的 Program Files/PwshMCP 使用 Get-Item，即使指定 SilentlyContinue 仍使组合命令退出 1；后续用 Test-Path 作为存在性判断，不把路径尚未安装记为构建失败。
- 2026-10-28 实机验收：安装完成但任务退出 1，协调器管道连接超时；已有授权 MCP 诊断得到 IOException: 客户端没有所需的特权。原因是整份 SACL 创建要求额外安全特权，改为仅设置 mandatory label（LABEL_SECURITY_INFORMATION / WRITE_OWNER）。只读代码图文件搜索无匹配返回 1；一次故障记录补丁上下文不存在导致整份补丁未应用，核对后重做。
- 2026-10-28：71c8fbe 首次推送遇 GitHub SSL_ERROR_SYSCALL，单次重试成功；修复构建与安装后的真实协调器测试通过。
- 2026-10-28 晚间实机批准：requires_user_elevation；只读进程令牌证实协调器 Elevated=1。Peer.Read 在原生 RunAsClient 内访问 WindowsIdentity.Name，触发 .NET RunImpersonated 的 AsyncLocal 捕获，后续 await 恢复普通客户端令牌。修复为回调内只捕获令牌，退出模拟后读取身份字段。读取误猜 Store.cs 路径失败后，通过 rg --files 找到 StateStore.cs，后续不猜路径。
