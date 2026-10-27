# PwshMCP 托盘 UI 0.3.0 交付说明

范围：仅 `src/PwshMcp.Tray/**`。未改 Host/Shared、用户配置，未提交、未构建、未操作真实 UI/UAC。构建与验收由主代理执行。

## 变更

| 维度 | 实现 | 位置 |
| --- | --- | --- |
| 单语显示 | 所有界面文案走 `Copy.*`（`L.T(zh,en)`），一次只显示一种语言 | `Consts.cs`、`Lang.cs` |
| 可发现的切换 | 主窗标题栏右侧有「中文 / English」分段开关（有提示文字和无障碍名称），托盘菜单也有「界面语言」子菜单；选择保存到 HKCU（`Prefs`） | `MainWindow.xaml`、`TrayHost.cs`、`Vms.cs`（`MainVm.Pick`） |
| 切换不扰动 | 切换只重建主窗、刷新 VM 计算属性和托盘菜单（保留窗口位置和最大化状态）。不触发 UAC，不重启代理，不改变授权 | `App.Relang`、`MainWindow.Take/Retire` |
| 批准窗同语言 | 提权启动时追加 `--language zh/en`；只对当次运行生效，不覆盖已保存的偏好 | `Launch.Elevated`、`App.Main` |
| 可读名称优先 | 列表行先显示任务名或指令描述（加粗），下一行是客户端名称（`Fmt.Who(ClientName, Client)`）。名称带提示“由客户端提供，未经验证” | `MainWindow.xaml`、`Vms.cs` |
| 真实授权范围 | 请求、授权详情和批准窗都显示「授权范围」块：客户端授权键、Windows 用户、会话。这几项用可选中的等宽文本显示，不会被样式隐藏 | `Styles.xaml`（`K.Scope`/`K.Facts`/`K.Ident`） |
| 原始 ID 入诊断 | RequestId/GrantId、TaskId、Key、SID、SessionId、EnvironmentId 都收进默认折叠的「诊断信息」 | `K.Diag` Expander、`GrantVm/JobVm/ApproveVm.Diag` |
| 代码控件 | `CodeView`：按宽度自动换行（禁用横向滚动，悬挂缩进 18px），带 PowerShell 高亮（注释、字符串、变量、关键字、cmdlet、数字、参数、类型），提供“复制全部”按钮。Ctrl+C 和全选复制得到原文，选中部分复制则按原换行风格还原，视觉换行不会写入剪贴板。只读，从不执行 | `Code.cs` |
| 长内容 | 长单行、无空格长路径、1000 字描述都会换行。目录标签可换行。连接详情用省略号截断并附 tooltip，标题栏提示最大宽度 240 | XAML |
| 版本 | 0.3.0，在标题栏产品名旁显示 `Copy.Version` | `PwshMcp.Tray.csproj` |
| 不抢焦点 | 原有行为不变：只在用户明确操作时 Present 窗口，只在点击按钮时提权 | — |

## 自检与渲染

```
PwshMcp.Tray.exe --self-test   [输出目录]
PwshMcp.Tray.exe --render-check [输出目录]
```

- 默认输出到 `<当前目录>/artifacts/ui-0.3.0/`，报告文件为 `ui-selftest.txt`。退出码等于失败项数。
- 两种模式都不创建托盘、不连接代理管道、不调用 UAC，也不读写真实授权。窗口只构造、不显示，所以 `Loaded` 回调（其中会调用 `vm.Load`）不会触发。数据来自 `MainVm.Seed` 和 `ApproveVm.Seed` 的合成快照。
- 检查项：
  - `copy`/`placeholders`/`access-key`：反射遍历全部 `Copy.*` 文案，检查中英文都非空、`{n}` 占位符集合一致、`_X` 访问键字母一致。两种语言完全相同的条目记为 INFO。
  - `lexer`：15 个代表性片段的 token 类型。
  - `copy-fidelity`：LF、CRLF、混合换行、Tab、Unicode、2000 字长行、900 行、空串、超过 20000 字符的纯文本。验证 runs、文档文本、全选复制三条路径都还原为原文，部分选区（第 2–3 行）结果正确。
  - `code-wrap`：长行在 260px 下的高度大于 900px 下高度的两倍；无空格长路径不超出 260px 宽度。
  - `clipped`：主窗在 720/1000/1400px 宽度、三个标签页下，批准窗在 520/640px 宽度下，分别用中英文布局，报告被裁切且没有省略号的 TextBlock。
- `--render-check` 另外用 `RenderTargetBitmap` 输出 PNG，文件名形如 `main-zh-Requests-720.png`、`approve-en-520.png`。

## 限制

- 未构建、未运行：自检和渲染结果都还没有实际产出，也还没有视觉评审证据。需要主代理构建后跑 `--render-check` 并检查 PNG。
- 超过 20000 字符的代码以纯文本显示（不高亮），复制仍为原文。
- 部分选区遇到混合换行时，统一按命令的首个换行风格还原；全选或不选时复制的总是原文。
- 裁切检测只覆盖 TextBlock 的布局裁切，不覆盖 ScrollViewer 内可滚动的溢出；那部分属于预期行为。
- 切换语言后，已显示的操作结果消息保留原语言，下一条消息起使用新语言。
- 渲染用的是离线布局（没有窗口边框和 DPI 缩放，按 96 DPI），与实机显示可能有像素级差异。

## 后端对齐请求

无。当前用到的 `ClientName`、`TaskName`、`Run.Description` 和 `Client`（授权键）、`Sid`、`SessionId` 后端都已提供。

## 自评

- 双语一致：靠反射自检兜底，新增文案只要漏写一种语言，自检就会失败。
- 安全表达：显示名始终标注“未经验证”；授权键不放进折叠区，在审批位置可见。
- 宽窄适配：没有横向滚动，长内容一律换行或截断加 tooltip，操作按钮保持一行，空间不够时提示文字换行。
- 待实机确认：Expander 展开动画、高对比度主题、讲述人朗读顺序。
