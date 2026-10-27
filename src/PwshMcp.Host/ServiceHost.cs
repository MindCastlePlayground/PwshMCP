using System.Runtime.InteropServices;

namespace PwshMcp.Host;

internal static class ServiceHost
{
    private static readonly CancellationTokenSource Stop=new();
    private static readonly MainFn MainDelegate=ServiceMain;
    private static readonly HandlerFn HandlerDelegate=Handler;
    private static IntPtr handle;
    public static void Run()
    {
        var table=new[]{new Entry{Name=Names.ServiceName,Main=MainDelegate},new Entry()};
        if(!StartServiceCtrlDispatcher(table))throw new System.ComponentModel.Win32Exception();
    }
    private static void ServiceMain(int count,IntPtr args)
    {
        handle=RegisterServiceCtrlHandler(Names.ServiceName,HandlerDelegate);
        State(2);
        try{State(4);new Broker().RunAsync(Stop.Token).GetAwaiter().GetResult();State(1);}
        catch(OperationCanceledException){State(1);}
        catch{State(1,1);}
    }
    private static void Handler(int op){if(op is 1 or 5){State(3);Stop.Cancel();}}
    private static void State(int state,int error=0){var status=new Status{Type=16,State=state,Accepted=state==4?5:0,Error=error,Wait=state is 2 or 3?10000:0};SetServiceStatus(handle,ref status);}
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]private struct Entry{public string? Name;public MainFn? Main;}
    [StructLayout(LayoutKind.Sequential)]private struct Status{public int Type,State,Accepted,Error,Specific,Checkpoint,Wait;}
    private delegate void MainFn(int argc,IntPtr argv);
    private delegate void HandlerFn(int control);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool StartServiceCtrlDispatcher([In]Entry[] table);
    [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr RegisterServiceCtrlHandler(string name,HandlerFn handler);
    [DllImport("advapi32.dll",SetLastError=true)]private static extern bool SetServiceStatus(IntPtr handle,ref Status status);
}
