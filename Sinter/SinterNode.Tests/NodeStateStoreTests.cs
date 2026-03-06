using SinterNode.Services;

namespace SinterNode.Tests;

public sealed class NodeStateStoreTests
{
    [Fact]
    public async Task GetSnapshotAsync_CreatesStateAndApiKeyOnFirstUse()
    {
        using var workspace = new TestWorkspace();
        var store = new NodeStateStore(TestOptions.Create(workspace), TimeProvider.System);

        var snapshot = await store.GetSnapshotAsync(CancellationToken.None);

        Assert.NotEqual(Guid.Empty, snapshot.State.NodeId);
        Assert.False(snapshot.State.BootstrapCompleted);
        Assert.True(snapshot.ShowApiKey);
        Assert.False(string.IsNullOrWhiteSpace(snapshot.ApiKey));
        Assert.True(File.Exists(workspace.GetPath("config", "node-state.json")));
        Assert.True(File.Exists(workspace.GetPath("config", "client_secret")));
    }

    [Fact]
    public async Task UpdatePrefixesAsync_PersistsNormalizedPrefixesAndRequiresKeyValidationLater()
    {
        using var workspace = new TestWorkspace();
        var store = new NodeStateStore(TestOptions.Create(workspace), TimeProvider.System);
        var snapshot = await store.GetSnapshotAsync(CancellationToken.None);

        var updated = await store.UpdatePrefixesAsync(["HomeLab", "homelab", "Apps"], CancellationToken.None);

        Assert.True(updated.State.BootstrapCompleted);
        Assert.False(updated.ShowApiKey);
        Assert.Equal(new[] { "Apps", "HomeLab" }, updated.State.ServicePrefixes);
        Assert.True(await store.ValidateApiKeyAsync(snapshot.ApiKey, CancellationToken.None));
        Assert.False(await store.ValidateApiKeyAsync("wrong-key", CancellationToken.None));
    }
}