param([switch]$StartAtLogon,[string]$Source='artifacts\app')
$ErrorActionPreference='Stop'
if(-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run this installer explicitly as administrator.'}
$root=Split-Path -Parent $PSScriptRoot
$source=if([IO.Path]::IsPathRooted($Source)){$Source}else{Join-Path $root $Source}
$target=Join-Path $env:ProgramFiles 'PwshMCP'
$data=Join-Path $env:ProgramData 'PwshMCP'
if(-not (Test-Path -LiteralPath (Join-Path $source 'PwshMcp.Tray.exe'))){throw 'Build artifacts first.'}
foreach($path in @($target,$data)){
    if((Test-Path -LiteralPath $path) -and ((Get-Item -LiteralPath $path).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Installation target must not be a reparse point.'}
    New-Item -ItemType Directory -Path $path -Force | Out-Null
    & icacls.exe $path /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' '*S-1-5-32-545:(OI)(CI)RX' | Out-Null
    if($LASTEXITCODE -ne 0){throw 'ACL update failed'}
}
$service=Get-Service -Name PwshMcpBroker -ErrorAction SilentlyContinue
if($service){Stop-Service -Name PwshMcpBroker; $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(20))}
# Keep prior binaries for explicit rollback; do not remove a previous backup automatically.
if(Test-Path -LiteralPath (Join-Path $target 'PwshMcp.Host.exe')){
    $backup=Join-Path $target ('backup-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
    New-Item -ItemType Directory -Path $backup | Out-Null
    Get-ChildItem -LiteralPath $target -File | Copy-Item -Destination $backup
}
Copy-Item -Path (Join-Path $source '*') -Destination $target -Recurse -Force
$binary='"'+(Join-Path $target 'PwshMcp.Host.exe')+'" service'
if($service){& sc.exe config PwshMcpBroker binPath= $binary start= auto | Out-Null}else{& sc.exe create PwshMcpBroker binPath= $binary start= auto DisplayName= 'PwshMCP Broker' | Out-Null}
if($LASTEXITCODE -ne 0){throw 'Service configuration failed'}
Start-Service -Name PwshMcpBroker
if($StartAtLogon){
    $run='HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
    New-ItemProperty -Path $run -Name PwshMCP -PropertyType String -Value ('"'+(Join-Path $target 'PwshMcp.Tray.exe')+'"') -Force | Out-Null
}
Write-Output $target
