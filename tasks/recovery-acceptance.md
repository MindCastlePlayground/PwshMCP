# Offline recovery acceptance

## Incident and live recovery (2026-10-29)

- Installed Broker was reachable after restarting, but the administrator coordinator was absent. Its registered task had last returned exit code 1.
- Starting the existing administrator scheduled task restored the coordinator without UAC. A trusted pipe probe then reported both Broker and executor online, with no running commands or active grants.
- The previous installation did not persist process stderr. The original exit cause is therefore unconfirmed; no matching Windows Application error was found in the inspected interval.

## Changes

- Broker and administrator pipe loops retry Windows pipe-instance exhaustion (231).
- Per-client failures, including stream disposal and peer lookup after a client exits, are contained inside the serving call.
- Runtime diagnostics record process start, session end and exceptions without command contents or environment values.
- User-triggered connection recovery waits for a successful new snapshot. Explicit restart affects only a Broker owned by this tray.
- Executor recovery starts the registered task and verifies its trusted pipe. If that does not restore readiness, a separate explicit administrator action is offered; polling and normal approval never invoke UAC.

## Evidence boundaries

- New isolated Windows pipe tests passed: busy error classification, exited-client handling and a subsequent status request. They do not reproduce the original incident or prove whole-loop saturation recovery.
- Installed administrator boundary test passed: coordinator ready; non-tray approve/resume requests rejected.
- UI tests, final package installation and TaskHub crash recovery remain pending until their results are appended below.

## Published UI and review

- Published `f441ce5` adds the recovery window, header/status entry points, tray entry, explicit UAC fallback, busy states and bilingual text.
- Claude's first completed UI pass: 783 self-test checks and 995 render checks, all passing. These use fake transports and do not start executors or approve grants.
- Review requested a final correction for expanded diagnostics exceeding the screen, plus wording for a registered task that starts but never becomes ready. Final installation waits for this correction.
- Added a deterministic closed-stream fixture: the unguarded exchange faults, while the new serving boundary contains it. The isolated recovery test passes.

## Final package

- Final Claude UI correction passed 815 self-test checks and 1039 render checks. Constrained 480/660px views include expanded diagnostics and a long backend error; scrolling keeps controls reachable. Chinese and English renders were inspected.
- Release package built successfully in `artifacts/recovery-package`. Actual installation and TaskHub acceptance still require the user to approve the concrete installation request in the installed tray.
- The actual release package also passed all 815 offline self-tests after publication (`artifacts/recovery-package-check/ui-selftest.txt`). Installation request title: `安装离线恢复界面并接入 TaskHub`; submitted and waiting for tray approval.
- The old installed coordinator exited again with task result 1 during development, and was restarted through its existing scheduled task without UAC. This reinforces the need to validate the replacement runtime rather than equating a restart with a permanent fix.

## Installation and regression findings (2026-10-29)

- The approved whole upgrade completed at `2026-10-28T17:25:24Z`; protected receipt and binary hashes confirmed installation. The trusted Broker/executor probe and administrator boundary tests passed.
- Real tray crash recovery failed. TaskHub continued reporting the dead tray PID as running. A subsequent tray output-redirection patch was installed at `2026-10-28T17:32:53Z`, but the same acceptance still failed. Do not treat `dc6f4e0` as a verified daemon recovery fix.
- The user reported `CompositionTarget.SetRootVisual` / `MainWindow.Present` throwing `ArgumentException`. A thread-local CBT hook that reenters `Present` during HWND creation reproduced the same stack; reentry at `SourceInitialized` alone did not.
- `c182f9f` guards the full presentation operation against reentry. The same native regression now passes, including hide/show and close/reopen. The rebuilt release package passed all 815 offline self-tests. This patch is not yet installed.
- TaskHub restart run `ce6fd6fd-d5b5-48f5-9647-305ee24c3464` restored tray PID 91304 under host PID 67616. Manual recovery works; automatic recovery remains unaccepted pending the host fix.
- Claude delegation is temporarily disabled at the user's request.

## Combined correction ready for installation

- TaskHub `internal/daemon/host.go` now leaves `Cmd.Stdout` / `Cmd.Stderr` nil instead of assigning `io.Discard`. Output goes directly to the null device; no copying goroutines wait on inherited pipes after the tray exits.
- Review corrected the new regression helper to explicitly inherit `os.Stdout` and `os.Stderr`. With this real inherited-handle fixture, the old host fails after 10.604 seconds (dead parent still reported running), and the fixed host passes in 5.307 seconds. The earlier test without explicit inheritance did not establish this distinction.
- The standalone TaskHub repository has no Git metadata. Its source and test changes are local, not part of the PwshMCP Git history. Built host SHA256: `C031CC16F2E6F782BEE351881B64C233BB3078852268FA17734EADA1975C2477`.
- Combined installer backs up the tray and scheduled task, installs a private corrected host under `C:\Program Files\PwshMCP`, and changes only the PwshMCP task's executable. Shared TaskHub host binaries, other daemon tasks, Worker and administrator executor are not restarted or replaced.
- The pending tray request is `修复窗口打开异常与守护自动恢复`. Until it is approved and the protected receipt and live crash test are checked, installation and real recovery remain unaccepted.
