param([switch]$RemoveFiles)
$ErrorActionPreference='Stop'
if(-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)){throw 'Run this uninstaller explicitly as administrator.'}
$service=Get-Service -Name PwshMcpBroker -ErrorAction SilentlyContinue
if($service){Stop-Service -Name PwshMcpBroker; $service.WaitForStatus('Stopped',[TimeSpan]::FromSeconds(20)); & sc.exe delete PwshMcpBroker | Out-Null; if($LASTEXITCODE -ne 0){throw 'Service removal failed'}}
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name PwshMCP -ErrorAction SilentlyContinue
if($RemoveFiles){
    $expected=[IO.Path]::GetFullPath((Join-Path $env:ProgramFiles 'PwshMCP'))
    if(Test-Path -LiteralPath $expected){
        $resolved=(Get-Item -LiteralPath $expected).FullName
        if($resolved -ne $expected -or ((Get-Item -LiteralPath $expected).Attributes -band [IO.FileAttributes]::ReparsePoint)){throw 'Unexpected uninstall target'}
        Remove-Item -LiteralPath $resolved -Recurse -Force
    }
}
# Retain ProgramData metadata. System effects of previously executed scripts are not undone.
