using System.Diagnostics;
using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using PwshMcp.Shared;

namespace PwshMcp.Host;

/// <summary>Login-scoped elevated coordinator. No script execution API; only the installed tray can approve.</summary>
internal sealed class AdminHost
{
    readonly BrokerClient broker = new();
    readonly Dictionary<string, Process> workers = [];
    readonly SemaphoreSlim gate = new(1);
    readonly string sid = WindowsIdentity.GetCurrent().User!.Value;
    readonly int session = Process.GetCurrentProcess().SessionId;

    public async Task RunAsync(CancellationToken stop)
    {
        CheckInstall();
        if (!new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)) throw new UnauthorizedAccessException(Errors.Elevation);
        using var single = new Mutex(false, @"Local\" + AdminNames.Pipe);
        if (!single.WaitOne(0)) return;
        using var life = CancellationTokenSource.CreateLinkedTokenSource(stop);
        var stamp = SessionLife.Stamp(session);
        if (stamp == 0) throw new UnauthorizedAccessException(AdminNames.Forbidden);
        var watch = Watch(stamp, life);
        try
        {
            while (!life.IsCancellationRequested)
            {
                try
                {
                    using var pipe = Open();
                    await pipe.WaitForConnectionAsync(life.Token);
                    await Serve(pipe, life.Token);
                }
                // Pipe instance exhaustion is temporary; retry without terminating the coordinator.
                catch (IOException e) when (PipeFault.IsBusy(e)) { await Task.Delay(100, life.Token); }
            }
        }
        finally
        {
            life.Cancel();
            foreach (var p in workers.Values)
            {
                try { if (!p.HasExited) p.Kill(entireProcessTree: true); } catch (InvalidOperationException) { }
                p.Dispose();
            }
            try { await watch; } catch (OperationCanceledException) { }
        }
    }

    async Task Watch(long stamp, CancellationTokenSource life)
    {
        while (!life.IsCancellationRequested)
        {
            await Task.Delay(1000, life.Token);
            if (SessionLife.Stamp(session) != stamp) { RuntimeLog.Write(AdminNames.Mode, RuntimeLog.Session); life.Cancel(); }
        }
    }

    NamedPipeServerStream Open()
    {
        var acl = new PipeSecurity(); acl.SetAccessRuleProtection(true, false);
        acl.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.NetworkSid, null), PipeAccessRights.FullControl, AccessControlType.Deny));
        acl.AddAccessRule(new(new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null), PipeAccessRights.FullControl, AccessControlType.Allow));
        acl.AddAccessRule(new(new SecurityIdentifier(sid), PipeAccessRights.ReadWrite | PipeAccessRights.Synchronize, AccessControlType.Allow));
        // High integrity process creates pipe; explicitly permit medium integrity clients to connect.
        var pipe = NamedPipeServerStreamAcl.Create(AdminNames.Pipe, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous, 4096, 4096, acl, HandleInheritability.None, PipeAccessRights.TakeOwnership);
        try { PipeLabel.Apply(pipe); return pipe; }
        catch { pipe.Dispose(); throw; }
    }

    async Task Serve(NamedPipeServerStream pipe, CancellationToken stop)
    {
        try { await Exchange(pipe, stop); }
        // Includes stream disposal and a client exiting before its PID/token can be read.
        catch (Exception e) when (e is not OutOfMemoryException) { RuntimeLog.Write(AdminNames.Mode, RuntimeLog.Client, e); }
    }

    async Task Exchange(NamedPipeServerStream pipe, CancellationToken stop)
    {
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(stop); limit.CancelAfter(TimeSpan.FromSeconds(8));
        using var reader = new StreamReader(pipe, Encoding.UTF8, leaveOpen: true);
        using var writer = new StreamWriter(pipe, new UTF8Encoding(false), leaveOpen: true) { AutoFlush = true };
        try
        {
            var text = new StringBuilder(); var c = new char[1];
            while (text.Length <= AdminNames.MaxMessage)
            {
                if (await reader.ReadAsync(c, limit.Token) == 0) return;
                if (c[0] == '\n') break;
                text.Append(c[0]);
            }
            if (text.Length > AdminNames.MaxMessage) return;
            var request = JsonSerializer.Deserialize<AdminRequest>(text.ToString(), Protocol.Json) ?? throw new InvalidDataException();
            var peer = Peer.Read(pipe);
            var reply = peer.Sid != sid || peer.Session != session ? Fail(AdminNames.Forbidden) : await Dispatch(request, peer, limit.Token);
            await writer.WriteLineAsync(JsonSerializer.Serialize(reply, Protocol.Json).AsMemory(), limit.Token);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or UnauthorizedAccessException or JsonException or InvalidDataException) { }
    }

    async Task<AdminResponse> Dispatch(AdminRequest r, Peer peer, CancellationToken ct)
    {
        if (r.Op == AdminOp.Status) return new() { Ready = true };
        if (r.Op is not (AdminOp.Approve or AdminOp.Resume) || string.IsNullOrEmpty(r.Id) || r.Id.Length > 200) return Fail(AdminNames.Invalid);
        if (!ProcessPath.Is(peer.Pid, Path.Combine(AdminNames.Directory, AdminNames.Tray))) return Fail(AdminNames.Forbidden);
        await gate.WaitAsync(ct);
        try
        {
            var snap = await broker.SendAsync(new() { Op = Ops.Snapshot }, ct);
            var grant = snap.Snapshot?.Grants.FirstOrDefault(g => g.Id == r.Id && g.Sid == sid && g.SessionId == session);
            if (grant is null) return Fail(Errors.Missing);
            if (r.Op == AdminOp.Approve)
            {
                var reply = await broker.SendAsync(new() { Op = Ops.Approve, Id = r.Id, ExpiresAt = r.ExpiresAt }, ct);
                if (!reply.Ok) return Fail(reply.Error);
                grant = reply.Grant!;
            }
            else if (grant.State != AccessState.Active || grant.ExpiresAt <= DateTimeOffset.UtcNow) return Fail(Errors.Expired);
            if (workers.TryGetValue(r.Id, out var old))
            {
                if (!old.HasExited) return new() { Ready = true, Grant = grant };
                old.Dispose(); workers.Remove(r.Id);
            }
            // A legacy worker can already own this grant. Never start a competing worker.
            if (grant.HeartbeatAt is { } heartbeat && DateTimeOffset.UtcNow - heartbeat < TimeSpan.FromSeconds(3)) return new() { Ready = true, Grant = grant };
            var start = new ProcessStartInfo(Path.Combine(AdminNames.Directory, AdminNames.Host)) { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AdminNames.Directory };
            start.ArgumentList.Add(Names.Worker); start.ArgumentList.Add(Names.Grant); start.ArgumentList.Add(r.Id);
            start.ArgumentList.Add(Names.Parent); start.ArgumentList.Add(Environment.ProcessId.ToString());
            var worker = Process.Start(start) ?? throw new IOException(AdminNames.Worker); workers[r.Id] = worker;
            for (var i = 0; i < 20; i++)
            {
                await Task.Delay(100, ct);
                if (worker.HasExited) return Fail(AdminNames.Worker);
                var state = await broker.SendAsync(new() { Op = Ops.Snapshot }, ct);
                var current = state.Snapshot?.Grants.FirstOrDefault(g => g.Id == r.Id);
                if (current?.HeartbeatAt is not null) return new() { Ready = true, Grant = current };
            }
            return Fail(AdminNames.Worker);
        }
        catch (Exception e) when (e is IOException or OperationCanceledException or System.ComponentModel.Win32Exception) { return Fail(AdminNames.Offline); }
        finally { gate.Release(); }
    }

    static AdminResponse Fail(string error) => new() { Ok = false, Ready = true, Error = error };

    static void CheckInstall()
    {
        if (!string.Equals(Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory), AdminNames.Directory, StringComparison.OrdinalIgnoreCase)) throw new UnauthorizedAccessException(AdminNames.Protected);
        var root = new DirectoryInfo(AdminNames.Directory);
        for (var dir = root; dir is not null; dir = dir.Parent)
            if ((dir.Attributes & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException(AdminNames.Protected);
        CheckAcl(root.GetAccessControl());
        foreach (var file in root.GetFiles())
        {
            if ((file.Attributes & FileAttributes.ReparsePoint) != 0) throw new UnauthorizedAccessException(AdminNames.Protected);
            CheckAcl(file.GetAccessControl());
        }
    }

    static void CheckAcl(FileSystemSecurity acl)
    {
        var admin = new SecurityIdentifier(WellKnownSidType.BuiltinAdministratorsSid, null);
        var system = new SecurityIdentifier(WellKnownSidType.LocalSystemSid, null);
        var owner = acl.GetOwner(typeof(SecurityIdentifier));
        if (!Equals(owner, admin) && !Equals(owner, system)) throw new UnauthorizedAccessException(AdminNames.Protected);
        const FileSystemRights write = FileSystemRights.Write | FileSystemRights.Delete | FileSystemRights.DeleteSubdirectoriesAndFiles | FileSystemRights.ChangePermissions | FileSystemRights.TakeOwnership;
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow && (rule.FileSystemRights & write) != 0 && !Equals(rule.IdentityReference, admin) && !Equals(rule.IdentityReference, system)) throw new UnauthorizedAccessException(AdminNames.Protected);
    }
}
