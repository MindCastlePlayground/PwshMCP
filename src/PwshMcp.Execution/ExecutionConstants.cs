namespace PwshMcp.Execution;

internal static class ShellText
{
    internal const string NoProfile = "-NoProfile";
    internal const string NonInteractive = "-NonInteractive";
    internal const string Command = "-Command";
    internal const string JsonInput = "ConvertFrom-Json -InputObject ([Console]::In.ReadToEnd())";
    internal const string Success = "__PWMC_RESULT__";
    internal const string Init = "__PWMC_INIT__";
    internal const string Marker = "__PWMC_MARKER__";
    internal const string ScriptVar = "__pwmc_script";
    internal const string ResultVar = "__pwmc_result";
    internal const string InputVar = "__pwmc_input";
    internal const string CwdVar = "__pwmc_cwd";
    internal const string JsonDepth = "-Depth";
    internal const string JsonId = "id";
    internal const string JsonCode = "code";
    internal const string JsonCwd = "cwd";
    internal const string JsonOutput = "output";
    internal const string JsonError = "error";
    internal const string JsonExitCode = "exitCode";
    internal const string Wrapper = "$ErrorActionPreference='Continue'; $ProgressPreference='SilentlyContinue'; while($null -ne ($l=[Console]::In.ReadLine())) { try { $r=ConvertFrom-Json -InputObject $l; Set-Location -LiteralPath $r.cwd; $e=@(); $o=@(Invoke-Expression $r.code -ErrorVariable +e); $ok=$?; $last=$global:LASTEXITCODE; $errs=@($e | ForEach-Object { $_.ToString() }); $outs=@($o | ForEach-Object { $_.ToString() }); $x=if($null -ne $last) { [int]$last } elseif($ok) { 0 } else { 1 }; $global:LASTEXITCODE=$null; $z=[ordered]@{ id=$r.id; output=($outs -join [Environment]::NewLine); error=($errs -join [Environment]::NewLine); exitCode=$x }; [Console]::Out.WriteLine('__PWMC_RESULT__'+(ConvertTo-Json -InputObject $z -Compress -Depth 64)) } catch { $z=[ordered]@{id=$r.id;output='';error=$_.ToString();exitCode=1}; [Console]::Out.WriteLine('__PWMC_RESULT__'+(ConvertTo-Json -InputObject $z -Compress -Depth 64)) } }";
}

internal static class EnvNames
{
    internal const string Path = "Path";
    internal const string PathExt = "PATHEXT";
    internal const string SystemRoot = "SystemRoot";
    internal const string UserProfile = "USERPROFILE";
    internal const string UserName = "USERNAME";
    internal const string Temp = "TEMP";
    internal const string Tmp = "TMP";
    internal const string UserEnvKey = "Environment";
    internal const string MachineEnvKey = @"SYSTEM\CurrentControlSet\Control\Session Manager\Environment";
    internal static readonly string[] GeneratedNames = { UserProfile, UserName, Temp };
}

internal static class NativeNames
{
    internal const string Kernel32 = "kernel32.dll";
    internal const string Advapi32 = "advapi32.dll";
    internal const string Userenv = "userenv.dll";
    internal const uint UnicodeEnvironment = 0x00000400;
    internal const uint CreateNoWindow = 0x08000000;
    internal const uint TokenQuery = 0x0008;
}
