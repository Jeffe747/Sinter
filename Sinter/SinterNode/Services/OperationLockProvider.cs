namespace SinterNode.Services;

public interface IOperationLockProvider
{
    ValueTask<IAsyncDisposable> AcquireAsync(string scope, CancellationToken cancellationToken);
}

public sealed class OperationLockProvider : IOperationLockProvider
{
    private readonly Dictionary<string, SemaphoreSlim> locks = new(StringComparer.OrdinalIgnoreCase);
    private readonly object sync = new();

    public async ValueTask<IAsyncDisposable> AcquireAsync(string scope, CancellationToken cancellationToken)
    {
        SemaphoreSlim gate;
        lock (sync)
        {
            if (!locks.TryGetValue(scope, out gate!))
            {
                gate = new SemaphoreSlim(1, 1);
                locks[scope] = gate;
            }
        }

        await gate.WaitAsync(cancellationToken);
        return new Releaser(gate);
    }

    private sealed class Releaser(SemaphoreSlim gate) : IAsyncDisposable
    {
        public ValueTask DisposeAsync()
        {
            gate.Release();
            return ValueTask.CompletedTask;
        }
    }
}