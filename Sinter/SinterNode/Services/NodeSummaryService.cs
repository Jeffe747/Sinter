using System.Reflection;
using System.Runtime.InteropServices;
using Microsoft.Extensions.Options;
using SinterNode.Models;
using SinterNode.Options;

namespace SinterNode.Services;

public interface INodeSummaryService
{
    Task<NodeDashboard> GetDashboardAsync(bool includeApiKey, CancellationToken cancellationToken);
}

public sealed class NodeSummaryService(
    INodeStateStore stateStore,
    IServiceCatalog serviceCatalog,
    IManagedApplicationService managedApplicationService,
    INodeTelemetryCollector telemetryCollector,
    IOptions<NodeOptions> options) : INodeSummaryService
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
    var telemetry = await telemetryCollector.CollectAsync(cancellationToken);
        var process = Environment.ProcessId;
        _ = process;

        return new NodeDashboard(
            Environment.MachineName,
            RuntimeInformation.OSDescription,
            RuntimeInformation.ProcessArchitecture.ToString(),
            RuntimeInformation.FrameworkDescription,
            GetVersionDisplay(),
            GetVersionDetails(),
            FormatUptime(),
            new NodeCapabilities(
                "X-Sinter-Key",
                "ndjson",
                SupportsDotnetDeployments: true,
                SupportsServiceUnitManagement: true,
                SupportsOverrideManagement: true,
                SupportsSelfUpdate: true,
                SupportsRollback: true),
            new NodeEnvironmentInfo(
                ReadListenUrls(),
                options.Value.ManagedAppsRoot,
                options.Value.SystemdUnitDirectory,
                options.Value.NodeInstallRoot,
                options.Value.NodeReleaseRoot,
                options.Value.SelfServiceName),
            snapshot,
            telemetry,
            services,
            managedApps);
    }

    private static string GetVersionDisplay()
    {
        var version = GetAssemblyVersionCore();
        var commit = GetAssemblyCommit();
        return string.IsNullOrWhiteSpace(commit) ? $"v{version}" : $"v{version} · {ShortenCommit(commit)}";
    }

    private static string GetVersionDetails()
    {
        var details = new List<string> { $"Version {GetAssemblyVersionCore()}" };
        var commit = GetAssemblyCommit();
        if (!string.IsNullOrWhiteSpace(commit)) details.Add($"Build commit {commit}");
        return string.Join(" | ", details);
    }

    private static string GetAssemblyVersionCore()
    {
        var informationalVersion = GetInformationalVersion();
        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion.Split('+', 2, StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)[0];
        return typeof(NodeSummaryService).Assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }

    private static string? GetAssemblyCommit()
    {
        var informationalVersion = GetInformationalVersion();
        if (string.IsNullOrWhiteSpace(informationalVersion)) return null;
        var separatorIndex = informationalVersion.IndexOf('+');
        if (separatorIndex < 0 || separatorIndex == informationalVersion.Length - 1) return null;
        return informationalVersion[(separatorIndex + 1)..].Trim();
    }

    private static string? GetInformationalVersion() =>
        typeof(NodeSummaryService).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion?.Trim();

    private static string ShortenCommit(string commit)
    {
        var trimmed = commit.Trim();
        return trimmed.Length <= 8 ? trimmed : trimmed[..8];
    }

    private static string FormatUptime()
    {
        var uptime = TimeSpan.FromMilliseconds(Environment.TickCount64);
        return $"{uptime.Days}d {uptime.Hours}h {uptime.Minutes}m";
    }

    private static string[] ReadListenUrls()
    {
        var urls = Environment.GetEnvironmentVariable("ASPNETCORE_URLS");
        if (string.IsNullOrWhiteSpace(urls))
        {
            return [];
        }

        return urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
}