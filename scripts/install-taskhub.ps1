param(
    [string]$TaskHub = 'H:/Documents/Abyss/TaskHub',
    [string]$Root = 'C:/ProgramData/TaskHub'
)
$ErrorActionPreference = 'Stop'

class TaskHubInstall {
    static [string]$Id = 'pwshmcp'
    static [string]$Exe = 'C:/Program Files/PwshMCP/PwshMcp.Tray.exe'
    static [string]$RunKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    static [string]$RunName = 'PwshMCP'
    static [string]$Worker = 'TaskHubWorker'
    static [string]$Task = 'TaskHub Daemon pwshmcp'
    static [string]$ManifestName = 'pwshmcp.toml'
    static [string]$SourceManifestName = 'pwshmcp.daemon.toml'

    static [void] Admin() {
        $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
        if (-not ([Security.Principal.WindowsPrincipal]::new($identity)).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) { throw 'Run explicitly as administrator.' }
        $explorer = Get-CimInstance Win32_Process -Filter "Name='explorer.exe'" | Where-Object { $_.SessionId -eq (Get-Process -Id $PID).SessionId } | Select-Object -First 1
        if (-not $explorer) { throw 'No interactive Explorer session for this administrator; run elevated in the same user session.' }
        $owner = Invoke-CimMethod -InputObject $explorer -MethodName GetOwnerSid
        if ($owner.ReturnValue -ne 0 -or $owner.Sid -ne $identity.User.Value) { throw 'Elevated caller is not the interactive user.' }
    }

    static [string] Config([string]$Path, [string]$Manifest) {
        $text = [IO.File]::ReadAllText($Path)
        $pattern = '(?m)^daemon_manifests\s*=\s*\[(.*?)\]'
        $match = [regex]::Match($text, $pattern)
        if (-not $match.Success) { throw "Missing daemon registry in $Path" }
        if ($match.Groups[1].Value -match [regex]::Escape($Manifest)) { throw "Daemon manifest already listed in $Path; inspect before retry." }
        return [regex]::Replace($text, $pattern, { param($m) $items = $m.Groups[1].Value.Trim().TrimEnd(','); $join = if ($items) { $items + ', ' } else { '' }; 'daemon_manifests = [' + $join + '"' + $Manifest + '"]' }, 1)
    }

    static [void] Service([string]$Name, [string]$State) {
        $svc = Get-Service -Name $Name
        if ($svc.Status.ToString() -ne $State) {
            if ($State -eq 'Stopped') { Stop-Service -Name $Name } else { Start-Service -Name $Name }
            (Get-Service -Name $Name).WaitForStatus($State, [TimeSpan]::FromSeconds(30))
        }
    }
}

$changed = $false
$taskAdded = $false
$runExisted = $false
$trayWasRunning = $false
$workerWasRunning = $false
$workerStopped = $false
$manifestInstalled = $false
$manifestSource = $false
$backupDir = $null
$receiptPath = $null
$oldRun = $null
try {
    [TaskHubInstall]::Admin()
    $TaskHub = [IO.Path]::GetFullPath($TaskHub)
    $Root = [IO.Path]::GetFullPath($Root)
    $installedCfg = Join-Path $Root 'config\worker.toml'
    $sourceCfg = Join-Path $TaskHub 'config\worker.toml'
    $installedManifest = Join-Path $Root ('config\daemons\' + [TaskHubInstall]::ManifestName)
    $sourceManifest = Join-Path $TaskHub ('config\' + [TaskHubInstall]::SourceManifestName)
    $bridge = Join-Path $Root 'daemons\pwshmcp'
    $workerStatus = Join-Path $Root 'data\worker-status.json'
    $daemonStatus = Join-Path $bridge 'status.json'
    $daemonTool = Join-Path $TaskHub 'tools\daemon.ps1'
    $hostExe = Join-Path $Root 'bin\taskhub-daemon-host.exe'
    if (-not (Test-Path -LiteralPath $installedCfg -PathType Leaf) -or -not (Test-Path -LiteralPath $sourceCfg -PathType Leaf)) { throw 'Installed and source worker configs are both required.' }
    $trayExe = [IO.Path]::GetFullPath([TaskHubInstall]::Exe)
    if (-not (Test-Path -LiteralPath $trayExe -PathType Leaf)) { throw "Tray executable not found: $trayExe" }
    if (-not (Test-Path -LiteralPath $hostExe -PathType Leaf) -or -not (Test-Path -LiteralPath $daemonTool -PathType Leaf)) { throw 'TaskHub daemon host or registration script is missing.' }
    if ((Test-Path -LiteralPath $installedManifest) -or (Test-Path -LiteralPath $sourceManifest)) { throw 'PwshMCP daemon manifest already exists; inspect before retry.' }
    if (Get-ScheduledTask -TaskName ([TaskHubInstall]::Task) -ErrorAction SilentlyContinue) { throw 'PwshMCP daemon task already exists; inspect before retry.' }
    $worker = Get-Service -Name ([TaskHubInstall]::Worker)
    $workerWasRunning = $worker.Status -eq 'Running'
    if (-not $workerWasRunning) { throw 'TaskHub Worker must already be running.' }
    $state = Get-Content -LiteralPath $workerStatus -Raw | ConvertFrom-Json
    if ($state.active -ne 0 -or $state.status -ne 'online' -or ([DateTime]::UtcNow - [DateTime]::Parse($state.updated_at).ToUniversalTime()).TotalSeconds -gt 30) { throw 'TaskHub Worker is not online, freshly reporting, and idle; retry when idle.' }
    $interactiveSid = [Security.Principal.WindowsIdentity]::GetCurrent().User.Value
    $sessionId = (Get-Process -Id $PID).SessionId
    $trayProc = @(Get-CimInstance Win32_Process | Where-Object { $_.SessionId -eq $sessionId -and $_.ExecutablePath -and ([IO.Path]::GetFullPath($_.ExecutablePath)).Equals($trayExe, [StringComparison]::OrdinalIgnoreCase) })
    $trayWasRunning = $trayProc.Count -gt 0
    $runProp = Get-ItemProperty -LiteralPath ([TaskHubInstall]::RunKey) -Name ([TaskHubInstall]::RunName) -ErrorAction SilentlyContinue
    if ($runProp) { $runExisted = $true; $oldRun = $runProp.([TaskHubInstall]::RunName) }

    $stamp = Get-Date -Format 'yyyyMMdd-HHmmss-fff'
    $backupDir = Join-Path $Root ('backups\pwshmcp-' + $stamp)
    New-Item -ItemType Directory -Path $backupDir -Force | Out-Null
    & icacls.exe $backupDir '/inheritance:r' '/grant:r' '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Backup directory ACL failed.' }
    Copy-Item -LiteralPath $installedCfg -Destination (Join-Path $backupDir 'installed-worker.toml')
    Copy-Item -LiteralPath $sourceCfg -Destination (Join-Path $backupDir 'source-worker.toml')
    [ordered]@{ existed = $runExisted; value = $oldRun } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $backupDir 'hkcu-run.json') -Encoding UTF8
    $nextInstalled = [TaskHubInstall]::Config($installedCfg, './daemons/' + [TaskHubInstall]::ManifestName)
    $nextSource = [TaskHubInstall]::Config($sourceCfg, './' + [TaskHubInstall]::SourceManifestName)
    $state = Get-Content -LiteralPath $workerStatus -Raw | ConvertFrom-Json
    if ($state.active -ne 0 -or $state.status -ne 'online' -or ([DateTime]::UtcNow - [DateTime]::Parse($state.updated_at).ToUniversalTime()).TotalSeconds -gt 30) { throw 'TaskHub Worker became busy or stale before the change; retry when idle.' }

    if ($trayWasRunning) { foreach ($p in $trayProc) { Stop-Process -Id $p.ProcessId } }
    New-Item -ItemType Directory -Path $bridge -Force | Out-Null
    & icacls.exe $bridge '/inheritance:r' | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Bridge ACL inheritance update failed.' }
    $serviceSid = ([Security.Principal.NTAccount]'NT SERVICE\TaskHubWorker').Translate([Security.Principal.SecurityIdentifier]).Value
    & icacls.exe $bridge '/grant:r' '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' ('*' + $serviceSid + ':(OI)(CI)M') ('*' + $interactiveSid + ':(OI)(CI)M') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Bridge ACL grant failed.' }
    [TaskHubInstall]::Service([TaskHubInstall]::Worker, 'Stopped')
    $workerStopped = $true
    $bridgeConfig = $bridge.Replace('\', '/')
    $manifest = @"
id = "pwshmcp"
name = "PwshMCP"
program = "C:/Program Files/PwshMCP/PwshMcp.Tray.exe"
args = []
cwd = "C:/Program Files/PwshMCP"
bridge_dir = "$bridgeConfig"
auto_start = true
restart_seconds = 5
"@
    $changed = $true
    New-Item -ItemType Directory -Path (Split-Path -Parent $installedManifest) -Force | Out-Null
    $manifestInstalled = $true
    [IO.File]::WriteAllText($installedManifest, $manifest + "`n", [Text.UTF8Encoding]::new($false))
    $manifestSource = $true
    [IO.File]::WriteAllText($sourceManifest, $manifest + "`n", [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($installedCfg, $nextInstalled, [Text.UTF8Encoding]::new($false))
    [IO.File]::WriteAllText($sourceCfg, $nextSource, [Text.UTF8Encoding]::new($false))
    if ($runExisted) { Remove-ItemProperty -LiteralPath ([TaskHubInstall]::RunKey) -Name ([TaskHubInstall]::RunName) }
    [TaskHubInstall]::Service([TaskHubInstall]::Worker, 'Running')
    $workerStopped = $false
    & $daemonTool -Action Register -Id ([TaskHubInstall]::Id) -Manifest $installedManifest -HostExe $hostExe -UserId ([Security.Principal.WindowsIdentity]::GetCurrent().Name)
    $taskAdded = $true
    $deadline = [DateTime]::UtcNow.AddSeconds(15)
    $ready = $false
    do {
        if (Test-Path -LiteralPath $daemonStatus) {
            $entry = Get-Content -LiteralPath $daemonStatus -Raw | ConvertFrom-Json
            $live = Get-CimInstance Win32_Process | Where-Object { $_.ProcessId -eq $entry.pid -and $_.ExecutablePath -and ([IO.Path]::GetFullPath($_.ExecutablePath)).Equals($trayExe, [StringComparison]::OrdinalIgnoreCase) }
            if ($entry.state -eq 'running' -and $entry.pid -gt 0 -and $live) { $ready = $true; break }
        }
        Start-Sleep -Milliseconds 300
    } while ([DateTime]::UtcNow -lt $deadline)
    if (-not $ready) { throw 'PwshMCP bridge did not report running with the matching live tray PID within 15 seconds.' }
    $receiptPath = Join-Path $backupDir 'receipt.json'
    [ordered]@{ outcome='installed'; id=[TaskHubInstall]::Id; task=[TaskHubInstall]::Task; userSid=$interactiveSid; bridge=$bridge; backup=$backupDir; timestamp=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $receiptPath -Encoding UTF8
    Write-Output "Installed PwshMCP TaskHub daemon. Backup: $backupDir"
} catch {
    $failure = $_
    if ($backupDir -and (Test-Path -LiteralPath $backupDir)) {
        try {
            if ($taskAdded -or (Get-ScheduledTask -TaskName ([TaskHubInstall]::Task) -ErrorAction SilentlyContinue)) {
                Disable-ScheduledTask -TaskName ([TaskHubInstall]::Task) -ErrorAction SilentlyContinue | Out-Null
                Stop-ScheduledTask -TaskName ([TaskHubInstall]::Task) -ErrorAction SilentlyContinue
                Unregister-ScheduledTask -TaskName ([TaskHubInstall]::Task) -Confirm:$false -ErrorAction SilentlyContinue
            }
            if ($changed) {
                [TaskHubInstall]::Service([TaskHubInstall]::Worker, 'Stopped')
                [IO.File]::Copy((Join-Path $backupDir 'installed-worker.toml'), $installedCfg, $true)
                [IO.File]::Copy((Join-Path $backupDir 'source-worker.toml'), $sourceCfg, $true)
            }
            if ($manifestInstalled) { Remove-Item -LiteralPath $installedManifest -ErrorAction SilentlyContinue }
            if ($manifestSource) { Remove-Item -LiteralPath $sourceManifest -ErrorAction SilentlyContinue }
            if ($runExisted) { New-ItemProperty -LiteralPath ([TaskHubInstall]::RunKey) -Name ([TaskHubInstall]::RunName) -PropertyType String -Value $oldRun -Force | Out-Null }
            elseif (Test-Path -LiteralPath ([TaskHubInstall]::RunKey)) { Remove-ItemProperty -LiteralPath ([TaskHubInstall]::RunKey) -Name ([TaskHubInstall]::RunName) -ErrorAction SilentlyContinue }
            if ($workerWasRunning -and ($changed -or $workerStopped)) { [TaskHubInstall]::Service([TaskHubInstall]::Worker, 'Running') }
            [ordered]@{ outcome='failed-rolled-back'; error=$failure.Exception.Message; backup=$backupDir; trayRestart='Launch PwshMcp.Tray.exe from the normal user session if it was running before this attempt.'; timestamp=[DateTime]::UtcNow.ToString('o') } | ConvertTo-Json -Depth 4 | Set-Content -LiteralPath (Join-Path $backupDir 'receipt.json') -Encoding UTF8
        } catch { Write-Warning ('Rollback incomplete: ' + $_.Exception.Message + '; backup: ' + $backupDir) }
    }
    throw $failure
}
