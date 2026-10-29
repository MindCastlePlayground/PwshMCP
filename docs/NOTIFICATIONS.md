# 请求通知与快捷批准 / Request notifications

## 使用方式

- 新请求显示一个原生 WPF 窗口，位于鼠标所在屏幕工作区右下角，不抢键盘焦点、不播放声音，也不把批准设为默认回车按钮。
- 弹窗展示任务名、客户端、授权范围和高亮命令，可选择有效期后直接批准，或进入详情。“稍后处理”和关闭只隐藏提醒，不拒绝请求；之后新到的请求仍会提醒。
- 同时只展示一个请求窗口。其他请求合并为数量提示；当前请求批准或失效后显示下一条。新请求不会替换鼠标下方的批准目标。
- 托盘右键 → **批准指定请求**，按任务名和客户端选择；或 **批准当前全部请求**。快捷批准有效期默认为直到注销，可在菜单调整。
- 批量目标固定为打开菜单时的待处理请求；执行期间的新申请不会被批准。已处理或失效的请求会跳过，最后汇报批准数与未批准数。
- 手动免打扰与自动免打扰可分别开关，保存于当前用户的托盘设置。自动模式默认开启，读取 Windows 通知状态并检测前台窗口是否覆盖完整显示器；普通最大化窗口保留任务栏时不算全屏。
- 免打扰期间关闭自动弹窗、保留待处理标记；退出后用一个窗口继续提醒，不批量弹出。免打扰不会批准、拒绝或延长请求。主动点击菜单操作的结果仍可显示通知。
- 批准沿用受信任执行器通道，不自动唤起 UAC。离线时无法批准，可通过主窗口恢复入口修复。快捷批准与主窗口批准的授权范围一致，不限于展示的任务。

## English

New requests share one non-activating native window near the pointer's monitor corner. It shows the task, client, actual grant scope and highlighted commands. Choose a lifetime and approve, inspect details, or dismiss without denying. Approval has no default Enter shortcut.

The tray menu offers individual approval and a fixed batch of current requests. The duration defaults to sign-out and is adjustable. New arrivals never join a batch already selected. Results report approved and unapproved counts.

Manual muting and automatic full-screen/presentation muting are independent, remembered options. Automatic muting is enabled by default and uses Windows notification state plus foreground monitor coverage. Deferred requests remain pending and resume in one window after muting ends. No notification preference grants access or triggers UAC.

## 设计与验收边界

沿用现有深浅色主题、字体、按钮、高亮与滚动条；中英文控件采用相同排列。内容区可滚动，操作区固定。使用纯内存传输验证授权，不通过测试批准本机真实申请。

新功能需要安装新版托盘后生效；源码测试、离线渲染和本机安装验收分别记录，不互相替代。
