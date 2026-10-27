# 0.2.0 审计字段验收

2026-10-27。

- 隔离 Host 构建：0 警告、0 错误。
- `python tests/integration.py`：10 组检查通过，涵盖缺字段拒绝、真实 stdio MCP 到 PowerShell、正常执行、非终止错误、去重、管理员待批/拒绝、取消、worker 故障、broker 重启及审计字段留存。正文和环境值在恢复记录中为空。
- Claude 完成托盘任务名、描述、历史空值显示和错误映射；其独立托盘构建 0 警告、0 错误。主代理已审查字段绑定与差异。
- `scripts/build.ps1 -Output artifacts/package` 发布成功；包含生产通道，不使用测试通道。
- 尚未验证新界面实际渲染、长文本布局、读屏及真实 UAC。没有启动新窗口或操作安全提示。现有桌面验收实例未切换。
- 模型识别、Agent 会话发现和精确会话预批准尚未实现，设计见 AGENT-IDENTITY.md。

独立运行集成测试（不占用桌面 Broker）：

```powershell
dotnet build src/PwshMcp.Host/PwshMcp.Host.csproj -p:DefineConstants=PWMC_TEST_CHANNEL -o artifacts/test --nologo -v quiet
python tests/integration.py
```

编译标记只更换测试管道、单实例互斥量和状态目录，不放宽权限校验。发布时不设置该标记。
