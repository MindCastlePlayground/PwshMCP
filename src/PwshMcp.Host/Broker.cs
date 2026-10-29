using System.IO.Pipes;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using PwshMcp.Shared;

namespace PwshMcp.Host;

internal sealed class Broker
{
    private readonly object gate=new();
    private readonly Dictionary<string,AccessInfo> grants=[];
    private readonly Dictionary<string,JobInfo> jobs=[];
    private readonly Dictionary<string,string> fingerprints=[];
    private readonly Dictionary<string,int> workers=[];
    private readonly Dictionary<string,int> grantWorkers=[];
    private readonly Dictionary<string,int> normalWorkers=[];
    private readonly Dictionary<string,long> sessionStamps=[];
    private readonly HashSet<string> cancelled=[];
    private readonly StateStore store=new();
    private readonly bool elevated=new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator);

    public async Task RunAsync(CancellationToken ct)
    {
        using var singleton=new Mutex(false,@"Global\"+Protocol.Pipe);
        if(!singleton.WaitOne(0)) return;
        Restore();
        // Mutex ownership is thread-affine; process lifetime owns the mutex, no async ReleaseMutex.
        while(!ct.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try { pipe=OpenPipe(); }
            catch(IOException ex) when(PipeFault.IsBusy(ex)) { await Task.Delay(100,ct);continue; }
            try {await pipe.WaitForConnectionAsync(ct); _=Serve(pipe,ct);}
            catch {pipe.Dispose(); if(ct.IsCancellationRequested)break;throw;}
        }
    }
    private static NamedPipeServerStream OpenPipe()
    {
        var acl=new PipeSecurity();
        acl.SetAccessRuleProtection(true,false);
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.NetworkSid,null),PipeAccessRights.FullControl,AccessControlType.Deny));
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null),PipeAccessRights.FullControl,AccessControlType.Allow));
        acl.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User!,PipeAccessRights.FullControl,AccessControlType.Allow));
        // ReadWrite includes the attributes/control rights requested by generic client access,
        // but excludes CreateNewInstance, so clients cannot create another server instance.
        acl.AddAccessRule(new PipeAccessRule(new SecurityIdentifier(WellKnownSidType.AuthenticatedUserSid,null),PipeAccessRights.ReadWrite|PipeAccessRights.Synchronize,AccessControlType.Allow));
        return NamedPipeServerStreamAcl.Create(Protocol.Pipe,PipeDirection.InOut,32,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,65536,65536,acl);
    }
    private async Task Serve(NamedPipeServerStream pipe,CancellationToken ct)
    {
        try { await Exchange(pipe,ct); }
        catch(Exception ex) when(ex is not OutOfMemoryException) { RuntimeLog.Write(Names.Broker,RuntimeLog.Client,ex); }
    }
    private async Task Exchange(NamedPipeServerStream pipe,CancellationToken ct)
    {
        using(pipe)
        using(var reader=new StreamReader(pipe,Encoding.UTF8,leaveOpen:true))
        using(var writer=new StreamWriter(pipe,new UTF8Encoding(false),leaveOpen:true){AutoFlush=true})
        using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(ct))
        {
            deadline.CancelAfter(TimeSpan.FromSeconds(10));
            try
            {
                var line=await ReadBounded(reader,deadline.Token);
                var req=JsonSerializer.Deserialize<Request>(line,Protocol.Json) ?? throw new InvalidDataException();
                var peer=Peer.Read(pipe);
                Response response;
                lock(gate)
                {
                    Sweep();response=Dispatch(req,peer);
                    if(req.Op is Ops.Submit or Ops.Approve or Ops.Deny or Ops.Revoke or Ops.WorkerResult or Ops.Cancel or Ops.RequestAccess || req.Op==Ops.WorkerNext&&response.Job is not null)
                        store.Save(grants.Values,jobs.Values,fingerprints);
                }
                await writer.WriteLineAsync(JsonSerializer.Serialize(response,Protocol.Json).AsMemory(),deadline.Token);
            }
            catch(Exception ex) when(ex is not OutOfMemoryException)
            {try{await writer.WriteLineAsync(JsonSerializer.Serialize(new Response{Ok=false,Error=ex.GetType().Name},Protocol.Json));}catch(IOException){} }
        }
    }
    private static async Task<string> ReadBounded(StreamReader reader,CancellationToken ct)
    {
        var sb=new StringBuilder();var buf=new char[1];
        while(sb.Length<Names.MaxMessage){if(await reader.ReadAsync(buf,ct)==0)break;if(buf[0]=='\n')return sb.ToString();sb.Append(buf[0]);}
        throw new InvalidDataException("message_size");
    }
    private Response Dispatch(Request r,Peer p)
    {
        if(r.Client.Length>100 || r.Label.Length>500 || r.Id.Length>200 || r.TaskName is null || r.TaskName.Length>200 || (r.ClientName is not null&&!ClientNameRules.IsValid(r.ClientName)))return Fail(Errors.Invalid);
        switch(r.Op)
        {
            case Ops.Snapshot:return new(){Snapshot=new(){ElevatedBroker=elevated,Grants=grants.Values.Where(g=>Owned(g,p)).ToList(),Jobs=jobs.Values.Where(j=>Owned(j,p)).OrderByDescending(j=>j.CreatedAt).Take(100).Select(SafeJob).ToList()}};
            case Ops.RequestAccess:
                if(string.IsNullOrWhiteSpace(r.TaskName))return Fail(Errors.TaskName);
                return new(){Grant=RequestGrant(r.Client,r.Label,r.TaskName,r.ClientName??"",p)};
            case Ops.Submit:return Submit(r,p);
            case Ops.Approve:return Approve(r,p);
            case Ops.Deny:case Ops.Revoke:
                if(!grants.TryGetValue(r.Id,out var g)||!Owned(g,p))return Fail(Errors.Forbidden);
                g.State=r.Op==Ops.Deny?AccessState.Denied:AccessState.Revoked;
                foreach(var j in jobs.Values.Where(j=>j.GrantId==g.Id || (Owned(j,p)&&j.Client==g.Client&&j.State==JobState.NeedsApproval)))
                    if(j.State is JobState.Queued or JobState.NeedsApproval or JobState.NeedsSession) j.State=JobState.Rejected;
                return new(){Grant=g};
            case Ops.Status:case Ops.Output:case Ops.Cancel:
                if(!jobs.TryGetValue(r.Id,out var job)||!Owned(job,p))return Fail(Errors.Missing);
                if(r.Op==Ops.Cancel){cancelled.Add(job.Id);if(job.State!=JobState.Running && !Terminal(job.State))job.State=JobState.Cancelled;}
                if(r.Op==Ops.Output){var s=job.Result?.Output??"";var start=Math.Clamp(r.Cursor,0,s.Length);var end=Math.Min(s.Length,start+65536);return new(){Id=job.Id,Output=s[start..end],Cursor=end,Job=SafeJob(job)};}
                return new(){Job=SafeJob(job)};
            case Ops.WorkerNext:return Next(r,p);
            case Ops.WorkerResult:return Complete(r,p);
            default:return Fail(Errors.Invalid);
        }
    }
    private Response Submit(Request r,Peer p)
    {
        if(r.Run is null||string.IsNullOrWhiteSpace(r.Id)||r.Run.Code.Length>Names.MaxCode||!Path.IsPathFullyQualified(r.Run.Cwd)||!Directory.Exists(r.Run.Cwd)||r.Run.TimeoutSeconds<1||r.Run.TimeoutSeconds>86400)return Fail(Errors.Invalid);
        if(string.IsNullOrWhiteSpace(r.Run.Description))return Fail(Errors.Description);
        if(r.Run.Description.Length>1000)return Fail(Errors.Invalid);
        var key=$"{p.Sid}|{p.Session}|{r.Client}|{r.Id}";
        var hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(r.Run,Protocol.Json))));
        if(fingerprints.TryGetValue(key,out var old))return old==hash?new(){Job=SafeJob(jobs.Values.Single(j=>j.Id==Ids(key)))}:Fail(Errors.Conflict);
        if(jobs.Count>=Names.MaxJobs)return Fail(Errors.Full);
        var active=grants.Values.FirstOrDefault(g=>Owned(g,p)&&g.Client==r.Client&&g.State==AccessState.Active);
        AccessInfo? pending=null;
        if(r.Run.Privilege==Privilege.Elevated&&active is null)
        {
            pending=grants.Values.FirstOrDefault(g=>Owned(g,p)&&g.Client==r.Client&&g.State==AccessState.Pending);
            if(pending is null&&string.IsNullOrWhiteSpace(r.TaskName))return Fail(Errors.TaskName);
            pending??=RequestGrant(r.Client,r.Label,r.TaskName,r.ClientName??"",p);
        }
        var grant=active??pending;
        if(grant is not null&&r.ClientName is not null)grant.ClientName=r.ClientName;
        var job=new JobInfo{Id=Ids(key),Client=r.Client,ClientName=r.ClientName??"",TaskName=string.IsNullOrWhiteSpace(r.TaskName)?grant?.TaskName??"":r.TaskName.Trim(),OwnerSid=p.Sid,SessionId=p.Session,Run=r.Run,GrantId=r.Run.Privilege==Privilege.Elevated?grant?.Id:null,State=r.Run.Privilege==Privilege.Normal?JobState.Queued:active is null?JobState.NeedsApproval:JobState.Queued};
        jobs[job.Id]=job;fingerprints[key]=hash;
        return new(){Id=job.Id,Job=SafeJob(job),Grant=pending??active};
    }
    private AccessInfo RequestGrant(string client,string label,string taskName,string clientName,Peer p)
    {
        var found=grants.Values.FirstOrDefault(g=>Owned(g,p)&&g.Client==client&&g.State is AccessState.Pending or AccessState.Active);
        if(found is not null){if(clientName.Length>0)found.ClientName=clientName;return found;}
        if(grants.Count>=Names.MaxJobs)throw new InvalidOperationException(Errors.Full);
        var g=new AccessInfo{Id=Guid.NewGuid().ToString("N"),Client=client,ClientName=clientName,Label=label,TaskName=taskName.Trim(),User=p.User,Sid=p.Sid,SessionId=p.Session,State=AccessState.Pending};
        grants[g.Id]=g;return g;
    }
    private Response Approve(Request r,Peer p)
    {
        if(!p.Elevated)return Fail(Errors.Elevation);
        if(!grants.TryGetValue(r.Id,out var g)||!Owned(g,p)||g.State!=AccessState.Pending)return Fail(Errors.Forbidden);
        if(r.ExpiresAt<=DateTimeOffset.UtcNow)return Fail(Errors.Invalid);
        var stamp=SessionLife.Stamp(g.SessionId);if(stamp==0)return Fail("session_identity_unavailable");
        g.State=AccessState.Active;g.EndReason=AccessEnd.Unknown;g.ExpiresAt=r.ExpiresAt;g.HeartbeatAt=null;
        sessionStamps[g.Id]=stamp;
        foreach(var j in jobs.Values.Where(j=>Owned(j,p)&&j.Client==g.Client&&j.State==JobState.NeedsApproval)){j.GrantId=g.Id;j.State=JobState.Queued;}
        return new(){Grant=g};
    }
    private Response Next(Request r,Peer p)
    {
        AccessInfo? g=null;
        if(!string.IsNullOrEmpty(r.Id))
        {
            if(!p.Elevated||!grants.TryGetValue(r.Id,out g)||!Owned(g,p))return Fail(Errors.Forbidden);
            if(grantWorkers.TryGetValue(g.Id,out var previous)&&previous!=p.Pid&&Alive(previous))return Fail("worker_already_attached");
            grantWorkers[g.Id]=p.Pid;
            // Running tasks may finish after expiry/revocation; no new work is dispatched.
            g.HeartbeatAt=DateTimeOffset.UtcNow;
        }
        if(r.TaskId is not null)
        {
            if(!jobs.TryGetValue(r.TaskId,out var task)||!Owned(task,p)||!workers.TryGetValue(task.Id,out var pid)||pid!=p.Pid)return Fail(Errors.Forbidden);
            return new(){Cancel=cancelled.Contains(task.Id)};
        }
        if(g is not null&&g.State!=AccessState.Active)return Fail(Errors.Expired);
        if(g is null)
        {
            var key=$"{p.Sid}|{p.Session}|{r.Client}";
            if(normalWorkers.TryGetValue(key,out var owner)&&owner!=p.Pid&&Alive(owner))return new();
            normalWorkers[key]=p.Pid;
        }
        var job=jobs.Values.FirstOrDefault(j=>Owned(j,p)&&j.State==JobState.Queued&&(g is null?j.Run.Privilege==Privilege.Normal&&j.Client==r.Client:j.Run.Privilege==Privilege.Elevated&&j.GrantId==g.Id));
        if(job is null)return new();
        if(g is null&&p.Elevated)return Fail("normal_worker_must_not_be_elevated");
        job.State=JobState.Running;workers[job.Id]=p.Pid;
        return new(){Job=job};
    }
    private Response Complete(Request r,Peer p)
    {
        if(r.Result is null||!jobs.TryGetValue(r.Id,out var j)||!Owned(j,p)||!workers.TryGetValue(j.Id,out var pid)||pid!=p.Pid||j.State!=JobState.Running)return Fail(Errors.Forbidden);
        j.Result=r.Result;j.State=r.Result.Cancelled?JobState.Cancelled:r.Result.ExitCode==0?JobState.Succeeded:JobState.Failed;
        return new(){Job=SafeJob(j)};
    }
    private void Sweep()
    {
        var now=DateTimeOffset.UtcNow;
        if(jobs.Count>=Names.MaxJobs)
        {
            foreach(var old in jobs.Values.Where(j=>Terminal(j.State)).OrderBy(j=>j.CreatedAt).Take(Names.MaxJobs/5).ToArray())
            {
                jobs.Remove(old.Id);workers.Remove(old.Id);cancelled.Remove(old.Id);
                foreach(var key in fingerprints.Keys.Where(k=>Ids(k)==old.Id).ToArray())fingerprints.Remove(key);
            }
        }
        if(grants.Count>=Names.MaxJobs)
            foreach(var old in grants.Values.Where(g=>g.State is AccessState.Denied or AccessState.Revoked or AccessState.Expired).OrderBy(g=>g.CreatedAt).Take(Names.MaxJobs/5).ToArray())
                if(!jobs.Values.Any(j=>j.GrantId==old.Id&&!Terminal(j.State))){grants.Remove(old.Id);grantWorkers.Remove(old.Id);sessionStamps.Remove(old.Id);}
        foreach(var g in grants.Values)
        {
            if(g.State==AccessState.Pending&&now-g.CreatedAt>TimeSpan.FromHours(24))End(g,AccessEnd.RequestTimeout);
            if(g.State==AccessState.Active&&g.ExpiresAt<=now)End(g,AccessEnd.Duration);
            if(g.State==AccessState.Active&&sessionStamps.TryGetValue(g.Id,out var stamp)&&stamp!=0&&SessionLife.Stamp(g.SessionId)!=stamp)End(g,AccessEnd.Session);
        }
        foreach(var j in jobs.Values)
        {
            if(j.State==JobState.NeedsApproval&&now-j.CreatedAt>TimeSpan.FromHours(24))j.State=JobState.Expired;
            if(j.State==JobState.Queued&&j.GrantId is not null&&grants[j.GrantId].State!=AccessState.Active)j.State=JobState.NeedsSession;
            if(j.State==JobState.Running&&workers.TryGetValue(j.Id,out var pid))
                try{if(System.Diagnostics.Process.GetProcessById(pid).HasExited)j.State=JobState.OutcomeUnknown;}catch(ArgumentException){j.State=JobState.OutcomeUnknown;}
        }
    }
    private bool Owned(AccessInfo g,Peer p)=>g.Sid==p.Sid&&g.SessionId==p.Session;
    private static void End(AccessInfo g,AccessEnd reason){g.State=AccessState.Expired;g.EndReason=reason;}
    private bool Owned(JobInfo j,Peer p)=>j.OwnerSid==p.Sid&&j.SessionId==p.Session;
    private static JobInfo SafeJob(JobInfo j)=>j with{Run=j.Run with{Env=[]},Result=j.Result is null?null:j.Result with{Output=""}};
    private static Response Fail(string error)=>new(){Ok=false,Error=error};
    private static bool Terminal(JobState s)=>s is JobState.Succeeded or JobState.Failed or JobState.Cancelled or JobState.Rejected or JobState.Expired or JobState.OutcomeUnknown;
    private static string Ids(string key)=>Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32].ToLowerInvariant();
    private static bool Alive(int pid){try{return !System.Diagnostics.Process.GetProcessById(pid).HasExited;}catch(ArgumentException){return false;}}
    private void Restore()
    {
        var previous=store.Load();if(previous is null)return;
        foreach(var g in previous.Grants){if(g.State is AccessState.Active or AccessState.Pending)End(g,AccessEnd.Restart);g.HeartbeatAt=null;grants[g.Id]=g;}
        foreach(var j in previous.Jobs){if(!Terminal(j.State))j.State=JobState.OutcomeUnknown;jobs[j.Id]=j;}
        foreach(var pair in previous.Fingerprints)fingerprints[pair.Key]=pair.Value;
    }
}
