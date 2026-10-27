# UI / backend contract v1

Shared source: src/PwshMcp.Shared/Protocol.cs. net8.0-windows. UI references Shared only. UI owns all displayed copy, assets and windows. Backend owns transport, lifecycle and CLI.

Host executable: sibling PwshMcp.Host.exe. Commands:
- `broker`: foreground local broker for bound tray mode (no auto UAC). Tray may start hidden if pipe unavailable; track owned process and stop on tray explicit quit only after warning about tasks.
- `worker --grant <id>`: elevated worker, only start after successful approve.
- `mcp`: stdio MCP.
- `service`: SCM entry, installed by scripts separately.

Tray `--approve <id>`: UI launches itself via Verb=runas ONLY after user click; after elevation display immutable request from snapshot, confirm, call Approve Id + ExpiresAt (null = login session). If approved launch Host worker --grant id hidden under this elevated process. Exit approval window after successful handoff. Do not treat UAC cancellation as denied operation; retain pending request. Never start worker from normal tray token as elevated.

Normal tray polls snapshot ~1s asynchronously. snapshot has grants (Pending/Active/Denied/Revoked/Expired), jobs and broker mode. Pending icon distinct, no foreground activation or automatic UAC. List request client/label/user/time; grants expiry/heartbeat; revoke/deny via Request. Client-facing strings must be UI-owned.

Approve is enforced by server elevated identity (same requesting SID/session by default). Normal tray can deny/revoke its own requests, not approve. RequestAccess creates Pending for client. Submit elevated without grant creates Pending and job NeedsApproval. Approval automatically queues still valid pending jobs; approval dialog clearly says pending commands will start. No grant-expanding API from MCP.

Snapshot initially includes Run.Code for preview; do not render secret env values; output/logs may contain secrets, avoid displaying automatically. UI should show command on explicit request detail. Requests expire after 24h. Idempotency by client+request ID. User-supplied labels are text, not markup.

New requirements: Claude writes docs/UI-ALIGNMENT.md immediately with required field/action, rationale and blocking status. Main implements Shared/backend; Claude must not edit them. Main writes acknowledgement into that file. Claude can continue independent work, then reread acknowledgements before final integration. This is live collaboration within one implementation task.
