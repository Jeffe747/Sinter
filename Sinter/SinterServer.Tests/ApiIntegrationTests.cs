using System.Net.Http.Json;
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
        var state = await client.GetFromJsonAsync<ServerDashboard>("/api/state");

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

        var syncedNode = Assert.Single(state!.Nodes);
        var service = Assert.Single(syncedNode.Services);
        Assert.Equal("HomeLab.Api.service", service.Name);
        Assert.True(service.IsActive);
        Assert.False(service.IsEnabled);
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