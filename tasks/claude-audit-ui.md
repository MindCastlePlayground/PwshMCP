[EXECUTOR] 用户明确要求所有UI/UX由Claude完成。当前新增需求：每次MCP执行必填指令描述，首次管理员请求必填任务名，用于审计。只修改src/PwshMcp.Tray/**和docs/UI-RESULT.md；不修改后台/测试，不启动窗口/UAC，不使用Computer Use（用户要求用完收回权限），不委派。最多35次调用。加载技能D:/Code/data/codex/skills/frontend-design/SKILL.md和D:/Code/data/codex/skills/impeccable/SKILL.md。

后台Shared已添加：RunSpec.Description (string,每次run必填,<=1000字)，Request.TaskName (string,首次创建管理员授权必填,<=200字)，AccessInfo.TaskName (保存最初请求任务名)。Broker状态持久化保留这两项，正文Code/Env仍清空。任务名是最初申请理由背景，授权仍覆盖整个client后续指令，不得将任务名说成技术隔离/仅单任务授权。暂无Agent对话会话注册/模型识别功能，这些仅在可行性讨论，不实现UI入口。

实现：请求列表与详情、授权记录、提权审批窗口显示任务名；每条任务的列表/详情/审批预览优先显示指令描述，代码作为详情保留。字段为空的旧记录显示明确“旧记录未提供”或等价文案；不能用代码推测描述，也不能把client名当任务名。屏幕阅读器名称相应更新。错误映射description_required/task_name_required说明缺失字段应由Agent补齐。版本0.2.0（后端Protocol.Version已更新），项目csproj版本同步。所有新增显示文案常量统一Consts.cs；保持原生中文设计。

需要新非UI字段则写docs/UI-RESULT.md协调请求并返回，不自行改Shared。构建dotnet build src/PwshMcp.Tray/PwshMcp.Tray.csproj --nologo -v quiet，可用Bash(dotnet *)。报告变更、构建、未视觉验证。首轮环境已知PowerShell工具沙箱CreateProcessAsUserW错误5；你使用自己的Claude工具不必故意探测该错误。结果<=20行。
