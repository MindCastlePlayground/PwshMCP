using System.Runtime.InteropServices;
using System.Text.Json;
using PwshMcp.Shared;

namespace PwshMcp.Host;

internal sealed record ArchivedJob(string Fingerprint, JobInfo Job);

internal sealed class HistoryStore
{
    private readonly string root;
    internal HistoryStore(string statePath) => root=Path.Combine(Path.GetDirectoryName(statePath)!,Names.History.Directory);
    internal string JobPath(string id) { Validate(id); return Path.Combine(root,Names.History.JobsDirectory,id+Names.History.Extension); }
    internal string GrantPath(string id) { Validate(id); return Path.Combine(root,Names.History.GrantsDirectory,id+Names.History.Extension); }
    internal ArchivedJob? ReadJob(string id)
    {
        var path=JobPath(id); if(!File.Exists(path))return null;
        var bytes=Unprotect(File.ReadAllBytes(path));
        var record=JsonSerializer.Deserialize<ArchivedJob>(bytes,Protocol.Json)??throw new InvalidDataException(Errors.HistoryMissing);
        if(record.Job.Id!=id)throw new InvalidDataException(Errors.HistoryMissing);
        return record;
    }
    internal void WriteJob(string fingerprint,JobInfo job)=>Write(JobPath(job.Id),Protect(JsonSerializer.SerializeToUtf8Bytes(new ArchivedJob(fingerprint,job with{Run=job.Run with{Env=[]}}),Protocol.Json)));
    internal void WriteGrant(AccessInfo grant)=>Write(GrantPath(grant.Id),JsonSerializer.SerializeToUtf8Bytes(grant,Protocol.Json));
    private static void Validate(string id)
    { if(id.Length!=32||id.Any(c=>!Uri.IsHexDigit(c)))throw new ArgumentException("Invalid history identifier."); }
    private static void Write(string path,byte[] bytes)
    { Directory.CreateDirectory(Path.GetDirectoryName(path)!); var temp=path+"."+Guid.NewGuid().ToString("N")+".tmp"; try{File.WriteAllBytes(temp,bytes);File.Move(temp,path,true);}finally{if(File.Exists(temp))File.Delete(temp);} }
    private static byte[] Protect(byte[] data)=>Crypt(true,data);
    private static byte[] Unprotect(byte[] data)=>Crypt(false,data);
    private static byte[] Crypt(bool encrypt,byte[] input)
    {
        if(!OperatingSystem.IsWindows())throw new PlatformNotSupportedException("History encryption requires Windows DPAPI.");
        var source=new Blob(input); Blob output=default;
        try
        {
            var ok=encrypt?CryptProtectData(ref source,null,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,0x1,ref output):CryptUnprotectData(ref source,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,0x1,ref output);
            if(!ok)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error(),"Could not protect broker history with Windows DPAPI.");
            var result=new byte[output.Size];Marshal.Copy(output.Data,result,0,result.Length);return result;
        }
        finally{if(output.Data!=IntPtr.Zero)LocalFree(output.Data);Marshal.FreeHGlobal(source.Data);}
    }
    [StructLayout(LayoutKind.Sequential)] private struct Blob { public int Size; public IntPtr Data; public Blob(byte[] b){Size=b.Length;Data=Marshal.AllocHGlobal(b.Length);Marshal.Copy(b,0,Data,b.Length);} }
    [DllImport("crypt32.dll",CharSet=CharSet.Unicode,SetLastError=true)] private static extern bool CryptProtectData(ref Blob input,string? description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,ref Blob output);
    [DllImport("crypt32.dll",SetLastError=true)] private static extern bool CryptUnprotectData(ref Blob input,IntPtr description,IntPtr entropy,IntPtr reserved,IntPtr prompt,int flags,ref Blob output);
    [DllImport("kernel32.dll")] private static extern IntPtr LocalFree(IntPtr memory);
}
