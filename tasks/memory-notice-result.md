# Memory and notification update — 2026-11-01

## Backend

- Commit `af20207` pushed to `origin/main`.
- `dotnet run --project tests/PwshMcp.History.Tests/PwshMcp.History.Tests.csproj`: 24 checks passed. Covers bounded summaries, encrypted output, live task retention, null-result rejection, idempotency/conflict, ownership, traversal rejection, output cursor, restart, ended grants, write failure/retry, corrupt archive errors and exited worker cleanup. Worker cleanup committed and pushed as `c2b1d87`.
- Post-push isolated build: `dotnet publish src/PwshMcp.Host/PwshMcp.Host.csproj -c Release -p:DefineConstants=PWMC_TEST_CHANNEL --artifacts-path artifacts/history-build -o artifacts/test --nologo -v quiet`.
- `python tests/integration.py`: 11 checks passed, including actual stdio MCP → broker → worker → PowerShell, output/error retrieval, cancellation, worker death, broker restart and idempotency.
- Production installation and grants were not changed by these checks.

## UI

- UI implementation committed and pushed as `d86d707`. Claude Code used `claude-opus-5-5`, effort `medium`. The follow-up run ended with service quota HTTP 403 after writing its code; the parent built and verified the actual changes independently.
- Tray build: zero warnings/errors. Notification tests passed, including archived command selection/poll/release, output release, full-screen deferral, reclaim debounce, chime samples and icon states. Native Window tests passed, including reentrant activation, prewarm without an HWND, hide/reopen, placement restoration and notice reference release.
- Initial UI render check: 1049 checks, zero failures. Parent visually inspected the generated active/pending tray mark. Audio format/envelope checked; subjective listening remains for the user.
- Post-push candidate published to `artifacts/memory-package` (Host, Admin and Tray). Production installation remains unchanged.
- Post-push isolated backend integration repeated against `c2b1d87`: all 11 checks passed.
- Native interactive fixture launched successfully (`--inspect`, synthetic data, no production authorization). Computer Use application selection then timed out waiting for app approval, so manual click acceptance was not completed. The fixture process was stopped afterward.
- Real fullscreen apps and measured long-duration memory savings remain unmeasured. Policy and lifecycle behavior are covered by the focused tests above.
- Pinterest refresh could not start: browser inventory returned `browser access host unavailable`; existing index retained.
