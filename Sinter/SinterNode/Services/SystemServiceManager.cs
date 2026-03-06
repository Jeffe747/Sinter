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
        var result = await processRunner.RunAsync(new ProcessRequest("systemctl", $"is-active {serviceName}", "/"), cancellationToken);
        return result.ExitCode == 0 && result.StandardOutput.Contains("active", StringComparison.OrdinalIgnoreCase);
    }

    public async Task<bool> IsEnabledAsync(string serviceName, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(new ProcessRequest("systemctl", $"is-enabled {serviceName}", "/"), cancellationToken);
        if (result.ExitCode != 0)
        {
            return false;
        }

        return result.StandardOutput.Contains("enabled", StringComparison.OrdinalIgnoreCase)
            || result.StandardOutput.Contains("static", StringComparison.OrdinalIgnoreCase)
            || result.StandardOutput.Contains("indirect", StringComparison.OrdinalIgnoreCase)
            || result.StandardOutput.Contains("generated", StringComparison.OrdinalIgnoreCase)
            || result.StandardOutput.Contains("alias", StringComparison.OrdinalIgnoreCase);
    }

    private async Task EnsureSuccessAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        var result = await processRunner.RunAsync(request, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException($"Command failed: {request.FileName} {request.Arguments} {Environment.NewLine}{result.StandardError}");
        }
    }
}