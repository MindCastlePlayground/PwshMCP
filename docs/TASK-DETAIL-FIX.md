# 任务命令与输出修复

2026-11-04，源码提交 `a6da11a`。

选中的任务完成后，列表摘要会移除命令正文。界面现在会自动读取完整命令，读取期间的重复刷新共用正在进行的请求。切换任务后到达的旧响应会被丢弃。

输出加载期间可以收起并重新展开。每次展开使用独立的请求序号，旧请求的结果、错误及结束通知不会覆盖新请求。切换任务或离开任务页会收起输出并释放缓存。

## 验证与安装

- 新增回归用例先复现了任务完成后命令变空；修复后，已有通知/详情测试及新增命令恢复、输出竞态用例全部通过。
- Release 托盘发布成功。使用安装目录中的 Shared DLL 和最终安装的 Tray DLL 再次运行同一测试，通过。
- 已替换 `C:\Program Files\PwshMCP\PwshMcp.Tray.dll` 和 `.pdb`，安装文件与发布候选一致。原文件保存在 `C:\Program Files\PwshMCP\backup-detail-20261104-214644`。
- TaskHub 将托盘从 PID 25444 拉起为 PID 40776，守护 host PID 24028 保持运行。本次 Broker 同时重启，管理员授权需按现有流程重新开启。
- 安装后真实 MCP 的 run、status、output 调用通过：状态 succeeded，命令正文完整，输出包含 `PwshMCP task detail repair installed: 命令与输出验收`。
- 桌面点击验收待完成：本次 Computer Use 应用访问授权超时。上述测试覆盖安装程序集的行为及真实服务接口。

热更新方案见 [HOT-UPDATE-PROPOSAL.md](HOT-UPDATE-PROPOSAL.md)，目前为待审阅提案。
