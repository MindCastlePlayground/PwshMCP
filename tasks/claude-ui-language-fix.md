[EXECUTOR] 小范围UI验收修复，cwd H:\Documents\AgentAbility\PwshMCP，claude-opus-5-5 medium。当前main f67b1cc已推送，正式artifacts/package --render-check 88checks0failed，但主代理看实际PNG发现真实缺陷：

1. artifacts/ui-0.3.0/main-en-Tasks-720.png 的代码Cwd标签仍“目录”；main-en-Requests-1000.png及main-en-Access-1400.png的Scope标题仍“授权范围”。MainWindow本地标签已英文，但Styles.xaml共享模板x:Static Copy值在初始中文时捕获，Check.Windows切L.Current仅Fonts，App.Relang亦仅Fonts，真实运行切换语言也会模板残留旧语言。修复全局模板/资源随语言切换重新取文案（例如重新加载Styles资源字典，或可靠动态本地化）。不要只修测试。批准窗启动正确但要保持一致。检查其他共享资源含Copy文案的情况。
2. approve-en-520.png Requested by与Submitted是两列，approve-zh-520.png却分两行：当前WrapPanel按内容自然宽度，两种字体宽度不同。改成稳定列Grid并让值在列内换行；主请求详情同类字段也保持固定对齐逻辑。不要强制对长中英文逐像素相同，但布局列与字段顺序必须相同。

范围仅src/PwshMcp.Tray/**和docs/UI-0.3.0.md。最多25调用，不加载技能/整段历史。可局部构建离线诊断；主代理会完成后立刻提交推送再正式验收。Check补实际共享模板标签的双语验证，不能只有Copy属性对比。可新增独立代码控件PNG以便查看完整高亮/折行（当前长样例下半部在滚动区外）。所有操作离线，不启动真实window/broker/UAC，不写用户偏好。请用artifacts子目录放临时产物，不再新建Temp。不要git提交。完成<=12行。
