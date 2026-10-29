using PwshMcp.Shared;

internal static class Program
{
    static async Task<int> Main()
    {
        try { await Check(); return 0; }
        catch (Exception error)
        {
            Console.Error.WriteLine($"FAIL: installed coordinator check: {error.GetType().Name}: {error.Message}");
            return 1;
        }
    }

    static async Task Check()
    {
        var client = new AdminClient();
        var ready = await client.SendAsync(new() { Op = AdminOp.Status });
        if (!ready.Ok || !ready.Ready) throw new InvalidOperationException(AdminNames.Offline);
        foreach (var op in new[] { AdminOp.Approve, AdminOp.Resume })
        {
            var reply = await client.SendAsync(new() { Op = op, Id = "non-tray-probe" });
            if (reply.Ok || reply.Error != AdminNames.Forbidden) throw new InvalidOperationException("Non-tray approval was not rejected");
        }
        Console.WriteLine("PASS: installed administrator coordinator ready; non-tray approve/resume rejected");
    }
}
