# 参与贡献 · Contributing

[中文](#中文) · [English](#english)

## 中文

欢迎改进 PwshMCP。请让每个 Issue 或 PR 只聚焦一件事，并说明复现条件与预期行为。

### 报告问题

请附上：

- 版本或提交号；
- Windows 与 PowerShell 版本；
- 运行方式（桌面模式或服务模式）；
- 最小复现步骤和脱敏后的错误信息。

**不要上传凭据、真实命令输出或完整 Agent 日志。** 涉及权限边界的问题请勿公开可利用细节：先提交一份不含敏感内容的报告，请维护者提供私下沟通方式。

### 开发与检查

- 环境：Windows、.NET 8 SDK；Python 检查需要 Python 3。
- 先读 [AGENTS.md](AGENTS.md)：字符串键集中管理，避免无关重构。
- UI 改动需覆盖中英文两种语言和窄窗口。
- 只运行与改动相关的检查：

```powershell
powershell -NoProfile -File scripts/build.ps1 -Output artifacts/package
dotnet artifacts/package/PwshMcp.Tray.dll --render-check artifacts/ui-check
dotnet run --project tests/PwshMcp.Peer.Tests
```

UI 渲染检查使用合成数据，不连接真实 Broker。其他集成或安装检查可能启动执行器或修改测试环境，运行前请先读测试源码和 [验收记录](docs/ACCEPTANCE-0.4.0.md)。纯文档改动不需要执行管理员安装。

### 提交 Pull Request

PR 描述请写明：问题、改动、验证方式和未覆盖项。请区分**离线渲染**与**真人交互**、**构建成功**与**安装结果**。

## English

Contributions are welcome. Keep each issue or PR focused on one thing, and explain the reproduction steps and expected behavior.

### Reporting issues

Please include:

- the version or commit;
- Windows and PowerShell versions;
- the deployment mode (desktop or service);
- a minimal reproduction and redacted errors.

**Do not upload credentials, real command output or full agent logs.** For security-sensitive issues, avoid public exploit details: open a non-sensitive report first and ask the maintainers for a private contact.

### Development and checks

- Environment: Windows and .NET 8 SDK; Python checks need Python 3.
- Read [AGENTS.md](AGENTS.md) first (Chinese): keep string keys centralized and avoid unrelated refactors.
- UI changes must cover both languages and narrow windows.
- Run only the checks relevant to your change:

```powershell
powershell -NoProfile -File scripts/build.ps1 -Output artifacts/package
dotnet artifacts/package/PwshMcp.Tray.dll --render-check artifacts/ui-check
dotnet run --project tests/PwshMcp.Peer.Tests
```

UI render checks use synthetic data and do not connect to the real Broker. Other integration or installation checks may start workers or modify the test environment; read their source and the [acceptance record](docs/ACCEPTANCE-0.4.0.md) (Chinese) first. Documentation-only changes do not require an administrator installation.

### Pull requests

Describe the problem, the changes, how you checked them and what remains uncovered. Distinguish **offline rendering** from **live interaction**, and **successful builds** from **installation results**.
