using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SinterServer.Models;
using SinterServer.Services;

namespace SinterServer.Tests;

public sealed class SinterServerFactory : WebApplicationFactory<Program>
{
    public FakeNodeClient FakeNodeClient { get; } = new();
    public FakeServerSelfUpdateCoordinator FakeSelfUpdateCoordinator { get; } = new();

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var tempRoot = Path.Combine(Path.GetTempPath(), "sinter-server-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);

        builder.UseEnvironment("Development");
        builder.UseSetting("SinterServer:DatabasePath", Path.Combine(tempRoot, "server.db"));
        builder.UseSetting("SinterServer:PollIntervalSeconds", "300");
        builder.UseSetting("SinterServer:ServerName", "SinterServer Test");
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<INodeClient>();
            services.AddSingleton<INodeClient>(FakeNodeClient);
            services.RemoveAll<IServerSelfUpdateCoordinator>();
            services.AddSingleton<IServerSelfUpdateCoordinator>(FakeSelfUpdateCoordinator);
        });
    }
}

public sealed class FakeNodeClient : INodeClient
{
    public List<(string Action, string ServiceName)> ServiceActions { get; } = [];

    public Task<NodeStatusResponse> GetStatusAsync(string nodeUrl, CancellationToken cancellationToken)
    {
        _ = nodeUrl;
        _ = cancellationToken;
        return Task.FromResult(new NodeStatusResponse(
            new NodeSnapshot("test-node", "Linux", "x64", ".NET 10", new NodeCapabilities(true, true, true, true, true, "X-Sinter-Key", "ndjson"), new NodeEnvironment(["http://127.0.0.1:5000"], "/apps", "/etc/systemd/system", "/opt/sinter-node", "/var/lib/sinter-node/releases", "sinter-node.service"), "1.0.0", "0d 0h 1m", 1, 0),
            "Online",
            [new NodeServiceInventoryItem("HomeLab.Api.service", "Test service", false, true, false, false, "/etc/systemd/system/HomeLab.Api.service", [])],
            []));
    }

    public Task<RemoteActionResult> ReloadDaemonAsync(string nodeUrl, string apiKey, CancellationToken cancellationToken) => Success("reload", "Requested daemon reload.");
    public Task<RemoteActionResult> StartServiceAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken) => RecordServiceActionAsync("start", serviceName);
    public Task<RemoteActionResult> StopServiceAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken) => RecordServiceActionAsync("stop", serviceName);
    public Task<RemoteActionResult> EnableServiceAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken) => RecordServiceActionAsync("enable", serviceName);
    public Task<RemoteActionResult> DisableServiceAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken) => RecordServiceActionAsync("disable", serviceName);
    public Task<RemoteActionResult> DeployApplicationAsync(string nodeUrl, string apiKey, object request, CancellationToken cancellationToken) => Success("deploy", "Deployment requested.");
    public Task<RemoteActionResult> RestartApplicationServiceAsync(string nodeUrl, string apiKey, string appName, CancellationToken cancellationToken) => Success("restart-app", "Service restart requested.");
    public Task<RemoteActionResult> UninstallApplicationAsync(string nodeUrl, string apiKey, string appName, CancellationToken cancellationToken) => Success("uninstall-app", "Uninstall requested.");
    public Task<RemoteFileView> GetServiceUnitAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken) => Task.FromResult(new RemoteFileView(string.Empty, "Available"));
    public Task<RemoteFileView> GetServiceOverrideAsync(string nodeUrl, string apiKey, string serviceName, CancellationToken cancellationToken) => Task.FromResult(new RemoteFileView(string.Empty, "Available"));
    public Task<RemoteActionResult> UpdateServiceUnitAsync(string nodeUrl, string apiKey, string serviceName, UpdateRemoteFileRequest request, CancellationToken cancellationToken) => Success("update-unit", "Service unit updated.");
    public Task<RemoteActionResult> UpdateServiceOverrideAsync(string nodeUrl, string apiKey, string serviceName, UpdateRemoteFileRequest request, CancellationToken cancellationToken) => Success("update-override", "Service override updated.");

    private Task<RemoteActionResult> RecordServiceActionAsync(string action, string serviceName)
    {
        ServiceActions.Add((action, serviceName));
        return Success(action, $"Service {action} requested.");
    }

    private static Task<RemoteActionResult> Success(string command, string summary)
    {
        return Task.FromResult(new RemoteActionResult("Success", summary, [new RemoteEvent("info", summary, DateTimeOffset.UtcNow, command)]));
    }
}

public sealed class FakeServerSelfUpdateCoordinator : IServerSelfUpdateCoordinator
{
    public List<SelfUpdateRequest> Requests { get; } = [];

    public Task<RemoteActionResult> StartAsync(SelfUpdateRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        Requests.Add(request);
        return Task.FromResult(new RemoteActionResult("Success", "Self-update handoff completed. The server service will restart if the updater succeeds.", [new RemoteEvent("success", "Self-update handoff completed. The server service will restart if the updater succeeds.", DateTimeOffset.UtcNow, "self-update")]));
    }
}