param([switch]$RemoveFiles)
$ErrorActionPreference='Stop'

class DesktopRemove {
    static [string]$TaskPrefix='PwshMCP-Admin-'
    static [string]$RunName='PwshMCP'
    static [string]$HostName='PwshMcp.Host.exe'
    static [string]$TrayName='PwshMcp.Tray.exe'
    static [string]$InstallName='PwshMCP'
    static [string]$DataName='PwshMCP'

    static [void] Check([string]$Path) {
        $item=Get-Item -LiteralPath $Path -Force
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Reparse point rejected: $Path"}
    }
}

if(-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run this uninstaller explicitly as administrator.'}
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$taskName=[DesktopRemove]::TaskPrefix+$sid
$target=[IO.Path]::GetFullPath((Join-Path $env:ProgramFiles ([DesktopRemove]::InstallName)))
$task=Get-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
if($task){
    Stop-ScheduledTask -TaskName $taskName -ErrorAction SilentlyContinue
    $proc=Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase) }
    foreach($p in $proc){Stop-Process -Id $p.ProcessId -Force}
    Unregister-ScheduledTask -TaskName $taskName -Confirm:$false
}
$run='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
Remove-ItemProperty -Path $run -Name ([DesktopRemove]::RunName) -ErrorAction SilentlyContinue
if($RemoveFiles -and (Test-Path -LiteralPath $target)){
    [DesktopRemove]::Check($target)
    $resolved=[IO.Path]::GetFullPath((Get-Item -LiteralPath $target).FullName)
    if($resolved -ne $target){throw 'Unexpected uninstall target.'}
    $proc=Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase) }
    foreach($p in $proc){Stop-Process -Id $p.ProcessId -Force}
    $contents=@(Get-ChildItem -LiteralPath $target -Force -Recurse)
    foreach($item in $contents){if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Install directory contains a reparse point: $($item.FullName)"}}
    Remove-Item -LiteralPath $target -Recurse -Force
}
