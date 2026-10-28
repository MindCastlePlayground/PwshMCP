using PwshMcp.Shared;

internal static class Program
{
    static async Task Main()
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
