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
        Assert.NotNull(state.Telemetry);
        Assert.Equal(4, state.Telemetry.OpenPortCount);
        Assert.Equal(61.3, state.Telemetry.CpuUsagePercent);
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

    [Fact]
    public async Task ServiceActionEndpoints_InvokeSystemServiceOperations()
    {
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<INodeStateStore>();
        var snapshot = await store.GetSnapshotAsync(CancellationToken.None);
        await store.UpdatePrefixesAsync(["HomeLab"], CancellationToken.None);
        var systemServiceManager = (FakeSystemServiceManager)factory.Services.GetRequiredService<ISystemServiceManager>();

        client.DefaultRequestHeaders.Add("X-Sinter-Key", snapshot.ApiKey);

        var start = await client.PostAsync("/api/services/HomeLab.Api.service/start", null);
        var stop = await client.PostAsync("/api/services/HomeLab.Api.service/stop", null);
        var enable = await client.PostAsync("/api/services/HomeLab.Api.service/enable", null);
        var disable = await client.PostAsync("/api/services/HomeLab.Api.service/disable", null);

        Assert.Equal(HttpStatusCode.OK, start.StatusCode);
        Assert.Equal(HttpStatusCode.OK, stop.StatusCode);
        Assert.Equal(HttpStatusCode.OK, enable.StatusCode);
        Assert.Equal(HttpStatusCode.OK, disable.StatusCode);
        Assert.Contains("HomeLab.Api.service", systemServiceManager.StartedServices);
        Assert.Contains("HomeLab.Api.service", systemServiceManager.StoppedServices);
        Assert.Contains("HomeLab.Api.service", systemServiceManager.EnabledServices);
        Assert.Contains("HomeLab.Api.service", systemServiceManager.DisabledServices);
    }

    [Fact]
    public async Task UiSelfUpdate_ValidatesApiKey_AndTriggersCoordinator()
    {
        using var client = factory.CreateClient();
        var store = factory.Services.GetRequiredService<INodeStateStore>();
        await store.UpdatePrefixesAsync(["HomeLab"], CancellationToken.None);
        var snapshot = await store.GetSnapshotAsync(CancellationToken.None);
        var coordinator = (FakeSelfUpdateCoordinator)factory.Services.GetRequiredService<ISelfUpdateCoordinator>();

        var unauthorized = await client.PostAsJsonAsync("/ui/self-update", new UiSelfUpdateRequest("wrong-key"));
        Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);

        var authorized = await client.PostAsJsonAsync("/ui/self-update", new UiSelfUpdateRequest(snapshot.ApiKey));
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
        Assert.Single(coordinator.Requests);
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
            services.RemoveAll<INodeTelemetryCollector>();
            services.AddSingleton<INodeTelemetryCollector, FakeNodeTelemetryCollector>();
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