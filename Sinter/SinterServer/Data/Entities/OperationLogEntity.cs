namespace SinterServer.Data.Entities;

public sealed class OperationLogEntity
{
    public Guid Id { get; set; }
    public string ScopeType { get; set; } = string.Empty;
    public Guid ScopeId { get; set; }
    public string Action { get; set; } = string.Empty;
    public string Status { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public string? EventsJson { get; set; }
}