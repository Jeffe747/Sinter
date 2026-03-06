using SinterNode.Services;

namespace SinterNode.Tests;

public sealed class SystemServiceManagerTests
{
    [Fact]
    public async Task IsActiveAsync_TreatsActivatingAsStarted()
    {
        var runner = new ScriptedProcessRunner();
        runner.Add("show HomeLab.Api.service --property=ActiveState --value", new ProcessResult(0, "activating\n", string.Empty));
        var manager = new SystemServiceManager(runner);

        var isActive = await manager.IsActiveAsync("HomeLab.Api.service", CancellationToken.None);

        Assert.True(isActive);
    }

    [Fact]
    public async Task IsEnabledAsync_UsesUnitFileState()
    {
        var runner = new ScriptedProcessRunner();
        runner.Add("show HomeLab.Api.service --property=UnitFileState --value", new ProcessResult(0, "enabled-runtime\n", string.Empty));
        var manager = new SystemServiceManager(runner);

        var isEnabled = await manager.IsEnabledAsync("HomeLab.Api.service", CancellationToken.None);

        Assert.True(isEnabled);
    }

    [Fact]
    public async Task IsActiveAsync_ReturnsFalseForInactiveState()
    {
        var runner = new ScriptedProcessRunner();
        runner.Add("show HomeLab.Api.service --property=ActiveState --value", new ProcessResult(0, "inactive\n", string.Empty));
        var manager = new SystemServiceManager(runner);

        var isActive = await manager.IsActiveAsync("HomeLab.Api.service", CancellationToken.None);

        Assert.False(isActive);
    }

    private sealed class ScriptedProcessRunner : IProcessRunner
    {
        private readonly Dictionary<string, ProcessResult> results = new(StringComparer.Ordinal);

        public void Add(string arguments, ProcessResult result)
        {
            results[arguments] = result;
        }

        public Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
        {
            _ = cancellationToken;
            if (results.TryGetValue(request.Arguments, out var result))
            {
                return Task.FromResult(result);
            }

            return Task.FromResult(new ProcessResult(1, string.Empty, "missing scripted response"));
        }

        public async IAsyncEnumerable<ProcessOutputLine> StreamAsync(ProcessRequest request, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken)
        {
            _ = request;
            _ = cancellationToken;
            await Task.Yield();
            yield break;
        }
    }
}