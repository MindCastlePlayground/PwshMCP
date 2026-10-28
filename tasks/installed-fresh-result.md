# Installed MCP fresh environment acceptance

Run with the exact installed host executable:

```powershell
python tests/installed_fresh.py --host 'C:\Path\To\PwshMcp.Host.exe'
```

Add `--elevated` to validate an elevated run. If this client has no active grant, the script queues its own request and waits up to 180 seconds for tray approval; it never approves through `ctl` or opens UAC. The MCP client name defaults to `codex` and can be overridden with `--client`.

The script performs MCP stdio initialize, tools/list, run, status, and output calls for three sequential commands. It checks process-local shell state isolation, HKCU environment value refresh/update/deletion, three distinct process IDs and environment IDs, and the expected administrator token. The unique HKCU value is restored in `finally`. Only syntax validation was run during authoring; installed-host acceptance has not been executed.
