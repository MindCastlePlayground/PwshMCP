[EXECUTOR] 用户明确要求全部 UI/UX 由 Claude 设计实现。本任务只修复已验证的小范围 UI 问题，最多25次调用。工作区 H:\Documents\AgentAbility\PwshMCP。允许修改 src/PwshMcp.Tray/**、docs/UI-RESULT.md。读取现有UI和相关说明；必要技能仅 D:/Code/data/codex/skills/frontend-design/SKILL.md 与 D:/Code/data/codex/skills/impeccable/SKILL.md。不要委派、不要启动窗口/UAC、不要改后端。

主代理已构建和打开窗口，broker连接正常。computer-use访问窗口的accessibility成功；截图FrameArrived/window capture timed out，刷新一次仍失败，点击也coordinate input geometry is unavailable。不得虚构视觉验收。发现的证据：
1. 普通按钮的accessibility输出“全部撤销(_V)”且父Button无名称（子Text显示文案），请核对自定义Button模板是否使用RecognizesAccessKey，确保实际AccessText解析下划线和快捷键、可访问名称正确。中文括号中的快捷键可以保留但不要显示下划线。
2. 请求/授权列表项accessibility名称为“PwshMcp.Tray.GrantVm”，不是客户端名称。为列表项设置恰当可读名称（同样检查任务）。
3. 未获批准就过期的历史请求在授权页显示“授予时间”，实际字段是CreatedAt=请求创建时间。请修正为准确“请求时间”或等价表述；后端没有ApprovedAt，不得推测真正批准时间。
4. 图标已经主代理用你提供的--export-icons成功导出74个文件含SVG，Assets/app.ico现在存在。运行中托盘PID70276，不杀进程。仅dotnet build项目验证，产物bin/不冲突。

若需要非UI支持，写docs/UI-RESULT.md请求并返回阻塞点；没有需要时直接完成。更新结果记录这次修复与构建证据（旧“尚未导出图标”说明标为被后续主代理完成，不虚构GUI验证）。最终<=15行。
