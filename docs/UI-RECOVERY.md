# 修复连接 / Repair connections — UI

The local connection (tray ↔ broker) and the admin executor are repaired from one compact, modeless dialog, `RecoveryWindow`.
It is modeless rather than a light-dismiss popup because the Windows prompt from the elevated fallback takes focus, and a popup would close.

## Entry points (all click-only)

| Where | Opens on |
|---|---|
| Header connection chip (`pm.connBtn`) | Local connection |
| Header executor chip (`pm.gateBtn`) | Admin executor |
| Request's executor-down card, **修复执行器… / Fix executor…** (`pm.fixExec`) | Admin executor |
| Tray menu **修复连接… / Repair connections…** | The worst part: the connection while it is down, otherwise the executor |

Each chip is a button with a chevron, a hover veil, a focus ring and a tooltip ("See status and repair options"). It works offline and with no request selected.
Opening the dialog runs nothing. Polling never starts a repair and never raises UAC.
F5 still runs the plain retry.

## Layout

Both languages use the same order: a header and intro, then **Local connection**, then **Admin executor**, then a footer with a collapsed **Diagnostics** expander and **Close**. Each part has:
1. the name and a live chip,
2. what the component is,
3. what is wrong now,
4. its actions,
5. a checked result line (`AutomationProperties.LiveSetting=Polite`).

The window is a fixed 520 px wide and sizes to its content: 476–648 px tall across all states with diagnostics collapsed, so it fits a 660 px screen.
Its height is capped to the work area of its display (before it is placed, the owner's display). If it grows, for example when diagnostics open with a long backend error, it moves up so its bottom stays on screen (`RecoveryWindow.Bound`).
Past the cap, the shared `K.Scroll` area scrolls the content. Its 10 px gutter comes out of the right padding (24 → 14), so the text column keeps its width.
Close shares the Diagnostics row, so it is in view whenever that toggle is. Esc also closes.

| Part | Actions |
|---|---|
| Connection | **Reconnect** (primary, only when not online). **Restart broker** (danger, only when the tray owns the broker; two-step confirm), with the warning "ends all current access; N queued/running tasks may be interrupted". |
| Executor | **Start executor** (primary, scheduled task, normally no UAC). After `NeedsElevation`, an amber card with the shield and an explanation appears, with **Start as administrator**. This is the only shield in the dialog, and it runs only from its own click. |

Busy: the clicked button swaps to a spinner and a busy label (Reconnecting… / Restarting the broker… / Starting and checking… / Waiting for Windows…). Its command refuses a second click, and the other part stays usable.
Diagnostics show the connection and gate details and the last backend `State: Detail` of each part, in copyable text boxes.

## Backend contract mapping

The calls are `Monitor.RecoverAsync(restart)` and `Recovery.ExecutorAsync(elevate)`, then `Coord.Retry()` after each executor attempt.

| RepairState | Result line | Tone |
|---|---|---|
| Ready | Connected again / Executor ready (the probe answered) | green |
| Started | Started, not confirmed yet | amber |
| NeedsElevation | "Registered startup didn't restore the executor. No prompt was shown." The backend returns it when the task is missing or refused, or when the task starts but no trusted ready answer arrives within 8 s. It unlocks the shield card; elevation still needs its own click, never auto-UAC. | amber |
| Cancelled | Prompt cancelled; the fallback stays one click away | muted |
| NotOwned | Broker not owned, nothing restarted | red |
| Failed / exception | Not recovered; see diagnostics | red |

Only Ready is worded as working. Detail is shown only in diagnostics.

## Offline evidence, final pass (`artifacts/recovery-final`)

- Fresh build with `--artifacts-path artifacts/recovery-final/build`.
- Self-test: **815 checks, 0 failed** (`self/ui-selftest.txt`). Render check: **1039 checks, 0 failed** (`render/`). Fake transports only.
- Added:
  - a timed-out scheduled start (NeedsElevation with a long backend error) offers the fallback without invoking it
  - `Bound` caps the height to the work area and keeps the bottom on screen
  - with diagnostics expanded at 480 and 660 px work areas, in zh and en, the content scrolls in the themed gutter, the fallback and Close scroll whole into view, and the end of the error is reachable (`dialog-*-tight-*.png`)
  - every dialog state shows exactly one spinner per action in flight, and none at rest
- The at-rest height assertions (≤ 660 px) are kept.

## Offline evidence (`artifacts/recovery-ui`)

- Build: `dotnet build src/PwshMcp.Tray/PwshMcp.Tray.csproj --artifacts-path artifacts/recovery-ui/build`. bin and obj stay there, separate from the parent's build.
- Self-test: `dotnet artifacts/recovery-ui/build/bin/PwshMcp.Tray/debug/PwshMcp.Tray.dll --self-test artifacts/recovery-ui/self` gives **783 checks, 0 failed**.
- Render check: same DLL with `--render-check artifacts/recovery-ui/render` gives **995 checks, 0 failed**, with PNGs in `render/`.
- The `Check.Repairs` fakes (`FixVm.Seed`) count every broker, task and elevated call. No process, scheduled task, pipe or UAC is touched.
- The fakes resume through an inline `SynchronizationContext`, because the dispatcher queue is never pumped (it holds the app's shutdown callback).
- Covered:
  - polling starts nothing
  - both-down availability
  - every entry point aims the right part
  - busy labels and refused double clicks
  - the shield only after NeedsElevation and only on the fallback
  - Cancelled, Started and Failed are never worded ready
  - restart hidden unless owned, and two-click
  - NotOwned
  - the same action order in zh and en
  - no clipping at 440/520 dialog widths or in the 720/1000 main header (both status chips whole)
  - dialog composited over 720/1000

## Known limits

- In the renders, command-bound buttons can look disabled. That is because the WPF re-query (`CommandManager`) is a dispatcher post that the offline check never runs. The checks assert on the bound commands instead.
- Nothing here was opened as a real window, installed or run against the real backend. Real-UI acceptance is still to do.
