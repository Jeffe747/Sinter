namespace SinterServer.Models;

public sealed record UpsertNodeRequest(string Name, string Url, string ApiKey);
public sealed record UpsertGitCredentialRequest(string Name, string? Username, string? AccessToken);
public sealed record UpsertApplicationRequest(string Name, string RepoUrl, string ProjectPath, string? ServiceName, Guid? GitCredentialId);
public sealed record AssignApplicationRequest(Guid? NodeId);
public sealed record UpdateRemoteFileRequest(string Content, bool AllowOverwriteUnmanaged = false);

public sealed record ServerDashboard(string ServerName, IReadOnlyList<NodeListItem> Nodes, IReadOnlyList<ApplicationListItem> Applications, IReadOnlyList<GitCredentialListItem> AuthUsers);

public sealed record NodeListItem(
    Guid Id,
    string Name,
    string Url,
    string HealthStatus,
    DateTimeOffset? LastSeenUtc,
    string? LastError,
    NodeSnapshot? Snapshot,
    IReadOnlyList<NodeServiceInventoryItem> Services,
    IReadOnlyList<NodeManagedApplicationInventoryItem> ManagedApplications);

public sealed record NodeServiceActionRequest(string ServiceName);

public sealed record GitCredentialListItem(Guid Id, string Name, string? Username, int UsageCount);

public sealed record ApplicationListItem(
    Guid Id,
    string Name,
    string RepoUrl,
    string ProjectPath,
    string? ServiceName,
    Guid? NodeId,
    string? NodeName,
    Guid? GitCredentialId,
    string? GitCredentialName,
    bool IsAssignmentActive,
    string DeploymentStatus,
    DateTimeOffset? LastDeploymentUtc,
    string ActiveBaseUrl,
    string ActivePort,
    string? ServiceUnitContent,
    string? OverrideContent,
    string? LastOperationSummary);

public sealed record RemoteActionResult(string Status, string Summary, IReadOnlyList<RemoteEvent> Events);
public sealed record RemoteFileView(string Content, string Status);

public sealed record RemoteEvent(string Type, string Message, DateTimeOffset TimestampUtc, string? Command = null, int? ExitCode = null);

public sealed record NodeSnapshot(
    string? Hostname,
    string? OsDescription,
    string? ProcessArchitecture,
    string? FrameworkDescription,
    NodeCapabilities? Capabilities,
    NodeEnvironment? Environment,
    string? Version,
    string? Uptime,
    int ServicesCount,
    int ManagedAppsCount);

public sealed record NodeCapabilities(bool SupportsDotnetDeployments, bool SupportsServiceUnitManagement, bool SupportsOverrideManagement, bool SupportsSelfUpdate, bool SupportsRollback, string AuthHeaderName, string EventStreamFormat);

public sealed record NodeEnvironment(string[] ListenUrls, string ManagedAppsRoot, string SystemdUnitDirectory, string NodeInstallRoot, string NodeReleaseRoot, string SelfServiceName);

public sealed record NodeStatusResponse(
    NodeSnapshot? Snapshot,
    string Status,
    IReadOnlyList<NodeServiceInventoryItem> Services,
    IReadOnlyList<NodeManagedApplicationInventoryItem> ManagedApplications);

public sealed record NodeServiceInventoryItem(
    string Name,
    string Description,
    bool IsManagedByNode,
    bool HasOverride,
    string UnitPath,
    IReadOnlyList<string> OverrideWarnings);

public sealed record NodeManagedApplicationInventoryItem(
    string AppName,
    string RepoUrl,
    string Branch,
    string? ProjectPath,
    string ServiceName,
    string? CurrentRelease,
    string? LastSuccessfulRelease,
    DateTimeOffset? LastDeploymentUtc,
    bool IsNodeSelfManaged,
    int ReleaseCount,
    bool CurrentReleaseExists,
    bool LastSuccessfulReleaseExists,
    string? AppRoot,
    string? ReleasesRoot);