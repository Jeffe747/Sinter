namespace SinterNode.Services;

public interface ISystemServiceManager
{
    Task DaemonReloadAsync(CancellationToken cancellationToken);
    Task StartAsync(string serviceName, CancellationToken cancellationToken);
    Task RestartAsync(string serviceName, CancellationToken cancellationToken);
    Task EnableAsync(string serviceName, CancellationToken cancellationToken);
    Task StopAsync(string serviceName, CancellationToken cancellationToken);
    Task DisableAsync(string serviceName, CancellationToken cancellationToken);
    Task<bool> IsActiveAsync(string serviceName, CancellationToken cancellationToken);
    Task<bool> IsEnabledAsync(string serviceName, CancellationToken cancellationToken);
}

public sealed class SystemServiceManager(IProcessRunner processRunner) : ISystemServiceManager
{
    private static readonly string[] StartedStates = ["active", "activating", "reloading"];
    private static readonly string[] EnabledStates = ["enabled", "static", "indirect", "generated", "alias", "linked", "linked-runtime", "enabled-runtime"];

    public Task DaemonReloadAsync(CancellationToken cancellationToken) =>
        EnsureSuccessAsync(new ProcessRequest("systemctl", "daemon-reload", "/"), cancellationToken);

    public Task StartAsync(string serviceName, CancellationToken cancellationToken) =>
        EnsureSuccessAsync(new ProcessRequest("systemctl", $"start {serviceName}", "/"), cancellationToken);

    public Task RestartAsync(string serviceName, CancellationToken cancellationToken) =>
        EnsureSuccessAsync(new ProcessRequest("systemctl", $"restart {serviceName}", "/"), cancellationToken);

    public Task EnableAsync(string serviceName, CancellationToken cancellationToken) =>
        EnsureSuccessAsync(new ProcessRequest("systemctl", $"enable {serviceName}", "/"), cancellationToken);

    public Task StopAsync(string serviceName, CancellationToken cancellationToken) =>
        EnsureSuccessAsync(new ProcessRequest("systemctl", $"stop {serviceName}", "/"), cancellationToken);

    public Task DisableAsync(string serviceName, CancellationToken cancellationToken) =>
        EnsureSuccessAsync(new ProcessRequest("systemctl", $"disable {serviceName}", "/"), cancellationToken);

    public async Task<bool> IsActiveAsync(string serviceName, CancellationToken cancellationToken)
    {
        var activeState = await ReadUnitPropertyAsync(serviceName, "ActiveState", cancellationToken);
        return StartedStates.Contains(activeState, StringComparer.OrdinalIgnoreCase);
    }

    public async Task<bool> IsEnabledAsync(string serviceName, CancellationToken cancellationToken)
    {
        var unitFileState = await ReadUnitPropertyAsync(serviceName, "UnitFileState", cancellationToken);
        return EnabledStates.Contains(unitFileState, StringComparer.OrdinalIgnoreCase);
    }

    private async Task EnsureSuccessAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(request, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command failed: {request.FileName} {request.Arguments} {Environment.NewLine}{result.StandardError}");
        }
    }

    private async Task<string> ReadUnitPropertyAsync(string serviceName, string propertyName, CancellationToken cancellationToken)
    {
        var request = new ProcessRequest("systemctl", $"show {serviceName} --property={propertyName} --value", "/");
        var result = await processRunner.RunAsync(request, cancellationToken);
        if (result.ExitCode != 0)
        {
            return string.Empty;
        }

        return result.StandardOutput.Trim();
    }
}