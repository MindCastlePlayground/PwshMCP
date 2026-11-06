[EXECUTOR] 直接完成当前任务，不再委派。必须实际修改文件，不接受只读分析或只给方案。
目标：优化 PwshMCP 托盘 GUI 显示与中英文文本提示（排版层级、窄窗口显示、通知弹窗布局、状态与操作提示文案）。
实际工作目录：H:\Documents\AgentAbility\PwshMCP
模型与渠道：agy CLI `--model gemini-3.8-flash-high --dangerously-skip-permissions`

执行纪律（上一次运行因空 `run_command` 和被拒权限而零产出退出，本次必须避免）：
- 第一步直接 `view_file` / `read_file` 打开下面列出的目标文件，不要读 README、不要读 `.gemini` brain 目录或 transcript。
- 修改一律用 `replace_file_content` / `multi_replace_file_content` / `write_to_file` 落盘；不要只描述改动。
- `run_command` 只允许用于下面"验收命令"中的 dotnet 构建与检查；技能自身会引起 shell 失败时跳过命令并说明，绝不要发空命令行或空白命令。
- 最后一轮必须写出 `tasks/gui-polish-result.md`（改动文件、关键改动、构建/检查结果）。
- 禁止 `git commit`、`git push`、安装到 Program Files、停止任何现有进程。

上下文与已盘点问题：
1. 通知弹窗（`src/PwshMcp.Tray/NoticeWindow.xaml`、`src/PwshMcp.Tray/NoticeWindow.xaml.cs`）：
   - 顶部缺少状态标签（`Chip`）与请求时间（`Age`），`TaskName` 与 `Reason` 缺少字段标签（`Copy.Detail.TaskName`、`Copy.Detail.Reason`），视觉上直接堆叠。
   - `Scope` 未包裹在 `K.Scope` 边框卡片内，与主窗口和批准窗口层级不一致。
   - `Waiting` 命令列表缺少章节标题（`WaitingHead`），描述未使用 `K.Desc` 样式。
   - 滚动区（`Grid.Row="0"`）与底部操作区（`Grid.Row="1"`）之间缺少顶部分隔线；当 `count`、`Footer` 为空时未折叠（`Visibility="Collapsed"`），导致“授权有效期”上方出现大片空白；执行器不可用时 `Footer` 与 `unavailable` 文案重复。
   - 有效期双列单选按钮左右列 `Margin` 不对称（左列 `0,0,6,6`、右列无左距），导致两列宽度不一；批准按钮未使用带盾牌/加载动画的内容结构；底部“进入免打扰”全宽按钮视觉权重过重。
2. 主窗口窄宽度与底栏（`src/PwshMcp.Tray/MainWindow.xaml`、`src/PwshMcp.Tray/Consts.cs`、`src/PwshMcp.Tray/Vms.cs`）：
   - 页头 `ConnDetail`（`Copy.Conn.OwnedBroker` / `ExternalBroker` / `Elevated`）在 1000px 英文下即出现截断省略，在 720px 下被挤压为单字 `本...` 或 `.`。需精简连接状态副文案并在空间不足时避免渲染单字残影。
   - 720px 下底栏左下角有效期按钮（`有效期：直到注销`）与内联错误提示（`inline-*-failed-720`）折行局促，需优化中英文提示措辞与间距。
   - `Reason` 缺省值（`Copy.Detail.NoReason`）缺少类似 `HasTask` / `HasDesc` 的弱化触发器（`K.Muted`），与实际理由文本混淆。
3. 旧版独立批准窗口（`src/PwshMcp.Tray/ApproveWindow.xaml`）：
   - `Reason` 上方缺少 `Copy.Detail.Reason` 标题；`ScrollViewer` 未使用共享 `K.Scroll` 稳定槽位样式。
4. 文本提示整体润色（`src/PwshMcp.Tray/Consts.cs` 及 `src/PwshMcp.Tray/Check.cs` 示例数据）：
   - 检查并润色 `Copy` 中各模块（`Notify`、`Conn`、`Detail`、`Inline`、`Gate`、`Fix`、`Err`）的中英文提示文案，确保自然、准确、简洁，占位符与访问键在中英文间严格一致。
   - 将离线渲染自检（`Check.cs`）中的占位理由 `"sample"` 优化为真实可读的双语示例文案。

允许修改：
- `src/PwshMcp.Tray/Consts.cs`
- `src/PwshMcp.Tray/MainWindow.xaml`
- `src/PwshMcp.Tray/NoticeWindow.xaml`
- `src/PwshMcp.Tray/NoticeWindow.xaml.cs`
- `src/PwshMcp.Tray/ApproveWindow.xaml`
- `src/PwshMcp.Tray/RecoveryWindow.xaml`
- `src/PwshMcp.Tray/Styles.xaml`
- `src/PwshMcp.Tray/Controls.cs`
- `src/PwshMcp.Tray/Vms.cs`
- `src/PwshMcp.Tray/ApproveVm.cs`
- `src/PwshMcp.Tray/Check.cs`

禁止触碰：
- `src/PwshMcp.Shared/`、`src/PwshMcp.Host/`、`src/PwshMcp.Admin/`、`src/PwshMcp.Execution/` 等后端与协议代码。
- 保留 `ENVIRONMENT_FAILURES.md` 既有未提交记录。

编码与设计约束：
- 界面支持中英文，相同信息在两种语言下保持一致位置与层级。
- 可读名称与内部授权标识分开；字符串键集中为常量/枚举（`K`、`Rk`、`Names`、`Copy`、`Report`）。
- 保持既有深浅色主题、字体、焦点环和稳定滚动槽位规范。

验收命令：
1. `dotnet build src/PwshMcp.Tray/PwshMcp.Tray.csproj -c Debug --nologo`
2. `dotnet src/PwshMcp.Tray/bin/Debug/net8.0-windows/PwshMcp.Tray.dll --render-check Temp/render-check`（确认 `ui-selftest.txt` 0 failed，并检查生成的 PNG 截图）
3. `dotnet run --project tests/PwshMcp.Notification.Tests/PwshMcp.Notification.Tests.csproj`
4. `dotnet run --project tests/PwshMcp.Window.Tests/PwshMcp.Window.Tests.csproj`
5. 结果写入 `tasks/gui-polish-result.md`。

::OBJECTIVE{site_design|pri:OVERRIDE_ALL}
 target: 构建页面达到奖项级视觉标准
 ACCEPT: 满足用户目标，相关CHECK维度达到一致设计质量，范围内已知视觉与交互问题已处理
 BENCHMARK: Awwwards | Webby Awards | FWA获奖作品
::CHECK{typography|standard:award_tier}
::CHECK{whitespace|standard:award_tier}
::CHECK{visual_hierarchy|standard:award_tier}
::CHECK{color|standard:award_tier}
::CHECK{motion|standard:award_tier}
::CHECK{micro_interaction|standard:award_tier}
::CHECK{responsive|standard:award_tier}
::CHECK{originality|standard:award_tier}
::LOOP{self_review|trigger:on_completion}
 [READ] 根据CHECK列表审阅本次改动涉及的维度
 [COMPARE] 保持既有视觉语言与双语一致性，消除拥挤、缺标签、截断与空白断层
 [IF] 发现影响当前目标的具体问题 => [PATCH] 并仅复查受影响部分
 [IF] 达到ACCEPT => [Ω] 交付
