namespace PwshMcp.Tray;

public static class Cli
{
    public const string Show = "--show", Approve = "--approve", Resume = "--resume", BackendDir = "--backend-dir", ExportIcons = "--export-icons";
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
    public const string UiFont = "Segoe UI Variable Text, Segoe UI, Microsoft YaHei UI", MonoFont = "Cascadia Mono, Consolas, Microsoft YaHei UI";
}

public static class Timing
{
    public static readonly TimeSpan Poll = TimeSpan.FromSeconds(1), Probe = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan Heartbeat = TimeSpan.FromSeconds(15), WorkerGrace = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan Confirm = TimeSpan.FromSeconds(4), DoneClose = TimeSpan.FromMilliseconds(1500);
    public static readonly TimeSpan RequestLife = TimeSpan.FromHours(24), RestartWindow = TimeSpan.FromMinutes(1);
    public const int MaxRestarts = 3;
}

/// <summary>All user-visible copy (Chinese primary). Actions are named by outcome; errors give cause and next step.</summary>
public static class Copy
{
    public const string Product = "PwshMCP";

    public static class Tray
    {
        public const string Offline = "PwshMCP · 未连接", Idle = "PwshMCP · 无管理员授权";
        public const string ActiveOne = "PwshMCP · 管理员授权生效中", ActiveMany = "PwshMCP · {0} 项管理员授权生效中";
        public const string PendingOne = "PwshMCP · 1 个请求待处理", PendingMany = "PwshMCP · {0} 个请求待处理";
        public const string Open = "打开 PwshMCP", Review = "查看请求（{0}）", RevokeAll = "撤销全部授权", Exit = "退出";
        public const string RevokedAll = "已撤销 {0} 个客户端的管理员授权。", RevokeFailed = "撤销授权失败";
        public const string StartFailed = "无法启动本地代理";
    }

    public static class Exit
    {
        public const string Title = "退出 PwshMCP";
        public const string OwnedHead = "退出并停止本地代理？";
        public const string OwnedBody = "本地代理由托盘启动，退出托盘会同时停止它。再次启动 PwshMCP 之前，智能体将无法连接。";
        public const string OwnedTasks = "有 {0} 个任务正在排队或运行，退出后它们会停止且没有结果。";
        public const string ServiceHead = "关闭托盘？";
        public const string ServiceBody = "PwshMCP 代理独立运行，不受影响。新的请求会等待，直到你再次打开托盘。";
        public const string Stop = "退出并停止代理", Keep = "继续运行", Close = "关闭托盘";
    }

    public static class Conn
    {
        public const string Connecting = "正在连接…", Online = "已连接", OwnedBroker = "本地代理（由托盘启动）";
        public const string ExternalBroker = "代理独立运行", Elevated = "代理以管理员身份运行";
        public const string Offline = "未连接", Stale = "显示的是 {0} 的数据";
        public const string HostMissing = "在 {0} 中找不到 PwshMcp.Host.exe。请使用 --backend-dir 指向包含它的文件夹后重新启动托盘。";
        public const string BrokerExited = "本地代理已停止（退出代码 {0}），正在重新启动…";
        public const string BrokerGaveUp = "本地代理在一分钟内停止了 {0} 次。请确认 PwshMcp.Host.exe 能正常运行，然后点“立即重试”。";
        public const string Unreachable = "代理管道没有响应：{0}";
        public const string Retry = "立即重试(_R)";
    }

    public static class Tabs
    {
        public const string Requests = "请求", Access = "授权", Tasks = "任务", Ended = "已结束";
        /// <summary>Screen-reader name of a list row: primary text, state, secondary text.</summary>
        public const string Row = "{0}，{1}，{2}";
    }

    public static class State
    {
        public const string Pending = "待批准", Active = "生效中", Denied = "已拒绝", Revoked = "已撤销", Expired = "已过期";
        public const string NeedsApproval = "等待批准", NeedsSession = "等待会话", Queued = "排队中", Running = "运行中";
        public const string Succeeded = "成功", Failed = "失败", Cancelled = "已取消", Rejected = "被拒绝", Unknown = "结果未知";
        public const string Elevated = "管理员", Normal = "普通用户";
    }

    public static class Empty
    {
        public const string RequestsHead = "没有请求", RequestsBody = "智能体申请管理员权限时，请求会出现在这里，托盘图标右上角会出现琥珀色圆点。";
        public const string AccessHead = "尚未授予任何授权", AccessBody = "批准后的授权会显示在这里，并标明有效期。你可以随时撤销。";
        public const string TasksHead = "还没有任务", TasksBody = "智能体发送的命令会在这里显示排队、运行和完成状态。";
        public const string Select = "在左侧选择一项以查看详情。";
    }

    public static class Detail
    {
        public const string From = "{0} 的请求", Grant = "{0} 的授权", Task = "{0} 的任务";
        public const string NoReason = "未提供理由。", Reason = "理由", Command = "命令", Result = "结果", Output = "输出";
        public const string User = "Windows 用户", Session = "会话", Submitted = "提交时间", RequestEnds = "请求过期时间";
        public const string Waiting = "等待中的命令（{0}）", WaitingClient = "此客户端等待中的命令（{0}）", NoWaiting = "目前没有等待中的命令。批准后，此客户端之后发送的命令也会以管理员身份运行。";
        public const string Cwd = "目录", Timeout = "超时", EnvNames = "设置变量", Profile = "加载配置文件", SessionName = "Shell 会话";
        public const string Approve = "以管理员身份批准(_A)…", Deny = "拒绝(_D)", ConfirmDeny = "确认拒绝";
        public const string Revoke = "撤销授权(_R)", ConfirmRevoke = "确认撤销", RestartWorker = "重启管理员执行器(_W)…";
        public const string Cancel = "取消任务(_C)", ConfirmCancel = "确认取消", ShowOutput = "显示输出(_S)", HideOutput = "隐藏输出(_S)";
        public const string UacCancelled = "已在 Windows 提示中取消批准。请求仍在等待。";
        public const string UacFailed = "Windows 无法打开批准窗口：{0}";
        public const string Denied = "已拒绝请求。", Revoked = "已撤销授权。", Cancelled = "已请求取消。代理确认停止后，任务会显示为“已取消”。";
        public const string ActionFailed = "{0}失败：{1}";
        // Backend only records when the request was created; there is no approval timestamp to show.
        public const string Requested = "请求时间", Ends = "结束", SignOut = "直到注销", Now = "现在";
        public const string WorkerOn = "管理员执行器已连接 · 最近心跳 {0}", WorkerOff = "正在等待管理员执行器连接。连接前命令会排队等待。";
        public const string Privilege = "运行身份", Engine = "引擎", Identity = "实际身份", ExitCode = "退出代码", Environment = "全新环境";
        public const string NoOutput = "此任务没有记录输出。", OutputPending = "任务完成后可查看输出。";
        public const string OutputWarn = "输出可能包含机密信息，只在你要求时显示。";
        public const string ErrorStream = "错误";
    }

    /// <summary>Action names used in "{0}失败" messages.</summary>
    public static class Verb
    {
        public const string Deny = "拒绝", Revoke = "撤销", Cancel = "取消";
    }

    public static class Header
    {
        public const string RevokeAll = "全部撤销(_V)", ConfirmAll = "确认全部撤销";
    }

    public static class Approve
    {
        public const string Title = "批准管理员授权";
        public const string ResumeTitle = "重启管理员执行器";
        public const string Loading = "正在读取请求…";
        public const string Head = "授予 {0} 管理员权限？";
        public const string ResumeHead = "重启 {0} 的管理员执行器？";
        public const string AllowsHead = "批准后";
        public const string Allows = "{0} 的命令将以管理员身份运行，每条命令都在全新的 PowerShell 环境中执行。授权结束前，Windows 不会再次询问。PwshMCP 不会隔离或撤回这些命令造成的更改。";
        public const string StartsHead = "立即开始运行（{0}）", StartsNone = "目前没有等待中的命令。";
        public const string Later = "授权有效期内，{0} 之后发送的命令也会以管理员身份运行，不再询问。";
        public const string DurationHead = "授权有效期";
        public const string SignOut = "直到注销 Windows(_U)", OneHour = "1 小时(_1)", FourHours = "4 小时(_4)", Today = "直到今天结束(_E)";
        public const string SignOutNote = "锁屏不会结束授权；注销或重启会。";
        public const string EndsAt = "将于 {0} 结束";
        public const string Confirm = "批准并开始(_S)", ResumeConfirm = "重启执行器(_S)", Cancel = "取消", Close = "关闭(_C)", Retry = "重试(_R)", RevokeNow = "撤销授权(_V)";
        public const string NotAdmin = "此窗口没有以管理员身份运行，无法批准。请关闭它，然后在 PwshMCP 中点“以管理员身份批准”。";
        public const string Gone = "此请求已不再等待，当前状态：{0}。";
        public const string Missing = "此请求已不存在，可能已过期或已被处理。";
        public const string NotActive = "此授权已不再生效，当前状态：{0}。";
        public const string Sending = "正在批准…", Starting = "正在启动管理员执行器…";
        public const string Done = "已批准授权，管理员执行器正在运行。";
        public const string ResumeDone = "管理员执行器已重新运行。";
        public const string ApproveFailed = "代理未批准此请求：{0}";
        public const string NotActiveAfter = "代理已响应，但授权未生效（状态：{0}）。没有启动任何内容。";
        public const string WorkerFailed = "授权已批准，但管理员执行器未能启动：{0}。执行器连接前命令会等待。你可以重试，或撤销授权。";
        public const string WorkerExited = "执行器已退出，退出代码 {0}";
        public const string Unreachable = "无法连接到代理：{0}";
        public const string RevokedAfter = "已撤销授权，不会运行任何命令。";
        public const string AlreadyOn = "此授权的管理员执行器已经连接，无需重启。";
        public const string NoId = "没有指定请求。请在 PwshMCP 中通过“以管理员身份批准”打开此窗口。";
        public const string Requester = "请求方", Elevation = "此窗口以管理员身份运行。只有点“批准并开始”后才会授予权限。";
    }

    public static class Time
    {
        public const string JustNow = "刚刚", Minutes = "{0} 分钟前", Hours = "{0} 小时前", Days = "{0} 天前";
        public const string HoursMin = "{0} 小时 {1} 分钟", Left = "剩余 {0}";
        public const string Clock = "HH:mm", ClockSec = "HH:mm:ss", Full = "M月d日 HH:mm";
        public const string Seconds = "{0} 秒", MinutesShort = "{0} 分钟";
    }

    public static class Err
    {
        public const string Generic = "意外错误（{0}）", NoResponse = "没有响应";
        public const string Timeout = "代理未及时响应", NoPipe = "代理未运行";
        public const string WrongBroker = "响应的不是预期的代理程序 {0}。请停止另一个代理，或用 --backend-dir 指向它所在的文件夹";
    }
}
