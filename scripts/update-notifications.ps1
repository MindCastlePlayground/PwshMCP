$ErrorActionPreference='Stop'
$target='C:\Program Files\PwshMCP'
$daemon='TaskHub Daemon pwshmcp'
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$admin='PwshMCP-Admin-'+$sid
$receipt='C:\ProgramData\PwshMCP-Desktop\notification-update.json'
$source=Join-Path $PSScriptRoot 'package'
$backup=Join-Path $PSScriptRoot 'previous'
$hostPath=Join-Path $target 'taskhub-daemon-host.exe'
$changed=@()
$stopped=$false
try {
    $hostHash=(Get-FileHash -LiteralPath $hostPath).Hash
    $action=(Get-ScheduledTask -TaskName $daemon).Actions[0]
    if($action.Execute -ne $hostPath){throw 'Unexpected daemon action; no changes made.'}
    $files=Get-ChildItem -LiteralPath $source -File
    foreach($file in $files){if($file.Name -notmatch '^PwshMcp\.[A-Za-z.]+$'){throw 'Unexpected package filename'}}
    New-Item -ItemType Directory -Path $backup | Out-Null
    Start-Sleep -Seconds 3
    $stopped=$true
    Stop-ScheduledTask -TaskName $daemon
    Stop-ScheduledTask -TaskName $admin
    # The launcher task can exit while its coordinator child remains alive.
    Get-CimInstance Win32_Process -Filter "Name='PwshMcp.Host.exe'" | Where-Object {
        $_.ExecutablePath -eq (Join-Path $target 'PwshMcp.Host.exe') -and $_.CommandLine -match '\sadmin\s*$'
    } | ForEach-Object { Stop-Process -Id $_.ProcessId -Force }
    Start-Sleep -Seconds 3
    foreach($file in $files){
        $dest=Join-Path $target $file.Name
        $old=Join-Path $backup $file.Name
        $exists=Test-Path -LiteralPath $dest
        if($exists){Move-Item -LiteralPath $dest -Destination $old}
        $changed+=@{Name=$file.Name;Existed=$exists}
        Copy-Item -LiteralPath $file.FullName -Destination $dest
        if((Get-FileHash -LiteralPath $dest).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash){throw 'Installed hash mismatch'}
    }
    if((Get-FileHash -LiteralPath $hostPath).Hash -ne $hostHash){throw 'Daemon host changed'}
    Start-ScheduledTask -TaskName $admin
    Start-ScheduledTask -TaskName $daemon
    [ordered]@{outcome='installed';at=[DateTime]::UtcNow.ToString('o');backup=$backup;files=$changed.Name;hostHash=$hostHash;trayHash=(Get-FileHash (Join-Path $target 'PwshMcp.Tray.dll')).Hash} | ConvertTo-Json | Set-Content -LiteralPath $receipt -Encoding UTF8
} catch {
    $failure=$_.Exception.Message
    $rollback=@()
    foreach($file in $changed){
        try {
            $dest=Join-Path $target $file.Name
            if(Test-Path -LiteralPath $dest){Remove-Item -LiteralPath $dest -Force}
            if($file.Existed){Move-Item -LiteralPath (Join-Path $backup $file.Name) -Destination $dest}
        } catch {$rollback+=$_.Exception.Message}
    }
    if($stopped){Start-ScheduledTask -TaskName $admin -ErrorAction SilentlyContinue; Start-ScheduledTask -TaskName $daemon -ErrorAction SilentlyContinue}
    [ordered]@{outcome='failed';error=$failure;rollbackErrors=$rollback;at=[DateTime]::UtcNow.ToString('o')} | ConvertTo-Json | Set-Content -LiteralPath $receipt -Encoding UTF8
    throw
} finally {
    Unregister-ScheduledTask -TaskName 'PwshMCP-Notification-Update' -Confirm:$false -ErrorAction SilentlyContinue
}
