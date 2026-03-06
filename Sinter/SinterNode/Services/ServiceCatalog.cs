using Microsoft.Extensions.Options;
using SinterNode.Models;
using SinterNode.Options;

namespace SinterNode.Services;

public interface IServiceCatalog
{
    Task<IReadOnlyList<ServiceSummary>> ListAsync(IReadOnlyCollection<string> prefixes, CancellationToken cancellationToken);
    Task<string?> ReadUnitFileAsync(string serviceName, CancellationToken cancellationToken);
    Task<string?> ReadOverrideFileAsync(string serviceName, CancellationToken cancellationToken);
    Task WriteUnitFileAsync(string serviceName, string content, bool allowOverwriteUnmanaged, CancellationToken cancellationToken);
    Task WriteManagedUnitFileAsync(string serviceName, string content, CancellationToken cancellationToken);
    Task WriteOverrideFileAsync(string serviceName, string content, CancellationToken cancellationToken);
    IAsyncEnumerable<OperationEvent> StartServiceAsync(string serviceName, CancellationToken cancellationToken);
    IAsyncEnumerable<OperationEvent> RestartServiceAsync(string serviceName, CancellationToken cancellationToken);
    IAsyncEnumerable<OperationEvent> StopServiceAsync(string serviceName, CancellationToken cancellationToken);
    IAsyncEnumerable<OperationEvent> EnableServiceAsync(string serviceName, CancellationToken cancellationToken);
    IAsyncEnumerable<OperationEvent> DisableServiceAsync(string serviceName, CancellationToken cancellationToken);
    bool IsManagedContent(string content);
}

public sealed class ServiceCatalog(
    IOptions<NodeOptions> options,
    ISystemServiceManager systemServiceManager,
    ISystemdOverrideValidator overrideValidator) : IServiceCatalog
{
    private const string ManagedMarker = "# Managed by SinterNode";

    public async Task<IReadOnlyList<ServiceSummary>> ListAsync(IReadOnlyCollection<string> prefixes, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (prefixes.Count == 0 || !Directory.Exists(options.Value.SystemdUnitDirectory))
        {
            return [];
        }

        var files = Directory.EnumerateFiles(options.Value.SystemdUnitDirectory, "*.service", SearchOption.TopDirectoryOnly)
            .Where(file => prefixes.Any(prefix => Path.GetFileNameWithoutExtension(file).StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(static file => file, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var results = new List<ServiceSummary>(files.Length);
        foreach (var file in files)
        {
            var content = await File.ReadAllTextAsync(file, cancellationToken);
            var serviceName = Path.GetFileName(file);
            var overridePath = GetOverridePath(serviceName);
            results.Add(new ServiceSummary(
                serviceName,
                ExtractDescription(content),
                IsManagedContent(content),
                File.Exists(overridePath),
                file,
                File.Exists(overridePath)
                    ? overrideValidator.GetWarnings(await File.ReadAllTextAsync(overridePath, cancellationToken))
                    : []));
        }

        return results;
    }

    public Task<string?> ReadUnitFileAsync(string serviceName, CancellationToken cancellationToken)
    {
        var path = GetUnitPath(serviceName);
        return ReadIfExistsAsync(path, cancellationToken);
    }

    public Task<string?> ReadOverrideFileAsync(string serviceName, CancellationToken cancellationToken)
    {
        return ReadIfExistsAsync(GetOverridePath(serviceName), cancellationToken);
    }

    public async Task WriteUnitFileAsync(string serviceName, string content, bool allowOverwriteUnmanaged, CancellationToken cancellationToken)
    {
        var path = GetUnitPath(serviceName);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        if (File.Exists(path) && !allowOverwriteUnmanaged)
        {
            var existing = await File.ReadAllTextAsync(path, cancellationToken);
            if (!IsManagedContent(existing))
            {
                throw new InvalidOperationException("Refusing to overwrite an unmanaged service file without explicit permission.");
            }
        }

        await File.WriteAllTextAsync(path, content, cancellationToken);
    }

    public Task WriteManagedUnitFileAsync(string serviceName, string content, CancellationToken cancellationToken)
    {
        if (!content.Contains(ManagedMarker, StringComparison.Ordinal))
        {
            content = $"{ManagedMarker}{Environment.NewLine}{content}";
        }

        return WriteUnitFileAsync(serviceName, content, allowOverwriteUnmanaged: true, cancellationToken);
    }

    public async Task WriteOverrideFileAsync(string serviceName, string content, CancellationToken cancellationToken)
    {
        var path = GetOverridePath(serviceName);
        overrideValidator.Validate(content);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllTextAsync(path, content, cancellationToken);
    }

    public async IAsyncEnumerable<OperationEvent> StartServiceAsync(string serviceName, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ValidateServiceName(serviceName);
        yield return OperationEvent.Info($"Starting {serviceName}.", serviceName);
        await systemServiceManager.StartAsync(serviceName, cancellationToken);

        if (await systemServiceManager.IsActiveAsync(serviceName, cancellationToken))
        {
            yield return OperationEvent.Success($"{serviceName} is active.", serviceName);
        }
        else
        {
            yield return OperationEvent.Error($"{serviceName} did not report an active state after start.", serviceName);
        }
    }

    public async IAsyncEnumerable<OperationEvent> RestartServiceAsync(string serviceName, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ValidateServiceName(serviceName);
        yield return OperationEvent.Info($"Reloading systemd and restarting {serviceName}.", serviceName);
        await systemServiceManager.DaemonReloadAsync(cancellationToken);
        await systemServiceManager.RestartAsync(serviceName, cancellationToken);

        if (await systemServiceManager.IsActiveAsync(serviceName, cancellationToken))
        {
            yield return OperationEvent.Success($"{serviceName} is active.", serviceName);
        }
        else
        {
            yield return OperationEvent.Error($"{serviceName} did not report an active state after restart.", serviceName);
        }
    }

    public async IAsyncEnumerable<OperationEvent> StopServiceAsync(string serviceName, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ValidateServiceName(serviceName);
        yield return OperationEvent.Info($"Stopping {serviceName}.", serviceName);
        await systemServiceManager.StopAsync(serviceName, cancellationToken);

        if (await systemServiceManager.IsActiveAsync(serviceName, cancellationToken))
        {
            yield return OperationEvent.Error($"{serviceName} still reports an active state after stop.", serviceName);
        }
        else
        {
            yield return OperationEvent.Success($"{serviceName} is stopped.", serviceName);
        }
    }

    public async IAsyncEnumerable<OperationEvent> EnableServiceAsync(string serviceName, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ValidateServiceName(serviceName);
        yield return OperationEvent.Info($"Enabling {serviceName}.", serviceName);
        await systemServiceManager.EnableAsync(serviceName, cancellationToken);
        yield return OperationEvent.Success($"{serviceName} is enabled.", serviceName);
    }

    public async IAsyncEnumerable<OperationEvent> DisableServiceAsync(string serviceName, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ValidateServiceName(serviceName);
        yield return OperationEvent.Info($"Disabling {serviceName}.", serviceName);
        await systemServiceManager.DisableAsync(serviceName, cancellationToken);
        yield return OperationEvent.Success($"{serviceName} is disabled.", serviceName);
    }

    public bool IsManagedContent(string content)
    {
        return content.Contains(ManagedMarker, StringComparison.Ordinal);
    }

    private static Task<string?> ReadIfExistsAsync(string path, CancellationToken cancellationToken)
    {
        return File.Exists(path)
            ? File.ReadAllTextAsync(path, cancellationToken).ContinueWith(static task => (string?)task.Result, cancellationToken)
            : Task.FromResult<string?>(null);
    }

    private string GetUnitPath(string serviceName)
    {
        ValidateServiceName(serviceName);
        return Path.Combine(options.Value.SystemdUnitDirectory, serviceName);
    }

    private string GetOverridePath(string serviceName)
    {
        ValidateServiceName(serviceName);
        return Path.Combine(options.Value.SystemdUnitDirectory, $"{serviceName}.d", "override.conf");
    }

    private static string ExtractDescription(string content)
    {
        foreach (var line in content.Split('\n', StringSplitOptions.TrimEntries))
        {
            if (line.StartsWith("Description=", StringComparison.OrdinalIgnoreCase))
            {
                return line["Description=".Length..];
            }
        }

        return string.Empty;
    }

    private static void ValidateServiceName(string serviceName)
    {
        if (string.IsNullOrWhiteSpace(serviceName) || serviceName.Contains(Path.DirectorySeparatorChar) || !serviceName.EndsWith(".service", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Service name must be a .service file name.");
        }
    }
}