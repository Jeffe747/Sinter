using System.Net.Http.Json;
using SinterServer.Models;

namespace SinterServer.Tests;

public sealed class ApiIntegrationTests : IClassFixture<SinterServerFactory>
{
    private readonly HttpClient client;

    public ApiIntegrationTests(SinterServerFactory factory)
    {
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
}