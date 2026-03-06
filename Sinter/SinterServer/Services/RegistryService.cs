using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SinterServer.Data;
using SinterServer.Data.Entities;
using SinterServer.Models;
using SinterServer.Options;

namespace SinterServer.Services;

public interface IRegistryService
{
    Task<ServerDashboard> GetDashboardAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<NodeListItem>> GetNodesAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<GitCredentialListItem>> GetAuthUsersAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<ApplicationListItem>> GetApplicationsAsync(CancellationToken cancellationToken);
    Task<NodeListItem> CreateNodeAsync(UpsertNodeRequest request, CancellationToken cancellationToken);
    Task<NodeListItem> UpdateNodeAsync(Guid nodeId, UpsertNodeRequest request, CancellationToken cancellationToken);
    Task DeleteNodeAsync(Guid nodeId, CancellationToken cancellationToken);
    Task<NodeListItem> RefreshNodeAsync(Guid nodeId, CancellationToken cancellationToken);
    Task<RemoteActionResult> ReloadDaemonAsync(Guid nodeId, CancellationToken cancellationToken);
    Task<RemoteActionResult> StartServiceAsync(Guid nodeId, string serviceName, CancellationToken cancellationToken);
    Task<RemoteActionResult> StopServiceAsync(Guid nodeId, string serviceName, CancellationToken cancellationToken);
    Task<RemoteActionResult> EnableServiceAsync(Guid nodeId, string serviceName, CancellationToken cancellationToken);
    Task<RemoteActionResult> DisableServiceAsync(Guid nodeId, string serviceName, CancellationToken cancellationToken);
    Task<GitCredentialListItem> CreateAuthUserAsync(UpsertGitCredentialRequest request, CancellationToken cancellationToken);
    Task<GitCredentialListItem> UpdateAuthUserAsync(Guid credentialId, UpsertGitCredentialRequest request, CancellationToken cancellationToken);
    Task DeleteAuthUserAsync(Guid credentialId, CancellationToken cancellationToken);
    Task<ApplicationListItem> CreateApplicationAsync(UpsertApplicationRequest request, CancellationToken cancellationToken);
    Task<ApplicationListItem> UpdateApplicationAsync(Guid applicationId, UpsertApplicationRequest request, CancellationToken cancellationToken);
    Task DeleteApplicationAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<ApplicationListItem> AssignApplicationAsync(Guid applicationId, AssignApplicationRequest request, CancellationToken cancellationToken);
    Task<RemoteActionResult> DeployApplicationAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<RemoteActionResult> UninstallApplicationAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<RemoteActionResult> RestartApplicationServiceAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<RemoteFileView> GetServiceUnitAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<RemoteActionResult> UpdateServiceUnitAsync(Guid applicationId, UpdateRemoteFileRequest request, CancellationToken cancellationToken);
    Task<RemoteFileView> GetServiceOverrideAsync(Guid applicationId, CancellationToken cancellationToken);
    Task<RemoteActionResult> UpdateServiceOverrideAsync(Guid applicationId, UpdateRemoteFileRequest request, CancellationToken cancellationToken);
}

public sealed class RegistryService(
    SinterServerDbContext dbContext,
    IGitCredentialProtector credentialProtector,
    INodeClient nodeClient,
    IOptions<SinterServerOptions> options,
    TimeProvider timeProvider) : IRegistryService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<ServerDashboard> GetDashboardAsync(CancellationToken cancellationToken)
    {
        return new ServerDashboard(options.Value.ServerName, await GetNodesAsync(cancellationToken), await GetApplicationsAsync(cancellationToken), await GetAuthUsersAsync(cancellationToken));
    }

    public async Task<IReadOnlyList<NodeListItem>> GetNodesAsync(CancellationToken cancellationToken)
    {
        var nodes = await dbContext.Nodes.AsNoTracking().OrderBy(node => node.Name).ToListAsync(cancellationToken);
        return nodes.Select(MapNode).ToArray();
    }

    public async Task<IReadOnlyList<GitCredentialListItem>> GetAuthUsersAsync(CancellationToken cancellationToken)
    {
        var credentials = await dbContext.GitCredentials.Include(credential => credential.Applications).AsNoTracking().OrderBy(credential => credential.Name).ToListAsync(cancellationToken);
        return credentials.Select(credential => new GitCredentialListItem(credential.Id, credential.Name, credential.Username, credential.Applications.Count)).ToArray();
    }

    public async Task<IReadOnlyList<ApplicationListItem>> GetApplicationsAsync(CancellationToken cancellationToken)
    {
        var applications = await dbContext.Applications.Include(application => application.Node).Include(application => application.GitCredential).AsNoTracking().OrderBy(application => application.Name).ToListAsync(cancellationToken);
        return applications.Select(MapApplication).ToArray();
    }

    public async Task<NodeListItem> CreateNodeAsync(UpsertNodeRequest request, CancellationToken cancellationToken)
    {
        var entity = new NodeEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Url = NormalizeUrl(request.Url),
            ApiKey = request.ApiKey.Trim(),
            HealthStatus = "Unknown"
        };
        dbContext.Nodes.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return await RefreshNodeAsync(entity.Id, cancellationToken);
    }

    public async Task<NodeListItem> UpdateNodeAsync(Guid nodeId, UpsertNodeRequest request, CancellationToken cancellationToken)
    {
        var entity = await RequireNodeAsync(nodeId, cancellationToken);
        entity.Name = request.Name.Trim();
        entity.Url = NormalizeUrl(request.Url);
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            entity.ApiKey = request.ApiKey.Trim();
        }

        entity.LastError = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        return await RefreshNodeAsync(entity.Id, cancellationToken);
    }

    public async Task DeleteNodeAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        var node = await RequireNodeAsync(nodeId, cancellationToken);
        var applications = await dbContext.Applications.Where(application => application.NodeId == nodeId).ToListAsync(cancellationToken);
        foreach (var application in applications)
        {
            application.NodeId = null;
            application.IsAssignmentActive = false;
            application.DeploymentStatus = "Inactive";
        }

        dbContext.Nodes.Remove(node);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<NodeListItem> RefreshNodeAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        var node = await RequireNodeAsync(nodeId, cancellationToken);
        node.LastRefreshUtc = timeProvider.GetUtcNow();
        try
        {
            var status = await nodeClient.GetStatusAsync(node.Url, cancellationToken);
            node.HealthStatus = status.Status;
            node.LastSeenUtc = timeProvider.GetUtcNow();
            node.LastError = null;
            if (status.Snapshot is not null)
            {
                node.SnapshotJson = JsonSerializer.Serialize(status.Snapshot, JsonOptions);
                node.CapabilitiesJson = JsonSerializer.Serialize(status.Snapshot.Capabilities, JsonOptions);
                node.EnvironmentJson = JsonSerializer.Serialize(status.Snapshot.Environment, JsonOptions);
                node.ListenUrlsJson = JsonSerializer.Serialize(status.Snapshot.Environment?.ListenUrls ?? Array.Empty<string>(), JsonOptions);
            }

            node.ServicesJson = JsonSerializer.Serialize(status.Services, JsonOptions);
            node.ManagedApplicationsJson = JsonSerializer.Serialize(status.ManagedApplications, JsonOptions);
            await SyncAssignedApplicationsFromNodeAsync(node.Id, status.ManagedApplications, cancellationToken);
        }
        catch (Exception ex)
        {
            node.HealthStatus = "Offline";
            node.LastError = ex.Message;
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return MapNode(node);
    }

    public async Task<RemoteActionResult> ReloadDaemonAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        var node = await RequireNodeAsync(nodeId, cancellationToken);
        return await nodeClient.ReloadDaemonAsync(node.Url, node.ApiKey, cancellationToken);
    }

    public async Task<RemoteActionResult> StartServiceAsync(Guid nodeId, string serviceName, CancellationToken cancellationToken)
    {
        return await ExecuteNodeServiceActionAsync(nodeId, serviceName, static (client, node, name, ct) => client.StartServiceAsync(node.Url, node.ApiKey, name, ct), cancellationToken);
    }

    public async Task<RemoteActionResult> StopServiceAsync(Guid nodeId, string serviceName, CancellationToken cancellationToken)
    {
        return await ExecuteNodeServiceActionAsync(nodeId, serviceName, static (client, node, name, ct) => client.StopServiceAsync(node.Url, node.ApiKey, name, ct), cancellationToken);
    }

    public async Task<RemoteActionResult> EnableServiceAsync(Guid nodeId, string serviceName, CancellationToken cancellationToken)
    {
        return await ExecuteNodeServiceActionAsync(nodeId, serviceName, static (client, node, name, ct) => client.EnableServiceAsync(node.Url, node.ApiKey, name, ct), cancellationToken);
    }

    public async Task<RemoteActionResult> DisableServiceAsync(Guid nodeId, string serviceName, CancellationToken cancellationToken)
    {
        return await ExecuteNodeServiceActionAsync(nodeId, serviceName, static (client, node, name, ct) => client.DisableServiceAsync(node.Url, node.ApiKey, name, ct), cancellationToken);
    }

    public async Task<GitCredentialListItem> CreateAuthUserAsync(UpsertGitCredentialRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(request.AccessToken))
        {
            throw new InvalidOperationException("Access token is required for new auth users.");
        }

        var entity = new GitCredentialEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            Username = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim(),
            EncryptedAccessToken = credentialProtector.Protect(request.AccessToken)
        };
        dbContext.GitCredentials.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new GitCredentialListItem(entity.Id, entity.Name, entity.Username, 0);
    }

    public async Task<GitCredentialListItem> UpdateAuthUserAsync(Guid credentialId, UpsertGitCredentialRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.GitCredentials.Include(credential => credential.Applications).SingleAsync(credential => credential.Id == credentialId, cancellationToken);
        entity.Name = request.Name.Trim();
        entity.Username = string.IsNullOrWhiteSpace(request.Username) ? null : request.Username.Trim();
        if (!string.IsNullOrWhiteSpace(request.AccessToken))
        {
            entity.EncryptedAccessToken = credentialProtector.Protect(request.AccessToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return new GitCredentialListItem(entity.Id, entity.Name, entity.Username, entity.Applications.Count);
    }

    public async Task DeleteAuthUserAsync(Guid credentialId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.GitCredentials.SingleAsync(credential => credential.Id == credentialId, cancellationToken);
        var applications = await dbContext.Applications.Where(application => application.GitCredentialId == credentialId).ToListAsync(cancellationToken);
        foreach (var application in applications)
        {
            application.GitCredentialId = null;
        }

        dbContext.GitCredentials.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ApplicationListItem> CreateApplicationAsync(UpsertApplicationRequest request, CancellationToken cancellationToken)
    {
        var entity = new ApplicationEntity
        {
            Id = Guid.NewGuid(),
            Name = request.Name.Trim(),
            RepoUrl = request.RepoUrl.Trim(),
            ProjectPath = request.ProjectPath.Trim(),
            ServiceName = string.IsNullOrWhiteSpace(request.ServiceName) ? null : request.ServiceName.Trim(),
            GitCredentialId = request.GitCredentialId,
            DeploymentStatus = "Inactive",
            ActiveBaseUrl = "<undefined>",
            ActiveBaseUrlStatus = "Undefined"
        };
        dbContext.Applications.Add(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.Entry(entity).Reference(application => application.GitCredential).LoadAsync(cancellationToken);
        return MapApplication(entity);
    }

    public async Task<ApplicationListItem> UpdateApplicationAsync(Guid applicationId, UpsertApplicationRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Applications.Include(application => application.Node).Include(application => application.GitCredential).SingleAsync(application => application.Id == applicationId, cancellationToken);
        entity.Name = request.Name.Trim();
        entity.RepoUrl = request.RepoUrl.Trim();
        entity.ProjectPath = request.ProjectPath.Trim();
        entity.ServiceName = string.IsNullOrWhiteSpace(request.ServiceName) ? null : request.ServiceName.Trim();
        entity.GitCredentialId = request.GitCredentialId;
        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.Entry(entity).Reference(application => application.GitCredential).LoadAsync(cancellationToken);
        return MapApplication(entity);
    }

    public async Task DeleteApplicationAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Applications.SingleAsync(application => application.Id == applicationId, cancellationToken);
        dbContext.Applications.Remove(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<ApplicationListItem> AssignApplicationAsync(Guid applicationId, AssignApplicationRequest request, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Applications.Include(application => application.Node).Include(application => application.GitCredential).SingleAsync(application => application.Id == applicationId, cancellationToken);
        entity.NodeId = request.NodeId;
        entity.IsAssignmentActive = false;
        entity.DeploymentStatus = request.NodeId.HasValue ? "Inactive" : "Unassigned";
        entity.ActiveBaseUrl = request.NodeId.HasValue ? "<undefined>" : "<unavailable>";
        entity.ActivePort = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        await dbContext.Entry(entity).Reference(application => application.Node).LoadAsync(cancellationToken);
        return MapApplication(entity);
    }

    public async Task<RemoteActionResult> DeployApplicationAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var entity = await LoadApplicationForActionAsync(applicationId, cancellationToken);
        var token = entity.GitCredential is null ? null : credentialProtector.Unprotect(entity.GitCredential.EncryptedAccessToken);
        var request = new
        {
            repoUrl = entity.RepoUrl,
            appName = entity.Name,
            branch = "main",
            token,
            projectPath = entity.ProjectPath,
            serviceName = entity.ServiceName
        };

        var result = await TryNodeActionAsync(
            entity,
            static (status, summary, events) => new RemoteActionResult(status, summary, events),
            () => nodeClient.DeployApplicationAsync(entity.Node!.Url, entity.Node.ApiKey, request, cancellationToken),
            "Deployment request failed before reaching the node.");
        entity.IsAssignmentActive = result.Status == "Success";
        entity.DeploymentStatus = result.Status == "Success" ? "Active" : "Error";
        entity.LastDeploymentUtc = timeProvider.GetUtcNow();
        entity.LastOperationSummary = result.Summary;
        entity.ServiceName ??= $"{entity.Name}.service";
        if (result.Status == "Success")
        {
            await RefreshAppFilesAsync(entity, cancellationToken);
        }

        await dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<RemoteActionResult> UninstallApplicationAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var entity = await LoadApplicationForActionAsync(applicationId, cancellationToken);
        var result = await TryNodeActionAsync(
            entity,
            static (status, summary, events) => new RemoteActionResult(status, summary, events),
            () => nodeClient.UninstallApplicationAsync(entity.Node!.Url, entity.Node.ApiKey, entity.Name, cancellationToken),
            "Uninstall request failed before reaching the node.");
        entity.IsAssignmentActive = false;
        entity.DeploymentStatus = result.Status == "Success" ? "Inactive" : "Error";
        entity.LastOperationSummary = result.Summary;
        entity.ServiceUnitContent = null;
        entity.OverrideContent = null;
        entity.ActiveBaseUrl = "<undefined>";
        entity.ActivePort = null;
        await dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<RemoteActionResult> RestartApplicationServiceAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var entity = await LoadApplicationForActionAsync(applicationId, cancellationToken);
        var result = await TryNodeActionAsync(
            entity,
            static (status, summary, events) => new RemoteActionResult(status, summary, events),
            () => nodeClient.RestartApplicationServiceAsync(entity.Node!.Url, entity.Node.ApiKey, entity.Name, cancellationToken),
            "Restart request failed before reaching the node.");
        entity.LastOperationSummary = result.Summary;
        await dbContext.SaveChangesAsync(cancellationToken);
        return result;
    }

    public async Task<RemoteFileView> GetServiceUnitAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var entity = await LoadApplicationForActionAsync(applicationId, cancellationToken);
        var serviceName = ResolveServiceName(entity);
        var file = await TryNodeFileReadAsync(entity, () => nodeClient.GetServiceUnitAsync(entity.Node!.Url, entity.Node.ApiKey, serviceName, cancellationToken), "Unable to read service unit from node.");
        entity.ServiceUnitContent = file.Content;
        await DeriveActiveAddressAsync(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return file;
    }

    public async Task<RemoteActionResult> UpdateServiceUnitAsync(Guid applicationId, UpdateRemoteFileRequest request, CancellationToken cancellationToken)
    {
        var entity = await LoadApplicationForActionAsync(applicationId, cancellationToken);
        var serviceName = ResolveServiceName(entity);
        var result = await TryNodeActionAsync(
            entity,
            static (status, summary, events) => new RemoteActionResult(status, summary, events),
            () => nodeClient.UpdateServiceUnitAsync(entity.Node!.Url, entity.Node.ApiKey, serviceName, request, cancellationToken),
            "Unable to update service unit on node.");
        if (result.Status == "Success")
        {
            entity.ServiceUnitContent = request.Content;
            await DeriveActiveAddressAsync(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    public async Task<RemoteFileView> GetServiceOverrideAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var entity = await LoadApplicationForActionAsync(applicationId, cancellationToken);
        var serviceName = ResolveServiceName(entity);
        var file = await TryNodeFileReadAsync(entity, () => nodeClient.GetServiceOverrideAsync(entity.Node!.Url, entity.Node.ApiKey, serviceName, cancellationToken), "Unable to read override from node.");
        entity.OverrideContent = file.Content;
        await DeriveActiveAddressAsync(entity);
        await dbContext.SaveChangesAsync(cancellationToken);
        return file;
    }

    public async Task<RemoteActionResult> UpdateServiceOverrideAsync(Guid applicationId, UpdateRemoteFileRequest request, CancellationToken cancellationToken)
    {
        var entity = await LoadApplicationForActionAsync(applicationId, cancellationToken);
        var serviceName = ResolveServiceName(entity);
        var result = await TryNodeActionAsync(
            entity,
            static (status, summary, events) => new RemoteActionResult(status, summary, events),
            () => nodeClient.UpdateServiceOverrideAsync(entity.Node!.Url, entity.Node.ApiKey, serviceName, request, cancellationToken),
            "Unable to update override on node.");
        if (result.Status == "Success")
        {
            entity.OverrideContent = request.Content;
            await DeriveActiveAddressAsync(entity);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        return result;
    }

    private async Task RefreshAppFilesAsync(ApplicationEntity entity, CancellationToken cancellationToken)
    {
        if (entity.Node is null)
        {
            return;
        }

        var serviceName = ResolveServiceName(entity);
        var unit = await TryNodeFileReadAsync(entity, () => nodeClient.GetServiceUnitAsync(entity.Node.Url, entity.Node.ApiKey, serviceName, cancellationToken), "Unable to refresh service unit from node.");
        entity.ServiceUnitContent = unit.Content;
        var overrideFile = await TryNodeFileReadAsync(entity, () => nodeClient.GetServiceOverrideAsync(entity.Node.Url, entity.Node.ApiKey, serviceName, cancellationToken), "Unable to refresh override from node.");
        entity.OverrideContent = overrideFile.Content;
        await DeriveActiveAddressAsync(entity);
    }

    private async Task SyncAssignedApplicationsFromNodeAsync(Guid nodeId, IReadOnlyList<NodeManagedApplicationInventoryItem> managedApplications, CancellationToken cancellationToken)
    {
        var assignedApplications = await dbContext.Applications.Where(application => application.NodeId == nodeId).ToListAsync(cancellationToken);
        foreach (var application in assignedApplications)
        {
            var synced = managedApplications.FirstOrDefault(item => string.Equals(item.AppName, application.Name, StringComparison.OrdinalIgnoreCase));
            if (synced is null)
            {
                continue;
            }

            application.ServiceName = synced.ServiceName;
            application.LastDeploymentUtc = synced.LastDeploymentUtc ?? application.LastDeploymentUtc;
            application.DeploymentStatus = synced.CurrentReleaseExists ? "Active" : application.DeploymentStatus;
        }
    }

    private Task DeriveActiveAddressAsync(ApplicationEntity entity)
    {
        var content = string.Join('\n', new[] { entity.ServiceUnitContent, entity.OverrideContent }.Where(static value => !string.IsNullOrWhiteSpace(value)));
        if (string.IsNullOrWhiteSpace(content))
        {
            entity.ActiveBaseUrl = "<unavailable>";
            entity.ActivePort = null;
            entity.ActiveBaseUrlStatus = "Unavailable";
            return Task.CompletedTask;
        }

        var port = ExtractPort(content);
        if (port is null)
        {
            entity.ActiveBaseUrl = "<undefined>";
            entity.ActivePort = null;
            entity.ActiveBaseUrlStatus = "Undefined";
            return Task.CompletedTask;
        }

        entity.ActivePort = port;
        var host = entity.Node?.Url is null ? "<undefined>" : new Uri(entity.Node.Url).Host;
        entity.ActiveBaseUrl = $"http://{host}:{port}";
        entity.ActiveBaseUrlStatus = "Derived";
        return Task.CompletedTask;
    }

    private static int? ExtractPort(string content)
    {
        var urlsMatch = Regex.Match(content, @"ASPNETCORE_URLS=http://[^:\s]+:(?<port>\d{1,5})", RegexOptions.IgnoreCase);
        if (urlsMatch.Success && int.TryParse(urlsMatch.Groups["port"].Value, out var port))
        {
            return port;
        }

        var envMatch = Regex.Match(content, @"SINTER_PORT=(?<port>\d{1,5})", RegexOptions.IgnoreCase);
        if (envMatch.Success && int.TryParse(envMatch.Groups["port"].Value, out var configuredPort))
        {
            return configuredPort;
        }

        return null;
    }

    private async Task<NodeEntity> RequireNodeAsync(Guid nodeId, CancellationToken cancellationToken)
    {
        return await dbContext.Nodes.SingleAsync(node => node.Id == nodeId, cancellationToken);
    }

    private async Task<RemoteActionResult> ExecuteNodeServiceActionAsync(
        Guid nodeId,
        string serviceName,
        Func<INodeClient, NodeEntity, string, CancellationToken, Task<RemoteActionResult>> action,
        CancellationToken cancellationToken)
    {
        var node = await RequireNodeAsync(nodeId, cancellationToken);
        var services = string.IsNullOrWhiteSpace(node.ServicesJson)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<NodeServiceInventoryItem>>(node.ServicesJson, JsonOptions) ?? [];
        var matchedService = services.FirstOrDefault(item => string.Equals(item.Name, serviceName, StringComparison.OrdinalIgnoreCase));
        if (matchedService is null)
        {
            throw new InvalidOperationException("Service is not currently part of the node's synced prefix inventory.");
        }

        try
        {
            var result = await action(nodeClient, node, matchedService.Name, cancellationToken);
            await RefreshNodeAsync(nodeId, cancellationToken);
            return result;
        }
        catch (Exception ex)
        {
            var summary = string.IsNullOrWhiteSpace(ex.Message) ? "Node service action failed." : ex.Message;
            return new RemoteActionResult("Error", summary, [new RemoteEvent("error", summary, DateTimeOffset.UtcNow)]);
        }
    }

    private async Task<ApplicationEntity> LoadApplicationForActionAsync(Guid applicationId, CancellationToken cancellationToken)
    {
        var entity = await dbContext.Applications.Include(application => application.Node).Include(application => application.GitCredential).SingleAsync(application => application.Id == applicationId, cancellationToken);
        if (entity.Node is null)
        {
            throw new InvalidOperationException("Application is not assigned to a node.");
        }

        return entity;
    }

    private static async Task<RemoteActionResult> TryNodeActionAsync(
        ApplicationEntity entity,
        Func<string, string, IReadOnlyList<RemoteEvent>, RemoteActionResult> resultFactory,
        Func<Task<RemoteActionResult>> action,
        string fallbackMessage)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            var summary = string.IsNullOrWhiteSpace(ex.Message) ? fallbackMessage : ex.Message;
            entity.LastOperationSummary = summary;
            return resultFactory("Error", summary, [new RemoteEvent("error", summary, DateTimeOffset.UtcNow)]);
        }
    }

    private static async Task<RemoteFileView> TryNodeFileReadAsync(ApplicationEntity entity, Func<Task<RemoteFileView>> action, string fallbackMessage)
    {
        try
        {
            return await action();
        }
        catch (Exception ex)
        {
            var summary = string.IsNullOrWhiteSpace(ex.Message) ? fallbackMessage : ex.Message;
            entity.LastOperationSummary = summary;
            return new RemoteFileView(string.Empty, summary);
        }
    }

    private static string NormalizeUrl(string url) => url.Trim().TrimEnd('/');
    private static string ResolveServiceName(ApplicationEntity entity) => string.IsNullOrWhiteSpace(entity.ServiceName) ? $"{entity.Name}.service" : entity.ServiceName!;

    private NodeListItem MapNode(NodeEntity entity)
    {
        NodeSnapshot? snapshot = null;
        if (!string.IsNullOrWhiteSpace(entity.SnapshotJson))
        {
            snapshot = JsonSerializer.Deserialize<NodeSnapshot>(entity.SnapshotJson, JsonOptions);
        }

        var services = string.IsNullOrWhiteSpace(entity.ServicesJson)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<NodeServiceInventoryItem>>(entity.ServicesJson, JsonOptions) ?? [];
        var managedApplications = string.IsNullOrWhiteSpace(entity.ManagedApplicationsJson)
            ? []
            : JsonSerializer.Deserialize<IReadOnlyList<NodeManagedApplicationInventoryItem>>(entity.ManagedApplicationsJson, JsonOptions) ?? [];

        return new NodeListItem(entity.Id, entity.Name, entity.Url, entity.HealthStatus, entity.LastSeenUtc, entity.LastError, snapshot, services, managedApplications);
    }

    private static ApplicationListItem MapApplication(ApplicationEntity entity)
    {
        return new ApplicationListItem(entity.Id, entity.Name, entity.RepoUrl, entity.ProjectPath, entity.ServiceName, entity.NodeId, entity.Node?.Name, entity.GitCredentialId, entity.GitCredential?.Name, entity.IsAssignmentActive, entity.DeploymentStatus, entity.LastDeploymentUtc, entity.ActiveBaseUrl ?? "<undefined>", entity.ActivePort?.ToString() ?? "<undefined>", entity.ServiceUnitContent, entity.OverrideContent, entity.LastOperationSummary);
    }
}