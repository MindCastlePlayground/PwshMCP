# Fresh environment correction

`FreshEnvironment.Create` now builds the block with the current process token, overlays current machine and current-user registry values, expands only `REG_EXPAND_SZ` values, and appends the user PATH to the machine PATH. The token belongs to the same Windows identity as the process. Generated `USERPROFILE`, `USERNAME`, and `TEMP` values are retained. Explicit overrides are applied last, so null removes a key including PATH.

Run the no-package Windows console checks with:

```powershell
dotnet run --project tests/PwshMcp.Environment.Tests/PwshMcp.Environment.Tests.csproj
```

The checks temporarily modify only two values under the current user's `Environment` registry key, then restore their original values and types in `finally`. They verify current-user identity variables, machine and user PATH participation, command lookup through the resulting PATH, literal override preservation, explicit removal, and live user value update/deletion.
