namespace PwshMcp.Tray;

public static class Cli
{
    public const string Show = "--show", Approve = "--approve", Resume = "--resume", BackendDir = "--backend-dir", ExportIcons = "--export-icons";
    public const string Language = "--language", SelfTest = "--self-test", RenderCheck = "--render-check", Flag = "--";
    public const string Broker = "broker", Worker = "worker", Grant = "--grant";
    public const string RunAs = "runas";
}

public static class Names
{
    public const string Mutex = @"Local\PwshMcp.Tray.v1", ShowEvent = @"Local\PwshMcp.Tray.v1.Show";
    public const string HostExe = "PwshMcp.Host.exe", TrayExe = "PwshMcp.Tray.exe";
    public const string ThemeKey = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    public const string AppsLight = "AppsUseLightTheme", SystemLight = "SystemUsesLightTheme";
    public const string TaskbarCreated = "TaskbarCreated";
    /// <summary>Exception text BrokerClient raises when the pipe server isn't the expected Host exe (A9).</summary>
    public const string WrongBroker = "Unexpected broker executable";
    public const string IcoFile = "app.ico", PngPattern = "badge-{0}-{1}.png", AssetDir = "Assets";
    public const string Styles = "pack://application:,,,/PwshMcp.Tray;component/Styles.xaml";
    /// <summary>Chinese UI sets Latin and CJK in one face so mixed lines share a baseline; English keeps YaHei only as fallback for agent text.</summary>
    public const string ZhUiFont = "Microsoft YaHei UI, Segoe UI", EnUiFont = "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI";
    public const string MonoFont = "Cascadia Mono, Consolas, Microsoft YaHei UI";
    /// <summary>Per-user tray preferences (HKCU). Only the language lives here; nothing about authorization.</summary>
    public const string PrefsKey = @"Software\PwshMCP\Tray", LangValue = "Language";
    public const string ZhTag = "zh", EnTag = "en", ZhCulture = "zh-CN", EnCulture = "en-US";
    public const string ArtifactDir = "artifacts", ArtifactSub = "ui-0.4.0", Report = "ui-selftest.txt", PngExt = ".png";
    public const string Armed = "armed";
    public const string QuietValue = "Quiet", AutoQuietValue = "AutoQuiet";
    /// <summary>ScrollViewer Tag that turns on the "more above / more below" edge rules (sidebar lists).</summary>
    public const string Rules = "rules";
    /// <summary>Automation id of the footer lifetime link that anchors the lifetime popup.</summary>
    public const string LifeBtn = "pm.lifeBtn";
    /// <summary>Automation ids of the controls that open the repair window: header chips, header button, executor-down block.</summary>
    public const string ConnBtn = "pm.connBtn", GateBtn = "pm.gateBtn", FixExec = "pm.fixExec";
}

/// <summary>Resource keys added for the repair window (the base set lives in <see cref="K"/>).</summary>
public static class Rk
{
    public const string ChipBtn = "pm.chipBtn", Result = "pm.result", Part = "pm.part";
}

public static class Timing
{
    public static readonly TimeSpan Poll = TimeSpan.FromSeconds(1), Probe = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15), WorkerGrace = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan Confirm = TimeSpan.FromSeconds(4), DoneClose = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan RequestLife = TimeSpan.FromHours(24), RestartWindow = TimeSpan.FromMinutes(1);
    public static readonly TimeSpan Copied = TimeSpan.FromMilliseconds(1500), Fade = TimeSpan.FromMilliseconds(180), Turn = TimeSpan.FromMilliseconds(120);
    public static readonly TimeSpan GateReady = TimeSpan.FromSeconds(5), GateDown = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan Cue = TimeSpan.FromMilliseconds(700);
    public const int MaxRestarts = 3;
}

/// <summary>
/// All user-visible copy. Each entry carries both languages and returns the current one only (<see cref="L"/>).
/// Actions are named by outcome; errors give cause and next step. Access keys use the same letter in both languages.
/// </summary>
public static class Copy
{
    public const string Product = "PwshMCP";
    /// <summary>Tray build version (major.minor.patch) from the assembly, shown beside the product name.</summary>
    public static string Version => typeof(Copy).Assembly.GetName().Version is { } v ? $"{v.Major}.{v.Minor}.{v.Build}" : "";
    static string T(string zh, string en) => L.T(zh, en);

    public static class Notify
    {
        public static string Title => T("新的管理员授权请求", "New admin access request");
        public static string Quiet => T("免打扰", "Do not disturb");
        public static string Manual => T("手动免打扰", "Mute notifications");
        public static string Auto => T("全屏或演示时自动免打扰", "Mute during full screen or presentation");
        public static string Muted => T("免打扰中 · 请求仍会保留", "Muted · requests are kept");
        public static string AutoMuted => T("自动免打扰中 · 请求仍会保留", "Automatically muted · requests are kept");
        public static string Enable => T("进入免打扰", "Mute notifications");
        public static string Later => T("稍后处理", "Later");
        public static string Details => T("查看详情", "View details");
        public static string Pending => T("还有 {0} 个请求待处理", "{0} more requests waiting");
        public static string Select => T("批准指定请求", "Approve a request");
        public static string All => T("批准当前全部请求（{0}）", "Approve all current requests ({0})");
        public static string Duration => T("快捷批准有效期", "Quick approval duration");
        public static string Result => T("已批准 {0} 个，未批准 {1} 个。", "Approved: {0}. Not approved: {1}.");
        public static string Scope => T("将允许该客户端在此 Windows 登录会话中以管理员身份运行命令；不限于此任务。", "Allows this client to run admin commands in this Windows sign-in session, beyond this task.");
        public static string SavedFailed => T("无法保存免打扰设置：{0}", "Couldn't save notification settings: {0}");
        public static string Unavailable => T("连接或执行器未就绪，请在主界面修复后批准。", "Connection or executor unavailable. Repair it in the main window before approving.");
    }

    public static class Tray
    {
        public static string Offline => T("PwshMCP · 未连接", "PwshMCP · Not connected");
        public static string Idle => T("PwshMCP · 无管理员授权", "PwshMCP · No admin access");
        public static string ActiveOne => T("PwshMCP · 管理员授权生效中", "PwshMCP · Admin access active");
        public static string ActiveMany => T("PwshMCP · {0} 项管理员授权生效中", "PwshMCP · {0} admin grants active");
        public static string PendingOne => T("PwshMCP · 1 个请求待处理", "PwshMCP · 1 request waiting");
        public static string PendingMany => T("PwshMCP · {0} 个请求待处理", "PwshMCP · {0} requests waiting");
        public static string Open => T("打开 PwshMCP", "Open PwshMCP");
        public static string Review => T("查看请求（{0}）", "Review requests ({0})");
        public static string RevokeAll => T("撤销全部授权", "Revoke all access");
        public static string Exit => T("退出", "Exit");
        public static string Language => T("界面语言", "Language");
        public static string RevokedOne => T("已撤销 1 个客户端的管理员授权。", "Revoked admin access for 1 client.");
        public static string RevokedAll => T("已撤销 {0} 个客户端的管理员授权。", "Revoked admin access for {0} clients.");
        public static string RevokeFailed => T("撤销授权失败", "Couldn't revoke access");
        public static string StartFailed => T("无法启动本地代理", "Couldn't start the local broker");
    }

    public static class Exit
    {
        public static string Title => T("退出 PwshMCP", "Exit PwshMCP");
        public static string OwnedHead => T("退出并停止本地代理？", "Exit and stop the local broker?");
        public static string OwnedBody => T("本地代理由托盘启动，退出托盘会同时停止它。再次启动 PwshMCP 之前，智能体将无法连接。",
            "The tray started the local broker, so exiting also stops it. Agents can't connect until you start PwshMCP again.");
        public static string OwnedTasks => T("有 {0} 个任务正在排队或运行，退出后它们会停止且没有结果。", "Tasks queued or running: {0}. They will stop without a result.");
        public static string ServiceHead => T("关闭托盘？", "Close the tray?");
        public static string ServiceBody => T("PwshMCP 代理独立运行，不受影响。新的请求会等待，直到你再次打开托盘。",
            "The PwshMCP broker runs on its own and keeps running. New requests wait until you open the tray again.");
        public static string Stop => T("退出并停止代理", "Exit and stop broker");
        public static string Keep => T("继续运行", "Keep running");
        public static string Close => T("关闭托盘", "Close tray");
    }

    public static class Conn
    {
        public static string Connecting => T("正在连接…", "Connecting…");
        public static string Online => T("已连接", "Connected");
        public static string OwnedBroker => T("本地代理（由托盘启动）", "Local broker (started by the tray)");
        public static string ExternalBroker => T("代理独立运行", "Broker runs on its own");
        public static string Elevated => T("代理以管理员身份运行", "Broker runs as administrator");
        public static string Offline => T("未连接", "Not connected");
        public static string Stale => T("显示的是 {0} 的数据", "Showing data from {0}");
        public static string HostMissing => T("在 {0} 中找不到 PwshMcp.Host.exe。请使用 --backend-dir 指向包含它的文件夹后重新启动托盘。",
            "Can't find PwshMcp.Host.exe in {0}. Restart the tray with --backend-dir pointing to the folder that contains it.");
        public static string BrokerExited => T("本地代理已停止（退出代码 {0}），正在重新启动…", "The local broker stopped (exit code {0}). Restarting…");
        public static string BrokerGaveUp => T("本地代理在一分钟内停止了 {0} 次。请确认 PwshMcp.Host.exe 能正常运行，然后点“立即重试”。",
            "The local broker stopped {0} times within a minute. Check that PwshMcp.Host.exe runs, then choose Retry now.");
        public static string Unreachable => T("代理管道没有响应：{0}", "The broker pipe isn't responding: {0}");
        public static string Retry => T("立即重试(_R)", "_Retry now");
    }

    public static class Tabs
    {
        public static string Requests => T("请求", "Requests");
        public static string Access => T("授权", "Access");
        public static string Tasks => T("任务", "Tasks");
        public static string Ended => T("已结束", "Ended");
        /// <summary>Screen-reader name of a grant row: task, state, client name, secondary text.</summary>
        public static string GrantRow => T("{0}，{1}，来自 {2}，{3}", "{0}, {1}, from {2}, {3}");
        /// <summary>Task row: description, state, client name.</summary>
        public static string TaskRow => T("{0}，{1}，来自 {2}", "{0}, {1}, from {2}");
    }

    public static class State
    {
        public static string Pending => T("待批准", "Pending");
        public static string Active => T("生效中", "Active");
        public static string Denied => T("已拒绝", "Denied");
        public static string Revoked => T("已撤销", "Revoked");
        public static string Expired => T("已过期", "Expired");
        public static string TimedOut => T("已超时", "Timed out");
        public static string Ended => T("已结束", "Ended");
        public static string NeedsApproval => T("等待批准", "Awaiting approval");
        public static string NeedsSession => T("等待会话", "Awaiting session");
        public static string Queued => T("排队中", "Queued");
        public static string Running => T("运行中", "Running");
        public static string Succeeded => T("成功", "Succeeded");
        public static string Failed => T("失败", "Failed");
        public static string Cancelled => T("已取消", "Cancelled");
        public static string Rejected => T("被拒绝", "Rejected");
        public static string Unknown => T("结果未知", "Outcome unknown");
        public static string Elevated => T("管理员", "Administrator");
        public static string Normal => T("普通用户", "Standard user");
    }

    public static class Empty
    {
        public static string RequestsHead => T("没有请求", "No requests");
        public static string RequestsBody => T("智能体申请管理员权限时，请求会出现在这里，托盘图标右上角会出现琥珀色圆点。",
            "When an agent asks for admin access, the request appears here and the tray icon shows an amber dot.");
        public static string AccessHead => T("尚未授予任何授权", "No access granted yet");
        public static string AccessBody => T("批准后的授权会显示在这里，并标明有效期。你可以随时撤销。",
            "Approved access appears here with its time limit. You can revoke it at any time.");
        public static string TasksHead => T("还没有任务", "No tasks yet");
        public static string TasksBody => T("智能体发送的命令会在这里显示排队、运行和完成状态。", "Commands sent by agents appear here as they queue, run and finish.");
        public static string Select => T("在左侧选择一项以查看详情。", "Select an item on the left to see its details.");
    }

    public static class Detail
    {
        public static string From => T("来自 {0} 的请求", "Request from {0}");
        public static string Grant => T("{0} 的授权", "Access for {0}");
        public static string Task => T("{0} 发送的命令", "Sent by {0}");
        public static string NoReason => T("未提供理由。", "No reason given.");
        public static string Reason => T("理由", "Reason");
        public static string Result => T("结果", "Result");
        public static string Output => T("输出", "Output");
        public static string User => T("Windows 用户", "Windows user");
        public static string Session => T("会话", "Session");
        public static string Submitted => T("提交时间", "Submitted");
        public static string RequestEnds => T("请求过期时间", "Request expires");
        public static string Waiting => T("等待中的命令（{0}）", "Waiting commands ({0})");
        public static string WaitingClient => T("此客户端等待中的命令（{0}）", "Waiting commands from this client ({0})");
        public static string NoWaiting => T("目前没有等待中的命令。批准后，此客户端之后发送的命令也会以管理员身份运行。",
            "No commands are waiting. After approval, later commands from this client also run as administrator.");
        public static string Cwd => T("目录", "Directory");
        public static string Timeout => T("超时", "Timeout");
        public static string EnvNames => T("设置变量", "Sets variables");
        public static string Profile => T("加载配置文件", "Loads profile");
        public static string SessionName => T("Shell 会话", "Shell session");
        public static string Approve => T("批准(_A)", "_Approve");
        public static string Deny => T("拒绝(_D)", "_Deny");
        public static string ConfirmDeny => T("确认拒绝", "Confirm deny");
        public static string Revoke => T("撤销授权(_R)", "_Revoke access");
        public static string ConfirmRevoke => T("确认撤销", "Confirm revoke");
        public static string RestartWorker => T("重启管理员执行器(_W)", "Restart admin _worker");
        public static string Cancel => T("取消任务(_C)", "_Cancel task");
        public static string ConfirmCancel => T("确认取消", "Confirm cancel");
        public static string ShowOutput => T("显示输出(_S)", "_Show output");
        public static string HideOutput => T("隐藏输出(_H)", "_Hide output");
        public static string Denied => T("已拒绝请求。", "Request denied.");
        public static string Revoked => T("已撤销授权。", "Access revoked.");
        public static string Cancelled => T("已请求取消。代理确认停止后，任务会显示为“已取消”。", "Cancellation requested. The task shows as Cancelled once the broker confirms it stopped.");
        public static string ActionFailed => T("{0}失败：{1}", "{0} failed: {1}");
        // Backend only records when the request was created; there is no approval timestamp to show.
        public static string Requested => T("请求时间", "Requested");
        public static string Ends => T("结束", "Ends");
        public static string SignOut => T("直到注销", "Until sign-out");
        public static string WorkerOn => T("管理员执行器已连接 · 最近心跳 {0}", "Admin worker connected · last heartbeat {0}");
        public static string WorkerOff => T("正在等待管理员执行器连接。连接前命令会排队等待。", "Waiting for the admin worker to connect. Commands queue until it does.");
        public static string Privilege => T("运行身份", "Runs as");
        public static string Engine => T("引擎", "Engine");
        public static string Identity => T("实际身份", "Actual identity");
        public static string ExitCode => T("退出代码", "Exit code");
        public static string Environment => T("环境创建于", "Environment started");
        public static string NoOutput => T("此任务没有记录输出。", "This task recorded no output.");
        public static string OutputPending => T("任务完成后可查看输出。", "Output is available when the task finishes.");
        public static string OutputWarn => T("输出可能包含机密信息，只在你要求时显示。", "Output may contain secrets, so it's shown only when you ask.");
        public static string ErrorStream => T("错误", "Errors");
        // Task name and description are supplied by the agent; older records predate them and are never back-filled from code or client.
        public static string TaskName => T("任务名", "Task");
        public static string NoTaskName => T("旧记录未提供任务名", "Older record without a task name");
        public static string TaskNote => T("任务名是智能体首次申请时说明的背景。授权覆盖此客户端之后发送的所有命令，不限于这个任务。",
            "The task name is the context the agent gave when it first asked. Access covers every later command from this client, not just this task.");
        public static string Description => T("指令描述", "Description");
        public static string NoDescription => T("旧记录未提供指令描述", "Older record without a description");
        public static string Code => T("代码", "Code");
    }

    /// <summary>Readable client names versus the authorization key. Names are display-only and never called an identity.</summary>
    public static class Who
    {
        public static string Name => T("客户端名称", "Client name");
        public static string Unnamed => T("未命名客户端", "Unnamed client");
        public static string SelfReported => T("由客户端或其配置提供，未经验证", "From the client or its config, not verified");
        public static string Scope => T("授权范围", "Access scope");
        public static string Key => T("客户端授权键", "Client authorization key");
        public static string NoKey => T("（空）", "(empty)");
        public static string ScopeNote => T("授权绑定到客户端授权键、Windows 用户和会话。客户端名称仅用于显示，不是经过验证的身份。",
            "Access is bound to the client authorization key, Windows user and session. The client name is for display only and isn't a verified identity.");
    }

    /// <summary>Action names used in "{0} failed" messages.</summary>
    public static class Verb
    {
        public static string Deny => T("拒绝", "Deny");
        public static string Revoke => T("撤销", "Revoke");
        public static string Cancel => T("取消", "Cancel");
        public static string RevokeAll => T("全部撤销", "Revoke all");
    }

    public static class Header
    {
        public static string RevokeAll => T("全部撤销(_V)", "Re_voke all");
        public static string ConfirmAll => T("确认全部撤销", "Confirm revoke all");
    }

    /// <summary>Language picker. Each language is named in itself so it stays findable from the other.</summary>
    public static class Lang
    {
        public static string Label => T("界面语言", "Interface language");
        public static string Zh => T("中文", "中文");
        public static string En => T("English", "English");
        public static string Hint => T("切换界面语言。代理、任务和授权都保持不变，也不会弹出 Windows 提示。",
            "Switch the interface language. The broker, tasks and access stay as they are, and no Windows prompt appears.");
    }

    public static class Code
    {
        public static string Kind => T("PowerShell", "PowerShell");
        public static string OneLine => T("1 行", "1 line");
        public static string Lines => T("{0} 行", "{0} lines");
        public static string CopyAll => T("复制命令", "Copy command");
        public static string Copied => T("已复制", "Copied");
        public static string Plain => T("命令较长，已按纯文本显示", "Long command, shown as plain text");
        public static string Name => T("PowerShell 代码，只读", "PowerShell code, read-only");
    }

    public static class Diag
    {
        public static string Head => T("诊断信息", "Diagnostics");
        public static string Note => T("内部标识，仅用于诊断与精确引用。", "Internal identifiers, for diagnostics and exact references.");
        public static string RequestId => T("请求 ID", "Request ID");
        public static string GrantId => T("授权 ID", "Grant ID");
        public static string TaskId => T("任务 ID", "Task ID");
        public static string Key => T("客户端授权键", "Client authorization key");
        public static string Sid => T("用户 SID", "User SID");
        public static string SessionId => T("会话 ID", "Session ID");
        public static string EnvId => T("环境 ID", "Environment ID");
    }

    public static class Approve
    {
        public static string Title => T("批准管理员授权", "Approve admin access");
        public static string ResumeTitle => T("重启管理员执行器", "Restart admin worker");
        public static string Loading => T("正在读取请求…", "Reading the request…");
        public static string Head => T("授予 {0} 管理员权限？", "Give {0} admin access?");
        public static string ResumeHead => T("重启 {0} 的管理员执行器？", "Restart the admin worker for {0}?");
        public static string AllowsHead => T("批准后", "After approval");
        public static string Allows => T("{0} 的命令将以管理员身份运行，每条命令都在全新的 PowerShell 环境中执行。授权结束前，Windows 不会再次询问。PwshMCP 不会隔离或撤回这些命令造成的更改。",
            "Commands from {0} run as administrator, each in a fresh PowerShell environment. Windows won't ask again until the access ends. PwshMCP doesn't isolate or undo changes these commands make.");
        public static string StartsHead => T("立即开始运行（{0}）", "Starts right away ({0})");
        public static string StartsNone => T("目前没有等待中的命令。", "No commands are waiting.");
        public static string Later => T("授权有效期内，{0} 之后发送的命令也会以管理员身份运行，不再询问。", "While access lasts, later commands from {0} also run as administrator without asking.");
        public static string DurationHead => T("授权有效期", "Access lasts");
        public static string SignOut => T("直到注销 Windows(_U)", "Until I sign o_ut of Windows");
        public static string OneHour => T("1 小时(_1)", "_1 hour");
        public static string FourHours => T("4 小时(_4)", "_4 hours");
        public static string Today => T("直到今天结束(_E)", "Until the _end of today");
        public static string SignOutNote => T("锁屏不会结束授权；注销或重启会。", "Locking the screen doesn't end access; signing out or restarting does.");
        public static string EndsAt => T("将于 {0} 结束", "Ends {0}");
        public static string Confirm => T("批准并开始(_S)", "Approve and _start");
        public static string ResumeConfirm => T("重启执行器(_S)", "Re_start worker");
        public static string Cancel => T("取消", "Cancel");
        public static string Close => T("关闭(_C)", "_Close");
        public static string Retry => T("重试(_R)", "_Retry");
        public static string RevokeNow => T("撤销授权(_V)", "Re_voke access");
        public static string NotAdmin => T("此窗口没有以管理员身份运行，无法批准。请关闭它，然后在 PwshMCP 中点“以管理员身份批准”。",
            "This window isn't running as administrator, so it can't approve. Close it, then choose Approve as administrator in PwshMCP.");
        public static string Gone => T("此请求已不再等待，当前状态：{0}。", "This request is no longer waiting. Current state: {0}.");
        public static string Missing => T("此请求已不存在，可能已过期或已被处理。", "This request no longer exists. It may have expired or been handled.");
        public static string NotActive => T("此授权已不再生效，当前状态：{0}。", "This access is no longer active. Current state: {0}.");
        public static string Sending => T("正在批准…", "Approving…");
        public static string Starting => T("正在启动管理员执行器…", "Starting the admin worker…");
        public static string Done => T("已批准授权，管理员执行器正在运行。", "Access approved. The admin worker is running.");
        public static string ResumeDone => T("管理员执行器已重新运行。", "The admin worker is running again.");
        public static string ApproveFailed => T("代理未批准此请求：{0}", "The broker didn't approve this request: {0}");
        public static string NotActiveAfter => T("代理已响应，但授权未生效（状态：{0}）。没有启动任何内容。", "The broker answered, but access isn't active (state: {0}). Nothing was started.");
        public static string WorkerFailed => T("授权已批准，但管理员执行器未能启动：{0}。执行器连接前命令会等待。你可以重试，或撤销授权。",
            "Access was approved, but the admin worker didn't start: {0}. Commands wait until it connects. Retry, or revoke the access.");
        public static string WorkerExited => T("执行器已退出，退出代码 {0}", "the worker exited with code {0}");
        public static string Unreachable => T("无法连接到代理：{0}", "Can't reach the broker: {0}");
        public static string RevokedAfter => T("已撤销授权，不会运行任何命令。", "Access revoked. No commands will run.");
        public static string AlreadyOn => T("此授权的管理员执行器已经连接，无需重启。", "The admin worker for this access is already connected. No restart needed.");
        public static string NoId => T("没有指定请求。请在 PwshMCP 中通过“以管理员身份批准”打开此窗口。", "No request was specified. Open this window from PwshMCP with Approve as administrator.");
        public static string Requester => T("请求方", "Requested by");
        public static string Elevation => T("此窗口以管理员身份运行。只有点“批准并开始”后才会授予权限。", "This window runs as administrator. Access is granted only when you choose Approve and start.");
    }

    /// <summary>Approving inside the main window through the installed admin executor. No second window, no Windows prompt.</summary>
    public static class Inline
    {
        public static string Head => T("批准此请求", "Approve this request");
        public static string Allows => T("批准后，{0} 在本次 Windows 登录中发送的命令将以管理员身份运行，每条都在全新的 PowerShell 环境中执行，直到授权结束。PwshMCP 不会隔离或撤回这些命令造成的更改。",
            "Once approved, commands {0} sends during this Windows sign-in run as administrator, each in a fresh PowerShell environment, until the access ends. PwshMCP doesn't isolate or undo changes they make.");
        public static string Lasts => T("有效期", "Lasts");
        public static string SignOut => T("直到注销(_U)", "Until sign-o_ut");
        public static string OneHour => T("1 小时(_1)", "_1 hour");
        public static string FourHours => T("4 小时(_4)", "_4 hours");
        public static string Today => T("今天结束前(_E)", "Until _end of day");
        public static string Until => T("至 {0}", "Until {0}");
        public static string UntilSignOut => T("直到注销", "Until sign-out");
        public static string SignOutNote => T("锁屏不会结束授权；注销或重启会。", "Locking the screen doesn't end access; signing out or restarting does.");
        public static string Picked => T("有效期：{0}", "Lasts: {0}");
        public static string Change => T("更改授权有效期", "Change how long access lasts");
        public static string AtSignOut => T("注销或重启时结束，锁屏不会", "Ends at sign-out or restart, not on lock");
        public static string Applies => T("适用于 {0} 在本次 Windows 登录中发送的所有命令，不只是此任务。", "Covers every command {0} sends during this Windows sign-in, not just this task.");
        public static string Sending => T("正在批准…", "Approving…");
        public static string Resuming => T("正在重启…", "Restarting…");
        public static string Done => T("已批准，授权生效中。", "Approved. Access is active.");
        public static string Resumed => T("已请求重启管理员执行器，连接后心跳会更新。", "Restart requested. The heartbeat updates once the admin worker connects.");
        public static string Failed => T("批准失败：{0}。请求仍在等待，可以重试。", "Approval failed: {0}. The request is still waiting; you can retry.");
        public static string NotActive => T("管理员执行器已响应，但授权未生效（{0}）。没有标记为已批准，可以重试。", "The admin executor answered, but access isn't active ({0}). Nothing was marked approved; you can retry.");
        public static string ResumeFailed => T("重启失败：{0}。可以重试。", "Restart failed: {0}. You can retry.");
    }

    /// <summary>The installed admin executor that applies approvals without a Windows prompt.</summary>
    public static class Gate
    {
        public static string Checking => T("检查执行器…", "Checking executor…");
        public static string Ready => T("执行器就绪", "Executor ready");
        public static string Down => T("执行器离线", "Executor offline");
        public static string Broken => T("执行器需修复", "Executor needs repair");
        public static string Head => T("暂时无法在此批准", "Can't approve here right now");
        public static string DownBody => T("没有连上已安装的管理员执行器。它由安装程序注册，在你登录 Windows 时启动。点“修复执行器”可以立即启动它；从未安装则以管理员身份运行一次 scripts\\install-desktop.ps1。",
            "The installed admin executor isn't answering. The installer registers it to start when you sign in to Windows. Choose Fix executor to start it now. Never installed: run scripts\\install-desktop.ps1 once as administrator.");
        public static string BrokenBody => T("管理员执行器有响应，但未通过检查：{0}。请以管理员身份重新运行 scripts\\install-desktop.ps1 修复安装。",
            "The admin executor answered but didn't pass checks: {0}. Rerun scripts\\install-desktop.ps1 as administrator to repair the install.");
        public static string NoPrompt => T("托盘不会改为弹出 Windows 提示。请求会继续等待，你仍可拒绝。", "The tray won't fall back to a Windows prompt. The request keeps waiting, and you can still deny it.");
        public static string Recheck => T("重新检查(_K)", "Chec_k again");
        public static string Checked => T("上次检查 {0}", "Checked {0}");
        public static string NoPipe => T("未找到管理员执行器", "admin executor not found");
        public static string Untrusted => T("响应的程序不是 Program Files\\PwshMCP 中受保护的 PwshMcp.Host.exe", "the responding program isn't the protected PwshMcp.Host.exe in Program Files\\PwshMCP");
        public static string NotReady => T("执行器报告尚未就绪", "it reports it isn't ready");
        public static string Offline => T("管理员执行器不可用", "the admin executor is unavailable");
        public static string Forbidden => T("执行器只接受安装目录中的 PwshMcp.Tray.exe，请从已安装的 PwshMCP 打开托盘", "it only accepts PwshMcp.Tray.exe from the install folder; open PwshMCP from its installed location");
        public static string Invalid => T("执行器拒绝了这个请求", "it rejected the request");
        public static string Protected => T("执行器不在受保护的安装位置运行", "it isn't running from the protected install location");
        public static string Worker => T("授权已批准，但管理员命令执行器未能启动", "access was approved, but the admin worker didn't start");
    }

    /// <summary>Repair window: the local connection and the admin executor, each with its own state, action and checked result.</summary>
    public static class Fix
    {
        public static string Title => T("修复连接", "Repair connections");
        public static string Menu => T("修复连接…", "Repair connections…");
        public static string Intro => T("两部分分别修复。只在你点击时尝试，结果以实际检查为准。",
            "Each part is repaired on its own, only when you click. Results come from a real check.");
        public static string Hint => T("查看状态和修复选项", "See status and repair options");
        public static string Spoken => T("{0}，查看状态和修复选项", "{0}, see status and repair options");

        public static string ConnHead => T("本地连接", "Local connection");
        public static string ConnWhat => T("托盘与本地代理之间的连接，智能体通过代理提交命令。", "The tray's link to the local broker that agents send commands through.");
        public static string ConnOn => T("已连接，请求可以正常到达。", "Connected. Requests come through.");
        public static string ConnWait => T("正在连接本地代理…", "Connecting to the local broker…");
        public static string ConnOff => T("托盘连不上本地代理。“重新连接”会再检查一次；没有代理在运行时，托盘会启动它。",
            "The tray can't reach the local broker. Reconnect checks again, and starts the broker if none is running.");
        public static string Reconnect => T("重新连接(_N)", "Reco_nnect");
        public static string Reconnecting => T("正在重新连接…", "Reconnecting…");
        public static string Restart => T("重启本地代理(_B)", "Restart _broker");
        public static string ConfirmRestart => T("确认重启", "Confirm restart");
        public static string Restarting => T("正在重启代理…", "Restarting the broker…");
        public static string RestartWarn => T("重启会使当前所有授权失效，正在运行的任务可能中断。", "Restarting ends all current access, and running tasks may be interrupted.");
        public static string RestartTasks => T("重启会使当前所有授权失效，{0} 个排队或运行中的任务可能中断。", "Restarting ends all current access, and {0} queued or running tasks may be interrupted.");
        public static string Other => T("代理由其他程序运行，托盘不会结束它。", "Another program runs this broker; the tray won't stop it.");

        public static string ExecHead => T("管理员执行器", "Admin executor");
        public static string ExecWhat => T("已安装的后台程序。你批准请求时，由它授予管理员权限，不弹出 Windows 提示。",
            "The installed service that applies your approvals without a Windows prompt.");
        public static string ExecOn => T("执行器就绪，可以在此批准请求。", "Ready. You can approve requests here.");
        public static string ExecCheck => T("正在检查执行器…", "Checking the executor…");
        public static string ExecOff => T("执行器没有响应。“启动执行器”会运行安装时注册的计划任务，通常不会弹出 Windows 提示。",
            "The executor isn't answering. Start executor runs the scheduled task set up at install, which normally shows no Windows prompt.");
        public static string ExecBroken => T("执行器有响应，但未通过检查。可以再启动一次；仍然失败时，以管理员身份重新运行 scripts\\install-desktop.ps1。",
            "The executor answered but failed its checks. Try starting it again; if that fails, rerun scripts\\install-desktop.ps1 as administrator.");
        public static string Start => T("启动执行器(_S)", "_Start executor");
        public static string Starting => T("正在启动并检查…", "Starting and checking…");
        public static string ElevHead => T("需要管理员确认", "Needs administrator approval");
        public static string ElevBody => T("可改为以管理员身份启动执行器。Windows 会询问一次，效果持续到注销。",
            "Start the executor as administrator instead. Windows asks once, and it lasts until you sign out.");
        public static string ElevNote => T("这不会批准任何请求，之后每个请求仍需你批准。", "This approves nothing; each request still needs your approval.");
        public static string Elevate => T("以管理员身份启动(_A)", "Start as _administrator");
        public static string Elevating => T("等待 Windows 确认…", "Waiting for Windows…");
        public static string ExecLink => T("修复执行器(_F)…", "_Fix executor…");

        public static string ConnReady => T("已重新连接，检查通过。", "Reconnected. The check passed.");
        public static string ExecReady => T("执行器已就绪，检查通过。", "The executor is ready. The check passed.");
        public static string Started => T("已启动，但 8 秒内没有通过检查。稍后会自动再查，也可以重试。", "Started, but it didn't pass the check within 8 seconds. It's checked again shortly, or you can retry.");
        public static string Failed => T("未能恢复。原因见下方诊断信息。", "Not recovered. Diagnostics below has the cause.");
        public static string Cancelled => T("已取消 Windows 提示，没有做任何更改。", "The Windows prompt was cancelled. Nothing changed.");
        // NeedsElevation from the scheduled path: task missing or refused, or started without a trusted ready answer within 8 s.
        // The cause is in diagnostics; this line stays generic and never implies a prompt was shown.
        public static string NeedsElev => T("已注册的启动方式没有恢复执行器，没有弹出提示。", "Registered startup didn't restore the executor. No prompt was shown.");
        public static string NotOwned => T("代理由其他程序运行，托盘没有结束它。请在那个程序中重启，或重新连接。", "Another program runs the broker, so the tray didn't stop it. Restart it there, or reconnect.");

        public static string DiagConn => T("连接详情", "Connection detail");
        public static string DiagExec => T("执行器详情", "Executor detail");
        public static string DiagConnLast => T("上次连接修复", "Last connection repair");
        public static string DiagExecLast => T("上次执行器修复", "Last executor repair");
        public static string Close => T("关闭(_C)", "_Close");
    }

    public static class Time
    {
        public const string Clock = "HH:mm", ClockSec = "HH:mm:ss";
        public static string JustNow => T("刚刚", "just now");
        public static string Minutes => T("{0} 分钟前", "{0} min ago");
        public static string Hours => T("{0} 小时前", "{0} h ago");
        public static string Days => T("{0} 天前", "{0} d ago");
        public static string HoursMin => T("{0} 小时 {1} 分钟", "{0} h {1} min");
        public static string Left => T("剩余 {0}", "{0} left");
        public static string Full => T("M月d日 HH:mm", "MMM d, HH:mm");
        public static string Seconds => T("{0} 秒", "{0} s");
        public static string MinutesShort => T("{0} 分钟", "{0} min");
    }

    public static class Err
    {
        public static string Generic => T("意外错误（{0}）", "Unexpected error ({0})");
        public static string NoResponse => T("没有响应", "No response");
        public static string Timeout => T("代理未及时响应", "The broker didn't respond in time");
        public static string NoPipe => T("代理未运行", "The broker isn't running");
        public static string WrongBroker => T("响应的不是预期的代理程序 {0}。请停止另一个代理，或用 --backend-dir 指向它所在的文件夹",
            "The responding broker isn't the expected program {0}. Stop the other broker, or point --backend-dir to its folder");
        public static string DescriptionRequired => T("代理拒绝了缺少指令描述的命令。需要智能体为每条命令填写指令描述后重新发送，托盘无法代填",
            "The broker refused a command without a description. The agent must describe each command and resend it; the tray can't fill it in");
        public static string TaskNameRequired => T("代理拒绝了缺少任务名的首次管理员申请。需要智能体填写任务名后重新申请，托盘无法代填",
            "The broker refused a first admin request without a task name. The agent must name the task and ask again; the tray can't fill it in");
    }
}

/// <summary>Offline self-test report vocabulary (English only: it is a machine-checked log, not interface copy).</summary>
public static class Report
{
    public const string Pass = "PASS", Fail = "FAIL", Info = "INFO", Line = "{0} {1}: {2}", Sum = "{0} checks, {1} failed";
    public const string Keys = "copy", Holes = "placeholders", Access = "access-key", Lex = "lexer", Wrap = "code-wrap", Fidelity = "copy-fidelity";
    public const string Layout = "layout", Clip = "clipped", Render = "render", Same = "same-in-both";
    public const string Main = "main", Approve = "approve", Name = "{0}-{1}-{2}{3}";
    public const string Gate = "gate", Inline = "inline", Sent = "sent", Down = "down", Broken = "broken", Failed = "failed";
    public const string Pop = "popup";
    public const string Repair = "repair", Dialog = "dialog", Both = "both-down", Elev = "elevate", Busy = "busy", Tight = "tight";
    /// <summary>x:Name of the turning arc in K.BusyTpl.</summary>
    public const string Arc = "arc";
    public const string Ended = "ended", Mixed = "mixed", EndedOnly = "ended-only", ActiveOnly = "active-only";
    public const string Scroll = "scroll", Top = "top", Bottom = "bottom", Detail = "detail";
}

/// <summary>Broker error codes the tray translates (mirrors PwshMcp.Host Errors).</summary>
public static class Codes
{
    public const string DescriptionRequired = "description_required", TaskNameRequired = "task_name_required";
}
