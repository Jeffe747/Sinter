using System.Text.Json;
using Microsoft.Extensions.Options;
using SinterNode.Models;
using SinterNode.Options;

namespace SinterNode.Services;

public interface IManagedApplicationService
{
    Task<IReadOnlyList<ManagedApplicationState>> ListAsync(CancellationToken cancellationToken);
    IAsyncEnumerable<OperationEvent> DeployAsync(DeployApplicationRequest request, CancellationToken cancellationToken);
    IAsyncEnumerable<OperationEvent> RestartAsync(string appName, CancellationToken cancellationToken);
    IAsyncEnumerable<OperationEvent> UninstallAsync(string appName, CancellationToken cancellationToken);
    IAsyncEnumerable<OperationEvent> SelfUpdateAsync(SelfUpdateRequest request, CancellationToken cancellationToken);
}

public sealed class ManagedApplicationService(
    IOptions<NodeOptions> options,
    IProcessRunner processRunner,
    ISystemServiceManager systemServiceManager,
    IServiceCatalog serviceCatalog,
    ISelfUpdateCoordinator selfUpdateCoordinator,
    IReleasePointerManager releasePointerManager,
    IOperationLockProvider operationLockProvider,
    TimeProvider timeProvider,
    ILogger<ManagedApplicationService> logger) : IManagedApplicationService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    public async Task<IReadOnlyList<ManagedApplicationState>> ListAsync(CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        if (!Directory.Exists(options.Value.ManagedAppsRoot))
        {
            return [];
        }

        var manifests = Directory.EnumerateDirectories(options.Value.ManagedAppsRoot)
            .Select(static path => Path.Combine(path, "manifest.json"))
            .Where(File.Exists)
            .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var items = new List<ManagedApplicationState>(manifests.Length);
        foreach (var manifestPath in manifests)
        {
            var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
            var state = JsonSerializer.Deserialize<ManagedApplicationState>(json, SerializerOptions);
            if (state is not null)
            {
                var appRoot = Path.GetDirectoryName(manifestPath)!;
                var releasesRoot = Path.Combine(appRoot, "releases");
                var releaseCount = Directory.Exists(releasesRoot)
                    ? Directory.EnumerateDirectories(releasesRoot).Count()
                    : 0;
                items.Add(state with
                {
                    ReleaseCount = releaseCount,
                    CurrentReleaseExists = !string.IsNullOrWhiteSpace(state.CurrentRelease) && Directory.Exists(state.CurrentRelease),
                    LastSuccessfulReleaseExists = !string.IsNullOrWhiteSpace(state.LastSuccessfulRelease) && Directory.Exists(state.LastSuccessfulRelease),
                    AppRoot = appRoot,
                    ReleasesRoot = releasesRoot
                });
            }
        }

        return items;
    }

    public IAsyncEnumerable<OperationEvent> DeployAsync(DeployApplicationRequest request, CancellationToken cancellationToken)
        => ExecuteOperationSafelyAsync(request.AppName, token => DeployCoreAsync(request, token), cancellationToken);

    public IAsyncEnumerable<OperationEvent> RestartAsync(string appName, CancellationToken cancellationToken)
        => ExecuteOperationSafelyAsync(appName, token => RestartCoreAsync(appName, token), cancellationToken);

    public IAsyncEnumerable<OperationEvent> UninstallAsync(string appName, CancellationToken cancellationToken)
        => ExecuteOperationSafelyAsync(appName, token => UninstallCoreAsync(appName, token), cancellationToken);

    public IAsyncEnumerable<OperationEvent> SelfUpdateAsync(SelfUpdateRequest request, CancellationToken cancellationToken)
        => ExecuteOperationSafelyAsync("self-update", token => SelfUpdateCoreAsync(request, token), cancellationToken);

    private async IAsyncEnumerable<OperationEvent> ExecuteOperationSafelyAsync(
        string scope,
        Func<CancellationToken, IAsyncEnumerable<OperationEvent>> operation,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        OperationEvent? errorEvent = null;
        await using var enumerator = operation(cancellationToken).GetAsyncEnumerator(cancellationToken);
        while (true)
        {
            bool hasNext;
            try
            {
                hasNext = await enumerator.MoveNextAsync();
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Managed operation failed for {Scope}.", scope);
                errorEvent = OperationEvent.Error(ex.Message, scope);
                break;
            }

            if (!hasNext)
            {
                break;
            }

            yield return enumerator.Current;
        }

        if (errorEvent is not null)
        {
            yield return errorEvent;
        }
    }

    private async IAsyncEnumerable<OperationEvent> DeployCoreAsync(DeployApplicationRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var appLock = await operationLockProvider.AcquireAsync($"app:{request.AppName}", cancellationToken);
        yield return OperationEvent.Info($"Starting deployment for {request.AppName}.", request.AppName);

        var appRoot = Path.Combine(options.Value.ManagedAppsRoot, request.AppName);
        var repoRoot = Path.Combine(appRoot, "repo-cache");
        var releasesRoot = Path.Combine(appRoot, "releases");
        var currentPointerPath = Path.Combine(appRoot, "current");
        var timestamp = timeProvider.GetUtcNow().ToString("yyyyMMdd-HHmmss");
        var releaseRoot = Path.Combine(releasesRoot, timestamp);
        var publishRoot = Path.Combine(releaseRoot, "publish");
        var serviceName = string.IsNullOrWhiteSpace(request.ServiceName) ? $"{request.AppName}.service" : request.ServiceName.Trim();
        var previousTarget = await releasePointerManager.GetCurrentTargetAsync(currentPointerPath, cancellationToken);

        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(releasesRoot);
        Directory.CreateDirectory(releaseRoot);

        var repoUrl = InjectToken(request.RepoUrl, request.Token);
        if (Directory.Exists(Path.Combine(repoRoot, ".git")))
        {
            yield return OperationEvent.Info("Refreshing existing repository cache.", request.AppName);
            await foreach (var evt in StreamCommandAsync(new ProcessRequest("git", "fetch origin", repoRoot), request.AppName, cancellationToken))
            {
                yield return evt;
            }

            await foreach (var evt in StreamCommandAsync(new ProcessRequest("git", $"reset --hard origin/{request.Branch}", repoRoot), request.AppName, cancellationToken))
            {
                yield return evt;
            }
        }
        else
        {
            ResetDirectory(repoRoot);
            Directory.CreateDirectory(repoRoot);
            yield return OperationEvent.Info("Cloning repository.", request.AppName);
            await foreach (var evt in StreamCommandAsync(new ProcessRequest("git", $"clone -b {request.Branch} {repoUrl} .", repoRoot), request.AppName, cancellationToken))
            {
                yield return evt;
            }
        }

        var publishArguments = string.IsNullOrWhiteSpace(request.ProjectPath)
            ? $"publish -c Release -o \"{publishRoot}\""
            : $"publish \"{request.ProjectPath}\" -c Release -o \"{publishRoot}\"";

        yield return OperationEvent.Info("Publishing application.", request.AppName);
        await foreach (var evt in StreamCommandAsync(new ProcessRequest(options.Value.DotnetPath, publishArguments, repoRoot), request.AppName, cancellationToken))
        {
            yield return evt;
        }

        var dllName = PublishedArtifactSelector.SelectDllName(publishRoot, request.AppName, request.ProjectPath);
        yield return OperationEvent.Info($"Selected {dllName} for ExecStart.", request.AppName);

        var unitContent = BuildManagedServiceFile(serviceName, currentPointerPath, dllName);
        await releasePointerManager.PointToAsync(currentPointerPath, publishRoot, cancellationToken);
        await serviceCatalog.WriteManagedUnitFileAsync(serviceName, unitContent, cancellationToken);
        await systemServiceManager.DaemonReloadAsync(cancellationToken);
        await systemServiceManager.EnableAsync(serviceName, cancellationToken);

        OperationEvent? completionEvent = null;
        List<OperationEvent>? failureEvents = null;
        try
        {
            await systemServiceManager.RestartAsync(serviceName, cancellationToken);
            if (!await systemServiceManager.IsActiveAsync(serviceName, cancellationToken))
            {
                throw new InvalidOperationException($"{serviceName} did not become active after restart.");
            }

            var state = new ManagedApplicationState(
                request.AppName,
                request.RepoUrl,
                request.Branch,
                request.ProjectPath,
                serviceName,
                publishRoot,
                publishRoot,
                timeProvider.GetUtcNow(),
                ReleaseCount: Directory.EnumerateDirectories(releasesRoot).Count(),
                CurrentReleaseExists: true,
                LastSuccessfulReleaseExists: true,
                AppRoot: appRoot,
                ReleasesRoot: releasesRoot);

            await WriteManifestAsync(appRoot, state, cancellationToken);
            CleanupOldReleases(releasesRoot, publishRoot);
            completionEvent = OperationEvent.Success($"Deployment completed for {request.AppName}.", request.AppName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Deployment failed for {AppName}; attempting rollback.", request.AppName);
            failureEvents = [OperationEvent.Warning($"Deployment failed: {ex.Message}", request.AppName)];
            if (!string.IsNullOrWhiteSpace(previousTarget) && Directory.Exists(previousTarget))
            {
                await releasePointerManager.PointToAsync(currentPointerPath, previousTarget, cancellationToken);
                await systemServiceManager.RestartAsync(serviceName, cancellationToken);
                failureEvents.Add(OperationEvent.Warning("Rolled back to the previous successful release.", request.AppName));
            }
            else
            {
                failureEvents.Add(OperationEvent.Error("No previous successful release was available for rollback.", request.AppName));
            }
        }

        if (failureEvents is not null)
        {
            foreach (var failureEvent in failureEvents)
            {
                yield return failureEvent;
            }
        }

        if (completionEvent is not null)
        {
            yield return completionEvent;
        }
    }

    private async IAsyncEnumerable<OperationEvent> RestartCoreAsync(string appName, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var manifest = await ReadManifestAsync(appName, cancellationToken);
        if (manifest is null)
        {
            yield return OperationEvent.Error($"No managed application metadata exists for {appName}.", appName);
            yield break;
        }

        await foreach (var evt in serviceCatalog.RestartServiceAsync(manifest.ServiceName, cancellationToken))
        {
            yield return evt;
        }
    }

    private async IAsyncEnumerable<OperationEvent> UninstallCoreAsync(string appName, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var appLock = await operationLockProvider.AcquireAsync($"app:{appName}", cancellationToken);
        var manifest = await ReadManifestAsync(appName, cancellationToken);
        var serviceName = manifest?.ServiceName ?? $"{appName}.service";
        var appRoot = Path.Combine(options.Value.ManagedAppsRoot, appName);
        var unitPath = Path.Combine(options.Value.SystemdUnitDirectory, serviceName);
        var overrideDirectory = Path.Combine(options.Value.SystemdUnitDirectory, $"{serviceName}.d");
        var hasKnownArtifacts = Directory.Exists(appRoot) || File.Exists(unitPath) || Directory.Exists(overrideDirectory);

        if (manifest is null && !hasKnownArtifacts)
        {
            yield return OperationEvent.Success($"{appName} was already removed.", appName);
            yield break;
        }

        yield return OperationEvent.Info($"Stopping and uninstalling {appName}.", appName);
        await foreach (var evt in StopAndDisableBestEffortAsync(serviceName, appName, cancellationToken))
        {
            yield return evt;
        }

        if (File.Exists(unitPath))
        {
            File.Delete(unitPath);
        }

        if (Directory.Exists(overrideDirectory))
        {
            Directory.Delete(overrideDirectory, recursive: true);
        }

        await systemServiceManager.DaemonReloadAsync(cancellationToken);

        if (Directory.Exists(appRoot))
        {
            Directory.Delete(appRoot, recursive: true);
        }

        yield return OperationEvent.Success($"{appName} was removed.", appName);
    }

    private async IAsyncEnumerable<OperationEvent> SelfUpdateCoreAsync(SelfUpdateRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await using var updateLock = await operationLockProvider.AcquireAsync("node:self-update", cancellationToken);
        await foreach (var evt in selfUpdateCoordinator.StartAsync(request, cancellationToken))
        {
            yield return evt;
        }
    }

    private async Task<ManagedApplicationState?> ReadManifestAsync(string appName, CancellationToken cancellationToken)
    {
        var appRoot = Path.Combine(options.Value.ManagedAppsRoot, appName);
        var manifestPath = Path.Combine(appRoot, "manifest.json");
        if (!File.Exists(manifestPath))
        {
            return null;
        }

        var json = await File.ReadAllTextAsync(manifestPath, cancellationToken);
        return JsonSerializer.Deserialize<ManagedApplicationState>(json, SerializerOptions);
    }

    private async Task WriteManifestAsync(string appRoot, ManagedApplicationState state, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(appRoot);
        var manifestPath = Path.Combine(appRoot, "manifest.json");
        var json = JsonSerializer.Serialize(state, SerializerOptions);
        await File.WriteAllTextAsync(manifestPath, json, cancellationToken);
    }

    private async IAsyncEnumerable<OperationEvent> StopAndDisableBestEffortAsync(string serviceName, string appName, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        string? stopWarning = null;
        try
        {
            await systemServiceManager.StopAsync(serviceName, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Unable to stop service {ServiceName} during uninstall for {AppName}; continuing cleanup.", serviceName, appName);
            stopWarning = $"Unable to stop {serviceName}; continuing cleanup: {ex.Message}";
        }

        if (!string.IsNullOrWhiteSpace(stopWarning))
        {
            yield return OperationEvent.Warning(stopWarning, appName);
        }

        string? disableWarning = null;
        try
        {
            await systemServiceManager.DisableAsync(serviceName, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogInformation(ex, "Unable to disable service {ServiceName} during uninstall for {AppName}; continuing cleanup.", serviceName, appName);
            disableWarning = $"Unable to disable {serviceName}; continuing cleanup: {ex.Message}";
        }

        if (!string.IsNullOrWhiteSpace(disableWarning))
        {
            yield return OperationEvent.Warning(disableWarning, appName);
        }
    }

    private async IAsyncEnumerable<OperationEvent> StreamCommandAsync(ProcessRequest request, string scope, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
    {
        int? exitCode = null;
        string? lastOutputLine = null;
        string? lastErrorLine = null;
        await foreach (var line in processRunner.StreamAsync(request, cancellationToken))
        {
            if (line.IsTerminal)
            {
                exitCode = line.ExitCode;
                continue;
            }

            if (!string.IsNullOrWhiteSpace(line.Text))
            {
                lastOutputLine = line.Text.Trim();
                if (line.Text.Contains(": error ", StringComparison.OrdinalIgnoreCase))
                {
                    lastErrorLine = line.Text.Trim();
                }
            }

            yield return OperationEvent.CommandOutput(line.Text, $"{request.FileName} {request.Arguments}", scope);
        }

        if (exitCode.GetValueOrDefault() != 0)
        {
            throw new InvalidOperationException(BuildCommandFailureMessage(request, exitCode, lastErrorLine, lastOutputLine));
        }
    }

    private static string BuildCommandFailureMessage(ProcessRequest request, int? exitCode, string? lastErrorLine, string? lastOutputLine)
    {
        var command = $"{request.FileName} {request.Arguments}";
        var detail = !string.IsNullOrWhiteSpace(lastErrorLine)
            ? lastErrorLine
            : !string.IsNullOrWhiteSpace(lastOutputLine)
                ? lastOutputLine
                : null;

        return string.IsNullOrWhiteSpace(detail)
            ? $"Command failed with exit code {exitCode.GetValueOrDefault()}: {command}"
            : $"Command failed with exit code {exitCode.GetValueOrDefault()}: {detail}";
    }

    private string BuildManagedServiceFile(string serviceName, string currentPointerPath, string dllName)
    {
        return $"""
[Unit]
Description={serviceName} managed by SinterNode
After=network.target

[Service]
WorkingDirectory={currentPointerPath}
ExecStart={options.Value.DotnetPath} {Path.Combine(currentPointerPath, dllName)}
Restart=always
RestartSec=10
SyslogIdentifier={Path.GetFileNameWithoutExtension(serviceName)}
User=root
Environment=ASPNETCORE_ENVIRONMENT=Production

[Install]
WantedBy=multi-user.target
""";
    }

    private void CleanupOldReleases(string releasesRoot, string keepPublishRoot)
    {
        if (!Directory.Exists(releasesRoot))
        {
            return;
        }

        var keepReleaseRoot = Directory.GetParent(keepPublishRoot)?.FullName;
        var releases = Directory.EnumerateDirectories(releasesRoot)
            .OrderByDescending(static path => path, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        foreach (var release in releases.Skip(options.Value.RetainedReleaseCount))
        {
            if (string.Equals(release, keepReleaseRoot, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Directory.Delete(release, recursive: true);
        }
    }

    private static string InjectToken(string repoUrl, string? token)
    {
        if (string.IsNullOrWhiteSpace(token) || !repoUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return repoUrl;
        }

        return repoUrl.Insert("https://".Length, $"oauth2:{token}@");
    }

    private static void ResetDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, recursive: true);
        }
    }

}

public static class PublishedArtifactSelector
{
    public static string SelectDllName(string publishRoot, string appName, string? projectPath)
    {
        if (!Directory.Exists(publishRoot))
        {
            throw new InvalidOperationException($"Publish output directory {publishRoot} does not exist.");
        }

        var dllFiles = Directory.EnumerateFiles(publishRoot, "*.dll", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(static fileName => !string.IsNullOrWhiteSpace(fileName))
            .Cast<string>()
            .ToArray();

        if (!string.IsNullOrWhiteSpace(projectPath))
        {
            var preferred = $"{Path.GetFileNameWithoutExtension(projectPath)}.dll";
            if (dllFiles.Contains(preferred, StringComparer.OrdinalIgnoreCase))
            {
                return preferred;
            }
        }

        var appNameDll = $"{appName}.dll";
        if (dllFiles.Contains(appNameDll, StringComparer.OrdinalIgnoreCase))
        {
            return appNameDll;
        }

        var userAssemblies = dllFiles.Where(static fileName => !fileName.StartsWith("Microsoft.", StringComparison.OrdinalIgnoreCase) &&
                                                               !fileName.StartsWith("System.", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (userAssemblies.Length == 1)
        {
            return userAssemblies[0];
        }

        throw new InvalidOperationException("Unable to determine the application DLL for ExecStart.");
    }
}