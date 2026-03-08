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
    public async Task DeployAsync_CleansStaleRepoCacheBeforeClone()
    {
        using var workspace = new TestWorkspace();
        var options = TestOptions.Create(workspace);
        Directory.CreateDirectory(options.Value.ManagedAppsRoot);
        Directory.CreateDirectory(options.Value.SystemdUnitDirectory);

        var staleRepoRoot = workspace.GetPath("apps", "MyApp", "repo-cache");
        Directory.CreateDirectory(staleRepoRoot);
        File.WriteAllText(Path.Combine(staleRepoRoot, "stale.txt"), "stale");

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
        var services = new FakeSystemServiceManager();
        var sut = new ManagedApplicationService(
            options,
            runner,
            services,
            new ServiceCatalog(options, services, new SystemdOverrideValidator()),
            new FakeSelfUpdateCoordinator(),
            new FakeReleasePointerManager(),
            new OperationLockProvider(),
            TimeProvider.System,
            NullLogger<ManagedApplicationService>.Instance);

        var events = await CollectAsync(sut.DeployAsync(new DeployApplicationRequest("https://github.com/example/app.git", "MyApp"), CancellationToken.None));

        Assert.False(File.Exists(Path.Combine(staleRepoRoot, "stale.txt")));
        Assert.Contains(events, static evt => evt.Type == "success" && evt.Message.Contains("Deployment completed", StringComparison.Ordinal));
    }

    [Fact]
    public async Task DeployAsync_ReturnsErrorEventWhenCommandFails()
    {
        using var workspace = new TestWorkspace();
        var options = TestOptions.Create(workspace);
        Directory.CreateDirectory(options.Value.ManagedAppsRoot);
        Directory.CreateDirectory(options.Value.SystemdUnitDirectory);

        var services = new FakeSystemServiceManager();
        var sut = new ManagedApplicationService(
            options,
            new CompilerErrorProcessRunner(),
            services,
            new ServiceCatalog(options, services, new SystemdOverrideValidator()),
            new FakeSelfUpdateCoordinator(),
            new FakeReleasePointerManager(),
            new OperationLockProvider(),
            TimeProvider.System,
            NullLogger<ManagedApplicationService>.Instance);

        var events = await CollectAsync(sut.DeployAsync(new DeployApplicationRequest("https://github.com/example/app.git", "MyApp"), CancellationToken.None));

        Assert.Contains(events, static evt => evt.Type == "error" && evt.Message.Contains("error CS1513", StringComparison.Ordinal));
    }

    [Fact]
    public async Task UninstallAsync_RemovesArtifactsWithoutManifest()
    {
        using var workspace = new TestWorkspace();
        var options = TestOptions.Create(workspace);
        Directory.CreateDirectory(options.Value.SystemdUnitDirectory);

        var appRoot = workspace.GetPath("apps", "MyApp");
        var unitPath = workspace.GetPath("systemd", "MyApp.service");
        var overrideRoot = workspace.GetPath("systemd", "MyApp.service.d");
        Directory.CreateDirectory(appRoot);
        Directory.CreateDirectory(overrideRoot);
        File.WriteAllText(unitPath, "[Unit]");
        File.WriteAllText(Path.Combine(appRoot, "orphan.txt"), "stale");

        var services = new FakeSystemServiceManager();
        var sut = new ManagedApplicationService(
            options,
            new FakeProcessRunner(),
            services,
            new ServiceCatalog(options, services, new SystemdOverrideValidator()),
            new FakeSelfUpdateCoordinator(),
            new FakeReleasePointerManager(),
            new OperationLockProvider(),
            TimeProvider.System,
            NullLogger<ManagedApplicationService>.Instance);

        var events = await CollectAsync(sut.UninstallAsync("MyApp", CancellationToken.None));

        Assert.False(Directory.Exists(appRoot));
        Assert.False(File.Exists(unitPath));
        Assert.False(Directory.Exists(overrideRoot));
        Assert.Contains(events, static evt => evt.Type == "success" && evt.Message.Contains("was removed", StringComparison.Ordinal));
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

    private sealed class CompilerErrorProcessRunner : IProcessRunner
    {
        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            return Task.FromResult(new ProcessResult(1, string.Empty, "failed"));
        }

        public async IAsyncEnumerable<ProcessOutputLine> StreamAsync(ProcessRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            await Task.Yield();
            yield return new ProcessOutputLine("  Determining projects to restore...", false);
            yield return new ProcessOutputLine("/tmp/MyApp/Program.cs(12,5): error CS1513: } expected [/tmp/MyApp/MyApp.csproj]", false);
            yield return new ProcessOutputLine("Process exited with code 1.", true, IsTerminal: true, ExitCode: 1);
        }
    }
}