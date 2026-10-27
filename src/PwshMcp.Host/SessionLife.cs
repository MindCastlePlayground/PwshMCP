using System.Runtime.InteropServices;

namespace PwshMcp.Host;

internal static class SessionLife
{
    public static long Stamp(int session)
    {
        if(!WTSQuerySessionInformation(IntPtr.Zero,session,24,out var ptr,out _))return 0;
        try{return Marshal.PtrToStructure<Info>(ptr).LogonTime;}
        finally{WTSFreeMemory(ptr);}
    }
    [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] private struct Info
    {
        public int State,Session;
        public uint IncomingBytes,OutgoingBytes,IncomingFrames,OutgoingFrames,IncomingCompressedBytes,OutgoingCompressedBytes;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Station;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=17)]public string Domain;
        [MarshalAs(UnmanagedType.ByValTStr,SizeConst=21)]public string User;
        public long ConnectTime,DisconnectTime,LastInputTime,LogonTime,CurrentTime;
    }
    [DllImport("wtsapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]private static extern bool WTSQuerySessionInformation(IntPtr server,int session,int kind,out IntPtr buffer,out int bytes);
    [DllImport("wtsapi32.dll")]private static extern void WTSFreeMemory(IntPtr memory);
}
