# Desktop installer implementation result

- Added `scripts/install-desktop.ps1` for v0.4.0: requires elevation and an exact current-user SID match, validates package contents and rejects reparse points, refuses the legacy broker service and in-use install processes, keeps a protected backup, installs the fixed `PwshMcp.Host.exe admin` logon task, adds the user's normal tray Run entry, writes an outcome receipt, and starts only the task.
- Added `scripts/uninstall-desktop.ps1`: removes only the current SID's fixed task and Run value; optional file removal checks the canonical Program Files target and reparse paths and stops only processes whose executable path is inside that target.
- Validation: The first AST pass found invalid static-method call syntax; it was corrected and the final parser pass reports both scripts clean. `git diff --check` passed. Installer/UAC and runtime task registration were not executed.
- Environment failures: none during this task. The initial default shell launch failed with `CreateProcessAsUserW 5`; the requested inspection and validation used the configured Windows PowerShell with `require_escalated` and succeeded.
