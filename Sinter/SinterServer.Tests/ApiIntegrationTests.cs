using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SinterServer.Data;
using SinterServer.Models;

namespace SinterServer.Tests;

public sealed class ApiIntegrationTests : IClassFixture<SinterServerFactory>
{
    private readonly HttpClient client;
    private readonly SinterServerFactory factory;

    public ApiIntegrationTests(SinterServerFactory factory)
    {
        this.factory = factory;
        client = factory.CreateClient();
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        var response = await client.GetAsync("/health");

        response.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task StateEndpoint_StartsEmpty()
    {
        using var isolatedFactory = new SinterServerFactory();
        using var isolatedClient = isolatedFactory.CreateClient();
        var state = await isolatedClient.GetFromJsonAsync<ServerDashboard>("/api/state");

        Assert.NotNull(state);
        Assert.Empty(state.Nodes);
        Assert.Empty(state.Applications);
        Assert.Empty(state.AuthUsers);
    }

    [Fact]
    public async Task CanCreateAuthUser()
    {
        var response = await client.PostAsJsonAsync("/api/auth-users", new UpsertGitCredentialRequest("GitHub", "octocat", "ghp_example_token"));

        response.EnsureSuccessStatusCode();

        var users = await client.GetFromJsonAsync<List<GitCredentialListItem>>("/api/auth-users");
        Assert.NotNull(users);
        Assert.Contains(users, user => user.Name == "GitHub" && user.Username == "octocat");
    }

    [Fact]
    public async Task CanControlDiscoveredNodeService()
    {
        factory.FakeNodeClient.ServiceActions.Clear();

        var createNode = await client.PostAsJsonAsync("/api/nodes", new UpsertNodeRequest("Node Service Control", "http://node-service-control:5000", "secret"));
        createNode.EnsureSuccessStatusCode();
        var node = await createNode.Content.ReadFromJsonAsync<NodeListItem>();

        var refresh = await client.PostAsync($"/api/nodes/{node!.Id}/refresh", content: null);
        refresh.EnsureSuccessStatusCode();

        var response = await client.PostAsJsonAsync($"/api/nodes/{node!.Id}/services/start", new NodeServiceActionRequest("HomeLab.Api.service"));
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RemoteActionResult>();
        Assert.NotNull(result);
        Assert.Equal("Success", result!.Status);
        Assert.Contains(factory.FakeNodeClient.ServiceActions, action => action.Action == "start" && action.ServiceName == "HomeLab.Api.service");
    }

    [Fact]
    public async Task StateEndpoint_ExposesNodeServiceRuntimeFlags()
    {
        var createNode = await client.PostAsJsonAsync("/api/nodes", new UpsertNodeRequest("Node Runtime Flags", "http://node-runtime-flags:5000", "secret"));
        createNode.EnsureSuccessStatusCode();

        var node = await createNode.Content.ReadFromJsonAsync<NodeListItem>();
        Assert.NotNull(node);

        var refresh = await client.PostAsync($"/api/nodes/{node!.Id}/refresh", content: null);
        refresh.EnsureSuccessStatusCode();

        var state = await client.GetFromJsonAsync<ServerDashboard>("/api/state");
        Assert.NotNull(state);

        var syncedNode = Assert.Single(state!.Nodes, item => item.Id == node.Id);
        var service = Assert.Single(syncedNode.Services);
        Assert.Equal("HomeLab.Api.service", service.Name);
        Assert.True(service.IsActive);
        Assert.False(service.IsEnabled);
    }

    [Fact]
    public async Task StateEndpoint_ExposesNodeTelemetry()
    {
        var createNode = await client.PostAsJsonAsync("/api/nodes", new UpsertNodeRequest("Node Telemetry", "http://node-telemetry:5000", "secret"));
        createNode.EnsureSuccessStatusCode();

        var node = await createNode.Content.ReadFromJsonAsync<NodeListItem>();
        Assert.NotNull(node);

        var refresh = await client.PostAsync($"/api/nodes/{node!.Id}/refresh", content: null);
        refresh.EnsureSuccessStatusCode();

        var state = await client.GetFromJsonAsync<ServerDashboard>("/api/state");
        Assert.NotNull(state);

        var syncedNode = Assert.Single(state!.Nodes, item => item.Id == node.Id);
        Assert.NotNull(syncedNode.Snapshot?.Telemetry);
        Assert.Equal(72.4, syncedNode.Snapshot!.Telemetry!.CpuUsagePercent);
        Assert.Equal(3, syncedNode.Snapshot.Telemetry.OpenPortCount);
        Assert.Contains("Load is climbing quickly.", syncedNode.Snapshot.Telemetry.HealthSignals);
    }

    [Fact]
    public async Task RefreshNode_PersistsTelemetrySamples_AtBoundedCadence_AndPrunesOlderThanThreeWeeks()
    {
        factory.TimeProvider.Advance(TimeSpan.Zero);

        var createNode = await client.PostAsJsonAsync("/api/nodes", new UpsertNodeRequest("Node Telemetry History", "http://node-telemetry-history:5000", "secret"));
        createNode.EnsureSuccessStatusCode();

        var node = await createNode.Content.ReadFromJsonAsync<NodeListItem>();
        Assert.NotNull(node);

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SinterServerDbContext>();
            Assert.Equal(1, await db.NodeTelemetrySamples.CountAsync(sample => sample.NodeId == node!.Id));
        }

        var immediateRefresh = await client.PostAsync($"/api/nodes/{node!.Id}/refresh", content: null);
        immediateRefresh.EnsureSuccessStatusCode();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SinterServerDbContext>();
            Assert.Equal(1, await db.NodeTelemetrySamples.CountAsync(sample => sample.NodeId == node!.Id));
        }

        factory.TimeProvider.Advance(TimeSpan.FromMinutes(6));

        var laterRefresh = await client.PostAsync($"/api/nodes/{node!.Id}/refresh", content: null);
        laterRefresh.EnsureSuccessStatusCode();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SinterServerDbContext>();
            Assert.Equal(2, await db.NodeTelemetrySamples.CountAsync(sample => sample.NodeId == node!.Id));
        }

        factory.TimeProvider.Advance(TimeSpan.FromDays(22));

        var pruningRefresh = await client.PostAsync($"/api/nodes/{node!.Id}/refresh", content: null);
        pruningRefresh.EnsureSuccessStatusCode();

        await using (var scope = factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<SinterServerDbContext>();
            var sample = await db.NodeTelemetrySamples.SingleAsync(existing => existing.NodeId == node!.Id);
            Assert.True(sample.CapturedUtc >= factory.TimeProvider.GetUtcNow().AddDays(-21));
        }
    }

    [Fact]
    public async Task NodeTelemetryEndpoint_ReturnsChronologicalHistory()
    {
        var createNode = await client.PostAsJsonAsync("/api/nodes", new UpsertNodeRequest("Node Telemetry Endpoint", "http://node-telemetry-endpoint:5000", "secret"));
        createNode.EnsureSuccessStatusCode();

        var node = await createNode.Content.ReadFromJsonAsync<NodeListItem>();
        Assert.NotNull(node);

        factory.TimeProvider.Advance(TimeSpan.FromMinutes(6));
        var refresh = await client.PostAsync($"/api/nodes/{node!.Id}/refresh", content: null);
        refresh.EnsureSuccessStatusCode();

        var history = await client.GetFromJsonAsync<NodeTelemetryHistoryResponse>($"/api/nodes/{node.Id}/telemetry");
        Assert.NotNull(history);
        Assert.Equal(node.Id, history!.NodeId);
        Assert.Equal(21, history.RetentionDays);
        Assert.Equal(300, history.SampleIntervalSeconds);
        Assert.Equal(2, history.Samples.Count);
        Assert.True(history.Samples[0].CapturedUtc <= history.Samples[1].CapturedUtc);
        Assert.Equal(72.4, history.Samples[1].CpuUsagePercent);
        Assert.Equal(3, history.Samples[1].OpenPortCount);
    }

    [Fact]
    public async Task SystemSelfUpdate_TriggersCoordinator()
    {
        factory.FakeSelfUpdateCoordinator.Requests.Clear();

        var response = await client.PostAsync("/api/system/self-update", content: null);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<RemoteActionResult>();
        Assert.NotNull(result);
        Assert.Equal("Success", result!.Status);
        Assert.Single(factory.FakeSelfUpdateCoordinator.Requests);
        Assert.Equal("https://github.com/Jeffe747/Sinter.git", factory.FakeSelfUpdateCoordinator.Requests[0].RepoUrl);
        Assert.Equal("main", factory.FakeSelfUpdateCoordinator.Requests[0].Branch);
    }
}