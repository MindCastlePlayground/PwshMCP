using System.ComponentModel;
using System.IO.Pipes;
using System.Runtime.InteropServices;

namespace PwshMcp.Host;

internal static class PipeLabel
{
    const string Medium = "S:(ML;;NW;;;ME)";
    const uint Revision = 1, Label = 0x10;
    const int KernelObject = 6;

    // Setting the entire SACL requests SeSecurityPrivilege; only set the mandatory label.
    public static void Apply(NamedPipeServerStream pipe)
    {
        if (!ConvertStringSecurityDescriptorToSecurityDescriptor(Medium, Revision, out var sd, out _))
            throw new Win32Exception(Marshal.GetLastWin32Error());
        try
        {
            if (!GetSecurityDescriptorSacl(sd, out var present, out var sacl, out _) || !present)
                throw new Win32Exception(Marshal.GetLastWin32Error());
            var error = SetSecurityInfo(pipe.SafePipeHandle.DangerousGetHandle(), KernelObject, Label, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, sacl);
            if (error != 0) throw new Win32Exception((int)error);
        }
        finally { LocalFree(sd); }
    }

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text, uint revision, out IntPtr sd, out uint length);
    [DllImport("advapi32.dll", SetLastError = true)]
    static extern bool GetSecurityDescriptorSacl(IntPtr sd, out bool present, out IntPtr sacl, out bool defaulted);
    [DllImport("advapi32.dll")]
    static extern uint SetSecurityInfo(IntPtr handle, int type, uint info, IntPtr owner, IntPtr group, IntPtr dacl, IntPtr sacl);
    [DllImport("kernel32.dll")]
    static extern IntPtr LocalFree(IntPtr memory);
}
