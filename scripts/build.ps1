param([ValidateSet('Debug','Release')][string]$Configuration='Release',[string]$Output='artifacts\app')
$ErrorActionPreference='Stop'
$root=Split-Path -Parent $PSScriptRoot
$out=if([IO.Path]::IsPathRooted($Output)){$Output}else{Join-Path $root $Output}
dotnet publish (Join-Path $root 'src\PwshMcp.Host\PwshMcp.Host.csproj') -c $Configuration -o $out --nologo -v quiet
if($LASTEXITCODE -ne 0){throw 'Host publish failed'}
dotnet publish (Join-Path $root 'src\PwshMcp.Admin\PwshMcp.Admin.csproj') -c $Configuration -o $out --nologo -v quiet
if($LASTEXITCODE -ne 0){throw 'Admin launcher publish failed'}
dotnet publish (Join-Path $root 'src\PwshMcp.Tray\PwshMcp.Tray.csproj') -c $Configuration -o $out --nologo -v quiet
if($LASTEXITCODE -ne 0){throw 'Tray publish failed'}
Get-ChildItem -LiteralPath $out -File | ForEach-Object { [pscustomobject]@{Name=$_.Name;SHA256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash} } | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $root 'artifacts\manifest.json') -Encoding utf8
Write-Output $out
