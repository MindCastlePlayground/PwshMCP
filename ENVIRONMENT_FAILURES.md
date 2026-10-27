# 环境故障记录

## 2026-10-27：apply_patch 重复目标拒绝

- 同一补丁中对 DESIGN.md 同时 Delete File 与 Add File 被工具拒绝：multiple operations target。
- 未执行该补丁；改用单独新建修订文档，并在原文添加修订入口。以后同路径改写使用 Update File。

## 2026-10-27：只读 shell 启动失败

- 操作：读取当前目录及检索文件、相关记忆。
- 错误：`CreateProcessAsUserW failed: 5 (拒绝访问。)`。
- 已显式指定系统 Windows PowerShell，但工具错误中实际启动路径仍为 WindowsApps 下的 pwsh.exe。
- 有效降级：保持原只读命令范围，使用 `sandbox_permissions=require_escalated`，成功返回。
- 此降级仅表示越过工具沙箱执行限制，不证明进程取得 Windows 管理员令牌；不要对相同失败反复重试默认沙箱。
