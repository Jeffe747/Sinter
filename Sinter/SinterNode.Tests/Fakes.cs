using Microsoft.Extensions.Options;
using SinterNode.Models;
using SinterNode.Options;
using SinterNode.Services;

namespace SinterNode.Tests;

internal sealed class FakeProcessRunner(Action<ProcessRequest>? onRun = null) : IProcessRunner
{
    public List<ProcessRequest> StreamedRequests { get; } = [];
    public List<ProcessRequest> RunRequests { get; } = [];
    public int NextExitCode { get; set; }

    public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        RunRequests.Add(request);
        onRun?.Invoke(request);
        return Task.FromResult(new ProcessResult(NextExitCode, NextExitCode == 0 ? "active" : string.Empty, NextExitCode == 0 ? string.Empty : "failed"));
    }

    public async IAsyncEnumerable<ProcessOutputLine> StreamAsync(ProcessRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        StreamedRequests.Add(request);
        onRun?.Invoke(request);
        await Task.Yield();
        yield return new ProcessOutputLine($"Executed {request.FileName} {request.Arguments}", false);
        yield return new ProcessOutputLine($"Process exited with code {NextExitCode}.", NextExitCode != 0, IsTerminal: true, ExitCode: NextExitCode);
    }
}

internal sealed class FakeSystemServiceManager : ISystemServiceManager
{
    public bool ActiveState { get; set; } = true;
    public bool EnabledState { get; set; } = true;
    public List<string> StartedServices { get; } = [];
    public List<string> RestartedServices { get; } = [];
    public List<string> StoppedServices { get; } = [];
    public List<string> EnabledServices { get; } = [];
    public List<string> DisabledServices { get; } = [];

    public Task DaemonReloadAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.CompletedTask;
    }

    public Task StartAsync(string serviceName, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        StartedServices.Add(serviceName);
        ActiveState = true;
        return Task.CompletedTask;
    }

    public Task RestartAsync(string serviceName, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        RestartedServices.Add(serviceName);
        return Task.CompletedTask;
    }

    public Task EnableAsync(string serviceName, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        EnabledServices.Add(serviceName);
        EnabledState = true;
        return Task.CompletedTask;
    }

    public Task StopAsync(string serviceName, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        StoppedServices.Add(serviceName);
        ActiveState = false;
        return Task.CompletedTask;
    }

    public Task DisableAsync(string serviceName, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        DisabledServices.Add(serviceName);
        EnabledState = false;
        return Task.CompletedTask;
    }

    public Task<bool> IsActiveAsync(string serviceName, CancellationToken cancellationToken)
    {
        _ = serviceName;
        _ = cancellationToken;
        return Task.FromResult(ActiveState);
    }

    public Task<bool> IsEnabledAsync(string serviceName, CancellationToken cancellationToken)
    {
        _ = serviceName;
        _ = cancellationToken;
        return Task.FromResult(EnabledState);
    }
}

internal sealed class FakeReleasePointerManager : IReleasePointerManager
{
    private readonly Dictionary<string, string> targets = new(StringComparer.OrdinalIgnoreCase);

    public Task<string?> GetCurrentTargetAsync(string pointerPath, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(targets.TryGetValue(pointerPath, out var value) ? value : null);
    }

    public Task PointToAsync(string pointerPath, string targetPath, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        targets[pointerPath] = targetPath;
        return Task.CompletedTask;
    }

    public void Seed(string pointerPath, string targetPath)
    {
        targets[pointerPath] = targetPath;
    }

    public string? Read(string pointerPath)
    {
        return targets.TryGetValue(pointerPath, out var value) ? value : null;
    }
}

internal static class TestOptions
{
    public static IOptions<NodeOptions> Create(TestWorkspace workspace)
    {
        return Microsoft.Extensions.Options.Options.Create(new NodeOptions
        {
            StateFilePath = workspace.GetPath("config", "node-state.json"),
            ApiKeyFilePath = workspace.GetPath("config", "client_secret"),
            ManagedAppsRoot = workspace.GetPath("apps"),
            NodeInstallRoot = workspace.GetPath("node-install"),
            NodeReleaseRoot = workspace.GetPath("node-release"),
            SystemdUnitDirectory = workspace.GetPath("systemd"),
            DotnetPath = "dotnet",
            SelfServiceName = "sinter-node.service",
            SelfProjectPath = "Sinter/SinterNode/SinterNode.csproj",
            DefaultSourceRepository = "https://github.com/Jeffe747/Sinter.git",
            RetainedReleaseCount = 3
        });
    }
}

internal sealed class FakeSelfUpdateCoordinator : ISelfUpdateCoordinator
{
    public List<SelfUpdateRequest> Requests { get; } = [];

    public async IAsyncEnumerable<OperationEvent> StartAsync(SelfUpdateRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        Requests.Add(request);
        await Task.Yield();
        yield return OperationEvent.Info("Handoff requested.", "self-update");
        yield return OperationEvent.Success("Updater handoff completed.", "self-update");
    }
}

internal sealed class FakeNodeTelemetryCollector : INodeTelemetryCollector
{
    public NodeTelemetry Telemetry { get; set; } = new(
        8,
        61.3,
        2.4,
        1.8,
        1.5,
        16L * 1024 * 1024 * 1024,
        6L * 1024 * 1024 * 1024,
        62.5,
        "/",
        512L * 1024 * 1024 * 1024,
        180L * 1024 * 1024 * 1024,
        64.8,
        4,
        ["22/tcp", "80/tcp", "443/tcp", "5000/tcp"],
        ["Load is climbing quickly."]);

    public Task<NodeTelemetry> CollectAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        return Task.FromResult(Telemetry);
    }
}