namespace SinterNode.Models;

public sealed record NodeDashboard(
    string Hostname,
    string OsDescription,
    string ProcessArchitecture,
    string FrameworkDescription,
    string Version,
    string Uptime,
    NodeCapabilities Capabilities,
    NodeEnvironmentInfo Environment,
    NodeStateSnapshot Snapshot,
    IReadOnlyList<ServiceSummary> Services,
    IReadOnlyList<ManagedApplicationState> ManagedApplications);

public sealed record NodeCapabilities(
    string AuthHeaderName,
    string EventStreamFormat,
    bool SupportsDotnetDeployments,
    bool SupportsServiceUnitManagement,
    bool SupportsOverrideManagement,
    bool SupportsSelfUpdate,
    bool SupportsRollback);

public sealed record NodeEnvironmentInfo(
    string[] ListenUrls,
    string ManagedAppsRoot,
    string SystemdUnitDirectory,
    string NodeInstallRoot,
    string NodeReleaseRoot,
    string SelfServiceName);

public sealed record ServiceSummary(
    string Name,
    string Description,
    bool IsManagedByNode,
    bool IsActive,
    bool IsEnabled,
    bool HasOverride,
    string UnitPath,
    IReadOnlyList<string> OverrideWarnings);