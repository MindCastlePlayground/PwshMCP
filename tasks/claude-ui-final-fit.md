[EXECUTOR] 最后一处 UI 修复。cwd H:\Documents\AgentAbility\PwshMCP。仅修改 src/PwshMcp.Tray/**，不提交。最多20调用，不加载历史或技能。当前 b8511a8。看 artifacts/ui-0.3.0/main-en-Tasks-720.png，MainWindow 三列 UniformGrid 的可用宽度太小，Administrator 被折成 Administrato/r，客户名也极窄。请修复请求/任务详情的元数据布局：窄窗仍可读，中文英文使用相同布局结构。例如任务占整行，客户名/执行身份占下方两列；考虑 Fact 的右侧36margin。不要破坏已正常的 ApproveWindow 两列布局。所有显示/UI 修改由你负责。

顺便修 Check.Texts 的实际标签提取：当前只 yield TextBlock.Text，含 Run 的目录行未被读取，导致4个 Detail.Cwd断言假失败。读取实际 Inlines 文本并适当去首空白，保留原断言，不能删除或放宽语言检查。主代理完成后提交推送，再运行正式build/render-check。你可离线诊断，不启动实际窗口/UAC/broker，不改偏好。完成后<=10行，说明文件和验证。
