# PwshMcp.Peer.Tests

Environment failures recorded for this test project:

- 2026-10-28: the default unelevated PowerShell launch failed with `CreateProcessAsUserW failed: 5`; retrying the same bounded inspection with `require_escalated` succeeded. This is an execution environment failure, not a test result.
- 2026-10-28: a final read-only `git status` request hit the same default launcher error 5; project status was not rechecked by that command. No files outside this test directory were edited by this task.
- 2026-10-28: an initial test version blocked in the pipe handshake and left test processes holding the build outputs. The handshake now starts the server read before the client write and has a 10-second connection timeout; the blocked test processes were stopped before the successful runs.
