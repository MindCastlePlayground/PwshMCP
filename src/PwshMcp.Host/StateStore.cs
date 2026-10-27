using System.Security.Principal;
using System.Text.Json;
using PwshMcp.Shared;

namespace PwshMcp.Host;

internal sealed record StoredState(List<AccessInfo> Grants,List<JobInfo> Jobs,Dictionary<string,string> Fingerprints);
internal sealed class StateStore
{
    private readonly string path;
    public StateStore()
    {
        var root=WindowsIdentity.GetCurrent().IsSystem?Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData):Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        path=Path.Combine(root,Protocol.Product,"broker-state.json");
    }
    public StoredState? Load()
    {
        if(!File.Exists(path))return null;
        try{return JsonSerializer.Deserialize<StoredState>(File.ReadAllText(path),Protocol.Json);}
        catch(JsonException){File.Move(path,path+".invalid-"+DateTimeOffset.UtcNow.ToUnixTimeSeconds());return null;}
    }
    public void Save(IEnumerable<AccessInfo> grants,IEnumerable<JobInfo> jobs,Dictionary<string,string> hashes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var safe=jobs.Select(j=>j with{Run=j.Run with{Code="",Env=[]},Result=j.Result is null?null:j.Result with{Output="",Error=""}}).ToList();
        var json=JsonSerializer.Serialize(new StoredState(grants.ToList(),safe,hashes),Protocol.Json);
        var temp=path+".tmp";File.WriteAllText(temp,json);File.Move(temp,path,true);
    }
}
