# v0.4.0 验收

目标：安装时一次 UAC，登录期间由管理员协调器服务；日常请求在托盘主窗口内批准，独立命令使用新进程和新环境。显式命名的持久会话仍按原接口保留状态。

## 已验证

- 后台及无控制台启动器构建：零警告、零错误。
- 环境测试：重新读取用户环境、组合 PATH、命令发现、字面覆盖、删除与恢复通过。
- 执行测试：15 项通过。
- 独立测试通道集成：11 项通过。
- 开发目录直接运行管理员协调器被拒绝，返回 `admin_requires_protected_install`。
- 安装/卸载 PowerShell 脚本语法检查通过。
- `d73d84a` 推送后，`scripts/build.ps1 -Output artifacts/package` 正式构建通过；最终包运行 `--render-check artifacts/ui-release-0.4.0`，190 项检查通过、0 失败。中英文截图已检查。
- Claude Code `claude-opus-5-5 --effort medium` 完成全部 UI 改动，任务结果 `is_error=false`、进程退出码 0。

## 本机验收入口

- `tests/PwshMcp.Admin.Tests`：真实安装路径的协调器状态；非托盘进程不能批准或恢复授权。
- `tests/installed_fresh.py --host "C:\Program Files\PwshMCP\PwshMcp.Host.exe"`：实际 MCP 普通权限命令，三次运行 PID/环境 ID 不同，用户环境修改与删除立即反映，前次 shell 临时变量不泄漏。
- 加 `--elevated`：首次请求由用户在托盘内批准，后两次复用授权，验证相同环境断言及真实管理员身份。

升级切换 broker 会使旧授权过期，需要在新托盘重新批准。旧 Codex 会话固定校验旧安装路径，需重新连接 MCP。原 0.3.0 安装保留用于回退。

真实安装、管理员权限端到端、重新登录自启与高 DPI 的结论以本文件后续实测记录为准；离线渲染不能替代这些验收。

## 2026-10-28 本机安装结果

- 用户确认安装 UAC；安装回执 `outcome=installed`，目标 `C:\Program Files\PwshMCP`。
- 首次实测发现协调器创建管道时要求未启用的 SACL 安全特权。`71c8fbe` 改为单独设置 mandatory label，保留中完整性客户端接入与原 DACL。通过原有 Codex 有效授权更新 DLL、重启固定任务，没有再次请求 UAC。相关 Windows API 权限要求见 [Microsoft SECURITY_INFORMATION](https://learn.microsoft.com/en-us/windows/win32/secauthz/security-information)。
- 修复后计划任务 `Running`；普通权限测试程序能读取 Ready，Approve/Resume 均返回 `admin_forbidden`。安装 Host.dll 与修复构建 SHA256 一致。
- 已切换普通托盘、broker 和 Codex MCP 配置到 Program Files；备份原配置与快捷方式，移除旧 Startup 快捷方式，保留安装器创建的 HKCU Run。原版文件保留。
- 实际 MCP 普通权限测试通过：3 次调用、3 个 PID、3 个环境 ID；用户环境修改和删除可见、shell 临时变量不泄漏。验收入口自身不是管理员。
- 管理员环境验收已发起，等待用户在新版托盘内批准。重新登录自启、锁屏/注销及高 DPI 尚未实测。
