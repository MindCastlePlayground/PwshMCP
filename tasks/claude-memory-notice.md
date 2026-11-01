[EXECUTOR] Implement requested tray optimizations directly, no delegation. Model claude-opus-5-5 --effort medium. cwd H:\Documents\AgentAbility\PwshMCP. Max 100 calls. Load only D:/Code/data/codex/skills/frontend-design/SKILL.md. Allowed edits: src/PwshMcp.Tray/**, tests/PwshMcp.Notification.Tests/**, tests/PwshMcp.Window.Tests/**, docs/UI-MEMORY.md. Parent/backend agent owns Host and Shared; no changes there. Do not commit/push/install or operate live grants, settings or UAC. Keep fake transport/offline checks. Build only explicit Tray csproj, not solution. Existing launcher permits Read/Write/Edit/Glob/Grep and dotnet/node shell commands. Do not include unrelated shell commands.

User goals: (1) long-running UI history memory reclamation with disk archive, (2) no notifications while fullscreen, (3) preload UI before non-fullscreen authorization popup and release UI resources otherwise, (4) optional gentle short notification sound, (5) richer readable tray icons.

Backend contract: parent implements encrypted archive for finished jobs, hot recent100 terminal jobs and100 ended grants + ALL live records; snapshot same schema, output/status read archive transparently. UI must bound caches and release loaded command outputs, seen/handoff sets; snapshot remains lightweight after completed job archive. Do not invent new APIs. Maintain current permission scope and approval behavior.

Implement Tray scope completely:
- Fullscreen suppression unconditional even if auto-DND setting is off. Keep manual DND/Windows quiet auto preference. No fullscreen balloons/toasts/sound, no fullscreen hint banner; requests remain pending and appear when fullscreen ends. Suppress action balloons too if fullscreen. QuietProbe split fullscreen vs other OS state if necessary.
- On incoming pending request while online/unmuted and not fullscreen, construct/measure authorization notice and prewarm main window before display, no focus theft. On idle/hide/minimize after short debounce (e.g.10s), retire hidden main window, detach handlers/popups/bindings, clear history VM/output caches while retaining minimum live grant/request data for tray. Rehydrate on explicit show; preserve tab and window bounds as lightweight state. Avoid repeated prewarm each poll for dismissed pending ids, avoid Release→prewarm loop; create windows only for a newly presented notification or explicit user action. Existing MainWindow.Retire and Present reentry guard must be preserved. Minimized main window can be released, recreate on tray click. Never release during active submit or visible windows. Request popup close should release its heavy refs. GC at most once after actual idle teardown, no poll timer forced GC. Background Monitor cached snapshots should not retain large completed output/error (backend will clear). Discuss any actual backend gap via docs/UI-MEMORY.md.
- Optional tray sound toggle, default OFF, persisted HKCU via NoticePrefs. Soft ~0.45s low-volume two-note sine chime (e.g.523+659Hz, smooth attack/release, no harsh click); create original .wav asset or deterministic generator. No network/audio service needed. Nonblocking playback once for new notification, DND/fullscreen suppresses, dispose player. Add explicit preview menu item that plays on user click and no auth changes. Keep bilingual menu labels. Export wav to Assets so user can audition it. Do not auto-play during tests.
- Richer tray artwork with recognizable terminal >_ or shield/terminal motif, clear idle/active/offline/pending shape+color states, retain high contrast at16px dark/light and 20/24/32/40/48. Avoid decorative clutter, gradients only where readable. Fix drawing resource leaks you touch. Export checked-in icon/png assets using existing export entrypoint.
- Focused tests extend existing Notification/Window tests or Check.cs covering preference defaults, suppression/deferred notification, lifecycle teardown/reopen/prewarm once, output release, icon render sizes and wave envelope/format without audible playback. Do not change real prefs in tests. Build/render-check once, inspect relevant outputs. Parent will reuse evidence.

Pinterest reference refresh attempted by parent 2026-11-01, cua.getState returned no browsers and 'browser access host unavailable'; no login/account confirmed. Keep prior index intact. Record refresh blocked, don't fabricate cases. Use existing native visual language and actual existing glyph design as this local revision reference. Do not expand into a full redesign.

Full required visual design prompt (applies only changed areas):
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
  [COMPARE] 结合选定案例与BENCHMARK审阅实际页面，聚焦具体差距
  [IF] 发现影响当前目标的具体问题 => [PATCH] 并仅复查受影响部分
  [IF] 达到ACCEPT => [Ω] 交付，不为追求无穷优化追加循环

Only requested changes, constants/enums centralized in Consts.cs, bilingual same hierarchy. Final <=25 lines with file list, exact tests, remaining issues; details docs/UI-MEMORY.md. User-facing docs explain product behavior only, process/reference/testing notes belong this tasks file or artifacts, not product copy. Do not claim real fullscreen/subjective sound acceptance from offline tests.

## Executor notes — 2026-11-01

### References
- Pinterest refresh blocked: `cua.getState` listed no browsers ("browser access host unavailable"). No new references were collected or invented; the existing index was kept.

### Implementation notes
- Snapshots redact `Result.Output` (`SafeJob`), so the details pane now reads output through the existing `Ops.Output` chunked call (`MainVm.Read`, capped by `Limits.Output`). No new API.
- Bug found and fixed: `NoticeWindow` Closed handler assigned the constructor parameters (same names), not the fields, so closed notices kept their references. Now uses `this.`; covered by the Window test (`Released`).
- Fixed a GraphicsPath leak in `Glyphs.Filled`; stale tray icons are disposed after replacement.
- GC runs only inside `App.Drop` (after an actual teardown), never on the poll timer.

### Tests run (offline, fake transport, no audio played)
- `dotnet build src/PwshMcp.Tray/PwshMcp.Tray.csproj`: 0 warnings, 0 errors.
- `dotnet run --project tests/PwshMcp.Notification.Tests`: PASS covers queue, preference defaults (sound off), Hush truth table, full-screen deferral/warm once, lean/rehydrate, output load/release/cap, remembered-id bound, Reclaim hold/fire/cancel, wav header/duration/peak/envelope, glyph state colours at 16–48 px on light/dark with and without pending, and app.ico frames.
- `dotnet run --project tests/PwshMcp.Window.Tests`: PASS covers native creation and reentry, hide/show, close/reopen, Spot after show, Retire drops DataContext, Warm creates no HWND, placement carried to the recreated window, and notice Warm/close release with deferral.
- `PwshMcp.Tray.dll --export-icons src/PwshMcp.Tray/Assets`: regenerated the PNGs, app.ico and chime.wav. 16 px and 32 px PNGs were inspected visually.
- `PwshMcp.Tray.dll --render-check`: `artifacts/ui-0.4.0/ui-selftest.txt` reports 1049 checks, 0 failed.

### Not verified
- Real full-screen behaviour (game, video, presentation) and multi-monitor `Covered()` detection were not exercised live.
- The chime was not listened to; subjective loudness and tone are unverified.
- Memory savings were not profiled on a long-running process.
- `badge.svg` was not updated to the `>_` motif.
