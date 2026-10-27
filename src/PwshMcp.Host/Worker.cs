using System.Diagnostics;
using PwshMcp.Shared;
using PwshMcp.Execution;

namespace PwshMcp.Host;

internal sealed class Worker
{
    private readonly BrokerClient client=new();
    public async Task RunAsync(string? grant,string name,int parent,CancellationToken stop)
    {
        await using var executor=new Executor();
        using var lifetime=CancellationTokenSource.CreateLinkedTokenSource(stop);
        var running=new Dictionary<string,(Task Work,CancellationTokenSource Stop)>();
        var session=Process.GetCurrentProcess().SessionId;
        var sessionStamp=SessionLife.Stamp(session);
        try
        {
            while(!lifetime.IsCancellationRequested)
            {
                if(parent>0&&!Alive(parent))break;
                if(sessionStamp!=0&&SessionLife.Stamp(session)!=sessionStamp)break;
                foreach(var id in running.Where(x=>x.Value.Work.IsCompleted).Select(x=>x.Key).ToArray()){await running[id].Work;running[id].Stop.Dispose();running.Remove(id);}
                foreach(var item in running)
                {
                    var pulse=await client.SendAsync(new(){Op=Ops.WorkerNext,Id=grant??"",Client=name,TaskId=item.Key},lifetime.Token);
                    if(!pulse.Ok||pulse.Cancel)item.Value.Stop.Cancel();
                }
                var response=await Poll(new(){Op=Ops.WorkerNext,Id=grant??"",Client=name},grant is null&&running.Count==0,parent,lifetime.Token);
                if(!response.Ok){if(running.Count==0)break;}
                else if(response.Job is { } job)
                {
                    var ct=CancellationTokenSource.CreateLinkedTokenSource(lifetime.Token);
                    running[job.Id]=(Execute(executor,job,ct.Token),ct);
                }
                // At most four active processes per worker; polling still handles cancellation.
                while(running.Count>=4&&!running.Values.Any(t=>t.Work.IsCompleted))
                {
                    await Task.Delay(250,lifetime.Token);
                    foreach(var item in running){var pulse=await client.SendAsync(new(){Op=Ops.WorkerNext,Id=grant??"",Client=name,TaskId=item.Key},lifetime.Token);if(!pulse.Ok||pulse.Cancel)item.Value.Stop.Cancel();}
                }
                await Task.Delay(250,lifetime.Token);
            }
        }
        finally
        {
            lifetime.Cancel();
            foreach(var item in running.Values)item.Stop.Cancel();
            try{await Task.WhenAll(running.Values.Select(t=>t.Work));}catch(Exception){}
            foreach(var item in running.Values)item.Stop.Dispose();
        }
    }
    private async Task<Response> Poll(Request request,bool retry,int parent,CancellationToken ct)
    {
        while(true)
        {
            ct.ThrowIfCancellationRequested();
            if(parent>0&&!Alive(parent))throw new OperationCanceledException();
            try{return await client.SendAsync(request,ct);}
            catch(Exception ex) when(retry&&!ct.IsCancellationRequested&&(ex is IOException or TimeoutException or OperationCanceledException))
            {await Task.Delay(500,ct);}
        }
    }
    private async Task Execute(Executor executor,JobInfo job,CancellationToken ct)
    {
        ExecutionResult result;
        try
        {
            if(job.Run.Code==SessionCommands.Reset&&job.Run.Session is not null){await executor.ResetAsync(job.Run.Session);result=new(){Output="session_reset"};}
            else if(job.Run.Code==SessionCommands.Close&&job.Run.Session is not null){await executor.CloseAsync(job.Run.Session);result=new(){Output="session_closed"};}
            else result=await executor.RunAsync(job.Run,ct);
        }
        catch(OperationCanceledException){result=new(){Cancelled=true,ExitCode=-1};}
        catch(Exception ex){result=new(){ExitCode=-1,Error=ex.GetType().Name};}
        try{await client.SendAsync(new(){Op=Ops.WorkerResult,Id=job.Id,Result=result});}catch(IOException){}catch(OperationCanceledException){}
    }
    private static bool Alive(int pid){try{return !Process.GetProcessById(pid).HasExited;}catch(ArgumentException){return false;}}
}
internal static class SessionCommands
{
    public const string Reset="#pwshmcp:session_reset",Close="#pwshmcp:session_close";
}
