[EXECUTOR] 直接完成任务，不再委派。你是用户明确指定的 UI/UX 实现者，模型 claude-opus-5-5，medium。

工作目录 H:\Documents\AgentAbility\PwshMCP。仅修改 src/PwshMcp.Tray/** 和 docs/UI-0.3.0.md。不要修改Host/Shared、git提交、推送或用户配置。先读 AGENTS.md；加载 D:/Code/data/codex/skills/frontend-design/SKILL.md 与 D:/Code/data/codex/skills/impeccable/SKILL.md，仅按任务相关部分使用。

后端已完成：Shared AccessInfo/JobInfo 新增 ClientName（可读客户端名）；JobInfo 新增 TaskName。内部 Client 是授权键，Id/SessionId/EnvironmentId是机器诊断信息。MCP --display-name 优先，initialize.clientInfo.title/name 为备用显示来源。不要把显示名称称为已认证身份。

用户目标：提升整体可读性，别满屏 ID；支持真正的英文 UI 和中文 UI，文本位置/基线/留白一致，长英文不裁切，让强迫症舒服。命令展示必须适配窗口宽度、自动折行、PowerShell语法高亮；精确复制原命令（不能复制可视折行插入的换行）。列表以任务名/描述/客户端可读名称为主，原始ID只放折叠诊断区，审批仍可查看真实Client授权键，不能因美化隐藏权限范围。

实现完整双语（托盘菜单、主窗、批准窗、状态、错误、时间、空状态、无障碍名称等）。可选 --language en/zh，且提供用户可发现的语言选择，保存选择并让提权批准窗口保持同语言；语言切换不得自动触发UAC、重启broker或撤销授权。复用集中Copy/常量风格。可以使用静态属性/资源绑定替换const；保证XAML可工作。中文和英文是选择一种显示，不能每条同时堆叠两种语言。新增依赖尽量避免；高亮若自行词法实现至少正确处理注释、字符串、变量、关键字、cmdlet、数字，代码只作文本不能执行。长单行、长路径、1000字描述、窗口窄宽切换都要考虑。

视觉要求：Typography/Whitespace/Visual hierarchy/Color/Motion/Micro interaction/Responsive/Originality参照Awwwards、Webby、FWA优秀作品，但这是native Windows授权工具，要清晰克制；逐维自查并修正可见问题，不宣称无证据的获奖级验收。保留不抢焦点、不自动弹UAC的原交互。版本更新0.3.0。

用户指定完成即提交推送，验收放在推送后：你完成代码后不要运行构建或测试，主代理会立刻提交推送，然后执行验收并修正。可静态审查。请提供可自动执行的离线UI自检入口（如 --self-test 或专门项目），验证双语键/格式占位符一致、关键窗口在两种语言下可构造/布局、代码控件折行和复制保真；不得弹UAC、启动broker、改变现有托盘。若需新增入口仅在Tray目录中实现。如能加入--render-check在无窗口WPF RenderTargetBitmap渲染双语主窗/批准窗的合成样例到artifacts，供主代理审查布局（是离线渲染，不操控用户桌面），请实现，且不要接触真实授权。

需要后端支持就写 docs/UI-0.3.0.md 的对齐请求，不擅自改变权限契约。最多110次工具调用。最终<=25行：修改、运行自检/离线渲染方式、限制。详细结果落docs/UI-0.3.0.md。
