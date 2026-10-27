using Microsoft.Win32;
using PwshMcp.Execution;
using PwshMcp.Shared;

internal static class Program
{
    private const string EnvKey = "PWMC_EXEC_TEST_VALUE";
    private static async Task<int> Main()
    {
        var old = Environment.GetEnvironmentVariable(EnvKey, EnvironmentVariableTarget.User);
        try
        {
            Environment.SetEnvironmentVariable(EnvKey, "persisted-one", EnvironmentVariableTarget.User);
            Check(FreshEnvironment.Create().GetValueOrDefault(EnvKey) == "persisted-one", "fresh user environment update");
            Environment.SetEnvironmentVariable(EnvKey, null, EnvironmentVariableTarget.User);
            Check(!FreshEnvironment.Create().ContainsKey(EnvKey), "fresh user environment deletion");
            Check(FreshEnvironment.Create(new() { ["PATH"] = "patched", ["REMOVE_ME"] = null }).GetValueOrDefault("Path") == "patched", "case insensitive environment overrides");
            var cwd = Path.GetTempPath();
            await using var executor = new Executor();
            var one = await executor.RunAsync(new RunSpec { Code = "'hello'; Write-Error 'problem'", Cwd = cwd, Privilege = Privilege.Normal });
            Check(one.ExitCode != 0 && one.Output.Contains("hello") && one.Error.Contains("problem"), $"stdout and stderr with failure status: exit={one.ExitCode}, out={one.Output}, error={one.Error}");
            var code = await executor.RunAsync(new RunSpec { Code = "& $env:SystemRoot\\System32\\cmd.exe /c exit 7", Cwd = cwd, Privilege = Privilege.Normal });
            Check(code.ExitCode == 7, "native exit status");
            var large = await executor.RunAsync(new RunSpec { Code = "'x' * (1024 * 1024 + 128)", Cwd = cwd, Privilege = Privilege.Normal });
            Check(System.Text.Encoding.UTF8.GetByteCount(large.Output) <= 1024 * 1024, "bounded output");
            var isolated = await executor.RunAsync(new RunSpec { Code = "$global:mark='set'; 'one'", Cwd = cwd, Privilege = Privilege.Normal });
            var clean = await executor.RunAsync(new RunSpec { Code = "if($global:mark){'dirty'}else{'clean'}", Cwd = cwd, Privilege = Privilege.Normal });
            Check(isolated.Output.Contains("one") && clean.Output.Contains("clean"), "fresh process isolation");
            var session = await executor.RunAsync(new RunSpec { Code = "$global:mark='kept'; function hi {'ok'}; 'ready'", Cwd = cwd, Session = "test", Privilege = Privilege.Normal });
            var kept = await executor.RunAsync(new RunSpec { Code = "hi; $global:mark", Cwd = cwd, Session = "test", Privilege = Privilege.Normal });
            Check(kept.Output.Contains("ok") && kept.Output.Contains("kept"), "persistent state");
            await executor.ResetAsync("test");
            var reset = await executor.RunAsync(new RunSpec { Code = "if($global:mark){'dirty'}else{'reset'}", Cwd = cwd, Session = "test", Privilege = Privilege.Normal });
            Check(reset.Output.Contains("reset"), "session reset");
            using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
            var stopped = await executor.RunAsync(new RunSpec { Code = "Start-Sleep -Seconds 10", Cwd = cwd, TimeoutSeconds = 30, Privilege = Privilege.Normal }, cancel.Token);
            Check(stopped.Cancelled, "cancellation");
            var raw = await executor.RunAsync(new RunSpec { Code = "[Console]::WriteLine('direct'); Write-Host 'host'; exit 7", Cwd = cwd });
            Check(raw.ExitCode==7 && raw.Output.Contains("direct"),"explicit exit and direct console output");
            var unicode = await executor.RunAsync(new RunSpec { Code = "'中' * (1024 * 1024)", Cwd = cwd });
            Check(System.Text.Encoding.UTF8.GetByteCount(unicode.Output)<=1024*1024,"unicode output byte bound");
            var location = await executor.RunAsync(new RunSpec { Code = "Set-Location -LiteralPath $env:SystemRoot; (Get-Location).Path", Cwd = cwd, Session="location" });
            var locationKept = await executor.RunAsync(new RunSpec { Code = "(Get-Location).Path", Cwd = cwd, Session="location" });
            Check(location.Output==locationKept.Output && location.EnvironmentId==locationKept.EnvironmentId,"persistent location and environment snapshot identity");
            using var againCancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            await executor.RunAsync(new RunSpec { Code="Start-Sleep 10",Cwd=cwd,Session="recover"},againCancel.Token);
            var recovered=await executor.RunAsync(new RunSpec{Code="'recovered'",Cwd=cwd,Session="recover"});
            Check(recovered.Output.Contains("recovered"),"persistent session recovery after cancellation");
            var noLine=await executor.RunAsync(new RunSpec{Code="[Console]::Write('no-newline')",Cwd=cwd,TimeoutSeconds=5});
            Check(noLine.ExitCode==0 && noLine.Output.Contains("no-newline"),"direct console output without newline");
            Console.WriteLine("Execution harness passed: 15 checks.");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine($"FAILED: {ex.Message}"); return 1; }
        finally { Environment.SetEnvironmentVariable(EnvKey, old, EnvironmentVariableTarget.User); }
    }
    private static void Check(bool ok, string name) { if (!ok) throw new InvalidOperationException(name); }
}
