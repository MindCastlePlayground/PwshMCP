using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Principal;

namespace PwshMcp.Host;

internal sealed record Peer(string Sid,string User,int Session,string Logon,bool Elevated,int Pid)
{
    public static Peer Read(NamedPipeServerStream pipe)
    {
        if(!GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out var pid)) throw new UnauthorizedAccessException();
        var session=Process.GetProcessById((int)pid).SessionId;
        Peer? peer=null;
        pipe.RunAsClient(()=>
        {
            using var id=WindowsIdentity.GetCurrent(true) ?? throw new UnauthorizedAccessException();
            var principal=new WindowsPrincipal(id);
            peer=new(id.User!.Value,id.Name,session,ReadLogon(id),principal.IsInRole(WindowsBuiltInRole.Administrator),(int)pid);
        });
        return peer ?? throw new UnauthorizedAccessException();
    }
    private static string ReadLogon(WindowsIdentity id)
    {
        GetTokenInformation(id.Token,10,IntPtr.Zero,0,out var size);
        var ptr=Marshal.AllocHGlobal(size);
        try { if(!GetTokenInformation(id.Token,10,ptr,size,out _)) throw new UnauthorizedAccessException(); var s=Marshal.PtrToStructure<TokenStats>(ptr); return $"{s.Auth.High}:{s.Auth.Low}"; }
        finally {Marshal.FreeHGlobal(ptr);}
    }
    [StructLayout(LayoutKind.Sequential)] private struct Luid {public uint Low;public int High;}
    [StructLayout(LayoutKind.Sequential)] private struct TokenStats {public Luid Token;public Luid Auth;public long Expiry;public int Type;public int Level;public uint DynamicCharged,DynamicAvailable,GroupCount,PrivilegeCount;public Luid Modified;}
    [DllImport("kernel32.dll",SetLastError=true)] private static extern bool GetNamedPipeClientProcessId(IntPtr pipe,out uint pid);
    [DllImport("advapi32.dll",SetLastError=true)] private static extern bool GetTokenInformation(IntPtr token,int info,IntPtr data,int length,out int needed);
}
