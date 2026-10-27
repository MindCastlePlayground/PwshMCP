# API 0.3.0 验收记录

日期：2026-10-28

## 本机隔离验收

- 构建：`dotnet build src/PwshMcp.Host/PwshMcp.Host.csproj -p:DefineConstants=PWMC_TEST_CHANNEL -o artifacts/test --nologo -v quiet`；成功，0 警告、0 错误。
- 集成：`python tests/integration.py`；11 组通过。原有 10 组未删改，另加入真实 stdio MCP 客户端元数据组。
- 单测：`python -m unittest tests/model_evidence_test.py`；1 项通过。
- 未操作生产 tray 或 UAC。

## 元数据覆盖

真实 MCP 进程验证 `clientInfo.title` 优先于 `name`，空 title 回退到 `name`，以及 `--display-name` 覆盖 initialize 提供的名称。MCP 返回任务保留 `clientName` 和 `taskName`，而 `client` 始终等于启动时的稳定授权键。`request_access` 返回值及 broker snapshot 中的 grant 均保留客户端显示名称和任务名称。

Broker 对空名称、含控制字符名称和超过 120 字符名称拒绝请求；未提供 `clientName` 的旧式请求仍被接受。上述无效和兼容性请求通过真实本机 broker 请求验证。

模型证据测试确认助手正文中的伪造模型文本不作为证据；宿主 init 的结构化 `model` 与 assistant 响应元数据的结构化 `message.model` 才进入结果。

## 环境故障记录

第一次启动 PowerShell 命令时，进程创建失败：`CreateProcessAsUserW failed: 5 (拒绝访问。)`。按仓库工作约定改用系统 Windows PowerShell 并以受审批准入模式重试后，读取、构建和验收均成功。未重复默认启动方式；此环境失败不影响测试结果。
