# PwshMCP tray · UI design

Owner: Claude (UI/UX). Backend gaps live in [UI-ALIGNMENT.md](UI-ALIGNMENT.md). Result and evidence: [UI-RESULT.md](UI-RESULT.md).

## Direction contract

- **THESIS** — Administrator access is a *badge issued to a visitor*: someone asks at the desk, a person checks who they are and what they will do, issues a badge with a visible validity window, and can pull it at any time. Refuses the category default of a log console with a toggle.
- **OWN-WORLD** — Windows-native neutrals that follow the system light/dark theme, one indigo accent for primary actions and selection, reader-LED semantics for state (amber waiting, green issued, red refused, grey ended) always paired with a shape/line rule: *dashed outline = waiting, solid fill = issued, struck text = ended, slashed = offline*. The badge silhouette (rounded card with clip slot) is the icon, the tray glyph and the grant card.
- **STORY** — The user notices a changed tray badge, opens the window when convenient, reads who asks, why, and the exact commands waiting, approves through one UAC prompt with a clear duration, then sees the badge active with a time band and can revoke in one step.
- **FIRST VIEWPORT** — Header strip: badge mark, product name, connection line, "Revoke all" (disabled when nothing is active). Left pane: segmented Requests / Access / Tasks with counts, list of rows. Right pane: the selected item's detail at reading size, primary action bottom-right. With a pending request it is selected by default and its detail is the first thing read.
- **FORM** — Grounded list #3 ("visitor desk / access badge"), raised by: *gate board* (a new row stays marked until viewed), *exposure record* (state readable by line style, not colour), *dance score* (the validity band's length is proportional to real time). Seed c30e08ef.
- **FINISH** — unreviewed and undocumented is unfinished; this build ends with the finish review, the verdict, DESIGN.md, and every shipping raster carrying its provenance.

## Surfaces

### 1. Tray icon (always)
Badge glyph drawn per DPI at 16/20/24/32 px, re-rendered when the taskbar theme changes and when Explorer restarts (TaskbarCreated).

| State | Glyph | Tooltip |
|---|---|---|
| Offline | outline badge + diagonal slash, 60% opacity | PwshMCP · Not connected |
| Idle | outline badge | PwshMCP · No active access |
| Active | solid badge with punched slot | PwshMCP · Administrator access on (n) |
| Pending | current glyph + solid amber corner dot with ring | PwshMCP · n request(s) waiting |

No blinking, no balloon for new requests, no focus change. Left double-click opens the window.

Menu: status line (disabled) · **Open PwshMCP** (default) · Review requests (n) · — · Revoke all access · — · Exit.
Exit: if the tray started the broker, a Windows task dialog explains what stops (n running/queued tasks are cancelled, agents disconnect) with *Exit and stop broker* / *Keep running*. Connected to a service: tray just closes, says the service keeps running.

### 2. Dashboard window (on demand, `--show`, menu)
Resizable (min 720×480, default 1000×660), per-monitor DPI v2, remembers nothing on disk. Layout: header strip → split view with GridSplitter.

- **Requests**: pending grants. Row: client, reason (1 line), age; dashed amber chip "Waiting"; unseen dot until selected. Detail: client, reason (full, plain text), Windows user, session, submitted time (absolute + relative), expiry of the request (24 h), *Commands waiting (n)* each with working directory, timeout, env variable **names** only, monospace code block (scroll, selectable). Actions: **Approve as administrator…** (shield; launches `--approve id` via runas on click) and **Deny**. UAC cancelled → inline note "Approval was cancelled at the Windows prompt. The request is still waiting."
- **Access**: Active first, then Ended (Denied / Revoked / Expired) under a divider. Detail: badge card with validity band (issued → now → ends; "until sign-out" draws an open, fading end), worker connection line (A3), **Revoke access** with two-step inline confirm, **Restart administrator worker…** when no heartbeat (A4).
- **Tasks**: jobs newest first, state chip, client, first code line, age. Detail: code, cwd, privilege, timeout, engine/identity/exit code when finished; **Show output** reveals output on click only; **Cancel task** for NeedsApproval/Queued/Running (A5).
- Empty states teach: "No requests. When an agent asks for administrator access it appears here and the tray icon gets an amber dot."
- Offline: header turns to "Not connected" with cause + **Retry now**; lists keep last known data greyed with "as of hh:mm:ss".

### 3. Approval window (elevated, `--approve <id>`)
Opened only from a user click → UAC. Loads one snapshot, freezes it (immutable view). Not elevated → error and disabled approval.
Content: headline "Give <client> administrator access?", requester facts, *What this allows* (fresh PowerShell per command, elevated, no further prompts until access ends), *Starts right away* list of waiting commands with previews, duration radios (default *Until you sign out of Windows*, 1 hour, 4 hours, End of today) with live band preview and absolute end time. Buttons: **Approve and start** / Cancel (Esc). Enter does not approve.
States: Loading · Ready · Already handled (shows its current state) · Sending · Starting worker · Done (closes after 1.5 s) · Failed (cause + Retry / Revoke / Close). UAC cancel never reaches here; approve failure never shows active.

### 4. Resume window (elevated, `--resume <id>`)
Same shell: verifies the grant is still Active and owned, launches worker hidden, reports result.

## Tokens
- Type: Segoe UI Variable Text → Segoe UI; code Cascadia Mono → Consolas. Scale 12 / 13 / 14 / 16 / 20 / 26, weights 400 / 600. Tabular numerals for times.
- Space: 4-pt grid; 24 px pane padding; 8 px within groups, 20–28 px between groups.
- Radius: 6 controls, 10 cards; badge card 12.
- Motion: 150 ms opacity/colour on hover/press; detail pane 180 ms fade+4 px rise on selection change; disabled entirely when Windows "Show animations" is off.
- Light: ground #F4F5F7, panel #FFFFFF, raised #EEF0F3, line #DDE1E6, ink #171A21, muted #555E6C, accent #3346C8, amber #8A5300/#FFF1D6, green #0B7A4E/#DFF4EA, red #B3261E/#FDE7E5.
- Dark: ground #14161B, panel #1B1E24, raised #242830, line #313640, ink #ECEEF2, muted #A0A8B6, accent #9AA6FF, amber #FFC46B/#3A2D14, green #6BD9A8/#15332A, red #FF8F86/#3D1B1A.
