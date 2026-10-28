# B2：安装时提权，命令环境独立

用户已授权实施。固定目标：v0.4.0，当前管理员用户交互登录期间的后台开发。安装/升级需要一次管理员授权；日常批准不触发 UAC。不是关闭系统 UAC，不使用 SYSTEM 执行用户命令。

## 后台接口

Shared 新增 `AdminOp { Status, Approve, Resume }`、`AdminRequest { Op, Id, ExpiresAt }`、`AdminResponse { Ok, Error, Ready, Grant }`、`AdminClient.SendAsync(request, ct)`，可选 `ExpectedHostPath`。同用户同 Windows session 的专用管道。Status 可由同用户查询，Approve/Resume 仅允许安装目录的真实 PwshMcp.Tray.exe 进程。服务器还验证 SID/session，客户端验证服务器路径。

Host `admin` 模式必须在受保护 Program Files/PwshMCP 中以管理员身份运行。由最高权限计划任务在指定用户登录时启动；仅接受批准/恢复，不接受命令正文。批准转交现有 broker，然后启动现有 elevated worker，父进程绑定协调器。协调器退出/注销后 worker 停止。每次独立命令沿用 FreshEnvironment + 独立子进程。broker 的原有管理员批准门槛保留，MCP 无批准工具。

## UI（Claude 独占实现）

在已有主窗请求详情内选择授权期限并批准，不开第二个窗口、不调用 RunAs。AdminClient Status 显示管理员执行器就绪/未安装或离线；不可用时禁用新批准并展示安装/修复指导，不悄悄回退弹 UAC。恢复执行器同样调用 Resume。中英文位置、层级一致，命令折行/高亮不退化。旧 elevated ApproveWindow 可保留兼容显式旧入口，但主窗正常路径不再使用它。不实现未受保护的跨登录自动批准名单；本次保留会话授权，登录后可一次批准整个 Codex 客户端，后续命令复用。

## 安装

计划任务实际入口为无控制台窗口的 `PwshMcp.Admin.exe`，固定以 CreateNoWindow 启动同目录 Host.exe admin 并等待其退出；不接收外部启动参数，协调器异常退出可由任务计划程序观察。

新增 scripts/install-desktop.ps1：必须管理员启动，输入目标 UserSid（普通调用者提前记录）、Source。验证用户属于当前安装管理员 SID，不支持凭另一个管理员账号给标准用户静默授予提权入口。Program Files/PwshMCP 安装且 ACL 仅 SYSTEM/Administrators 写，普通用户读执行。注册 UserSid + InteractiveToken + Highest 任务，启动固定 PwshMcp.Admin.exe，再由其启动 Host.exe admin；无任意脚本/参数输入、无密码保存。创建目标用户托盘登录启动入口，立即启动计划任务，普通托盘由外层普通用户启动。保留原服务安装独立入口，不同时部署该服务。

## 验收

先提交推送，再正式构建/测试。新环境至少验证不同 PID、不同环境 ID、用户环境修改后下次读取新值。验证普通 ctl/MCP 不能批准协调器请求；过期/撤销不派发新命令。真实 UAC 由用户确认安装，不能自动点击安全桌面；取消时记录未安装，不声称完成。
