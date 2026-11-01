namespace PwshMcp.Host;

internal static class Names
{
    public const string Broker="broker", Worker="worker", Mcp="mcp", Service="service", Control="ctl", Grant="--grant", Normal="--normal", Client="--client", DisplayName="--display-name", Parent="--parent", ServiceName="PwshMcpBroker";
    public const int MaxMessage=10_000_000, MaxJobs=1000, MaxCode=524288, MaxClientName=120;
    public static class History { public const int Jobs=100, Grants=100; public const string Directory="history", JobsDirectory="jobs", GrantsDirectory="grants", Extension=".bin"; }
}
internal static class ClientNameRules
{
    public static bool IsValid(string? name)=>!string.IsNullOrWhiteSpace(name)&&name.Length<=Names.MaxClientName&&!name.Any(char.IsControl);
}
internal static class Errors
{
    public const string HistoryMissing="history_record_missing_or_invalid";
    public const string Description="description_required", TaskName="task_name_required";
    public const string Missing="not_found", Forbidden="forbidden", Elevation="requires_user_elevation", Invalid="invalid_request", Conflict="request_id_conflict", Expired="grant_inactive", Offline="worker_offline", Full="capacity_reached";
}
