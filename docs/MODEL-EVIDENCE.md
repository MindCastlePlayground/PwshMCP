# 模型来源验证

2026-10-28，本机实际取证。

`scripts/model_evidence.py` 仅读取宿主 JSONL 的结构化元数据，不从助手消息正文提取模型名称，不接收模型自报工具参数。只输出来源及模型，不输出提示词、对话、令牌或用户路径。

| 来源 | 实测结果 | 能证明的范围 |
| --- | --- | --- |
| Claude CLI system/init.model | claude-opus-5-5 | 宿主初始化所选模型 |
| Claude assistant.message.model | claude-opus-5-5 | 宿主保存的响应模型元数据 |
| Claude result.modelUsage | claude-opus-5-5 | 此次运行的用量模型键，可能包含多个模型 |
| 当前 Codex rollout 的最近 turn_context.payload.model | gpt-6-astra | 此宿主记录的最近轮次模型 |

本次 Claude 取证使用已有真实 CLI 审计 UI 运行日志，Codex 使用当前线程记录；没有额外调用生成接口或让模型回答自己的名称。脚本对每种来源返回最后记录，usage 保留各模型；它不是自动确定某一次工具调用归属的算法。

运行：`python scripts/model_evidence.py <host-jsonl-path>`。路径由操作者提供，不开放为 MCP 工具参数，不自动扫描所有聊天日志。此脚本用于验证方法，尚未接到每条命令的审计模型字段。

可信边界：这些记录由宿主/响应协议产生，能排除普通提示词中的自报谎言，但本地同用户进程可修改文件，第三方网关也可改写响应模型名称。不能把它称为供应商签名证明，不能据此授予管理员权限。命令行 `--model` 与环境配置只能说明请求模型，无法证明实际路由。

后续逐命令归属需要宿主侧关联 session、turn、tool-call ID，遇到切换/子代理/单轮 fallback 时用对应响应元数据。没有匹配证据显示“未知”，不能从最近一次全局日志猜测。

Claude 官方 Hooks 提供 SessionStart 等宿主事件；新版本 PostModelSwitch 能报告会话切换，但文档明确单轮 fallback chain 不触发该事件，因此只用切换事件也不够。接口可用性需核对实际客户端版本。[官方 Hooks 文档](https://code.claude.com/docs/en/hooks#postmodelswitch)
