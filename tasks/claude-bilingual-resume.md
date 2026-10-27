[EXECUTOR] 用户已充值，恢复中断的UI开发。调用模型仍claude-opus-5-5 medium。

先读 docs/UI-0.3.0-CHECKPOINT.md、tasks/claude-bilingual-ui.md 和现有UI差异。遵循原任务所有范围和要求，不重做已经完成的Consts.cs/Lang.cs/Fmt.cs。当前在wip/bilingual-ui-0.3分支。原任务的后端已完成且验收通过，无需重新研究。技能前次已经按要求应用，这次聚焦剩余实现；避免再加载大量参考文档与全文件来回读取。

剩余必须完成：可发现的中英文切换及批准窗口语言传递、界面主要使用ClientName/TaskName/Description，原始ID移入诊断、窄宽适配的PowerShell高亮代码控件与原文复制、0.3.0托盘版本、离线布局/双语/复制自检与渲染入口、docs/UI-0.3.0.md交付说明。不要运行构建验收，主代理先提交推送后运行。不要git提交、不操作真实UI/UAC。全部UI/UX工作仍由你实现；如果后端需要支持写对齐请求。最多100调用，完成<=20行返回。
