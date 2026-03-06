namespace SinterServer.Data.Entities;

public sealed class NodeEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string HealthStatus { get; set; } = "Unknown";
    public DateTimeOffset? LastSeenUtc { get; set; }
    public DateTimeOffset? LastRefreshUtc { get; set; }
    public string? LastError { get; set; }
    public string? SnapshotJson { get; set; }
    public string? CapabilitiesJson { get; set; }
    public string? EnvironmentJson { get; set; }
    public string? ListenUrlsJson { get; set; }
    public string? ServicesJson { get; set; }
    public string? ManagedApplicationsJson { get; set; }
    public ICollection<ApplicationEntity> Applications { get; set; } = [];
    public ICollection<NodeTelemetrySampleEntity> TelemetrySamples { get; set; } = [];
}