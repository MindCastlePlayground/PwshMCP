using System.Text;
using System.Text.Json;
using PwshMcp.Shared;

namespace PwshMcp.Host;

internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.InputEncoding=new UTF8Encoding(false);Console.OutputEncoding=new UTF8Encoding(false);
        using var stop=new CancellationTokenSource();
        Console.CancelKeyPress+=(_,e)=>{e.Cancel=true;stop.Cancel();};
        try
        {
            switch(args.FirstOrDefault())
            {
                case Names.Broker:await new Broker().RunAsync(stop.Token);break;
                case Names.Worker:await new Worker().RunAsync(Value(args,Names.Grant),Value(args,Names.Client)??"agent",int.TryParse(Value(args,Names.Parent),out var parent)?parent:0,stop.Token);break;
                case Names.Mcp:await new Mcp(Value(args,Names.Client)??"agent",Value(args,Names.DisplayName)).RunAsync(stop.Token);break;
                case Names.Service:ServiceHost.Run();break;
                case Names.Control:
                    var req=JsonSerializer.Deserialize<Request>(await Console.In.ReadToEndAsync(),Protocol.Json)??throw new InvalidDataException();
                    Console.WriteLine(JsonSerializer.Serialize(await new BrokerClient().SendAsync(req,stop.Token),Protocol.Json));break;
                default:Console.Error.WriteLine("PwshMcp.Host broker | worker --grant ID | mcp | service | ctl");return 2;
            }
            return 0;
        }
        catch(OperationCanceledException){return 0;}
        catch(Exception ex){Console.Error.WriteLine(ex.GetType().Name+": "+ex.Message);return 1;}
    }
    private static string? Value(string[] args,string name){var i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
}
