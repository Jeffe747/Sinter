namespace SinterNode.Models;

public sealed record NodeDashboard(
    string Hostname,
    string OsDescription,
    string ProcessArchitecture,
    string FrameworkDescription,
    string Version,
    string Uptime,
    NodeStateSnapshot Snapshot,
    IReadOnlyList<ServiceSummary> Services,
    IReadOnlyList<ManagedApplicationState> ManagedApplications);

public sealed record ServiceSummary(
    string Name,
    string Description,
    bool IsManagedByNode,
    bool HasOverride,
    string UnitPath);