param(
    [string]$Source = 'artifacts/tray-gui-polish',
    [string]$Log,
    [string]$Task = 'TaskHub Daemon pwshmcp'
)
$ErrorActionPreference = 'Stop'

class TrayUpdate {
    static [string]$Target = 'C:\Program Files\PwshMCP'
    static [string[]]$Files = @('PwshMcp.Tray.dll', 'PwshMcp.Tray.exe', 'PwshMcp.Tray.pdb')
    static [string]$TrayName = 'PwshMcp.Tray.exe'

    static [void] Say([string]$Path, [string]$Message) {
        Add-Content -LiteralPath $Path -Value ('[{0}] {1}' -f (Get-Date -Format 'HH:mm:ss'), $Message) -Encoding UTF8
    }

    static [object] Tray() {
        $exe = Join-Path ([TrayUpdate]::Target) ([TrayUpdate]::TrayName)
        return Get-CimInstance Win32_Process | Where-Object {
            $_.Name -eq [TrayUpdate]::TrayName -and $_.ExecutablePath -and
            ([IO.Path]::GetFullPath($_.ExecutablePath)).Equals($exe, [StringComparison]::OrdinalIgnoreCase)
        } | Select-Object -First 1
    }
}

$root = Split-Path -Parent $PSScriptRoot
$source = if ([IO.Path]::IsPathRooted($Source)) { $Source } else { Join-Path $root $Source }
if (-not $Log) { $Log = Join-Path $root 'Temp\tray-update.log' }
$target = [TrayUpdate]::Target
$trayExe = Join-Path $target ([TrayUpdate]::TrayName)
$backup = $null

try {
    Set-Content -LiteralPath $Log -Value '' -Encoding UTF8
    if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw 'Run this script explicitly as administrator.'
    }
    [TrayUpdate]::Say($Log, "elevated; source=$source")
    foreach ($f in [TrayUpdate]::Files) {
        if (-not (Test-Path -LiteralPath (Join-Path $source $f) -PathType Leaf)) { throw "Missing source file: $f" }
    }

    $backup = Join-Path $target ('backup-ui-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $backup | Out-Null
    [TrayUpdate]::Say($Log, "backup=$backup")

    # Stop the daemon first so it cannot relaunch the tray while files are being replaced.
    $task = Get-ScheduledTask -TaskName $Task -ErrorAction SilentlyContinue
    if ($task) { Stop-ScheduledTask -TaskName $Task; [TrayUpdate]::Say($Log, 'daemon task stopped'); Start-Sleep -Seconds 2 }

    $old = [TrayUpdate]::Tray()
    while ($old) {
        [TrayUpdate]::Say($Log, "stop tray pid=$($old.ProcessId)")
        Stop-Process -Id $old.ProcessId -Force -ErrorAction SilentlyContinue
        Start-Sleep -Seconds 2
        $old = [TrayUpdate]::Tray()
    }

    foreach ($f in [TrayUpdate]::Files) {
        $dest = Join-Path $target $f
        if (Test-Path -LiteralPath $dest) { Copy-Item -LiteralPath $dest -Destination (Join-Path $backup $f) -Force }
        Copy-Item -LiteralPath (Join-Path $source $f) -Destination $dest -Force
        $installed = (Get-FileHash -LiteralPath $dest -Algorithm SHA256).Hash
        if ($installed -ne (Get-FileHash -LiteralPath (Join-Path $source $f) -Algorithm SHA256).Hash) { throw "Hash mismatch after copy: $f" }
        [TrayUpdate]::Say($Log, "copied $f $installed")
    }
    [TrayUpdate]::Say($Log, 'installed product version=' + (Get-Item (Join-Path $target ([TrayUpdate]::Files[0]))).VersionInfo.ProductVersion)

    if ($task) { Start-ScheduledTask -TaskName $Task; [TrayUpdate]::Say($Log, 'daemon task started') }

    $deadline = (Get-Date).AddSeconds(30)
    $new = $null
    do {
        Start-Sleep -Milliseconds 500
        $new = [TrayUpdate]::Tray()
    } while (-not $new -and (Get-Date) -lt $deadline)
    if (-not $new) {
        [TrayUpdate]::Say($Log, 'daemon did not relaunch the tray; starting it through explorer for a normal token')
        Start-Process explorer.exe -ArgumentList ('"' + $trayExe + '"')
        Start-Sleep -Seconds 5
        $new = [TrayUpdate]::Tray()
    }
    if (-not $new) { throw 'Tray did not start in the interactive session.' }

    [TrayUpdate]::Say($Log, "tray pid=$($new.ProcessId) created=$($new.CreationDate)")
    [TrayUpdate]::Say($Log, 'SUCCESS')
} catch {
    [TrayUpdate]::Say($Log, 'FAILED: ' + $_.Exception.Message)
    if ($backup -and (Test-Path -LiteralPath $backup)) { [TrayUpdate]::Say($Log, "rollback source: $backup") }
    try {
        if (Get-ScheduledTask -TaskName $Task -ErrorAction SilentlyContinue) {
            Start-ScheduledTask -TaskName $Task
            [TrayUpdate]::Say($Log, 'daemon task restarted after failure')
        }
    } catch { [TrayUpdate]::Say($Log, 'daemon restart failed: ' + $_.Exception.Message) }
    throw
}
