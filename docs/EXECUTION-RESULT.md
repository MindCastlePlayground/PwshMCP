# Execution library implementation result

Implemented `PwshMcp.Execution` as a `net8.0-windows` library referencing Shared. `Executor` starts a fresh trusted system PowerShell for each ordinary run, builds its environment from the current identity environment block and persisted machine/user values, then applies case-insensitive overrides and deletions. It validates absolute working directories, supports explicit persistent sessions with reset/close, preserves PowerShell output and error records, returns native exit status, kills process trees on timeout/cancellation, and caps returned output plus error at 1 MiB. Results include engine path, current identity, environment timestamp and unique environment ID.

`tests/PwshMcp.Execution.Tests` is a no-package console harness. `dotnet run --project tests/PwshMcp.Execution.Tests/PwshMcp.Execution.Tests.csproj` passed all 10 checks on 2026-10-27: persisted user environment update/deletion, case-insensitive overrides, output/error capture, native exit status, output bound, one-shot isolation, persistent session state and reset, and cancellation.

## Command environment notes

- Initial shell startup failed with `CreateProcessAsUserW failed: 5 (拒绝访问。)` under the configured sandbox. Subsequent repository commands used explicitly selected system Windows PowerShell with the required approval.
- `dotnet build ... --no-restore` initially failed because the new project had no `obj/project.assets.json`; a normal `dotnet run` restored both projects and built the library successfully.
- During implementation, harness checks caught and drove fixes for persistent scope and PowerShell error capture; the final harness passed.

## Evidence boundary

The harness ran under the current Windows user and validated normal, unprivileged execution. It did not exercise elevated approval, UAC handoff, broker integration, lock-screen behavior, or profile-enabled execution. `RunSpec.Privilege` is metadata owned by the host; this library does not elevate a child process.
