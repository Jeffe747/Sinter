using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using SinterNode.Models;
using SinterNode.Services;

namespace SinterNode.Tests;

public sealed class ApiIntegrationTests : IClassFixture<SinterNodeFactory>
{
    private readonly SinterNodeFactory factory;

    public ApiIntegrationTests(SinterNodeFactory factory)
    {
        this.factory = factory;
    }

    [Fact]
    public async Task RootPage_LoadsStaticShell_AndUiState_ShowsBootstrapKey_OnFirstLoad()
    {
        using var isolatedFactory = new SinterNodeFactory();
        using var client = isolatedFactory.CreateClient();

        var html = await client.GetStringAsync("/");
        var state = await client.GetFromJsonAsync<NodeDashboard>("/ui/state");

        Assert.NotNull(state);
        Assert.Contains("SinterNode", html, StringComparison.Ordinal);
        Assert.Contains("/app.js", html, StringComparison.Ordinal);
        Assert.True(state!.Snapshot.ShowApiKey);
        Assert.False(string.IsNullOrWhiteSpace(state.Snapshot.ApiKey));
    }

    [Fact]
    public async Task ProtectedApi_RejectsMissingKey_AndAcceptsStoredKey()
    {
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<INodeStateStore>();
        var snapshot = await store.GetSnapshotAsync(CancellationToken.None);
        await store.UpdatePrefixesAsync(["HomeLab"], CancellationToken.None);

        var unauthorized = await client.GetAsync("/api/services");
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        client.DefaultRequestHeaders.Add("X-Sinter-Key", snapshot.ApiKey);
        var authorized = await client.PutAsJsonAsync("/api/prefixes", new UpdatePrefixesRequest(["HomeLab", "Apps"]));

        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
    }
}

public sealed class SinterNodeFactory : WebApplicationFactory<Program>, IDisposable
{
    private readonly TestWorkspace workspace = new();

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        builder.ConfigureAppConfiguration((_, config) =>
        {
            config.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["SinterNode:StateFilePath"] = workspace.GetPath("config", "node-state.json"),
                ["SinterNode:ApiKeyFilePath"] = workspace.GetPath("config", "client_secret"),
                ["SinterNode:ManagedAppsRoot"] = workspace.GetPath("apps"),
                ["SinterNode:NodeInstallRoot"] = workspace.GetPath("node-install"),
                ["SinterNode:NodeReleaseRoot"] = workspace.GetPath("node-release"),
                ["SinterNode:SystemdUnitDirectory"] = workspace.GetPath("systemd"),
                ["SinterNode:DotnetPath"] = "dotnet",
                ["SinterNode:DefaultSourceRepository"] = "https://github.com/Jeffe747/Sinter.git"
            });
        });

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<ISystemServiceManager>();
            services.AddSingleton<ISystemServiceManager, FakeSystemServiceManager>();
            services.RemoveAll<IProcessRunner>();
            services.AddSingleton<IProcessRunner>(_ => new FakeProcessRunner());
            services.RemoveAll<IReleasePointerManager>();
            services.AddSingleton<IReleasePointerManager, FakeReleasePointerManager>();
            services.RemoveAll<ISelfUpdateCoordinator>();
            services.AddSingleton<ISelfUpdateCoordinator, FakeSelfUpdateCoordinator>();
        });
    }

    void IDisposable.Dispose()
    {
        workspace.Dispose();
        base.Dispose();
    }
}