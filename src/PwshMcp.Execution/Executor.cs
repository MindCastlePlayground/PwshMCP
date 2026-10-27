using PwshMcp.Shared;
using System.Diagnostics;
using System.Security.Principal;
using System.Text;
using System.Text.Json;

namespace PwshMcp.Execution;

public sealed class Executor : IAsyncDisposable
{
    private const int OutputLimit = 1024 * 1024;
    private readonly Dictionary<string, ShellSession> sessions = new(StringComparer.OrdinalIgnoreCase);
    private readonly SemaphoreSlim gate = new(1, 1);

    public async Task<ExecutionResult> RunAsync(RunSpec spec, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(spec);
        if (string.IsNullOrWhiteSpace(spec.Cwd) || !Path.IsPathFullyQualified(spec.Cwd) || !Directory.Exists(spec.Cwd)) throw new ArgumentException("Cwd must be an existing absolute directory.", nameof(spec));
        if (spec.TimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(spec.TimeoutSeconds));
        var env = FreshEnvironment.Create(spec.Env);
        if (spec.Session is null) return await OneShot(spec, env, ct);
        ShellSession session;
        await gate.WaitAsync(ct);
        try
        {
            if (!sessions.TryGetValue(spec.Session, out var current) || !current.IsAlive)
            {
                if (current is not null) await current.Stop();
                sessions[spec.Session] = current = Start(spec.Profile, env, spec.Cwd);
            }
            session=current;
        }
        finally { gate.Release(); }
        await session.Lock.WaitAsync(ct);
        try { return await Execute(session, spec, env, ct); }
        finally { session.Lock.Release(); }
    }

    public async Task ResetAsync(string session)
    {
        await gate.WaitAsync();
        try { if (sessions.Remove(session, out var old)) { await old.Lock.WaitAsync(); try {await old.Stop();} finally {old.Lock.Release();} } }
        finally { gate.Release(); }
    }

    public Task CloseAsync(string session) => ResetAsync(session);

    public async ValueTask DisposeAsync()
    {
        await gate.WaitAsync();
        try { foreach (var item in sessions.Values) await item.Stop(); sessions.Clear(); }
        finally { gate.Release(); gate.Dispose(); }
    }

    private static async Task<ExecutionResult> OneShot(RunSpec spec, Dictionary<string, string> env, CancellationToken ct)
    {
        await using var session = Start(spec.Profile, env, spec.Cwd);
        return await Execute(session, spec, env, ct);
    }

    private static async Task<ExecutionResult> Execute(ShellSession session, RunSpec spec, Dictionary<string, string> env, CancellationToken ct)
    {
        var started = DateTimeOffset.UtcNow;
        var id = Guid.NewGuid().ToString("N");
        var req = JsonSerializer.Serialize(new { code = spec.Code, cwd = spec.Cwd, id, keepLocation=spec.Session is not null });
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(spec.TimeoutSeconds));
        try
        {
            await session.Input.WriteLineAsync(req.AsMemory(), timeout.Token);
            var output = await session.ReadResult(id, timeout.Token);
            var text = Bound(output.Output);
            var error = Bound(output.Error, OutputLimit - Encoding.UTF8.GetByteCount(text));
            return new ExecutionResult { ExitCode = output.ExitCode, Output = text, Error = error, Engine = session.Engine, Identity = Identity(), EnvironmentAt = session.EnvironmentAt, EnvironmentId = session.EnvironmentId };
        }
        catch (OperationCanceledException)
        {
            await session.Stop();
            return new ExecutionResult { ExitCode = -1, Cancelled = ct.IsCancellationRequested, Error = ct.IsCancellationRequested ? "Cancelled." : "Timed out.", Engine = session.Engine, Identity = Identity(), EnvironmentAt = session.EnvironmentAt, EnvironmentId = session.EnvironmentId };
        }
        catch
        {
            await session.Stop();
            throw;
        }
    }

    private static ShellSession Start(bool profile, Dictionary<string, string> env, string cwd)
    {
        var engine = FindEngine();
        var start = new ProcessStartInfo(engine) { UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true, WorkingDirectory = cwd };
        start.ArgumentList.Add(ShellText.NoProfile); // Isolation always disables profiles by default; profile is honored only by explicit opt-in.
        if (profile) { start.ArgumentList.RemoveAt(0); }
        start.ArgumentList.Add(ShellText.NonInteractive);
        start.ArgumentList.Add(ShellText.Command);
        start.ArgumentList.Add(Wrapper);
        start.Environment.Clear();
        foreach (var pair in env) start.Environment[pair.Key] = pair.Value;
        var proc = new Process { StartInfo = start, EnableRaisingEvents = true };
        if (!proc.Start()) throw new InvalidOperationException("PowerShell process did not start.");
        return new ShellSession(proc, engine);
    }

    private static string FindEngine()
    {
        var candidates = new List<string>();
        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var sysRoot = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        candidates.Add(Path.Combine(programFiles, "PowerShell", "7", "pwsh.exe"));
        candidates.Add(Path.Combine(sysRoot, "System32", "WindowsPowerShell", "v1.0", "powershell.exe"));
        foreach (var path in candidates) if (File.Exists(path)) return path;
        throw new FileNotFoundException("No trusted system PowerShell engine was found.");
    }

    private static string Identity() => WindowsIdentity.GetCurrent().Name;
    private static string Bound(string value, int limit = OutputLimit)
    {
        if(limit<=0)return "";
        if(Encoding.UTF8.GetByteCount(value)<=limit)return value;
        Encoding.UTF8.GetEncoder().Convert(value.AsSpan(),new byte[limit],true,out var chars,out _,out _);
        return value[..chars];
    }

    private const string Wrapper = """
        [Console]::OutputEncoding=[Text.UTF8Encoding]::new($false)
        $OutputEncoding=[Text.UTF8Encoding]::new($false)
        $ErrorActionPreference='Continue'; $ProgressPreference='SilentlyContinue'
        while($null -ne ($__pmLine=[Console]::In.ReadLine())) {
          $__pmReq=ConvertFrom-Json -InputObject $__pmLine
          $__pmOut=[Text.StringBuilder]::new(); $__pmErr=[Text.StringBuilder]::new()
          $global:LASTEXITCODE=$null; $__pmExit=0; $__pmErrors=@()
          try {
            if(!$__pmReq.keepLocation) { Set-Location -LiteralPath $__pmReq.cwd -ErrorAction Stop }
            Invoke-Expression $__pmReq.code -ErrorVariable +__pmErrors *>&1 | ForEach-Object {
              $__pmText=$_.ToString()+[Environment]::NewLine
              if($_ -is [Management.Automation.ErrorRecord]) { $__pmTarget=$__pmErr; $__pmExit=1 } else { $__pmTarget=$__pmOut }
              $__pmRoom=1048576-$__pmOut.Length-$__pmErr.Length
              if($__pmRoom -gt 0) { [void]$__pmTarget.Append($__pmText.Substring(0,[Math]::Min($__pmRoom,$__pmText.Length))) }
            }
            if($__pmErrors.Count -gt 0) {
              $__pmExit=1
              if($__pmErr.Length -eq 0) { foreach($__pmFailure in $__pmErrors) {
                $__pmRoom=1048576-$__pmOut.Length-$__pmErr.Length
                if($__pmRoom -gt 0) { $__pmText=$__pmFailure.ToString()+[Environment]::NewLine; [void]$__pmErr.Append($__pmText.Substring(0,[Math]::Min($__pmRoom,$__pmText.Length))) }
              } }
            }
            if($null -ne $global:LASTEXITCODE -and $global:LASTEXITCODE -ne 0) { $__pmExit=[int]$global:LASTEXITCODE }
          } catch { $__pmExit=1; [void]$__pmErr.Append($_.ToString().Substring(0,[Math]::Min(65536,$_.ToString().Length))) }
          $__pmResult=[ordered]@{id=$__pmReq.id;output=$__pmOut.ToString();error=$__pmErr.ToString();exitCode=$__pmExit}
          [Console]::Out.WriteLine('__PWMC_RESULT__'+(ConvertTo-Json -InputObject $__pmResult -Compress -Depth 8))
        }
        """;

    private sealed class ShellSession : IAsyncDisposable
    {
        private readonly Process process;
        private readonly ProcessJob job;
        private readonly Task stderr;
        internal readonly SemaphoreSlim Lock = new(1, 1);
        internal readonly StreamWriter Input;
        internal readonly string Engine;
        internal readonly DateTimeOffset EnvironmentAt=DateTimeOffset.UtcNow;
        internal readonly string EnvironmentId=Guid.NewGuid().ToString("N");
        private readonly StringBuilder errors=new();
        private bool stopped;
        private readonly char[] buffer=new char[8192];
        private int offset,count;
        internal ShellSession(Process process, string engine) { this.process = process; job=new ProcessJob(process); Engine = engine; Input = process.StandardInput; stderr = Drain(process.StandardError); }
        internal bool IsAlive => !stopped && !process.HasExited;
        private async Task Drain(StreamReader reader) { try { var buf=new char[4096]; int count; while((count=await reader.ReadAsync(buf))>0) {lock(errors){var room=OutputLimit-errors.Length;if(room>0)errors.Append(buf,0,Math.Min(room,count));}} } catch(IOException) { } }
        internal async Task<ShellOutput> ReadResult(string id, CancellationToken ct)
        {
            var raw=new StringBuilder();
            while (true)
            {
                var line = await ReadLine(process.StandardOutput,ct);
                if(line is null) {await process.WaitForExitAsync(ct);await stderr;string err;lock(errors){err=errors.ToString();errors.Clear();}return new ShellOutput(process.ExitCode,Bound(raw.ToString()),Bound(err));}
                var marker=line.IndexOf(ShellText.Success,StringComparison.Ordinal);
                if (marker<0) {var room=OutputLimit-raw.Length;if(room>0)raw.AppendLine(line[..Math.Min(line.Length,room)]);continue;}
                if(marker>0){var room=OutputLimit-raw.Length;if(room>0)raw.Append(line.AsSpan(0,Math.Min(marker,room)));}
                using var doc = JsonDocument.Parse(line[(marker+ShellText.Success.Length)..]);
                var root = doc.RootElement;
                if (root.GetProperty(ShellText.JsonId).GetString() != id) continue;
                string extra;lock(errors){extra=errors.ToString();errors.Clear();}
                return new ShellOutput(root.GetProperty(ShellText.JsonExitCode).GetInt32(), Bound(raw+(root.GetProperty(ShellText.JsonOutput).GetString() ?? "")), Bound(extra+(root.GetProperty(ShellText.JsonError).GetString() ?? "")));
            }
        }
        private async Task<string?> ReadLine(StreamReader reader,CancellationToken ct)
        {
            var text=new StringBuilder();bool any=false;
            while(true)
            {
                if(offset==count){count=await reader.ReadAsync(buffer,ct);offset=0;if(count==0)return any?text.ToString():null;}
                any=true;var end=Array.IndexOf(buffer,'\n',offset,count-offset);var length=(end<0?count:end)-offset;
                var room=OutputLimit*8-text.Length;if(room>0)text.Append(buffer,offset,Math.Min(room,length));
                offset+=length;
                if(end>=0){offset++;return text.ToString().TrimEnd('\r');}
            }
        }
        internal async Task Stop() { if(stopped)return;stopped=true;job.Dispose();try { if (!process.HasExited) process.Kill(true); } catch(InvalidOperationException) { } try { await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(3)); } catch(TimeoutException) { } await stderr;process.Dispose(); }
        public async ValueTask DisposeAsync() { await Stop(); Lock.Dispose(); }
    }

    private sealed record ShellOutput(int ExitCode, string Output, string Error);
}
