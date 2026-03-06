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
        var catalog = new ServiceCatalog(options, services);

        var sut = new ManagedApplicationService(
            options,
            runner,
            services,
            catalog,
            pointerManager,
            new OperationLockProvider(),
            TimeProvider.System,
            NullLogger<ManagedApplicationService>.Instance);

        var events = await CollectAsync(sut.DeployAsync(new DeployApplicationRequest("https://github.com/example/app.git", "MyApp"), CancellationToken.None));

        Assert.Contains(events, static evt => evt.Type == "warning" && evt.Message.Contains("Rolled back", StringComparison.Ordinal));
        Assert.Equal(previousRelease, pointerManager.Read(Path.Combine(options.Value.ManagedAppsRoot, "MyApp", "current")));
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