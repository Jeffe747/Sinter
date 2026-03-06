using SinterNode.Services;

namespace SinterNode.Tests;

public sealed class ServiceCatalogTests
{
    [Fact]
    public async Task ListAsync_FiltersByConfiguredPrefixesAndDetectsManagedFiles()
    {
        using var workspace = new TestWorkspace();
        var systemdDirectory = workspace.GetPath("systemd");
        Directory.CreateDirectory(systemdDirectory);
        await File.WriteAllTextAsync(Path.Combine(systemdDirectory, "HomeLab.Api.service"), "# Managed by SinterNode\nDescription=API");
        await File.WriteAllTextAsync(Path.Combine(systemdDirectory, "Other.Api.service"), "Description=Other");
        Directory.CreateDirectory(Path.Combine(systemdDirectory, "HomeLab.Api.service.d"));
        await File.WriteAllTextAsync(Path.Combine(systemdDirectory, "HomeLab.Api.service.d", "override.conf"), "[Service]\nEnvironment=ENV=1");

        var catalog = new ServiceCatalog(TestOptions.Create(workspace), new FakeSystemServiceManager(), new SystemdOverrideValidator());

        var services = await catalog.ListAsync(["HomeLab"], CancellationToken.None);

        var service = Assert.Single(services);
        Assert.Equal("HomeLab.Api.service", service.Name);
        Assert.True(service.IsManagedByNode);
        Assert.True(service.HasOverride);
        Assert.Equal("API", service.Description);
        Assert.Empty(service.OverrideWarnings);
    }

    [Fact]
    public async Task WriteOverrideFileAsync_RejectsUnsupportedDirectives()
    {
        using var workspace = new TestWorkspace();
        var catalog = new ServiceCatalog(TestOptions.Create(workspace), new FakeSystemServiceManager(), new SystemdOverrideValidator());

        var error = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            catalog.WriteOverrideFileAsync("HomeLab.Api.service", "[Service]\nExecReload=/bin/true\n", CancellationToken.None));

        Assert.Contains("not allowed", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}