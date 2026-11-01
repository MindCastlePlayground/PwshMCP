# 托盘内存、全屏静默、提示音与图标 / Tray memory, full-screen hold, chime and icons

## 内存

- 主窗口隐藏或最小化闲置约 10 秒后被回收：窗口关闭并解除绑定，历史授权、已结束任务和已加载的命令输出一并释放，只做一次垃圾回收。托盘只保留待处理请求、生效中的授权和运行中的任务。
- 以下情况暂缓回收：窗口可见、修复窗口打开、请求弹窗显示中、正在批准、修复或读取输出。暂缓后下一周期再检查。
- 再次打开窗口时从最近一次状态快照重建历史，并恢复上次的位置、大小和最大化状态。
- 任务输出不随状态轮询下发。在任务详情中展开“输出”时才分段读取，最多保留 100 万字符，超出部分提示已截断；收起即释放。已归档任务的输出由后台透明读取。
- 已读请求和“批准后跳转”记录随每次快照裁剪，不会随运行时间增长。

## 全屏与提醒

- 任何应用全屏（包括 Windows 判定的忙碌/全屏状态，以及前台窗口覆盖整块显示器）时，不弹出请求窗口，不播放提示音，不显示气泡或横幅；这与“自动免打扰”开关无关。请求保持待处理，全屏结束后用一个窗口继续提醒。
- 手动免打扰和“自动跟随 Windows 勿扰”保持原有含义；托盘提示文字只说明手动或自动免打扰，不提示全屏。
- 新请求即将显示时预先完成弹窗排版并预加载主窗口，不抢焦点。只对新出现的请求预加载一次；预加载后若未打开，同样会在闲置后回收。
- 弹窗关闭后释放请求、命令文本和回调引用。

## 提示音

- 托盘菜单 → 提示音：“新请求时播放提示音”（默认关闭，保存在当前用户设置）与“试听提示音”。
- 约 0.45 秒、音量较低的上行双音（C5 → E5），起止柔和无爆音。每个新请求最多播放一次；免打扰或全屏时不播放。

## 图标

- 托盘图标采用终端提示符 `>_`：空闲为线框；有授权生效时为靛蓝实心并镂空提示符；离线为暗淡线框加斜线；有待处理请求时右上角显示琥珀色圆点。
- 深色与浅色任务栏分别配色，覆盖 16/20/24/32/40/48 像素；应用图标为渐变底白色提示符。

## 后台已知限制

- 状态快照仍包含全部未归档的已结束任务（热区上限 100 条终态任务 + 100 条已结束授权）；托盘在后台时只为其中的生效项创建视图对象，但快照本身仍在内存中。
- 完成任务的快照保留状态、退出码等摘要，命令正文、输出与错误文本从内存摘要释放。选中任务时读取完整命令，展开输出时读取输出与错误；切换任务或回收窗口后释放已加载内容。

## English

- **Memory.** When the main window has been hidden or minimized and idle for about 10 seconds, it is closed. History view models and loaded output are dropped, followed by one collection. The tray keeps only pending requests, active access and live tasks. Reclaim waits while any window is visible, a notice is open, or an approval, repair or output read is in flight. Reopening rebuilds history from the last snapshot and restores placement. Output loads on demand in chunks, is capped at 1M characters and is released when collapsed.
- **Full screen.** While any app is full screen there are no popups, sounds, balloons or banners, whatever the automatic quiet setting. Requests stay pending and appear in one window afterwards. The notice and main window are pre-laid-out once per new request, without taking focus. Closed notices drop their references.
- **Chime.** The "Notification sound" menu contains "Play a sound for new requests" (off by default) and "Play sample sound". The sound is a soft rising two-note chime of about 0.45 s. It plays at most once per new request and never during quiet hours or full screen.
- **Icons.** The tray icon uses a `>_` prompt in four states: idle outline, active indigo fill with a cut-out prompt, offline dimmed with a slash, and an amber dot when requests are pending. There are dark and light taskbar variants at 16–48 px.
- **Backend limits.** The hot window retains 100 terminal job summaries and 100 ended grants, plus live entries. Summaries retain state and exit code. Command text loads on selection; output and errors load when expanded. Loaded content is released on deselection or window reclamation.
