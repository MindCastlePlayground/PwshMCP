# Broker history

Broker state keeps every live job and grant, plus the latest 100 terminal job summaries and 100 ended grants. Completed jobs are written atomically under the broker state's `history/jobs` directory before their command and result payloads are released. Job records use Windows DPAPI CurrentUser encryption and validated deterministic job IDs. Archives retain command text, output and errors for `status`/`output` reads, and omit environment variable values. Completed-job snapshots and broker-state JSON contain summaries. Older ended grants are stored as metadata under `history/grants`.

After archival, older summaries leave memory while task IDs remain queryable and duplicate submissions still resolve to their original execution. Archive reads enforce the same owner and Windows session checks as live tasks. The broker releases references; it does not force a garbage collection on each poll.

A write error retains the original payload in memory and is recorded in the broker runtime log. Restoring disk access lets the next maintenance pass retry. Missing or corrupt archives produce an error when queried. Restart loads recent state summaries and retrieves archived details on demand; it does not load the entire archive.

The archive lives beside `broker-state.json`: `%LOCALAPPDATA%\PwshMCP\history` for a user broker, or `%PROGRAMDATA%\PwshMCP\history` for SYSTEM. DPAPI records are readable by the Windows account that created them. Disk history is retained for task retrieval and duplicate-submission detection; it is not automatically deleted.
