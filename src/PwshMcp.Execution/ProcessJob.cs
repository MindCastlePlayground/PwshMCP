using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace PwshMcp.Execution;

internal sealed class ProcessJob : IDisposable
{
    private readonly SafeFileHandle handle;
    public ProcessJob(Process process)
    {
        handle=new SafeFileHandle(CreateJobObject(IntPtr.Zero,null),true);
        if(handle.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
        var info=new Extended {Basic=new Basic{LimitFlags=0x2000}};
        var size=Marshal.SizeOf<Extended>();var ptr=Marshal.AllocHGlobal(size);
        try
        {
            Marshal.StructureToPtr(info,ptr,false);
            if(!SetInformationJobObject(handle,9,ptr,(uint)size)||!AssignProcessToJobObject(handle,process.Handle))throw new Win32Exception(Marshal.GetLastWin32Error());
        }
        catch{handle.Dispose();try{process.Kill(true);}catch(InvalidOperationException){}throw;}
        finally{Marshal.FreeHGlobal(ptr);}
    }
    public void Dispose()=>handle.Dispose();
    [StructLayout(LayoutKind.Sequential)]private struct Basic{public long PerProcessUserTimeLimit,PerJobUserTimeLimit;public uint LimitFlags;public UIntPtr MinimumWorkingSetSize,MaximumWorkingSetSize;public uint ActiveProcessLimit;public UIntPtr Affinity;public uint PriorityClass,SchedulingClass;}
    [StructLayout(LayoutKind.Sequential)]private struct Io{public ulong ReadOperationCount,WriteOperationCount,OtherOperationCount,ReadTransferCount,WriteTransferCount,OtherTransferCount;}
    [StructLayout(LayoutKind.Sequential)]private struct Extended{public Basic Basic;public Io Io;public UIntPtr ProcessMemoryLimit,JobMemoryLimit,PeakProcessMemoryUsed,PeakJobMemoryUsed;}
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern IntPtr CreateJobObject(IntPtr attrs,string? name);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool SetInformationJobObject(SafeFileHandle job,int kind,IntPtr data,uint length);
    [DllImport("kernel32.dll",SetLastError=true)]private static extern bool AssignProcessToJobObject(SafeFileHandle job,IntPtr process);
}
