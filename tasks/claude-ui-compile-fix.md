[EXECUTOR] 极小修复任务。cwd H:\Documents\AgentAbility\PwshMCP。UI已由你完成并推送main提交1b4305a，主代理推送后构建发现：

Code.cs(173,34) CS0103 ForegroundProperty不存在；Check.cs(280,16) CS0103 LayoutInformation不存在，连带(280,108) CS0165 tb未赋值。

仅修改 src/PwshMcp.Tray/Code.cs 和 Check.cs 修正WPF类型/命名空间引用，静态检查附近同类引用。不要重读其他项目文档，不加载技能，不重设计，不运行编译/测试（主代理按要求修复完成即推送再验证）。最多8工具调用，最终说明实际修复<=5行。不要git提交。模型claude-opus-5-5 medium。
