param(
    [Parameter(Mandatory=$true)][string]$UserSid,
    [string]$Source='artifacts\package'
)
$ErrorActionPreference='Stop'

class DesktopInstall {
    static [string]$TaskPrefix='PwshMCP-Admin-'
    static [string]$RunName='PwshMCP'
    static [string]$ServiceName='PwshMcpBroker'
    static [string]$HostName='PwshMcp.Host.exe'
    static [string]$AdminName='PwshMcp.Admin.exe'
    static [string]$TrayName='PwshMcp.Tray.exe'
    static [string]$InstallName='PwshMCP'
    static [string]$DataName='PwshMCP'

    static [void] Check([string]$Path) {
        $item=Get-Item -LiteralPath $Path -Force
        if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Reparse point rejected: $Path"}
    }
    static [void] Acl([string]$Path) {
        $admin=[Security.Principal.SecurityIdentifier]::new('S-1-5-32-544')
        $acl=[Security.AccessControl.DirectorySecurity]::new()
        $acl.SetAccessRuleProtection($true,$false)
        $acl.SetOwner($admin)
        foreach($entry in @(@('S-1-5-18','FullControl'),@('S-1-5-32-544','FullControl'),@('S-1-5-32-545','ReadAndExecute'))){
            $acl.AddAccessRule([Security.AccessControl.FileSystemAccessRule]::new([Security.Principal.SecurityIdentifier]::new($entry[0]),[Security.AccessControl.FileSystemRights]$entry[1],([Security.AccessControl.InheritanceFlags]'ContainerInherit,ObjectInherit'),[Security.AccessControl.PropagationFlags]::None,[Security.AccessControl.AccessControlType]::Allow))
        }
        Set-Acl -LiteralPath $Path -AclObject $acl
        & icacls.exe $Path /setowner '*S-1-5-32-544' /T /C | Out-Null
        if($LASTEXITCODE -ne 0){throw "Owner update failed: $Path"}
    }
    static [void] Receipt([string]$Path,[string]$Status,[string]$Detail,[string]$Sid,[string]$Task,[string]$Target) {
        [ordered]@{version='0.4.0';outcome=$Status;detail=$Detail;userSid=$Sid;taskName=$Task;installPath=$Target;updatedAt=[DateTime]::UtcNow.ToString('o')} |
            ConvertTo-Json -Depth 4 | Set-Content -LiteralPath $Path -Encoding UTF8
    }
}

$root=Split-Path -Parent $PSScriptRoot
$sid=[Security.Principal.WindowsIdentity]::GetCurrent().User.Value
$receipt=$null
try {
    if(-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run this installer explicitly as administrator.'}
    if($UserSid -notmatch '^S-1-5-21-(?:\d+-){3}\d+$' -or $UserSid -ne $sid){throw 'UserSid must exactly match the elevated caller SID; standard-user substitution is not supported.'}
    $sourcePath=[IO.Path]::GetFullPath($(if([IO.Path]::IsPathRooted($Source)){$Source}else{Join-Path $root $Source}))
    if(-not (Test-Path -LiteralPath $sourcePath -PathType Container)){throw 'Source directory does not exist.'}
    [DesktopInstall]::Check($sourcePath)
    $files=@(Get-ChildItem -LiteralPath $sourcePath -Force -Recurse)
    foreach($item in $files){if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Source contains a reparse point: $($item.FullName)"}}
    foreach($name in @([DesktopInstall]::HostName,[DesktopInstall]::TrayName,[DesktopInstall]::AdminName)) {if(-not (Test-Path -LiteralPath (Join-Path $sourcePath $name) -PathType Leaf)){throw "Missing required source file: $name"}}
    $target=Join-Path $env:ProgramFiles ([DesktopInstall]::InstallName)
    $data=Join-Path $env:ProgramData 'PwshMCP-Desktop'
    $receipt=Join-Path $data 'desktop-installer-result.json'
    $taskName=[DesktopInstall]::TaskPrefix+$sid
    $svc=Get-Service -Name ([DesktopInstall]::ServiceName) -ErrorAction SilentlyContinue
    if($svc){throw 'PwshMcpBroker service exists; desktop task and service installations cannot coexist.'}
    if(Test-Path -LiteralPath $target){
        [DesktopInstall]::Check($target)
        foreach($item in @(Get-ChildItem -LiteralPath $target -Force -Recurse)){if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Install directory contains a reparse point: $($item.FullName)"}}
    }
    $procs=Get-CimInstance Win32_Process | Where-Object { $_.ExecutablePath -and $_.ExecutablePath.StartsWith($target+'\',[StringComparison]::OrdinalIgnoreCase) }
    if($procs){throw 'Close PwshMCP processes running from the install directory, then retry.'}
    if(Test-Path -LiteralPath $data){[DesktopInstall]::Check($data)}
    New-Item -ItemType Directory -Path $data -Force | Out-Null
    [DesktopInstall]::Acl($data)
    if(Test-Path -LiteralPath $target){
        $backup=Join-Path $data ('backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss-fff'))
        New-Item -ItemType Directory -Path $backup | Out-Null
        [DesktopInstall]::Acl($backup)
        Get-ChildItem -LiteralPath $target -Force | Copy-Item -Destination $backup -Recurse -Force
    } else { New-Item -ItemType Directory -Path $target | Out-Null }
    [DesktopInstall]::Acl($target)
    Copy-Item -Path (Join-Path $sourcePath '*') -Destination $target -Recurse -Force
    & icacls.exe (Join-Path $target '*') /reset /T /C | Out-Null
    if($LASTEXITCODE -ne 0){throw 'Installed file ACL reset failed'}
    [DesktopInstall]::Acl($target)
    $installed=@(Get-ChildItem -LiteralPath $target -Force -Recurse)
    foreach($item in $installed){if($item.Attributes -band [IO.FileAttributes]::ReparsePoint){throw "Installed path contains a reparse point: $($item.FullName)"}}
    $hostPath=Join-Path $target ([DesktopInstall]::HostName)
    $trayPath=Join-Path $target ([DesktopInstall]::TrayName)
    $action=New-ScheduledTaskAction -Execute (Join-Path $target ([DesktopInstall]::AdminName)) -WorkingDirectory $target
    $trigger=New-ScheduledTaskTrigger -AtLogOn -User $sid
    $principal=New-ScheduledTaskPrincipal -UserId $sid -LogonType Interactive -RunLevel Highest
    $settings=New-ScheduledTaskSettingsSet -ExecutionTimeLimit ([TimeSpan]::Zero) -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -RestartCount 3 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew
    Register-ScheduledTask -TaskName $taskName -Action $action -Trigger $trigger -Principal $principal -Settings $settings -Force | Out-Null
    New-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name ([DesktopInstall]::RunName) -PropertyType String -Value ('"'+$trayPath+'"') -Force | Out-Null
    Start-ScheduledTask -TaskName $taskName
    [DesktopInstall]::Receipt($receipt,'installed','Task start requested; verify coordinator readiness separately.',$sid,$taskName,$target)
    Write-Output $receipt
} catch {
    if($receipt){try{[DesktopInstall]::Receipt($receipt,'failed',$_.Exception.Message,$sid,$taskName,$target)}catch{}}
    throw
}
