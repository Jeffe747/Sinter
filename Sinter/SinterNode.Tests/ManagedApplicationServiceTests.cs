using Microsoft.Extensions.Logging.Abstractions;
using SinterNode.Models;
using SinterNode.Services;

namespace SinterNode.Tests;

public sealed class ManagedApplicationServiceTests
{
    [Fact]
    public async Task DeployAsync_RollsBackToPreviousReleaseWhenServiceFailsToBecomeActive()
    {
        using var workspace = new TestWorkspace();
        var options = TestOptions.Create(workspace);
        Directory.CreateDirectory(options.Value.ManagedAppsRoot);
        Directory.CreateDirectory(options.Value.SystemdUnitDirectory);

        var pointerManager = new FakeReleasePointerManager();
        var previousRelease = workspace.GetPath("apps", "MyApp", "releases", "previous", "publish");
        Directory.CreateDirectory(previousRelease);
        pointerManager.Seed(Path.Combine(options.Value.ManagedAppsRoot, "MyApp", "current"), previousRelease);

        var runner = new FakeProcessRunner(request =>
        {
            if (request.FileName == "git" && request.Arguments.StartsWith("clone", StringComparison.Ordinal))
            {
                Directory.CreateDirectory(Path.Combine(request.WorkingDirectory, ".git"));
            }

            if (request.FileName == options.Value.DotnetPath && request.Arguments.Contains("publish", StringComparison.Ordinal))
            {
                var marker = "-o \"";
                var start = request.Arguments.IndexOf(marker, StringComparison.Ordinal);
                var outputStart = start + marker.Length;
                var end = request.Arguments.IndexOf("\"", outputStart, StringComparison.Ordinal);
                var output = request.Arguments.Substring(outputStart, end - outputStart);
                Directory.CreateDirectory(output);
                File.WriteAllText(Path.Combine(output, "MyApp.dll"), string.Empty);
            }
        });
        var services = new FakeSystemServiceManager { ActiveState = false };
        var catalog = new ServiceCatalog(options, services, new SystemdOverrideValidator());

        var sut = new ManagedApplicationService(
            options,
            runner,
            services,
            catalog,
            new FakeSelfUpdateCoordinator(),
            pointerManager,
            new OperationLockProvider(),
            TimeProvider.System,
            NullLogger<ManagedApplicationService>.Instance);

        var events = await CollectAsync(sut.DeployAsync(new DeployApplicationRequest("https://github.com/example/app.git", "MyApp"), CancellationToken.None));

        Assert.Contains(events, static evt => evt.Type == "warning" && evt.Message.Contains("Rolled back", StringComparison.Ordinal));
        Assert.Equal(previousRelease, pointerManager.Read(Path.Combine(options.Value.ManagedAppsRoot, "MyApp", "current")));
    }

    [Fact]
    public async Task SelfUpdateAsync_UsesCoordinatorHandoff()
    {
        using var workspace = new TestWorkspace();
        var options = TestOptions.Create(workspace);
        var coordinator = new FakeSelfUpdateCoordinator();

        var sut = new ManagedApplicationService(
            options,
            new FakeProcessRunner(),
            new FakeSystemServiceManager(),
            new ServiceCatalog(options, new FakeSystemServiceManager(), new SystemdOverrideValidator()),
            coordinator,
            new FakeReleasePointerManager(),
            new OperationLockProvider(),
            TimeProvider.System,
            NullLogger<ManagedApplicationService>.Instance);

        var events = await CollectAsync(sut.SelfUpdateAsync(new SelfUpdateRequest("https://github.com/Jeffe747/Sinter.git", "main", "Sinter/SinterNode/SinterNode.csproj", null), CancellationToken.None));

        Assert.Single(coordinator.Requests);
        Assert.Contains(events, static evt => evt.Type == "success");
    }

    private static async Task<List<OperationEvent>> CollectAsync(IAsyncEnumerable<OperationEvent> stream)
    {
        var events = new List<OperationEvent>();
        await foreach (var evt in stream)
        {
            events.Add(evt);
        }

        return events;
    }
}