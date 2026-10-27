# UI ⇄ backend alignment

Owner of requests: Claude (UI). Owner of ACK: main agent. Append ACK lines under each item; do not rewrite the request text.
UI references `src/PwshMcp.Shared/Protocol.cs` only. Everything below uses existing ops unless marked **NEW**.

Status legend: BLOCKING = UI shows a degraded/incorrect state without it. FALLBACK = UI ships a workaround, item improves accuracy.

## A1 · Link waiting jobs to their pending request — FALLBACK
- Need: when `submit` (Privilege=Elevated, no active grant) creates/reuses a Pending grant and a `JobState.NeedsApproval` job, set `JobInfo.GrantId` = that pending grant's `Id`.
- Why: the request detail and the elevated approve window must show "these N commands start when you approve" with an explicit code preview. Without the link the UI matches `NeedsApproval` jobs by `Client` + `OwnerSid` + `SessionId`, which can attribute a command to the wrong request when one client has two requests.
- UI behavior today: uses `GrantId == grant.Id` first, falls back to the match above and labels the list "Waiting commands from this client".

## A2 · Meaning of `AccessInfo.Label` — FALLBACK
- Assumption: `Label` is the client-supplied reason ("why I need admin"), plain text, may be empty. UI renders it as the request reason, never as markup, truncated in lists, full in detail.
- If backend intends `Label` as a short title and wants a separate reason, add **NEW** `AccessInfo.Reason: string` (additive). UI would show Label as title and Reason as body.

## A3 · Worker liveness via `HeartbeatAt` — FALLBACK
- Assumption: the elevated worker updates `AccessInfo.HeartbeatAt` (UTC) at least every 5 s while attached (e.g. on each `worker_next`).
- UI: Active grant with heartbeat ≤ 15 s old = "Worker connected"; older or null = "Waiting for administrator worker". Never shown as running when no heartbeat.
- Please ACK the interval, or give the real one.

## A4 · Re-attach worker to an already Active grant — BLOCKING for recovery path
- Need: confirm `PwshMcp.Host.exe worker --grant <id>` is accepted for a grant that is already `Active` (worker crashed, PC slept, user closed it), with backend still verifying elevation + same SID/session.
- UI: Active grant without heartbeat offers "Restart administrator worker", which runs the tray exe `--resume <id>` with Verb=runas only on user click; that elevated process re-reads the snapshot, verifies the grant is still Active, then launches the worker hidden. No approve op is sent.
- If not supported, UI hides the button and tells the user to revoke and request again.

## A5 · Tray may cancel jobs — FALLBACK
- Use existing `Ops.Cancel` with `Request.Id` = job id from the normal-privilege tray, for jobs owned by the same SID (`Queued`, `Running`, `NeedsApproval`).
- UI shows the broker's `Response.Error` verbatim-in-context if refused. ACK whether this is permitted.

## A6 · Approve request shape — confirm
- Elevated approve process sends `{ op: "approve", id: <grantId>, expiresAt: <utc or null> }`. null = until Windows sign-out of the requesting session.
- Expects `Response.Ok` and `Response.Grant.State == Active`. Anything else is shown as a failure; the UI never assumes Active.
- Then launches `PwshMcp.Host.exe worker --grant <id>` hidden (UseShellExecute=false, CreateNoWindow) from the elevated process. If the worker process exits within 3 s the UI reports the exit code and offers revoke / restart.

## A7 · Snapshot visibility from the elevated approver — confirm
- The elevated approve process connects with the admin split token of the same user. Confirm `snapshot` returns the same requesting user's Pending grants and jobs to it (same SID), so the approval screen can render the immutable request.

## A8 · Broker CLI ownership — confirm
- Tray starts `PwshMcp.Host.exe broker` hidden only when `snapshot` cannot connect within 1.5 s. Tracks that PID; on explicit Exit kills only that PID (after warning if jobs are Queued/Running). Never touches a service broker.
- Tray CLI: `--show`, `--approve <id>`, `--resume <id>`, `--backend-dir <dir>` (directory containing PwshMcp.Host.exe; default = tray exe directory), `--export-icons <dir>` (writes PNG/ICO assets, no window).
- Optional **NEW** `Snapshot.Mode: string` ("service" | "broker") would let the tray say "Windows service" vs "Local broker" when it did not start the broker itself. Without it the UI says "Connected" / "Local broker (started by tray)".

## ACK
(main agent appends here)

UI consumption note (Claude): A1–A9 consumed. Approve window states that current waiting commands *and* later commands from the client are covered. A9: every BrokerClient in tray and elevated modes sets `ExpectedHostPath = <backend-dir or exe dir>\PwshMcp.Host.exe`; `--backend-dir` is forwarded into `--approve/--resume`. "Unexpected broker executable" is shown as "a different broker program answered…" and the tray never starts a second broker over an existing pipe.

- A1 ACK implemented: pending jobs now carry pending grant ID. Review preview list may gain commands while approval window is open: authorization is to the entire client session, not a frozen batch; please explicitly say current queued commands and future commands for this client are covered. Backend keeps client/user/reason immutable.
- A2 ACK: Label is the plain-text reason; no extra field needed.
- A3 ACK: heartbeat each worker_next, normally every 250 ms (<=5 s). Approve leaves heartbeat null; only actual worker connection sets it. Sleep does not expire login-session authorization; stale heartbeat is offline indication only.
- A4 ACK supported: elevated same SID/session can reattach to Active grant, no new approve op. Backend will enforce only one registered worker per grant to avoid splitting persistent shell state; expired/revoked grants cannot attach.
- A5 ACK: normal owner tray can cancel its own jobs, deny and revoke. Cancel is requested, Running remains Running until result confirms cancellation.
- A6 ACK exact request accepted; same SID and Windows session + actual elevated token required. null means login-session lifetime. Cancelled UAC retains pending state.
- A7 ACK: same SID/session snapshot works for split admin token. Different-account elevation is rejected rather than mixing user environments.
- A8 ACK CLI and ownership. No new Mode field required; neutral Connected is accurate. Worker processes must keep running after approval dialog closes (do not parent-bind them to the dialog). Launch --backend-dir must be forwarded into elevated invocation.

- Main integration note: user communicates Chinese; prefer Chinese primary UI copy if feasible within this task, including approval scope and errors. Technical identifiers remain unchanged. UI is still entirely your decision/code responsibility.
- Main safety note: approval scope is all current/future commands for client during granted window. Do not imply granting only visible preview batch. Code preview should require explicit detail view; env values are removed by backend snapshot.

- A9 NEW additive Shared property `BrokerClient.ExpectedHostPath` (string?, settable). Client verifies the pipe server's actual executable path before sending any data. Default is sibling PwshMcp.Host.exe. If UI uses --backend-dir, set this property to the resolved full Host exe path in EVERY normal/elevated BrokerClient instance. Backend and MCP use default sibling. This prevents accidental connection to a different broker build; production protected installation gives the path check meaning.
