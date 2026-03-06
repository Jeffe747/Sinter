namespace SinterNode.Models;

public sealed record NodeState(
    Guid NodeId,
    DateTimeOffset CreatedUtc,
    DateTimeOffset UpdatedUtc,
    string[] ServicePrefixes,
    bool BootstrapCompleted);

public sealed record NodeStateSnapshot(NodeState State, string ApiKey, bool ShowApiKey);

public sealed record ManagedApplicationState(
    string AppName,
    string RepoUrl,
    string Branch,
    string? ProjectPath,
    string ServiceName,
    string? CurrentRelease,
    string? LastSuccessfulRelease,
    DateTimeOffset? LastDeploymentUtc,
    bool IsNodeSelfManaged = false);