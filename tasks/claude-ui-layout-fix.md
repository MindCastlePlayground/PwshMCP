[EXECUTOR] cwd H:\Documents\AgentAbility\PwshMCP，UI推送后验收修复，claude-opus-5-5 medium。前两处编译引用错误已修正，构建成功；主代理已修复Check.cs两个测试样本错误（LongDesc长度、字符串样本搜索词）。当前main d0253b3。

真实离线渲染执行 `dotnet artifacts/package/PwshMcp.Tray.dll --render-check artifacts/ui-0.3.0` 得到43 checks,1 failed，前42项语言/分词/复制/折行都通过；窗口布局抛XamlParseException，inner: 无法找到名为“pm.facts”的资源。完整堆栈 artifacts/ui-0.3.0/ui-selftest.txt 47行起。MainWindow.xaml的pm.scope/pm.diag模板用K.Facts，Styles.xaml151行确实定义DataTemplate K.Facts。需要辨别真实UI资源缺陷和Check.Detach离线资源作用域问题，不要通过删掉检查或省略模板逃避。

只修改 src/PwshMcp.Tray/**、docs/UI-0.3.0.md。不git提交、不操作真实UI/UAC。不加载技能/全历史，先读相关文件定位。可以为诊断进行局部构建及离线检查以定位并修复（主代理之后立即提交推送，再跑正式验收）；若后续离线渲染出现裁切也一起修复，确保命令显示和双语布局真实可用。最多35工具调用，最终列变更/诊断结果/仍未解决项<=15行。不要重复重设计。
