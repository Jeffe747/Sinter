using System.Runtime.InteropServices;
using SinterNode.Models;

namespace SinterNode.Services;

public interface INodeSummaryService
{
    Task<NodeDashboard> GetDashboardAsync(bool includeApiKey, CancellationToken cancellationToken);
}

public sealed class NodeSummaryService(
    INodeStateStore stateStore,
    IServiceCatalog serviceCatalog,
    IManagedApplicationService managedApplicationService) : INodeSummaryService
{
    public async Task<NodeDashboard> GetDashboardAsync(bool includeApiKey, CancellationToken cancellationToken)
    {
        var snapshot = await stateStore.GetSnapshotAsync(cancellationToken);
        if (!includeApiKey)
        {
            snapshot = snapshot with { ShowApiKey = false, ApiKey = string.Empty };
        }

        var services = await serviceCatalog.ListAsync(snapshot.State.ServicePrefixes, cancellationToken);
        var managedApps = await managedApplicationService.ListAsync(cancellationToken);
        var process = Environment.ProcessId;
        _ = process;

        return new NodeDashboard(
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription,
            typeof(Program).Assembly.GetName().Version?.ToString() ?? "0.0.0",
            FormatUptime(),
            snapshot,
            services,
            managedApps);
    }

    private static string FormatUptime()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m";
    }
}