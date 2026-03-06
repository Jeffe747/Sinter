namespace SinterServer.Data.Entities;

public sealed class ApplicationEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string RepoUrl { get; set; } = string.Empty;
    public string ProjectPath { get; set; } = string.Empty;
    public string? ServiceName { get; set; }
    public Guid? GitCredentialId { get; set; }
    public Guid? NodeId { get; set; }
    public bool IsAssignmentActive { get; set; }
    public string DeploymentStatus { get; set; } = "Inactive";
    public DateTimeOffset? LastDeploymentUtc { get; set; }
    public string? ActiveBaseUrl { get; set; }
    public int? ActivePort { get; set; }
    public string? ActiveBaseUrlStatus { get; set; }
    public string? LastOperationSummary { get; set; }
    public string? ServiceUnitContent { get; set; }
    public string? OverrideContent { get; set; }

    public NodeEntity? Node { get; set; }
    public GitCredentialEntity? GitCredential { get; set; }
}