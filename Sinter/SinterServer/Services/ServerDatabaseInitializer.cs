using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SinterServer.Data;
using SinterServer.Options;

namespace SinterServer.Services;

public interface IServerDatabaseInitializer
{
    Task InitializeAsync(CancellationToken cancellationToken);
}

public sealed class ServerDatabaseInitializer(
    SinterServerDbContext dbContext,
    IOptions<SinterServerOptions> options,
    TimeProvider timeProvider) : IServerDatabaseInitializer
{
    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.EnsureCreatedAsync(cancellationToken);
        await EnsureTelemetrySchemaAsync(cancellationToken);
        await PruneExpiredTelemetryAsync(cancellationToken);
    }

    private async Task EnsureTelemetrySchemaAsync(CancellationToken cancellationToken)
    {
        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE TABLE IF NOT EXISTS "NodeTelemetrySamples" (
                "Id" INTEGER NOT NULL CONSTRAINT "PK_NodeTelemetrySamples" PRIMARY KEY AUTOINCREMENT,
                "NodeId" TEXT NOT NULL,
                "CapturedUtc" TEXT NOT NULL,
                "LogicalCpuCount" INTEGER NOT NULL,
                "CpuUsagePercent" REAL NULL,
                "LoadAverage1m" REAL NULL,
                "LoadAverage5m" REAL NULL,
                "LoadAverage15m" REAL NULL,
                "MemoryTotalBytes" INTEGER NULL,
                "MemoryAvailableBytes" INTEGER NULL,
                "MemoryUsedPercent" REAL NULL,
                "DiskTotalBytes" INTEGER NULL,
                "DiskFreeBytes" INTEGER NULL,
                "DiskUsedPercent" REAL NULL,
                "OpenPortCount" INTEGER NOT NULL,
                CONSTRAINT "FK_NodeTelemetrySamples_Nodes_NodeId" FOREIGN KEY ("NodeId") REFERENCES "Nodes" ("Id") ON DELETE CASCADE
            )
            """,
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_NodeTelemetrySamples_NodeId_CapturedUtc\" ON \"NodeTelemetrySamples\" (\"NodeId\", \"CapturedUtc\");",
            cancellationToken);

        await dbContext.Database.ExecuteSqlRawAsync(
            "CREATE INDEX IF NOT EXISTS \"IX_NodeTelemetrySamples_CapturedUtc\" ON \"NodeTelemetrySamples\" (\"CapturedUtc\");",
            cancellationToken);
    }

    private Task<int> PruneExpiredTelemetryAsync(CancellationToken cancellationToken)
    {
        var cutoff = timeProvider.GetUtcNow().AddDays(-GetRetentionDays());
        return dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM \"NodeTelemetrySamples\" WHERE \"CapturedUtc\" < {cutoff};",
            cancellationToken);
    }

    private int GetRetentionDays() => Math.Max(1, options.Value.TelemetryRetentionDays);
}