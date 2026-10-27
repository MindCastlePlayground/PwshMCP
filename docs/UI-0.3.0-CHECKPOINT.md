# UI 0.3.0 未完成检查点

> 历史记录：用户充值后已恢复同一指定模型的调用，后续 UI 实现已合入 main。以下是中断时状态，当前结果见 UI-0.3.0.md 和 DELIVERY-2026-10-28.md。

2026-10-28。Claude Code `claude-opus-5-5 --effort medium` 在 30 轮后被上游 403 额度不足中断。剩余额度 ¥2.205090，下次请求预扣 ¥2.383188。进程退出码 1，不能按流事件的 subtype=success 判断成功；最终 is_error=true。

已写入但未构建、未验收：

- Consts.cs：集中双语文案及语言相关常量。
- Lang.cs：语言状态与用户选择保存。
- Fmt.cs：部分语言相关格式化。

尚未完成：界面语言切换接入、批准窗口语言传递、ClientName/TaskName 主展示与 ID 折叠、PowerShell 语法高亮、宽度适配/复制保真、0.3.0 托盘版本、离线布局检查入口、渲染检查与验收。

这些改动仅保存到 `wip/bilingual-ui-0.3` 分支，不能称为可用双语版本，不加入 main 或 v0.2.0 发布包。原始任务在 tasks/claude-bilingual-ui.md；恢复时由新的 Claude 任务读取本检查点和实际差异，不重复整段历史上下文。

后端与取证开发已经完成并推送，验收结果见 API-0.3.0-TEST.md 和 MODEL-EVIDENCE.md。用户要求 UI 交由 Claude，本次没有静默替换模型或自行实现剩余 UI。
