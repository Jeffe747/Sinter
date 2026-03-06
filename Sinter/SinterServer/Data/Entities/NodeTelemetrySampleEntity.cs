namespace SinterServer.Data.Entities;

public sealed class NodeTelemetrySampleEntity
{
    public long Id { get; set; }
    public Guid NodeId { get; set; }
    public NodeEntity Node { get; set; } = null!;
    public DateTimeOffset CapturedUtc { get; set; }
    public int LogicalCpuCount { get; set; }
    public double? CpuUsagePercent { get; set; }
    public double? LoadAverage1m { get; set; }
    public double? LoadAverage5m { get; set; }
    public double? LoadAverage15m { get; set; }
    public long? MemoryTotalBytes { get; set; }
    public long? MemoryAvailableBytes { get; set; }
    public double? MemoryUsedPercent { get; set; }
    public long? DiskTotalBytes { get; set; }
    public long? DiskFreeBytes { get; set; }
    public double? DiskUsedPercent { get; set; }
    public int OpenPortCount { get; set; }
}