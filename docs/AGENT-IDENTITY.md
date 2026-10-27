# Agent 身份与会话预批准

2026-10-27。审计字段已实现；下述模型识别、会话注册和预批准是设计结论，尚未实现。

## 模型名

MCP initialize 的 clientInfo 标识客户端软件及版本，不保证提供当前调用模型或聊天会话 ID。客户端名不能当成模型名。模型切换、路由和子代理也会使启动配置过时。

建议按来源记录 model 与 modelSource：宿主适配器逐次报告、启动配置提示、模型自行声明；未提供则显示未知。自行声明不能证明真实模型，不参与授权判断。Sampling 返回的 model 属于服务器请求的另一次生成，不是本次工具调用的模型证明。

## 会话发现与预批准

采用连接时自动注册、存活心跳和可选宿主适配器。Broker 分配 agentSessionId，关联客户端、工作目录、宿主 PID/启动时间、用户 SID 与 Windows 登录会话。托盘展示已连接会话，允许提前批准并开启管理员 worker，之后在授权有效期间执行无需再弹 UAC。

进程扫描只能辅助发现宿主实例；配置文件只能说明配置过 MCP。真正的聊天会话 ID 需要宿主提供：同一 MCP 进程可能服务多个聊天页，无法区分时必须标明实例级授权，不能宣称对某段对话授权。HTTP MCP 会话 ID 也是传输会话，不保证等于聊天会话。

会话断开后采用短暂重连宽限期，恢复需证明同一会话；新会话不能仅凭相同客户端名继承授权。关闭/撤销应阻止新命令，并允许用户取消在途工作。模型名变化只作审计，不自动成为权限边界。

预批准需要用户事先完成真实 UAC 并启动管理员 worker；仅保存批准记录不能取得管理员令牌。未提前激活、注销或重启后，无人值守不能凭空恢复管理员权限。

当前授权范围是客户端名 + 用户 SID + Windows 登录会话，并非聊天会话。现有 run.session 是持久 PowerShell 的名称，与 agentSessionId 必须分开。

## 已实现的审计约定（0.2.0）

- run、session_reset、session_close 必须填写 description（非空，最多 1000 字符），表达动作和目的。
- request_access 必须填写 taskName（非空，最多 200 字符）；首次 elevated run 在没有有效或待批准授权时也必须填写。
- 同一待批准请求后续命令无需重复任务名，不改写首次标题。任务名只是授权背景，不缩小现有客户端授权范围。
- Broker 独立校验，不能绕过 MCP schema 提交缺失字段。托盘展示任务名及每条描述；旧记录明确显示未提供。
- 元数据持久化保留描述和任务名，正文、环境值、输出不持久化。描述由 Agent 提供，不等于执行事实；禁止在这些文本中填写秘密。现有记录上限为 1000 条，不是永久或防篡改审计库。

来源：[MCP 生命周期](https://modelcontextprotocol.io/specification/2025-06-18/basic/lifecycle)、[传输](https://modelcontextprotocol.io/specification/2025-06-18/basic/transports)、[Sampling](https://modelcontextprotocol.io/specification/2025-06-18/client/sampling)。
