# UI result · PwshMCP tray and approval UI

Executor: Claude (UI). Scope: `src/PwshMcp.Tray/**`, `docs/UI-*.md`, `PRODUCT.md`, `DESIGN.md`. Shared, Host, Execution, the solution file and other docs were not modified.

## Files (src/PwshMcp.Tray)
| File | Role |
|---|---|
| `PwshMcp.Tray.csproj`, `app.manifest` | WinExe on net8.0-windows with WPF + WinForms. asInvoker, PerMonitorV2, common-controls v6. ProjectReference to Shared only, no packages. `ApplicationIcon` is used only if `Assets\app.ico` exists. |
| `Consts.cs` | `Cli`, `Names`, `Timing`, `Copy`. All copy is Chinese primary; technical identifiers stay unchanged. |
| `Launch.cs` | Argument parsing (`Mode`, `Args`); Host path resolution; `IsAdmin`; `Elevated` (runas, forwards `--backend-dir`); hidden `Broker` and `Worker` launch; `Send` with `ExpectedHostPath` (A9). |
| `Monitor.cs` | Background poll every 1 s with a 1.5 s probe; owned-broker start when no pipe exists; restart cap of 3 per minute; `Retry`; `StopOwned` kills only the owned PID. |
| `TrayHost.cs` | NotifyIcon and menu (status / Open / Review (n) / Revoke all / Exit); TaskbarCreated re-add; theme re-render; TaskDialog exit prompt. |
| `Glyphs.cs`, `Assets/badge.svg` | Vector badge redrawn per pixel size (16/20/24/32/48/256); PNG-ICO packing; `--export-icons`. |
| `Theme.cs`, `Styles.xaml`, `Controls.cs` | Light/dark palettes following `AppsUseLightTheme`, re-applied on WM_SETTINGCHANGE; button, segment, row and code styles; Chip (shape + colour); Band (validity bar). |
| `Fmt.cs`, `Vms.cs` | Formatting and error mapping; MVVM: Bindable, Relay, two-step Confirm, CmdVm, GrantVm, JobVm, MainVm. Sync keeps item instances, so selection survives polls. |
| `MainWindow.xaml(.cs)` | Dashboard: header with connection, notice, Retry and Revoke all; segmented Requests/Access/Tasks lists; splitter; detail pane with sticky action bar. |
| `ApproveVm.cs`, `ApproveWindow.xaml(.cs)` | Elevated approve/resume window: immutable snapshot, scope text, duration, Band, step machine. |
| `App.cs` | Custom Main; mode dispatch; single-instance mutex plus show event; theme; app icon; unhandled-exception routing; Quit. |

## Design summary
- Direction: admin access as a visitor badge (see `docs/UI-DESIGN.md`, `DESIGN.md`). Windows-native Operate register with system fonts (Segoe UI Variable → Segoe UI → Microsoft YaHei UI), a single indigo accent, flat 1 px lines and no custom chrome.
- States never rely on colour alone. The tray uses outline / slash / filled / amber-dot cut-out. Chips use dashed / dot / ring / cross / strikethrough. Nothing blinks.
- Keyboard:
  - access keys on every action;
  - Ctrl+1/2/3 switches tabs, F5 retries, Esc hides the dashboard;
  - focus ring on every control;
  - the approve window's initial focus is on Cancel.
- Motion: only the detail pane uses a 180 ms fade-and-rise, and it is off when Windows animations are off. Screen-reader status uses LiveSetting.
- No modals for destructive actions. Deny, Revoke, Revoke all and Cancel task use a two-step inline confirm (4 s arm). Exit uses a native TaskDialog that explains running tasks and owned-broker shutdown.
- Command preview appears only in the explicit detail view. Environment variable names are shown, never values.

## Workflows implemented
- **Tray mode (default):**
  - single instance per session (`Local\PwshMcp.Tray.v1`); a second launch only signals the first;
  - normal launch shows no window; `--show` opens the dashboard;
  - TaskbarCreated re-adds the icon.
- **Broker ownership:**
  - If no `\\.\pipe\` entry exists, the tray starts `PwshMcp.Host.exe broker` hidden and tracks its PID.
  - If a pipe exists but doesn't answer, the UI reports Down and never starts a duplicate.
  - Exit kills only the owned PID, after the TaskDialog.
- **Approve:**
  - The user clicks "以管理员身份批准(_A)…", which starts the same exe `--approve ID [--backend-dir D]` via runas.
  - UAC cancel (1223) means the request stays pending, with a message. Other launch failures show the cause.
- **Elevated window:**
  - Verifies it runs as admin, then loads a snapshot and requires the grant to be Pending.
  - Shows requester, user, session, time, reason, waiting commands, and the scope text (current and future commands from the client).
  - Duration defaults to Windows sign-out (`ExpiresAt=null`); 1 h, 4 h and end of day are also offered.
  - Sends Approve, then requires `Grant.State == Active` (or a re-query) before starting the worker.
  - Launches `PwshMcp.Host.exe worker --grant ID` hidden and not parent-bound. An exit within 3 s counts as failure and offers Retry or Revoke.
  - Never shows Active without a broker confirmation. Closing is blocked while sending/starting.
- **Resume (`--resume ID`):** for an Active grant without a heartbeat. Re-verifies Active and the missing heartbeat, then launches the worker. No approve op is sent.
- **Deny / Revoke / Cancel / Revoke all:** sent to the broker from the normal-privilege tray. The result is shown on the item. Cancel is shown as "requested" until the broker confirms.
- **Offline and reconnect:**
  - the header chip shows when data is stale;
  - Retry clears the give-up state;
  - errors are mapped (timeout, no pipe, wrong broker per A9, Host missing).

## Contract requests (docs/UI-ALIGNMENT.md)
- A1–A8 were all ACKed by the main agent and A9 was added; all are consumed.
- A1: GrantId link is used first; the client/SID/session match remains as a fallback.
- A2: Label = plain-text reason.
- A3: worker shown connected when the heartbeat is ≤ 15 s old.
- A4: `--resume` re-attaches to an Active grant.
- A5: tray cancel.
- A6: exact approve shape.
- A7: elevated snapshot.
- A8: CLI and ownership; worker not parent-bound; `--backend-dir` forwarded.
- A9: `ExpectedHostPath` is set on every BrokerClient.
- No wire API was invented; only existing `Ops`/`Request`/`Response` fields are used.

## CLI
`PwshMcp.Tray.exe [--show] [--backend-dir <dir>]` · `--approve <id>` · `--resume <id>` · `--export-icons <dir>` (writes `app.ico` and `badge-<state>-<size>.png`, no window).

## Build evidence
`dotnet build src/PwshMcp.Tray/PwshMcp.Tray.csproj -nologo` (after the Chinese copy, font fallback and `Copy.Verb` changes) → `已成功生成。0 个警告 0 个错误`.
Fixes along the way:
- CS8124 (single-element tuple) in Glyphs.cs.
- WFAC010 suppressed, with a comment explaining that WPF needs DPI awareness set in the manifest.

## Fix pass · accessibility and labels (after main-agent UIA probe)
Evidence came from the main agent's accessibility tree; screenshots and clicks were unavailable in that session (capture timed out, input geometry unavailable), so nothing below is visually verified.
- **Access keys / button names:** text buttons and duration radios used an explicit `<AccessText Text="{Binding …}"/>` child. ContentControl's plain text ignores AccessText, so the Button had no UIA name and the child exposed the raw `全部撤销(_V)`. Text buttons now set `Content` to the string, so the template's `RecognizesAccessKey="True"` ContentPresenter builds the AccessText: the underscore is parsed, the key is underlined only on Alt, and the text reads `全部撤销(V)`. In addition, the button and choice styles set `AutomationProperties.Name` through `All.Plain`, which strips markers from string, AccessText or icon-panel content. The icon buttons (Approve, Restart worker) keep a StackPanel. The stale hard-coded `AutomationProperties.Name` that still contained `_A` was removed from Approve.
- **List item names:** the Row style binds `AutomationProperties.Name` to `Spoken`. For requests and access rows this is `client，state，secondary line`; for tasks it is `client，state，first command line`. Previously rows read `PwshMcp.Tray.GrantVm`.
- **Time label:** the access detail showed `授予时间` with `CreatedAt`, which is wrong for requests that expired unapproved. It is now `请求时间` (`Copy.Detail.Requested` / `GrantVm.Requested`). The backend has no ApprovedAt, so no approval time is shown or inferred.
- Build: `dotnet build src/PwshMcp.Tray/PwshMcp.Tray.csproj -nologo` → `已成功生成。0 个警告 0 个错误`. The running tray (PID 70276) was not touched.
- Still to verify (main agent): Narrator/Inspect names on Button, RadioButton and ListBoxItem; Alt underline rendering.

## Environment failures (not retried)
- ~~Running `PwshMcp.Tray.exe --export-icons Assets` required tool approval and was not approved; no PNG/ICO assets were exported and no `Assets/app.ico` existed.~~ **Superseded:** the main agent later ran `--export-icons` successfully (74 files incl. SVG); `Assets/app.ico` now exists. No screenshots were taken by the UI executor.
- Shell-based text edits (python/perl/sed) required approval; I used the file Edit tool instead.

## Not verified (main agent owns this)
- Real rendering of both windows, light/dark themes, high-DPI and mixed-DPI moves, contrast with real fonts, and how Chinese text fits in buttons.
- Real UAC: runas prompt, cancel path, elevated snapshot under the split token, and worker launch and heartbeat.
- TaskbarCreated after an explorer restart; the tray menu on a light vs dark taskbar; 16/20/24/32 px legibility on a real screen.
- End-to-end broker integration: owned-broker start/restart/stop, service coexistence, A9 path check with `--backend-dir`.
- Screen reader announcements, and keyboard-only traversal in practice.
- A finish/critique review pass was not run (no subagents allowed in this task); self-review only.

## Requests to main agent
1. Add `src/PwshMcp.Tray` to the solution and the publish layout, side by side with `PwshMcp.Host.exe` (default backend dir = tray exe dir).
2. ~~Before packaging, run `PwshMcp.Tray.exe --export-icons src/PwshMcp.Tray/Assets` once and rebuild.~~ Done by the main agent; `Assets/app.ico` exists.
3. During real-UAC validation, check that an approval made while new commands arrive shows them (the scope text already covers future commands).

## 0.2.0 · Audit fields (task name, command description)
Uses only the Shared fields `AccessInfo.TaskName` and `RunSpec.Description`. No new non-UI fields were needed, and Shared, Host and tests are unchanged.
- **Task name:** the request list row's second line shows the task name instead of the reason. Access rows show client, task name, then remaining time or end time. Request, access and approval details show a "任务名" block under the title, followed by `Copy.Detail.TaskNote`. The note says the task name is the background the agent gave when it first applied, and that the grant covers every later command from the client. Nothing states or implies per-task isolation.
- **Description:** in the task list the primary line is the description (UI font). The client and age stay on the second line. Task detail shows "指令描述" under the title, and the code moves to a "代码" section (`pm.code` template). Waiting commands in request detail and in the approval preview show the description first, with cwd, code and meta below it.
- **Old records:** an empty value shows `旧记录未提供任务名` / `旧记录未提供指令描述` in muted text (`K.Task` / `K.Desc` styles with DataTriggers on `HasTask` / `HasDesc`). The fallback is never derived from the code, `Label` or the client name (`Fmt.Or`).
- **Screen reader:** grant rows read `client，state，任务名：…，secondary`. Task rows read `description，state，来自 client`. The description and task-name TextBlocks have their own names, and code boxes are named "代码".
- **Errors:** `Fmt.Err(Response)` maps `description_required` and `task_name_required` (`Codes` in Consts.cs) to copy saying the agent must fill the field and resend, and that the tray cannot fill it.
- Version: `PwshMcp.Tray.csproj` is now 0.2.0, matching `Protocol.Version`. All new copy is in `Consts.cs`.
- Build: `dotnet build src/PwshMcp.Tray/PwshMcp.Tray.csproj --nologo -v quiet` → `已成功生成。0 个警告 0 个错误`.
- Not verified: rendering, wrapping of long task names or descriptions (up to 200 / 1000 characters) in rows and details, and Narrator output. No window or UAC prompt was started.
