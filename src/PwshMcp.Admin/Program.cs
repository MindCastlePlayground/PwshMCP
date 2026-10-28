using System.Diagnostics;
using PwshMcp.Shared;

internal static class Program
{
    static async Task<int> Main()
    {
        try
        {
            var start = new ProcessStartInfo(Path.Combine(AppContext.BaseDirectory, AdminNames.Host))
            { UseShellExecute = false, CreateNoWindow = true, WorkingDirectory = AppContext.BaseDirectory };
            start.ArgumentList.Add(AdminNames.Mode);
            using var process = Process.Start(start) ?? throw new IOException(AdminNames.Offline);
            await process.WaitForExitAsync();
            return process.ExitCode;
        }
        catch { return 1; }
    }
}
