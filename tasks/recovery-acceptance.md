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
